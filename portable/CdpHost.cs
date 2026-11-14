using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Management;
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
        private static void Main(string[] args)
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            if (args != null && args.Length >= 2 &&
                String.Equals(args[0], "--native-refill", StringComparison.OrdinalIgnoreCase))
            {
                int amount;
                if (!Int32.TryParse(args[1], out amount) ||
                    amount < 1000 || amount > 1000000)
                {
                    Environment.ExitCode = 2;
                    return;
                }
                CdpHostForm form = new CdpHostForm();
                try
                {
                    SynchronizationContext.SetSynchronizationContext(null);
                    form.RunRefill(amount, CancellationToken.None);
                    Environment.ExitCode = 0;
                }
                catch (Exception ex)
                {
                    try
                    {
                        string logDirectory = Path.Combine(
                            AppDomain.CurrentDomain.BaseDirectory,
                            "runtime",
                            "logs");
                        Directory.CreateDirectory(logDirectory);
                        File.WriteAllText(
                            Path.Combine(logDirectory, "cdp-host-cli-error.txt"),
                            ex.ToString(),
                            new UTF8Encoding(false));
                    }
                    catch
                    {
                    }
                    Environment.ExitCode = 1;
                }
                return;
            }
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
            Text = "Wuwa Tappo 原生计数 Host";
            ClientSize = new Size(560, 220);
            MinimumSize = new Size(560, 220);
            StartPosition = FormStartPosition.CenterScreen;
            BackColor = Color.FromArgb(14, 17, 21);
            ForeColor = Color.FromArgb(240, 244, 248);
            Font = new Font("Segoe UI", 9f);

            Label title = new Label();
            title.Text = "页面原生计数一次性补次数";
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
            amountBox.Text = "1000";
            amountBox.Location = new Point(95, 63);
            amountBox.Width = 130;
            amountBox.TextAlign = HorizontalAlignment.Right;
            amountBox.BackColor = Color.Black;
            amountBox.ForeColor = Color.White;
            Controls.Add(amountBox);

            startButton = new Button();
            startButton.Text = "启动原生补足";
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
                "运行前请先停止自动助手后台。首次测试请保持 1000 次。";
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
                string errorDetail;
                string finalStatus = "";
                try
                {
                    finalStatus = RunRefill(amount, source.Token);
                    error = "";
                    errorDetail = "";
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    try
                    {
                        errorDetail = ex.ToString();
                    }
                    catch
                    {
                        errorDetail = ex.GetType().FullName + ": " + error;
                    }
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
                            WriteLog(
                                "run_failed",
                                String.IsNullOrWhiteSpace(errorDetail)
                                    ? error
                                    : errorDetail);
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

        internal string RunRefill(int amount, CancellationToken cancellationToken)
        {
            return RunRefillSession(
                amount,
                cancellationToken,
                0,
                0,
                0,
                "");
        }

        private string RunRefillSession(
            int amount,
            CancellationToken cancellationToken,
            int transportRecoveries,
            long resumeTargetAvailable,
            long resumeTargetCumulative,
            string resumeAccountKey)
        {
            EnsureMainAutomationIsStopped();
            string executable = FindWuwaExecutable();
            string finalStatus = "";
            Exception failure = null;
            bool restoreAttempted = false;
            bool resumeRequested = false;
            string resumeReason = "";
            long targetAvailable = resumeTargetAvailable;
            long targetCumulative = resumeTargetCumulative;
            string taskAccountKey = resumeAccountKey ?? "";

            activePort = AllocateLoopbackPort();
            WriteLog(
                "run_started",
                "amount=" + amount.ToString(CultureInfo.InvariantCulture) +
                "; port=" + activePort.ToString(CultureInfo.InvariantCulture));
            try
            {
                SetStatus("正在重启 Wuwa Tappo 并开启本机 CDP…");
                int debugAppProcessId = RestartWuwa(executable, activePort);
                cancellationToken.ThrowIfCancellationRequested();
                if (!WaitForRemoteDebugStart(
                    debugAppProcessId,
                    activePort,
                    TimeSpan.FromSeconds(8),
                    cancellationToken))
                {
                    throw new InvalidOperationException(
                        "WebView2 未接受远程调试参数。" +
                        "已确认旧 WebView2 进程退出，但新进程及其子进程命令中均未出现本次调试端口。");
                }

                CdpTarget target = WaitForCdpTarget(
                    activePort,
                    TimeSpan.FromSeconds(30),
                    cancellationToken);
                using (CdpClient client = new CdpClient())
                {
                    client.ConnectAsync(target.WebSocketDebuggerUrl, cancellationToken)
                        .GetAwaiter()
                        .GetResult();
                    SetStatus("CDP 已连接，正在发现页面原生计数能力…");
                    NativePageProfile profile = WaitForNativeProfile(
                        client,
                        TimeSpan.FromSeconds(30),
                        cancellationToken);
                    if (profile.CountMode != "increment")
                    {
                        throw new InvalidOperationException(
                            "当前页面使用非增量计数入口，原生 Host 暂不支持该版本。");
                    }

                    NativePageSnapshot start = WaitForNativeSnapshot(
                        client,
                        profile,
                        TimeSpan.FromSeconds(30),
                        cancellationToken);
                    long reportStartAvailable = start.Available;
                    long reportStartCumulative = start.Cumulative;
                    if (resumeTargetAvailable > 0 &&
                        resumeTargetCumulative > 0)
                    {
                        if (!String.IsNullOrWhiteSpace(resumeAccountKey) &&
                            !String.Equals(
                                resumeAccountKey,
                                start.AccountKey,
                                StringComparison.Ordinal))
                        {
                            throw new InvalidOperationException(
                                "会话恢复时账号摘要发生变化，已停止补数。");
                        }
                        targetAvailable = resumeTargetAvailable;
                        targetCumulative = resumeTargetCumulative;
                        reportStartAvailable =
                            Math.Max(0L, targetAvailable - amount);
                        reportStartCumulative =
                            Math.Max(0L, targetCumulative - amount);
                        WriteLog(
                            "native_session_rebased",
                            String.Format(
                                CultureInfo.InvariantCulture,
                                "recovery={0}; reopened_available={1}; " +
                                "reopened_cumulative={2}; target_available={3}; " +
                                "target_cumulative={4}; remaining={5}",
                                transportRecoveries,
                                start.Available,
                                start.Cumulative,
                                targetAvailable,
                                targetCumulative,
                                NativeRecoveryPolicy.Remaining(
                                    targetAvailable,
                                    targetCumulative,
                                    start)));
                    }
                    else
                    {
                        targetAvailable = start.Available + amount;
                        targetCumulative = start.Cumulative + amount;
                    }
                    if (String.IsNullOrWhiteSpace(taskAccountKey))
                        taskAccountKey = start.AccountKey;
                    SetStatus(String.Format(
                        "原生计数开始：{0:N0} → {1:N0}",
                        reportStartAvailable,
                        targetAvailable));
                    WriteLog(
                        "native_refill_started",
                        String.Format(
                            "available={0}; cumulative={1}; target_available={2}; " +
                            "target_cumulative={3}; mode={4}; " +
                            "preflight_retry_limit={5}; post_save_poll_count={6}",
                            start.Available,
                            start.Cumulative,
                            targetAvailable,
                            targetCumulative,
                            profile.CountMode,
                            NativeRecoveryPolicy.MaxPreflightRetries,
                            NativeRecoveryPolicy.PostSavePollCount));

                    NativePageSnapshot current = start;
                    int sent = 0;
                    int preflightFailures = 0;
                    int incrementFailures = 0;
                    int preflightRecoveries = 0;
                    int incrementRecoveries = 0;
                    int postSaveRecoveries = 0;
                    int postSaveRebaseFailures = 0;
                    int postSaveRebases = 0;
                    Stopwatch stopwatch = Stopwatch.StartNew();

                    while (NativeRecoveryPolicy.Remaining(
                               targetAvailable,
                               targetCumulative,
                               current) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();
                        long remaining = NativeRecoveryPolicy.Remaining(
                            targetAvailable,
                            targetCumulative,
                            current);
                        int batch = (int)Math.Min(
                            1000L,
                            Math.Max(1L, remaining));
                        NativePageBatchResult result = NativePageRefill.Boost(
                            client,
                            profile,
                            current,
                            batch,
                            cancellationToken);
                        NativePageSnapshot batchAfter = null;

                        if (!result.Ok)
                        {
                            if (NativeRecoveryPolicy.IsPreflightConflict(result.Error))
                            {
                                if (!NativeRecoveryPolicy.CanRetryPreflight(
                                        preflightFailures))
                                {
                                    throw new InvalidOperationException(
                                        "预检状态连续变化，已达到 " +
                                        NativeRecoveryPolicy.MaxPreflightRetries +
                                        " 次恢复上限：" + result.Error);
                                }

                                preflightFailures++;
                                NativePageSnapshot rebased =
                                    NativePageRefill.ReadSnapshot(
                                        client,
                                        profile,
                                        cancellationToken);
                                if (!NativeRecoveryPolicy.CanRebase(
                                        start,
                                        current,
                                        rebased))
                                {
                                    WriteLog(
                                        "native_batch_failed",
                                        NativeRecoveryPolicy.DescribeDelta(
                                            "preflight-rebase-rejected",
                                            preflightFailures,
                                            current.Available,
                                            current.Cumulative,
                                            current,
                                            rebased));
                                    throw new InvalidOperationException(
                                        "预检冲突后状态无法安全重基准：" +
                                        "账号变化或可用/累计计数倒退。");
                                }
                                WriteLog(
                                    "native_batch_preflight_conflict",
                                    NativeRecoveryPolicy.DescribeDelta(
                                        result.Error,
                                        preflightFailures,
                                        current.Available,
                                        current.Cumulative,
                                        current,
                                        rebased));
                                WriteLog(
                                    "native_batch_rebased",
                                    String.Format(
                                        CultureInfo.InvariantCulture,
                                        "attempt={0}; available={1}; cumulative={2}; " +
                                        "local={3}; remaining={4}",
                                        preflightFailures,
                                        rebased.Available,
                                        rebased.Cumulative,
                                        rebased.Local,
                                        NativeRecoveryPolicy.Remaining(
                                            targetAvailable,
                                            targetCumulative,
                                            rebased)));
                                current = rebased;
                                preflightRecoveries++;
                                SleepWithCancellation(
                                    Math.Min(
                                        800,
                                        200 * preflightFailures),
                                    cancellationToken);
                                continue;
                            }

                            if (NativeRecoveryPolicy.IsIncrementUnconfirmed(
                                    result.Error))
                            {
                                if (!NativeRecoveryPolicy.CanRetryPreflight(
                                        incrementFailures))
                                {
                                    throw new InvalidOperationException(
                                        "增量状态连续变化，已达到 " +
                                        NativeRecoveryPolicy.MaxPreflightRetries +
                                        " 次恢复上限：" + result.Error);
                                }

                                incrementFailures++;
                                NativePageSnapshot observed = result.After;
                                if (observed == null)
                                {
                                    observed = NativePageRefill.ReadSnapshot(
                                        client,
                                        profile,
                                        cancellationToken);
                                }
                                if (!NativeRecoveryPolicy.CanRebase(
                                        start,
                                        current,
                                        observed))
                                {
                                    WriteLog(
                                        "native_batch_failed",
                                        NativeRecoveryPolicy.DescribeDelta(
                                            "increment-rebase-rejected",
                                            incrementFailures,
                                            result.ExpectedAvailable,
                                            result.ExpectedCumulative,
                                            current,
                                            observed));
                                    throw new InvalidOperationException(
                                        "增量中断后状态无法安全重基准：" +
                                        "账号变化或可用/累计计数倒退。");
                                }

                                WriteLog(
                                    "native_batch_increment_conflict",
                                    NativeRecoveryPolicy.DescribeDelta(
                                        result.Error,
                                        incrementFailures,
                                        result.ExpectedAvailable,
                                        result.ExpectedCumulative,
                                        current,
                                        observed));
                                WriteLog(
                                    "native_batch_rebased",
                                    String.Format(
                                        CultureInfo.InvariantCulture,
                                        "attempt={0}; source=increment; " +
                                        "available={1}; cumulative={2}; " +
                                        "local={3}; remaining={4}",
                                        incrementFailures,
                                        observed.Available,
                                        observed.Cumulative,
                                        observed.Local,
                                        NativeRecoveryPolicy.Remaining(
                                            targetAvailable,
                                            targetCumulative,
                                            observed)));
                                current = observed;
                                incrementRecoveries++;
                                SleepWithCancellation(
                                    Math.Min(
                                        800,
                                        200 * incrementFailures),
                                    cancellationToken);
                                continue;
                            }

                            if (NativeRecoveryPolicy.IsPostSaveUnconfirmed(
                                    result.Error))
                            {
                                NativePageSnapshot observed = result.After;
                                NativePageSnapshot lastObserved = null;
                                bool confirmed = false;
                                bool coreChanged = false;
                                for (int poll = 1;
                                     poll <= NativeRecoveryPolicy.PostSavePollCount;
                                     poll++)
                                {
                                    cancellationToken.ThrowIfCancellationRequested();
                                    if (observed == null)
                                    {
                                        observed = NativePageRefill.ReadSnapshot(
                                            client,
                                            profile,
                                        cancellationToken);
                                    }
                                    EnsureSameAccount(start, observed);
                                    lastObserved = observed;
                                    if (!NativeRecoveryPolicy.IsStableNoCoreChange(
                                            current,
                                            observed))
                                    {
                                        coreChanged = true;
                                    }
                                    WriteLog(
                                        "native_post_save_poll",
                                        NativeRecoveryPolicy.DescribeDelta(
                                            result.Error,
                                            poll,
                                            result.ExpectedAvailable,
                                            result.ExpectedCumulative,
                                            current,
                                            observed));
                                    if (NativeRecoveryPolicy.IsBatchConfirmed(
                                            current,
                                            observed,
                                            batch))
                                    {
                                        confirmed = true;
                                        break;
                                    }
                                    observed = null;
                                    if (poll <
                                        NativeRecoveryPolicy.PostSavePollCount)
                                    {
                                        SleepWithCancellation(
                                            NativeRecoveryPolicy
                                                .PostSavePollDelayMilliseconds,
                                            cancellationToken);
                                    }
                                }

                                if (!confirmed)
                                {
                                    if (!coreChanged &&
                                        NativeRecoveryPolicy
                                            .IsStableNoCoreChange(
                                                current,
                                                lastObserved) &&
                                        NativeRecoveryPolicy
                                            .CanRetryPreflight(
                                                postSaveRebaseFailures))
                                    {
                                        postSaveRebaseFailures++;
                                        postSaveRebases++;
                                        WriteLog(
                                            "native_batch_rollback_rebased",
                                            NativeRecoveryPolicy.DescribeDelta(
                                                "post-save-stable-no-core-change",
                                                postSaveRebaseFailures,
                                                result.ExpectedAvailable,
                                                result.ExpectedCumulative,
                                                current,
                                                lastObserved));
                                        current = lastObserved;
                                        SleepWithCancellation(500, cancellationToken);
                                        continue;
                                    }

                                    NativePageSnapshot actual = null;
                                    try
                                    {
                                        actual = NativePageRefill.ReadSnapshot(
                                            client,
                                            profile,
                                            cancellationToken);
                                    }
                                    catch
                                    {
                                    }
                                    WriteLog(
                                        "native_batch_ambiguous",
                                        NativeRecoveryPolicy.DescribeDelta(
                                            "post-save-state-not-confirmed",
                                            NativeRecoveryPolicy.PostSavePollCount,
                                            result.ExpectedAvailable,
                                            result.ExpectedCumulative,
                                            current,
                                            actual ?? result.After));
                                    if (!coreChanged)
                                    {
                                        throw new InvalidOperationException(
                                            "保存后核心计数连续未前进，且已达到 " +
                                            NativeRecoveryPolicy
                                                .MaxPreflightRetries +
                                            " 次安全回滚重基准上限。");
                                    }
                                    throw new InvalidOperationException(
                                        "保存后连续 " +
                                        NativeRecoveryPolicy.PostSavePollCount +
                                        " 次只读复核仍未确认；为避免重复增加，" +
                                        "本批不会重发。");
                                }

                                batchAfter = observed;
                                postSaveRecoveries++;
                            }
                            else
                            {
                                NativePageSnapshot actual = result.After;
                                if (actual == null)
                                {
                                    try
                                    {
                                        actual = NativePageRefill.ReadSnapshot(
                                            client,
                                            profile,
                                            cancellationToken);
                                    }
                                    catch
                                    {
                                    }
                                }
                                WriteLog(
                                    "native_batch_failed",
                                    NativeRecoveryPolicy.DescribeDelta(
                                        String.IsNullOrWhiteSpace(result.Error)
                                            ? "unknown"
                                            : result.Error,
                                        0,
                                        result.ExpectedAvailable,
                                        result.ExpectedCumulative,
                                        current,
                                        actual));
                                throw new InvalidOperationException(
                                    "页面原生计数中止：" +
                                    (String.IsNullOrWhiteSpace(result.Error)
                                        ? "unknown"
                                        : result.Error));
                            }
                        }
                        else
                        {
                            batchAfter = result.After;
                        }

                        if (batchAfter == null)
                        {
                            throw new InvalidOperationException(
                                "页面原生计数已返回，但没有可确认的快照。");
                        }
                        EnsureSameAccount(start, batchAfter);

                        current = batchAfter;
                        sent += batch;
                        preflightFailures = 0;
                        incrementFailures = 0;
                        postSaveRebaseFailures = 0;
                        long rate = stopwatch.Elapsed.TotalSeconds > 0
                            ? (long)((current.Available - reportStartAvailable) /
                                     Math.Max(1L, (long)stopwatch.Elapsed.TotalSeconds))
                            : 0;
                        long confirmedDelta =
                            current.Available - reportStartAvailable;
                        SetStatus(String.Format(
                            "原生进度 {0:N0} / {1:N0}  ·  页面增加 {2:N0}  ·  {3:N0}/秒",
                            current.Available,
                            targetAvailable,
                            confirmedDelta,
                            rate));
                        WriteLog(
                            "native_refill_progress",
                            String.Format(
                                "available={0}; cumulative={1}; local={2}; consumed={3}; " +
                                "confirmed={4}; preflight_recoveries={5}; " +
                                "increment_recoveries={6}; post_save_recoveries={7}; " +
                                "post_save_rebases={8}",
                                current.Available,
                                current.Cumulative,
                                current.Local,
                                current.Consumed,
                                sent,
                                preflightRecoveries,
                                incrementRecoveries,
                                postSaveRecoveries,
                                postSaveRebases));
                    }

                    WriteLog(
                        "native_refill_completed",
                        String.Format(
                            CultureInfo.InvariantCulture,
                            "start_available={0}; start_cumulative={1}; " +
                            "target_available={2}; target_cumulative={3}; " +
                            "actual_available={4}; actual_cumulative={5}; " +
                            "requested_confirmed={6}; preflight_recoveries={7}; " +
                            "increment_recoveries={8}; post_save_recoveries={9}; " +
                            "post_save_rebases={10}; transport_recoveries={11}; " +
                            "reopened_available={12}; elapsed_ms={13}",
                            reportStartAvailable,
                            reportStartCumulative,
                            targetAvailable,
                            targetCumulative,
                            current.Available,
                            current.Cumulative,
                            sent,
                            preflightRecoveries,
                            incrementRecoveries,
                            postSaveRecoveries,
                            postSaveRebases,
                            transportRecoveries,
                            start.Available,
                            stopwatch.ElapsedMilliseconds));
                    finalStatus = String.Format(
                        "原生计数完成：{0:N0} → {1:N0}，页面增加 {2:N0}；" +
                        "预检恢复 {3} 次，增量重基准 {4} 次，" +
                        "保存后复核恢复 {5} 次，保存回滚重基准 {6} 次",
                        reportStartAvailable,
                        current.Available,
                        current.Available - reportStartAvailable,
                        preflightRecoveries,
                        incrementRecoveries,
                        postSaveRecoveries,
                        postSaveRebases);
                }
            }
            catch (OperationCanceledException)
            {
                finalStatus = "已取消本次 CDP 补次数";
            }
            catch (WebSocketException ex)
            {
                if (NativeRecoveryPolicy.CanRecoverSession(transportRecoveries))
                {
                    resumeRequested = true;
                    resumeReason = ex.Message;
                }
                else
                {
                    failure = ex;
                }
            }
            catch (TimeoutException ex)
            {
                if (NativeRecoveryPolicy.CanRecoverSession(transportRecoveries))
                {
                    resumeRequested = true;
                    resumeReason = ex.Message;
                }
                else
                {
                    failure = ex;
                }
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
            if (resumeRequested)
            {
                WriteLog(
                    "native_session_recovery",
                    String.Format(
                        CultureInfo.InvariantCulture,
                        "recovery={0}; max={1}; reason={2}; " +
                        "last_target_available={3}; last_target_cumulative={4}",
                        transportRecoveries + 1,
                        NativeRecoveryPolicy.MaxSessionRecoveries,
                        resumeReason ?? "",
                        targetAvailable,
                        targetCumulative));
                SleepWithCancellation(500, cancellationToken);
                return RunRefillSession(
                    amount,
                    cancellationToken,
                    transportRecoveries + 1,
                    targetAvailable,
                    targetCumulative,
                    taskAccountKey);
            }
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
            HashSet<string> seen = new HashSet<string>(
                StringComparer.OrdinalIgnoreCase);
            string baseDirectory = AppDomain.CurrentDomain.BaseDirectory;
            string portableRoot = SplitParent(baseDirectory);
            const int maxAncestorDepth = 32;
            int depth = 0;
            while (!String.IsNullOrWhiteSpace(portableRoot) &&
                   depth < maxAncestorDepth)
            {
                AddCandidate(
                    candidates,
                    seen,
                    Path.Combine(portableRoot, "WuwaTappo.exe"));
                portableRoot = SplitParent(portableRoot);
                depth++;
            }
            if (!String.IsNullOrWhiteSpace(portableRoot))
            {
                throw new InvalidOperationException(
                    "从便携目录向上查找 WuwaTappo.exe 时超过安全深度，已停止以避免内存耗尽。");
            }
            AddCandidate(candidates, seen, Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "WuwaTappo",
                "WuwaTappo.exe"));
            AddCandidate(candidates, seen, Path.Combine(
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
                        AddCandidate(
                            candidates,
                            seen,
                            process.MainModule.FileName);
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

        private static void AddCandidate(
            List<string> candidates,
            HashSet<string> seen,
            string candidate)
        {
            if (!String.IsNullOrWhiteSpace(candidate) && seen.Add(candidate))
                candidates.Add(candidate);
        }

        internal static string SplitParent(string path)
        {
            if (String.IsNullOrWhiteSpace(path))
                return "";
            string trimmed = path.TrimEnd(
                Path.DirectorySeparatorChar,
                Path.AltDirectorySeparatorChar);
            if (String.IsNullOrWhiteSpace(trimmed))
                return "";

            string root = Path.GetPathRoot(trimmed) ?? "";
            if (!String.IsNullOrWhiteSpace(root) &&
                String.Equals(
                    trimmed,
                    root.TrimEnd(
                        Path.DirectorySeparatorChar,
                        Path.AltDirectorySeparatorChar),
                    StringComparison.OrdinalIgnoreCase))
            {
                return "";
            }

            DirectoryInfo parent = Directory.GetParent(trimmed);
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

        private static int RestartWuwa(string executable, int port)
        {
            List<Process> targets = new List<Process>();
            List<Process> webViews = new List<Process>();
            try
            {
                Process[] existing = Process.GetProcessesByName("WuwaTappo");
                targets.AddRange(existing);
                HashSet<int> webViewIds = FindDescendantProcessIds(
                    "msedgewebview2.exe",
                    existing.Select(process => process.Id));
                foreach (int webViewId in webViewIds)
                {
                    try
                    {
                        webViews.Add(Process.GetProcessById(webViewId));
                    }
                    catch
                    {
                    }
                }
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
                WaitForWebViewExit(webViews, TimeSpan.FromSeconds(10));

                ProcessStartInfo startInfo = new ProcessStartInfo();
                startInfo.FileName = executable;
                startInfo.WorkingDirectory = Path.GetDirectoryName(executable);
                startInfo.UseShellExecute = false;
                startInfo.EnvironmentVariables.Remove("WEBVIEW2_ADDITIONAL_BROWSER_ARGUMENTS");
                if (port > 0)
                {
                    startInfo.Arguments =
                        "--edge-webview-switches=\"" +
                        "--remote-debugging-address=127.0.0.1 " +
                        "--remote-debugging-port=" + port +
                        "\"";
                }
                using (Process started = Process.Start(startInfo))
                {
                    if (started == null)
                        throw new InvalidOperationException("无法启动 Wuwa Tappo。");
                    return started.Id;
                }
            }
            finally
            {
                foreach (Process process in targets)
                    process.Dispose();
                foreach (Process process in webViews)
                    process.Dispose();
            }
        }

        private static void WaitForWebViewExit(
            List<Process> webViews,
            TimeSpan timeout)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                bool anyRunning = false;
                foreach (Process process in webViews)
                {
                    try
                    {
                        if (!process.HasExited)
                            anyRunning = true;
                    }
                    catch
                    {
                    }
                }
                if (!anyRunning)
                    return;
                Thread.Sleep(150);
            }

            foreach (Process process in webViews)
            {
                try
                {
                    if (!process.HasExited)
                        process.Kill();
                }
                catch
                {
                }
            }
            DateTime killDeadline = DateTime.UtcNow.AddSeconds(5);
            while (DateTime.UtcNow < killDeadline)
            {
                bool anyRunning = false;
                foreach (Process process in webViews)
                {
                    try
                    {
                        if (!process.HasExited)
                            anyRunning = true;
                    }
                    catch
                    {
                    }
                }
                if (!anyRunning)
                    return;
                Thread.Sleep(100);
            }
            throw new InvalidOperationException(
                "旧 Wuwa WebView2 子进程未能完全退出，无法安全切换调试参数。");
        }

        private static bool WaitForRemoteDebugStart(
            int appProcessId,
            int port,
            TimeSpan timeout,
            CancellationToken cancellationToken)
        {
            DateTime deadline = DateTime.UtcNow.Add(timeout);
            while (DateTime.UtcNow < deadline)
            {
                cancellationToken.ThrowIfCancellationRequested();
                List<ProcessTableEntry> processes = ReadProcessTable();
                HashSet<int> webViewIds = FindDescendantProcessIds(
                    processes,
                    "msedgewebview2.exe",
                    new[] { appProcessId });
                if (webViewIds.Count > 0 && IsLoopbackPortListening(port))
                    return true;
                foreach (ProcessTableEntry entry in processes)
                {
                    if (webViewIds.Contains(entry.ProcessId) &&
                        (entry.CommandLine ?? "").IndexOf(
                            "--remote-debugging-port=" + port,
                            StringComparison.OrdinalIgnoreCase) >= 0)
                    {
                        return true;
                    }
                }
                SleepWithCancellation(250, cancellationToken);
            }
            return false;
        }

        private static bool IsLoopbackPortListening(int port)
        {
            using (TcpClient client = new TcpClient())
            {
                try
                {
                    IAsyncResult result = client.BeginConnect(
                        IPAddress.Loopback,
                        port,
                        null,
                        null);
                    if (!result.AsyncWaitHandle.WaitOne(150))
                        return false;
                    client.EndConnect(result);
                    return true;
                }
                catch
                {
                    return false;
                }
            }
        }

        private static HashSet<int> FindDescendantProcessIds(
            string childProcessName,
            IEnumerable<int> rootProcessIds)
        {
            return FindDescendantProcessIds(
                ReadProcessTable(),
                childProcessName,
                rootProcessIds);
        }

        private static HashSet<int> FindDescendantProcessIds(
            List<ProcessTableEntry> processes,
            string childProcessName,
            IEnumerable<int> rootProcessIds)
        {
            HashSet<int> descendants = new HashSet<int>(rootProcessIds);
            bool changed;
            do
            {
                changed = false;
                foreach (ProcessTableEntry entry in processes)
                {
                    if (descendants.Contains(entry.ParentProcessId) &&
                        descendants.Add(entry.ProcessId))
                    {
                        changed = true;
                    }
                }
            }
            while (changed);

            HashSet<int> matches = new HashSet<int>();
            foreach (ProcessTableEntry entry in processes)
            {
                if (String.Equals(
                        entry.Name,
                        childProcessName,
                        StringComparison.OrdinalIgnoreCase) &&
                    descendants.Contains(entry.ProcessId))
                {
                    matches.Add(entry.ProcessId);
                }
            }
            return matches;
        }

        private static List<ProcessTableEntry> ReadProcessTable()
        {
            List<ProcessTableEntry> entries = new List<ProcessTableEntry>();
            using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                "SELECT ProcessId,ParentProcessId,Name,CommandLine FROM Win32_Process"))
            using (ManagementObjectCollection results = searcher.Get())
            {
                foreach (ManagementObject item in results)
                {
                    using (item)
                    {
                        entries.Add(new ProcessTableEntry {
                            ProcessId = Convert.ToInt32(
                                item["ProcessId"],
                                CultureInfo.InvariantCulture),
                            ParentProcessId = Convert.ToInt32(
                                item["ParentProcessId"],
                                CultureInfo.InvariantCulture),
                            Name = Convert.ToString(
                                item["Name"],
                                CultureInfo.InvariantCulture),
                            CommandLine = Convert.ToString(
                                item["CommandLine"],
                                CultureInfo.InvariantCulture)
                        });
                    }
                }
            }
            return entries;
        }

        private static NativePageProfile WaitForNativeProfile(
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
                    return NativePageRefill.Discover(client, cancellationToken);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                SleepWithCancellation(400, cancellationToken);
            }
            throw new InvalidOperationException(
                "页面原生计数能力未就绪。" +
                (last == null ? "" : " " + last.Message));
        }

        private static NativePageSnapshot WaitForNativeSnapshot(
            CdpClient client,
            NativePageProfile profile,
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
                    return NativePageRefill.ReadSnapshot(
                        client,
                        profile,
                        cancellationToken);
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                SleepWithCancellation(400, cancellationToken);
            }
            throw new InvalidOperationException(
                "页面登录或计数状态未就绪。" +
                (last == null ? "" : " " + last.Message));
        }

        private static void EnsureSameAccount(
            NativePageSnapshot start,
            NativePageSnapshot current)
        {
            if (NativeRecoveryPolicy.IsAccountChanged(start, current))
            {
                throw new InvalidOperationException(
                    "页面登录状态或账号摘要发生变化，已停止补数。");
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
                                !IsTauriMainPage(url) ||
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

        private static bool IsTauriMainPage(string value)
        {
            Uri uri;
            if (!Uri.TryCreate(value, UriKind.Absolute, out uri) ||
                !String.Equals(
                    uri.Host,
                    "tauri.localhost",
                    StringComparison.OrdinalIgnoreCase))
                return false;
            string path = uri.AbsolutePath;
            if (path != "/" &&
                !String.Equals(path, "/index.html", StringComparison.OrdinalIgnoreCase))
                return false;
            string fragment = uri.Fragment ?? "";
            return fragment.Length == 0 || fragment == "#" || fragment == "#/";
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

        private sealed class ProcessTableEntry
        {
            public int ProcessId;
            public int ParentProcessId;
            public string Name;
            public string CommandLine;
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

        public Task<Dictionary<string, object>> EvaluateAwaitAsync(
            string expression,
            CancellationToken cancellationToken)
        {
            return EvaluateAsync(expression, true, cancellationToken);
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
                    timeout.CancelAfter(TimeSpan.FromSeconds(45));
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
