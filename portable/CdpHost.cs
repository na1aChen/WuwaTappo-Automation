using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Net.WebSockets;
using System.Reflection;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;
using System.Windows.Forms;

[assembly: AssemblyTitle("Wuwa Tappo AutoChest CDP Host")]
[assembly: AssemblyDescription("WebView2 CDP Host by phrolova2405")]
[assembly: AssemblyCompany("by phrolova2405")]
[assembly: AssemblyMetadata("ProjectMarker", "by phrolova2405")]

namespace WuwaTappoCdpHost
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new CdpHostForm());
        }
    }

    internal sealed class CdpHostForm : Form
    {
        private readonly TextBox amountBox;
        private readonly Button startButton;
        private readonly Button cancelButton;
        private readonly Label statusLabel;
        private readonly Label warningLabel;
        private readonly object logLock = new object();
        private int activePort;
        private CancellationTokenSource activeCancellation;
        private volatile bool active;

        public CdpHostForm()
        {
            Text = "Wuwa Tappo CDP Host";
            ClientSize = new Size(560, 220);
            MinimumSize = new Size(560, 220);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(14, 17, 21);
            ForeColor = Color.FromArgb(240, 244, 248);
            Font = new Font("Segoe UI", 9f);

            Label title = new Label();
            title.Text = "WebView2 CDP 一次性补次数";
            title.Font = new Font("Segoe UI", 14f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(22, 18);
            Controls.Add(title);

            Label amountLabel = new Label();
            amountLabel.Text = "本次增加";
            amountLabel.AutoSize = true;
            amountLabel.Location = new Point(22, 67);
            Controls.Add(amountLabel);

            amountBox = new TextBox();
            amountBox.Text = "100000";
            amountBox.Location = new Point(95, 63);
            amountBox.Width = 130;
            amountBox.TextAlign = HorizontalAlignment.Right;
            amountBox.BackColor = Color.Black;
            amountBox.ForeColor = Color.White;
            Controls.Add(amountBox);

            startButton = new Button();
            startButton.Text = "启动 CDP 并补足";
            startButton.Location = new Point(245, 60);
            startButton.Size = new Size(150, 30);
            startButton.Click += delegate { StartRefill(); };
            Controls.Add(startButton);

            cancelButton = new Button();
            cancelButton.Text = "取消";
            cancelButton.Location = new Point(405, 60);
            cancelButton.Size = new Size(90, 30);
            cancelButton.Enabled = false;
            cancelButton.Click += delegate { CancelRefill(); };
            Controls.Add(cancelButton);

            statusLabel = new Label();
            statusLabel.Text = "未启动";
            statusLabel.AutoEllipsis = true;
            statusLabel.Location = new Point(22, 105);
            statusLabel.Size = new Size(510, 24);
            statusLabel.ForeColor = Color.FromArgb(112, 223, 191);
            Controls.Add(statusLabel);

            warningLabel = new Label();
            warningLabel.Text =
                "需要一次性 UAC。仅连接 127.0.0.1 随机端口，不读取 Cookie 或 Token。\r\n" +
                "运行前请先停止自动助手后台，并保留 Wuwa Tappo 登录状态。";
            warningLabel.ForeColor = Color.FromArgb(165, 174, 185);
            warningLabel.Location = new Point(22, 140);
            warningLabel.Size = new Size(510, 50);
            Controls.Add(warningLabel);

            FormClosing += delegate(object sender, FormClosingEventArgs e)
            {
                if (!active)
                    return;
                e.Cancel = true;
                CancelRefill();
                SetStatus("正在取消；等待 Wuwa Tappo 恢复普通模式后再关闭…");
            };
        }

        private void StartRefill()
        {
            int amount;
            string value = (amountBox.Text ?? "").Replace(",", "").Trim();
            if (!Int32.TryParse(value, out amount) || amount < 1000 || amount > 1000000)
            {
                MessageBox.Show(
                    this,
                    "请输入 1000 到 1000000 之间的本次增加数量。",
                    "数量无效",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            startButton.Enabled = false;
            cancelButton.Enabled = true;
            CancellationTokenSource source = new CancellationTokenSource();
            activeCancellation = source;
            active = true;
            SetStatus("正在定位 Wuwa Tappo…");
            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                string finalStatus = "";
                try
                {
                    finalStatus = RunRefill(amount, source.Token);
                    error = "";
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                }

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        active = false;
                        activeCancellation = null;
                        startButton.Enabled = true;
                        cancelButton.Enabled = false;
                        if (!String.IsNullOrWhiteSpace(error))
                        {
                            WriteLog("run_failed", error);
                            SetStatus("失败：" + error);
                            MessageBox.Show(
                                this,
                                error,
                                "CDP 补足失败",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                        else
                        {
                            WriteLog("run_finished", finalStatus);
                            SetStatus(finalStatus);
                        }
                        source.Dispose();
                    }));
                }
                catch
                {
                    source.Dispose();
                }
            });
        }

        private void CancelRefill()
        {
            CancellationTokenSource source = activeCancellation;
            if (!active || source == null || source.IsCancellationRequested)
                return;
            try
            {
                source.Cancel();
                cancelButton.Enabled = false;
                SetStatus("正在取消并恢复 Wuwa Tappo…");
            }
            catch
            {
            }
        }

        private string RunRefill(int amount, CancellationToken cancellationToken)
        {
            EnsureMainAutomationIsStopped();
            string executable = FindWuwaExecutable();
            string finalStatus = "";
            Exception failure = null;
            bool restoreAttempted = false;

            activePort = AllocateLoopbackPort();
            WriteLog(
                "run_started",
                "amount=" + amount.ToString(CultureInfo.InvariantCulture) +
                "; port=" + activePort.ToString(CultureInfo.InvariantCulture));
            try
            {
                SetStatus("正在重启 Wuwa Tappo 并开启本机 CDP…");
                RestartWuwa(executable, activePort);
                cancellationToken.ThrowIfCancellationRequested();

                CdpTarget target = WaitForCdpTarget(
                    activePort,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                using (CdpClient client = new CdpClient())
                {
                    client.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    SetStatus("CDP 已连接，等待页面加载…");

                    long startCount = WaitForCharacterCount(
                        client,
                        TimeSpan.FromSeconds(30),
                        cancellationToken);
                    ClickPoint clickPoint = ReadClickPoint(client);
                    long targetCount = startCount + amount;
                    SetStatus(String.Format(
                        "开始：{0:N0} → {1:N0}",
                        startCount,
                        targetCount));

                    long current = startCount;
                    int sent = 0;
                    int batchSize = 200;
                    int stalledBatches = 0;
                    Stopwatch stopwatch = Stopwatch.StartNew();
                    DateTime nextProgressUpdate = DateTime.UtcNow;

                    while (current < targetCount)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long requestedDelta = targetCount - startCount;
                        long remainingQuota = requestedDelta - sent;
                        if (remainingQuota <= 0)
                        {
                            current = WaitForCountChange(
                                client,
                                current,
                                TimeSpan.FromSeconds(5),
                                cancellationToken);
                            if (current < startCount + sent)
                            {
                                throw new InvalidOperationException(
                                    "本批点击已全部发送，但页面字符数未达到目标。" +
                                    "当前版本可能丢弃了部分页面输入。");
                            }
                            continue;
                        }

                        int batch = (int)Math.Min(
                            (long)batchSize,
                            Math.Max(1L, remainingQuota));
                        long before = current;
                        client.DispatchClicksAsync(
                                batch,
                                clickPoint.X,
                                clickPoint.Y,
                                cancellationToken)
                            .GetAwaiter()
                            .GetResult();
                        client.WaitForRenderingAsync(cancellationToken)
                            .GetAwaiter()
                            .GetResult();
                        sent += batch;
                        current = WaitForCountChange(
                            client,
                            before,
                            TimeSpan.FromSeconds(2.5),
                            cancellationToken);

                        long delta = current - before;
                        if (delta <= 0)
                        {
                            stalledBatches++;
                            batchSize = Math.Max(10, batchSize / 2);
                            SetStatus(String.Format(
                                "页面字符数未增长，正在降低批次重试（{0}/2）…",
                                stalledBatches));
                            if (stalledBatches >= 2)
                            {
                                throw new InvalidOperationException(
                                    "CDP 点击已发送，但页面字符数没有增长。" +
                                    "当前版本可能不响应 CDP 输入，或页面仍在加载/已退出登录。");
                            }
                            continue;
                        }

                        stalledBatches = 0;
                        if (delta >= Math.Max(1, (long)(batch * 0.9)))
                        {
                            batchSize = Math.Min(500, batchSize * 2);
                        }
                        else if (delta < Math.Max(1, (long)(batch * 0.4)))
                        {
                            batchSize = Math.Max(25, batchSize / 2);
                        }

                        if (DateTime.UtcNow >= nextProgressUpdate)
                        {
                            SetStatus(String.Format(
                                "进度 {0:N0} / {1:N0}  ·  已点击 {2:N0}  ·  {3:N0}/秒",
                                current,
                                targetCount,
                                sent,
                                stopwatch.Elapsed.TotalSeconds > 0
                                    ? (long)(current - startCount) /
                                      Math.Max(1L, (long)stopwatch.Elapsed.TotalSeconds)
                                    : 0));
                            nextProgressUpdate = DateTime.UtcNow.AddMilliseconds(200);
                        }
                    }

                    finalStatus = String.Format(
                        "完成：{0:N0} → {1:N0}，共点击 {2:N0}",
                        startCount,
                        current,
                        sent);
                }
            }
            catch (OperationCanceledException)
            {
                finalStatus = "已取消本次 CDP 补次数";
            }
            catch (Exception ex)
            {
                failure = ex;
            }
            finally
            {
                restoreAttempted = true;
                SetStatus("正在恢复 Wuwa Tappo 普通模式…");
                try
                {
                    RestartWuwa(executable, 0);
                }
                catch (Exception restoreError)
                {
                    if (failure == null)
                        failure = new InvalidOperationException(
                            "CDP 操作结束，但恢复普通模式失败：" + restoreError.Message);
                    else
                        failure = new InvalidOperationException(
                            failure.Message + "；恢复普通模式也失败：" + restoreError.Message);
                }
                activePort = 0;
            }

            if (failure != null)
                throw failure;
            return restoreAttempted
                ? finalStatus + " · 已恢复普通模式"
                : finalStatus;
        }

        private static void EnsureMainAutomationIsStopped()
        {
            Process[] processes = Process.GetProcessesByName("WuwaTappoAutoChest");
            try
            {
                if (processes.Length > 0)
                {
                    throw new InvalidOperationException(
                        "检测到主自动助手仍在运行。请先停止后台并关闭主控制面板，再启动 CDP Host。");
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
        }

        private void SetStatus(string text)
        {
            try
            {
                if (InvokeRequired)
                {
                    BeginInvoke(new Action(delegate { SetStatus(text); }));
                    return;
                }
                statusLabel.Text = text;
            }
            catch
            {
            }
        }

        private void WriteLog(string eventName, string message)
        {
            try
            {
                string logDirectory = Path.Combine(
                    AppDomain.CurrentDomain.BaseDirectory,
                    "runtime",
                    "logs");
                Directory.CreateDirectory(logDirectory);
                Dictionary<string, object> entry = new Dictionary<string, object> {
                    {"time", DateTimeOffset.Now.ToString("o", CultureInfo.InvariantCulture)},
                    {"event", eventName ?? ""},
                    {"message", message ?? ""}
                };
                string line = new JavaScriptSerializer().Serialize(entry) + "\r\n";
                lock (logLock)
                {
                    File.AppendAllText(
                        Path.Combine(logDirectory, "cdp-host.jsonl"),
                        line,
                        new UTF8Encoding(false));
                }
            }
            catch
            {
            }
        }

        private static string FindWuwaExecutable()
        {
            List<string> candidates = new List<string>();
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string portableRoot = SplitParent(baseDirectory);
            while (!String.IsNullOrWhiteSpace(portableRoot))
            {
                candidates.Add(Path.Combine(portableRoot, "WuwaTappo.exe"));
                portableRoot = SplitParent(portableRoot);
            }
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "WuwaTappo",
                "WuwaTappo.exe"));
            candidates.Add(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "WuwaTappo",
                "WuwaTappo.exe"));
            Process[] processes = Process.GetProcessesByName("WuwaTappo");
            try
            {
                foreach (Process process in processes)
                {
                    try
                    {
                        candidates.Add(process.MainModule.FileName);
                    }
                    catch
                    {
                    }
                }
            }
            finally
            {
                foreach (Process process in processes)
                    process.Dispose();
            }
            foreach (string candidate in candidates)
            {
                if (!String.IsNullOrWhiteSpace(candidate) && File.Exists(candidate))
                    return Path.GetFullPath(candidate);
            }
            throw new FileNotFoundException("找不到 WuwaTappo.exe。");
        }

        private static string SplitParent(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return "";
            DirectoryInfo parent = Directory.GetParent(path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar));
            return parent == null ? "" : parent.FullName;
        }

        private static int AllocateLoopbackPort()
        {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 0);
            listener.Start();
            int port = ((IPEndPoint)listener.LocalEndpoint).Port;
            listener.Stop();
            return port;
        }

        private static void RestartWuwa(string executable, int port)
        {
            List<Process> targets = new List<Process>();
            try
            {
                Process[] existing = Process.GetProcessesByName("WuwaTappo");
                targets.AddRange(existing);
                foreach (Process process in existing)
                {
                    try
                    {
                        process.CloseMainWindow();
                    }
                    catch
                    {
                    }
                }

                foreach (Process process in targets)
                {
                    try
                    {
                        if (!process.WaitForExit(1500))
                            process.Kill();
                        if (!process.WaitForExit(5000))
                            throw new InvalidOperationException(
                                "无法结束旧的 Wuwa Tappo 进程 PID " + process.Id + "。");
                    }
                    catch (InvalidOperationException)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        throw new InvalidOperationException(
                            "无法结束旧 Wuwa Tappo 进程 PID " + process.Id + "：" + ex.Message,
                            ex);
                    }
                }

                DateTime deadline = DateTime.UtcNow.AddSeconds(5);
                int remainingCount;
                while (true)
                {
                    Process[] remaining = Process.GetProcessesByName("WuwaTappo");
                    try
                    {
                        remainingCount = remaining.Length;
                    }
                    finally
                    {
                        foreach (Process process in remaining)
                            process.Dispose();
                    }
                    if (remainingCount == 0 || DateTime.UtcNow >= deadline)
                        break;
                    Thread.Sleep(100);
                }
                if (remainingCount > 0)
                    throw new InvalidOperationException("旧 Wuwa Tappo 进程未能完全退出。");

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executable;
                startInfo.WorkingDirectory = Path.GetDirectoryName(executable);
                startInfo.UseShellExecute = false;
                startInfo.EnvironmentVariables.Remove("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
                if (port > 0)
                {
                    startInfo.EnvironmentVariables["WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS"] =
                        "--remote-debugging-port=" + port +
                        " --remote-debugging-address=127.0.0.1";
                }
                using (Process started = Process.Start(startInfo))
                {
                    if (started == null)
                        throw new InvalidOperationException("无法启动 Wuwa Tappo。");
                }
            }
            finally
            {
                foreach (Process process in targets)
                    process.Dispose();
            }
        }

        private static CdpTarget WaitForCdpTarget(
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            Exception last = null;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string json = DownloadCdpTargets(port);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    object[] items = serializer.DeserializeObject(json) as object[];
                    if (items != null)
                    {
                        foreach (object item in items)
                        {
                            Dictionary<string, object> values =
                                item as Dictionary<string, object>;
                            if (values == null)
                                continue;
                            string type = GetString(values, "type");
                            string url = GetString(values, "url");
                            string socket = GetString(values, "webSocketDebuggerUrl");
                            if (type != "page" ||
                                String.IsNullOrWhiteSpace(socket) ||
                                url.IndexOf(
                                    "tauri.localhost",
                                    StringComparison.OrdinalIgnoreCase) < 0 ||
                                !IsLoopbackWebSocket(socket, port))
                                continue;
                            return new CdpTarget {
                                Url = url,
                                WebSocketDebuggerUrl = socket
                            };
                        }
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                SleepWithCancellation(400, cancellationToken);
            }
            throw new InvalidOperationException(
                "WebView2 CDP 未就绪。" +
                (last == null ? "" : " " + last.Message));
        }

        private static string DownloadCdpTargets(int port)
        {
            HttpWebRequest request = (HttpWebRequest)WebRequest.Create(
                "http://127.0.0.1:" + port + "/json");
            request.Proxy = null;
            request.KeepAlive = false;
            request.Timeout = 1500;
            request.ReadWriteTimeout = 1500;
            using (HttpWebResponse response = (HttpWebResponse)request.GetResponse())
            using (Stream stream = response.GetResponseStream())
            using (StreamReader reader = new StreamReader(stream, Encoding.UTF8))
                return reader.ReadToEnd();
        }

        private static bool IsLoopbackWebSocket(string value, int port)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                !String.Equals(uri.Scheme, "ws", StringComparison.OrdinalIgnoreCase) ||
                uri.Port != port)
                return false;
            if (String.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase))
                return true;

            string host = uri.Host.Trim('[', ']');
            IPAddress address;
            return IPAddress.TryParse(host, out address) && IPAddress.IsLoopback(address);
        }

        private static long WaitForCharacterCount(
            CdpClient client,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            Exception last = null;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    return ReadCharacterCount(client);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                SleepWithCancellation(300, cancellationToken);
            }
            throw new InvalidOperationException(
                "等待页面字符数超时。" +
                (last == null ? "" : " " + last.Message));
        }

        private static long ReadCharacterCount(CdpClient client)
        {
            const string expression =
                "(()=>{const root=document.querySelector('.pet-dock-content');" +
                "if(!root)return null;let best=null;" +
                "for(const e of root.querySelectorAll('*')){" +
                "if(e.children&&e.children.length)continue;" +
                "if(e.closest&&e.closest('.reward-countdown'))continue;" +
                "const t=(e.textContent||'').replace(/,/g,'').trim();" +
                "if(!/^\\d+$/.test(t))continue;const n=Number(t);" +
                "if(best===null||n>best)best=n;}return best;})()";
            Dictionary<string, object> response = client.EvaluateAsync(expression)
                .GetAwaiter()
                .GetResult();
            Dictionary<string, object> result = GetDictionary(response, "result");
            object value;
            if (result == null ||
                !result.TryGetValue("value", out value) ||
                value == null)
                throw new InvalidOperationException("页面中找不到字符数。");
            long count;
            if (!Int64.TryParse(Convert.ToString(value), out count))
                throw new InvalidOperationException("页面字符数格式无效。");
            return count;
        }

        private static ClickPoint ReadClickPoint(CdpClient client)
        {
            const string expression =
                "(()=>{const e=document.querySelector('.pet-dock-content');" +
                "if(!e)return null;const r=e.getBoundingClientRect();" +
                "if(r.width<8||r.height<8)return null;" +
                "return [r.left+r.width*0.5,r.top+r.height*0.42];})()";
            Dictionary<string, object> response = client.EvaluateAsync(expression)
                .GetAwaiter()
                .GetResult();
            Dictionary<string, object> result = GetDictionary(response, "result");
            object value;
            object[] coordinates = null;
            if (result != null && result.TryGetValue("value", out value))
                coordinates = value as object[];
            if (coordinates == null || coordinates.Length < 2)
                throw new InvalidOperationException("页面中找不到可点击的宠物区域。");
            double x = Convert.ToDouble(coordinates[0], CultureInfo.InvariantCulture);
            double y = Convert.ToDouble(coordinates[1], CultureInfo.InvariantCulture);
            if (Double.IsNaN(x) || Double.IsInfinity(x) ||
                Double.IsNaN(y) || Double.IsInfinity(y))
                throw new InvalidOperationException("宠物区域坐标无效。");
            return new ClickPoint {
                X = (int)Math.Round(x),
                Y = (int)Math.Round(y)
            };
        }

        private static long WaitForCountChange(
            CdpClient client,
            long previous,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            long latest = previous;
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                SleepWithCancellation(100, cancellationToken);
                latest = ReadCharacterCount(client);
                if (latest > previous)
                    return latest;
            }
            return latest;
        }

        private static Dictionary<string, object> GetDictionary(
            Dictionary<string, object> parent,
            string key)
        {
            object value;
            return parent != null && parent.TryGetValue(key, out value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static string GetString(Dictionary<string, object> values, string key)
        {
            object value;
            return values != null && values.TryGetValue(key, out value) && value != null
                ? Convert.ToString(value)
                : "";
        }

        private static void SleepWithCancellation(
            int milliseconds,
            CancellationToken cancellationToken)
        {
            if (cancellationToken.WaitHandle.WaitOne(milliseconds))
                cancellationToken.ThrowIfCancellationRequested();
        }

        private sealed class CdpTarget
        {
            public string Url;
            public string WebSocketDebuggerUrl;
        }

        private sealed class ClickPoint
        {
            public int X;
            public int Y;
        }
    }

    internal sealed class CdpClient : IDisposable
    {
        private readonly ClientWebSocket socket = new ClientWebSocket();
        private readonly ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>> pending =
            new ConcurrentDictionary<int, TaskCompletionSource<Dictionary<string, object>>>();
        private readonly SemaphoreSlim sendLock = new SemaphoreSlim(1, 1);
        private readonly CancellationTokenSource cancellation = new CancellationTokenSource();
        private readonly JavaScriptSerializer commandSerializer = new JavaScriptSerializer();
        private readonly JavaScriptSerializer receiveSerializer = new JavaScriptSerializer();
        private int nextId;
        private Task receiveTask;
        private bool mouseMoved;

        public async Task ConnectAsync(
            string webSocketUrl,
            CancellationToken cancellationToken)
        {
            socket.Options.Proxy = null;
            using (CancellationTokenSource linked = CreateLinkedSource(cancellationToken))
                await socket.ConnectAsync(new Uri(webSocketUrl), linked.Token);
            receiveTask = ReceiveLoopAsync();
            await SendAsync(
                "Runtime.enable",
                new Dictionary<string, object>(),
                cancellationToken);
        }

        public Task<Dictionary<string, object>> EvaluateAsync(string expression)
        {
            return EvaluateAsync(expression, false, CancellationToken.None);
        }

        public Task<Dictionary<string, object>> EvaluateAsync(
            string expression,
            CancellationToken cancellationToken)
        {
            return EvaluateAsync(expression, false, cancellationToken);
        }

        private Task<Dictionary<string, object>> EvaluateAsync(
            string expression,
            bool awaitPromise,
            CancellationToken cancellationToken)
        {
            Dictionary<string, object> parameters = new Dictionary<string, object> {
                {"expression", expression},
                {"returnByValue", true},
                {"awaitPromise", awaitPromise}
            };
            return SendAsync("Runtime.evaluate", parameters, cancellationToken);
        }

        public Task WaitForRenderingAsync(CancellationToken cancellationToken)
        {
            return EvaluateAsync(
                "new Promise(resolve=>requestAnimationFrame(" +
                "()=>requestAnimationFrame(()=>resolve(true))))",
                true,
                cancellationToken);
        }

        public async Task DispatchClicksAsync(
            int count,
            int x,
            int y,
            CancellationToken cancellationToken)
        {
            if (count <= 0)
                return;
            if (!mouseMoved)
            {
                await SendAsync(
                    "Input.dispatchMouseEvent",
                    MouseEvent("mouseMoved", x, y, "none", 0),
                    cancellationToken);
                mouseMoved = true;
            }

            const int chunkSize = 50;
            int remaining = count;
            while (remaining > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();
                int chunk = Math.Min(chunkSize, remaining);
                List<CommandRequest> requests = new List<CommandRequest>(chunk * 2);
                for (int index = 0; index < chunk; index++)
                {
                    requests.Add(PrepareCommand(
                        "Input.dispatchMouseEvent",
                        MouseEvent("mousePressed", x, y, "left", 1)));
                    requests.Add(PrepareCommand(
                        "Input.dispatchMouseEvent",
                        MouseEvent("mouseReleased", x, y, "left", 0)));
                }
                await SendBatchAsync(requests, cancellationToken);
                remaining -= chunk;
            }
        }

        public async Task<Dictionary<string, object>> SendAsync(
            string method,
            Dictionary<string, object> parameters,
            CancellationToken cancellationToken)
        {
            CommandRequest request = PrepareCommand(method, parameters);
            List<Dictionary<string, object>> responses =
                await SendBatchAsync(
                    new List<CommandRequest> { request },
                    cancellationToken);
            return responses[0];
        }

        private CommandRequest PrepareCommand(
            string method,
            Dictionary<string, object> parameters)
        {
            int id = Interlocked.Increment(ref nextId);
            TaskCompletionSource<Dictionary<string, object>> completion =
                new TaskCompletionSource<Dictionary<string, object>>();
            Dictionary<string, object> message = new Dictionary<string, object> {
                {"id", id},
                {"method", method},
                {"params", parameters ?? new Dictionary<string, object>()}
            };
            CommandRequest request = new CommandRequest {
                Id = id,
                Completion = completion,
                Bytes = Encoding.UTF8.GetBytes(commandSerializer.Serialize(message))
            };
            pending[id] = completion;
            return request;
        }

        private async Task<List<Dictionary<string, object>>> SendBatchAsync(
            List<CommandRequest> requests,
            CancellationToken cancellationToken)
        {
            using (CancellationTokenSource linked = CreateLinkedSource(cancellationToken))
            {
                try
                {
                    await sendLock.WaitAsync(linked.Token);
                    try
                    {
                        foreach (CommandRequest request in requests)
                        {
                            await socket.SendAsync(
                                new ArraySegment<byte>(request.Bytes),
                                WebSocketMessageType.Text,
                                true,
                                linked.Token);
                        }
                    }
                    finally
                    {
                        sendLock.Release();
                    }
                }
                catch (Exception ex)
                {
                    FailRequests(requests, ex);
                    throw;
                }

                List<Task<Dictionary<string, object>>> responseTasks =
                    new List<Task<Dictionary<string, object>>>(requests.Count);
                foreach (CommandRequest request in requests)
                    responseTasks.Add(request.Completion.Task);

                Task allResponses = Task.WhenAll(responseTasks);
                using (CancellationTokenSource timeout =
                    CancellationTokenSource.CreateLinkedTokenSource(linked.Token))
                {
                    timeout.CancelAfter(TimeSpan.FromSeconds(15));
                    Task timeoutTask = Task.Delay(Timeout.Infinite, timeout.Token);
                    Task completed = await Task.WhenAny(allResponses, timeoutTask);
                    if (completed != allResponses)
                    {
                        linked.Token.ThrowIfCancellationRequested();
                        TimeoutException timeoutError = new TimeoutException(
                            "等待 WebView2 CDP 响应超时。");
                        FailRequests(requests, timeoutError);
                        throw timeoutError;
                    }
                    timeout.Cancel();
                }

                await allResponses;
                List<Dictionary<string, object>> responses =
                    new List<Dictionary<string, object>>(responseTasks.Count);
                foreach (Task<Dictionary<string, object>> responseTask in responseTasks)
                {
                    Dictionary<string, object> response = await responseTask;
                    ThrowIfCdpError(response);
                    responses.Add(response);
                }
                return responses;
            }
        }

        private void FailRequests(List<CommandRequest> requests, Exception error)
        {
            foreach (CommandRequest request in requests)
            {
                TaskCompletionSource<Dictionary<string, object>> completion;
                if (pending.TryRemove(request.Id, out completion))
                    completion.TrySetException(error);
            }
        }

        private static Dictionary<string, object> MouseEvent(
            string type,
            int x,
            int y,
            string button,
            int buttons)
        {
            return new Dictionary<string, object> {
                {"type", type},
                {"x", x},
                {"y", y},
                {"button", button},
                {"buttons", buttons},
                {"clickCount", type == "mouseMoved" ? 0 : 1}
            };
        }

        private static void ThrowIfCdpError(Dictionary<string, object> response)
        {
            object errorValue;
            if (response == null || !response.TryGetValue("error", out errorValue))
                return;
            Dictionary<string, object> error = errorValue as Dictionary<string, object>;
            object messageValue;
            string message = error != null &&
                             error.TryGetValue("message", out messageValue) &&
                             messageValue != null
                ? Convert.ToString(messageValue)
                : Convert.ToString(errorValue);
            throw new InvalidOperationException("CDP 返回错误：" + message);
        }

        private CancellationTokenSource CreateLinkedSource(
            CancellationToken cancellationToken)
        {
            return CancellationTokenSource.CreateLinkedTokenSource(
                cancellationToken,
                cancellation.Token);
        }

        private void FailPending(Exception error)
        {
            try
            {
                foreach (KeyValuePair<int, TaskCompletionSource<Dictionary<string, object>>> item in pending)
                {
                    TaskCompletionSource<Dictionary<string, object>> completion;
                    if (pending.TryRemove(item.Key, out completion))
                        completion.TrySetException(error);
                }
            }
            catch
            {
            }
        }

        private async Task ReceiveLoopAsync()
        {
            byte[] buffer = new byte[65536];
            while (!cancellation.IsCancellationRequested &&
                   socket.State == WebSocketState.Open)
            {
                try
                {
                    using (MemoryStream message = new MemoryStream())
                    {
                        WebSocketReceiveResult result;
                        do
                        {
                            result = await socket.ReceiveAsync(
                                new ArraySegment<byte>(buffer),
                                cancellation.Token);
                            if (result.MessageType == WebSocketMessageType.Close)
                            {
                                FailPending(new WebSocketException(
                                    "WebView2 CDP 连接已关闭。"));
                                return;
                            }
                            message.Write(buffer, 0, result.Count);
                        }
                        while (!result.EndOfMessage);

                        string json = Encoding.UTF8.GetString(message.ToArray());
                        Dictionary<string, object> values =
                            receiveSerializer.DeserializeObject(json) as Dictionary<string, object>;
                        object idValue;
                        if (values == null ||
                            !values.TryGetValue("id", out idValue) ||
                            idValue == null)
                            continue;
                        int id;
                        if (!Int32.TryParse(Convert.ToString(idValue), out id))
                            continue;
                        TaskCompletionSource<Dictionary<string, object>> completion;
                        if (pending.TryRemove(id, out completion))
                            completion.TrySetResult(values);
                    }
                }
                catch (Exception ex)
                {
                    FailPending(ex);
                    return;
                }
            }
            FailPending(new WebSocketException("WebView2 CDP 连接已关闭。"));
        }

        public void Dispose()
        {
            FailPending(new ObjectDisposedException("CdpClient"));
            try { cancellation.Cancel(); } catch { }
            try
            {
                if (socket.State == WebSocketState.Open)
                {
                    using (CancellationTokenSource closeTimeout =
                        new CancellationTokenSource(TimeSpan.FromSeconds(1)))
                    {
                        socket.CloseAsync(
                            WebSocketCloseStatus.NormalClosure,
                            "done",
                            closeTimeout.Token).GetAwaiter().GetResult();
                    }
                }
            }
            catch
            {
            }
            try { socket.Abort(); } catch { }
            socket.Dispose();
            sendLock.Dispose();
            cancellation.Dispose();
        }
    }

    internal sealed class CommandRequest
    {
        public int Id;
        public byte[] Bytes;
        public TaskCompletionSource<Dictionary<string, object>> Completion;
    }
}
