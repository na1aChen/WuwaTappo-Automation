using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WuwaTappoPortable
{
    internal static class Program
    {
        [STAThread]
        private static int Main(string[] args)
        {
            try
            {
                if (args != null && args.Length > 0)
                {
                    string command = args[0].Trim().ToLowerInvariant();
                    if (command == "--start-background")
                        return PortableRuntime.Execute("start", false);
                    if (command == "--stop-background")
                        return PortableRuntime.Execute("stop", false);
                    if (command == "--worker-background")
                        return new NativeWorker().Run();
                    if (command == "--open-gallery")
                    {
                        PortableRuntime.OpenGallery();
                        return 0;
                    }
                }

                Application.EnableVisualStyles();
                Application.SetCompatibleTextRenderingDefault(false);
                Application.Run(new CodexMainForm());
                return 0;
            }
            catch (Exception ex)
            {
                try
                {
                    string logPath = Path.Combine(
                        AppDomain.CurrentDomain.BaseDirectory,
                        "launcher-error.log");
                    File.WriteAllText(logPath, ex.ToString());
                }
                catch
                {
                }
                return 1;
            }
        }
    }

    internal sealed class StatusSnapshot
    {
        public bool Running;
        public string Phase = "未知";
        public string CharacterCount = "-";
        public string ActivityEnabled = "-";
        public string ActivityKeysSent = "-";
        public string ActivityLastResult = "";
        public string RewardButtons = "-";
        public string LastAction = "";
        public string ItemName = "";
        public string ItemSource = "";
        public string LastActionStatus = "";
        public string LastActionSide = "";
        public string LastActionTime = "";
        public string LastError = "";
        public string Message = "";
        public string WorkerPid = "-";
    }

    internal sealed class HistoryEntry
    {
        public string Time = "";
        public string ItemName = "";
        public string Side = "";
        public string Result = "";
        public string Status = "";
        public string ItemSource = "";
    }

    internal static class PortableRuntime
    {
        public static readonly string BaseDirectory =
            AppDomain.CurrentDomain.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        public static readonly string RuntimeDirectory = Path.Combine(BaseDirectory, "runtime");
        public static readonly string ConfigPath = Path.Combine(RuntimeDirectory, @"config\autochest.json");
        public static readonly string StatusFile = Path.Combine(RuntimeDirectory, @"state\status.json");
        public static readonly string HistoryFile = Path.Combine(RuntimeDirectory, @"state\history.jsonl");
        public static readonly string PidFile = Path.Combine(RuntimeDirectory, @"state\worker.pid");
        public static readonly string StopFile = Path.Combine(RuntimeDirectory, @"state\stop.requested");
        public static readonly string GalleryPath = Path.Combine(RuntimeDirectory, @"gallery\index.html");
        public static readonly string ReadmePath = Path.Combine(RuntimeDirectory, "README.txt");

        public static int Execute(string action, bool captureDetails)
        {
            string error;
            int exitCode = action == "stop"
                ? StopWorker(out error)
                : StartWorker(out error);
            if (exitCode != 0)
            {
                try
                {
                    string details =
                        "Action: " + action + Environment.NewLine +
                        "ExitCode: " + exitCode + Environment.NewLine +
                        "Error:" + Environment.NewLine + error + Environment.NewLine;
                    File.WriteAllText(
                        Path.Combine(BaseDirectory, "launcher-error.log"),
                        details);
                }
                catch
                {
                }
            }
            return exitCode;
        }

        public static int StartWorker(out string error)
        {
            error = "";
            if (IsWorkerRunning())
                return 0;

            Directory.CreateDirectory(Path.GetDirectoryName(StopFile));
            try
            {
                if (File.Exists(StopFile))
                    File.Delete(StopFile);
            }
            catch (Exception ex)
            {
                error = "无法清理停止标记：" + ex.Message;
                return 2;
            }

            string executable = Process.GetCurrentProcess().MainModule.FileName;
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = executable;
            startInfo.Arguments = "--worker-background";
            startInfo.WorkingDirectory = BaseDirectory;
            startInfo.UseShellExecute = false;
            startInfo.CreateNoWindow = true;
            startInfo.WindowStyle = ProcessWindowStyle.Hidden;
            try
            {
                Process.Start(startInfo);
            }
            catch (Exception ex)
            {
                error = "无法创建后台进程：" + ex.Message;
                return 1;
            }

            DateTime deadline = DateTime.UtcNow.AddSeconds(20);
            while (DateTime.UtcNow < deadline)
            {
                if (IsWorkerRunning())
                {
                    Dictionary<string, object> status = ParseObject(StatusFile);
                    if (status != null && status.ContainsKey("phase"))
                        return 0;
                }
                Thread.Sleep(250);
            }
            error = "后台进程在 20 秒内没有写出状态，请检查 runtime\\logs。";
            return 3;
        }

        public static int StopWorker(out string error)
        {
            error = "";
            if (!IsWorkerRunning())
            {
                try
                {
                    if (File.Exists(StopFile))
                        File.Delete(StopFile);
                    if (File.Exists(PidFile))
                        File.Delete(PidFile);
                }
                catch
                {
                }
                return 0;
            }

            Directory.CreateDirectory(Path.GetDirectoryName(StopFile));
            File.WriteAllText(
                StopFile,
                "{\"requested_at\":\"" + DateTimeOffset.Now.ToString("o") + "\"}",
                new System.Text.UTF8Encoding(false));
            DateTime deadline = DateTime.UtcNow.AddSeconds(30);
            while (DateTime.UtcNow < deadline)
            {
                if (!IsWorkerRunning())
                {
                    try { File.Delete(StopFile); } catch { }
                    return 0;
                }
                Thread.Sleep(250);
            }

            Dictionary<string, object> pidData = ParseObject(PidFile);
            int processId;
            if (pidData != null &&
                Int32.TryParse(GetString(pidData, "worker_pid"), out processId))
            {
                try
                {
                    Process worker = Process.GetProcessById(processId);
                    string expected = Path.GetFileName(Process.GetCurrentProcess().MainModule.FileName);
                    string actual = Path.GetFileName(worker.MainModule.FileName);
                    if (String.Equals(expected, actual, StringComparison.OrdinalIgnoreCase))
                        worker.Kill();
                }
                catch
                {
                }
            }
            error = "后台进程没有在 30 秒内停止。";
            return 4;
        }

        private static Dictionary<string, object> ParseObject(string path)
        {
            if (!File.Exists(path))
                return null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    string json = File.ReadAllText(path);
                    JavaScriptSerializer serializer = new JavaScriptSerializer();
                    return serializer.Deserialize<Dictionary<string, object>>(json);
                }
                catch
                {
                    Thread.Sleep(100);
                }
            }
            return null;
        }

        private static string GetString(Dictionary<string, object> data, string key)
        {
            if (data == null || !data.ContainsKey(key) || data[key] == null)
                return "";
            return Convert.ToString(data[key]);
        }

        public static bool IsWorkerRunning()
        {
            Dictionary<string, object> pidData = ParseObject(PidFile);
            if (pidData == null || !pidData.ContainsKey("worker_pid"))
                return false;

            int processId;
            if (!Int32.TryParse(Convert.ToString(pidData["worker_pid"]), out processId))
                return false;

            try
            {
                Process process = Process.GetProcessById(processId);
                return process != null && !process.HasExited;
            }
            catch
            {
                return false;
            }
        }

        public static StatusSnapshot ReadStatus()
        {
            StatusSnapshot snapshot = new StatusSnapshot();
            Dictionary<string, object> status = ParseObject(StatusFile);
            snapshot.Running = IsWorkerRunning();
            if (status == null)
            {
                snapshot.Phase = snapshot.Running ? "启动中" : "未启动";
                return snapshot;
            }

            snapshot.Phase = GetString(status, "phase");
            snapshot.CharacterCount = GetString(status, "character_count");
            snapshot.ActivityEnabled = GetString(status, "activity_enabled");
            snapshot.ActivityKeysSent = GetString(status, "activity_keys_sent");
            snapshot.ActivityLastResult = GetString(status, "activity_last_result");
            snapshot.RewardButtons = GetString(status, "reward_button_count");
            snapshot.LastError = GetString(status, "last_error");
            snapshot.Message = GetString(status, "message");
            snapshot.WorkerPid = GetString(status, "worker_pid");

            object actionObject;
            if (status.TryGetValue("last_action", out actionObject) &&
                actionObject is Dictionary<string, object>)
            {
                Dictionary<string, object> action =
                    (Dictionary<string, object>)actionObject;
                snapshot.LastAction =
                    GetString(action, "status") + " / " +
                    GetString(action, "evidence") + " / " +
                    GetString(action, "time");
                snapshot.ItemName = GetString(action, "item_name");
                snapshot.ItemSource = GetString(action, "item_source");
                snapshot.LastActionStatus = GetString(action, "status");
                snapshot.LastActionSide = GetString(action, "side");
                snapshot.LastActionTime = GetString(action, "time");
            }
            return snapshot;
        }

        public static List<HistoryEntry> ReadHistory(int maximum)
        {
            List<HistoryEntry> entries = new List<HistoryEntry>();
            if (maximum <= 0 || !File.Exists(HistoryFile))
                return entries;

            try
            {
                List<string> lines = ReadTailLines(HistoryFile, maximum, 512 * 1024);
                JavaScriptSerializer serializer = new JavaScriptSerializer();
                foreach (string line in lines)
                {
                    if (String.IsNullOrWhiteSpace(line))
                        continue;
                    try
                    {
                        Dictionary<string, object> record =
                            serializer.Deserialize<Dictionary<string, object>>(line);
                        if (record == null)
                            continue;

                        string rawTime = GetString(record, "time");
                        DateTimeOffset parsedTime;
                        string displayTime = rawTime;
                        if (DateTimeOffset.TryParse(rawTime, out parsedTime))
                            displayTime = parsedTime.ToLocalTime().ToString("MM-dd HH:mm:ss");

                        string status = GetString(record, "status");
                        string result = GetString(record, "result");
                        entries.Add(new HistoryEntry {
                            Time = displayTime,
                            ItemName = GetString(record, "item_name"),
                            Side = GetString(record, "side"),
                            Status = status,
                            Result = String.IsNullOrWhiteSpace(result) ? status : result,
                            ItemSource = GetString(record, "item_source")
                        });
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }

            entries.Reverse();
            return entries;
        }

        private static List<string> ReadTailLines(
            string path,
            int maximumLines,
            int maximumBytes)
        {
            List<string> lines = new List<string>();
            using (FileStream stream = new FileStream(
                path,
                FileMode.Open,
                FileAccess.Read,
                FileShare.ReadWrite | FileShare.Delete))
            {
                long length = stream.Length;
                if (length <= 0)
                    return lines;
                int bytesToRead = (int)Math.Min(length, Math.Max(4096, maximumBytes));
                long start = length - bytesToRead;
                stream.Seek(start, SeekOrigin.Begin);
                byte[] buffer = new byte[bytesToRead];
                int read = stream.Read(buffer, 0, buffer.Length);
                string text = new System.Text.UTF8Encoding(false).GetString(buffer, 0, read);
                string[] split = text.Replace("\r\n", "\n").Split('\n');
                int first = start > 0 ? 1 : 0;
                for (int index = first; index < split.Length; index++)
                {
                    if (!String.IsNullOrWhiteSpace(split[index]))
                        lines.Add(split[index]);
                }
            }
            if (lines.Count > maximumLines)
                lines = lines.GetRange(lines.Count - maximumLines, maximumLines);
            return lines;
        }

        public static void OpenGallery()
        {
            if (!File.Exists(GalleryPath))
                throw new FileNotFoundException("找不到全图鉴页面。", GalleryPath);
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = GalleryPath;
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }

        public static void OpenReadme()
        {
            if (!File.Exists(ReadmePath))
                throw new FileNotFoundException("找不到使用说明。", ReadmePath);
            ProcessStartInfo startInfo = new ProcessStartInfo();
            startInfo.FileName = ReadmePath;
            startInfo.UseShellExecute = true;
            Process.Start(startInfo);
        }
    }

    internal sealed class MainForm : Form
    {
        private readonly Label statusLabel;
        private readonly Label phaseLabel;
        private readonly Label characterLabel;
        private readonly Label activityLabel;
        private readonly Label rewardLabel;
        private readonly Label lastActionLabel;
        private readonly Label messageLabel;
        private readonly Button startButton;
        private readonly Button stopButton;
        private readonly Button refreshButton;
        private readonly Button galleryButton;
        private readonly System.Windows.Forms.Timer refreshTimer;
        private bool busy;

        public MainForm()
        {
            Text = "Wuwa Tappo 自动助手";
            ClientSize = new Size(720, 440);
            MinimumSize = new Size(720, 440);
            MaximizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(22, 27, 34);
            ForeColor = Color.FromArgb(235, 240, 245);
            Font = new Font("Microsoft YaHei UI", 9.5f);

            Label title = new Label();
            title.Text = "Wuwa Tappo 自动助手";
            title.Font = new Font("Microsoft YaHei UI", 19f, FontStyle.Bold);
            title.AutoSize = true;
            title.Location = new Point(24, 20);
            Controls.Add(title);

            Label subtitle = new Label();
            subtitle.Text = "字符数按需补充 · 自动监测宝箱 · 后台静默运行";
            subtitle.ForeColor = Color.FromArgb(150, 166, 184);
            subtitle.AutoSize = true;
            subtitle.Location = new Point(27, 65);
            Controls.Add(subtitle);

            Panel statusPanel = new Panel();
            statusPanel.BackColor = Color.FromArgb(29, 37, 47);
            statusPanel.Location = new Point(24, 104);
            statusPanel.Size = new Size(672, 214);
            Controls.Add(statusPanel);

            statusLabel = CreateLabel("状态：读取中", 16, 14, 15f, FontStyle.Bold);
            statusPanel.Controls.Add(statusLabel);
            phaseLabel = CreateLabel("阶段：-", 18, 54, 10f, FontStyle.Regular);
            statusPanel.Controls.Add(phaseLabel);
            characterLabel = CreateLabel("字符数：-", 18, 84, 10f, FontStyle.Regular);
            statusPanel.Controls.Add(characterLabel);
            activityLabel = CreateLabel("活动注入：-", 18, 114, 10f, FontStyle.Regular);
            statusPanel.Controls.Add(activityLabel);
            rewardLabel = CreateLabel("宝箱按钮：-", 360, 54, 10f, FontStyle.Regular);
            statusPanel.Controls.Add(rewardLabel);
            lastActionLabel = CreateLabel("最近开箱：-", 360, 84, 10f, FontStyle.Regular);
            statusPanel.Controls.Add(lastActionLabel);
            messageLabel = CreateLabel("消息：-", 18, 150, 9f, FontStyle.Regular);
            messageLabel.AutoSize = false;
            messageLabel.Size = new Size(630, 48);
            statusPanel.Controls.Add(messageLabel);

            startButton = CreateButton("启动后台", 24, 340, 130);
            startButton.Click += delegate { ExecuteAction("start"); };
            Controls.Add(startButton);

            stopButton = CreateButton("停止后台", 164, 340, 130);
            stopButton.Click += delegate { ExecuteAction("stop"); };
            Controls.Add(stopButton);

            refreshButton = CreateButton("刷新状态", 304, 340, 120);
            refreshButton.Click += delegate { RefreshStatus(); };
            Controls.Add(refreshButton);

            galleryButton = CreateButton("打开全图鉴", 434, 340, 130);
            galleryButton.Click += delegate { OpenSafely(PortableRuntime.OpenGallery); };
            Controls.Add(galleryButton);

            Button readmeButton = CreateButton("使用说明", 574, 340, 122);
            readmeButton.Click += delegate { OpenSafely(PortableRuntime.OpenReadme); };
            Controls.Add(readmeButton);

            Label footer = new Label();
            footer.Text = "关闭此窗口不会停止后台任务。首次使用请先启动并登录 Wuwa Tappo。";
            footer.ForeColor = Color.FromArgb(150, 166, 184);
            footer.AutoSize = true;
            footer.Location = new Point(27, 395);
            Controls.Add(footer);

            refreshTimer = new System.Windows.Forms.Timer();
            refreshTimer.Interval = 1500;
            refreshTimer.Tick += delegate { RefreshStatus(); };
            refreshTimer.Start();
            RefreshStatus();
        }

        private static Label CreateLabel(
            string text,
            int x,
            int y,
            float size,
            FontStyle style)
        {
            Label label = new Label();
            label.Text = text;
            label.AutoSize = true;
            label.Location = new Point(x, y);
            label.Font = new Font("Microsoft YaHei UI", size, style);
            return label;
        }

        private static Button CreateButton(string text, int x, int y, int width)
        {
            Button button = new Button();
            button.Text = text;
            button.Location = new Point(x, y);
            button.Size = new Size(width, 38);
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderColor = Color.FromArgb(62, 80, 98);
            button.BackColor = Color.FromArgb(34, 45, 57);
            button.ForeColor = Color.FromArgb(235, 240, 245);
            return button;
        }

        private void ExecuteAction(string action)
        {
            if (busy)
                return;
            busy = true;
            SetButtonsEnabled(false);
            statusLabel.Text = action == "start" ? "状态：正在启动后台…" : "状态：正在停止后台…";

            ThreadPool.QueueUserWorkItem(delegate
            {
                string error;
                int exitCode = action == "start"
                    ? PortableRuntime.StartWorker(out error)
                    : PortableRuntime.StopWorker(out error);

                try
                {
                    BeginInvoke(new Action(delegate
                    {
                        busy = false;
                        SetButtonsEnabled(true);
                        RefreshStatus();
                        if (exitCode != 0)
                        {
                            MessageBox.Show(
                                error,
                                "操作失败",
                                MessageBoxButtons.OK,
                                MessageBoxIcon.Error);
                        }
                    }));
                }
                catch
                {
                }
            });
        }

        private void SetButtonsEnabled(bool enabled)
        {
            startButton.Enabled = enabled;
            stopButton.Enabled = enabled;
            refreshButton.Enabled = enabled;
            galleryButton.Enabled = enabled;
        }

        private void RefreshStatus()
        {
            StatusSnapshot snapshot = PortableRuntime.ReadStatus();
            statusLabel.Text = "状态：" + (snapshot.Running ? "运行中" : "未运行");
            statusLabel.ForeColor = snapshot.Running
                ? Color.FromArgb(112, 214, 197)
                : Color.FromArgb(255, 170, 120);
            phaseLabel.Text = "阶段：" + snapshot.Phase;
            characterLabel.Text = "字符数：" + snapshot.CharacterCount;
            activityLabel.Text =
                "活动注入：" +
                (snapshot.ActivityEnabled == "True" ? "启用" : "暂停") +
                " · 已发送 " +
                snapshot.ActivityKeysSent;
            rewardLabel.Text = "宝箱按钮：" + snapshot.RewardButtons;
            lastActionLabel.Text = String.IsNullOrWhiteSpace(snapshot.LastAction)
                ? "最近开箱：-"
                : "最近开箱：" + snapshot.LastAction;
            messageLabel.Text =
                "消息：" +
                (String.IsNullOrWhiteSpace(snapshot.LastError)
                    ? snapshot.Message
                    : snapshot.LastError + " · " + snapshot.Message);
        }

        private void OpenSafely(Action action)
        {
            try
            {
                action();
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    ex.Message,
                    "无法打开",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Error);
            }
        }
    }
}
