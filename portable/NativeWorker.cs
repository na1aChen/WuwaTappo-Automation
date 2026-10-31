using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Management;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Automation;

namespace WuwaTappoPortable
{
    internal sealed class NativeWorkerConfig
    {
        public string ProcessName = "WuwaTappo";
        public string WindowTitle = "Wuwa Tappo";
        public int ProcessId;
        public string ExecutablePath = "";
        public string WebViewLocalStorageDirectory = "";
        public int PollIntervalSeconds = 1;
        public int IdlePollIntervalSeconds = 5;
        public int ConfirmationPollMilliseconds = 250;
        public int ConfirmationTimeoutSeconds = 20;
        public int ActionCooldownSeconds = 3;
        public int UnknownOutcomeCooldownSeconds = 900;
        public int ActionSettleMilliseconds = 500;
        public int MaxOpensPerCycle = 2;
        public double MaxRuntimeMinutes;
        public bool ActivityEnabled;
        public int ActivityVirtualKey = 0x87;
        public int ActivityIntervalMilliseconds;
        public int ActivityBurstSize = 1000;
        public long CharacterThreshold = 10000;

        public static NativeWorkerConfig Load(string path)
        {
            Dictionary<string, object> root = JsonUtil.ParseObject(path);
            if (root == null)
                throw new InvalidDataException("无法解析配置文件：" + path);

            NativeWorkerConfig config = new NativeWorkerConfig();
            Dictionary<string, object> app = JsonUtil.GetDictionary(root, "app");
            Dictionary<string, object> automation = JsonUtil.GetDictionary(root, "automation");
            Dictionary<string, object> activity = JsonUtil.GetDictionary(root, "activity");

            config.ProcessName = JsonUtil.GetString(app, "processName", config.ProcessName);
            config.WindowTitle = JsonUtil.GetString(app, "windowTitle", config.WindowTitle);
            config.ProcessId = Math.Max(0, JsonUtil.GetInt(app, "processId", config.ProcessId));
            config.ExecutablePath = Environment.ExpandEnvironmentVariables(
                JsonUtil.GetString(app, "executablePath", config.ExecutablePath));
            config.WebViewLocalStorageDirectory = Environment.ExpandEnvironmentVariables(
                JsonUtil.GetString(app, "webViewLocalStorageDirectory", config.WebViewLocalStorageDirectory));
            config.PollIntervalSeconds = JsonUtil.GetInt(automation, "pollIntervalSeconds", config.PollIntervalSeconds);
            config.IdlePollIntervalSeconds = JsonUtil.GetInt(automation, "idlePollIntervalSeconds", config.IdlePollIntervalSeconds);
            config.ConfirmationPollMilliseconds = JsonUtil.GetInt(automation, "confirmationPollMilliseconds", config.ConfirmationPollMilliseconds);
            config.ConfirmationTimeoutSeconds = JsonUtil.GetInt(automation, "actionConfirmationTimeoutSeconds", config.ConfirmationTimeoutSeconds);
            config.ActionCooldownSeconds = JsonUtil.GetInt(automation, "actionCooldownSeconds", config.ActionCooldownSeconds);
            config.UnknownOutcomeCooldownSeconds = JsonUtil.GetInt(automation, "unknownOutcomeCooldownSeconds", config.UnknownOutcomeCooldownSeconds);
            config.ActionSettleMilliseconds = JsonUtil.GetInt(automation, "actionSettleMilliseconds", config.ActionSettleMilliseconds);
            config.MaxOpensPerCycle = Math.Max(1, JsonUtil.GetInt(automation, "maxOpensPerCycle", config.MaxOpensPerCycle));
            config.MaxRuntimeMinutes = JsonUtil.GetDouble(automation, "maxRuntimeMinutes", config.MaxRuntimeMinutes);
            config.ActivityEnabled = JsonUtil.GetBool(activity, "enabled", config.ActivityEnabled);
            config.ActivityVirtualKey = JsonUtil.GetInt(activity, "virtualKey", config.ActivityVirtualKey);
            config.ActivityIntervalMilliseconds = Math.Max(0, JsonUtil.GetInt(activity, "intervalMilliseconds", config.ActivityIntervalMilliseconds));
            config.ActivityBurstSize = Math.Max(1, JsonUtil.GetInt(activity, "burstSize", config.ActivityBurstSize));
            config.CharacterThreshold = Math.Max(1, JsonUtil.GetLong(activity, "characterThreshold", config.CharacterThreshold));
            return config;
        }
    }

    internal static class JsonUtil
    {
        private static readonly JavaScriptSerializer Serializer = new JavaScriptSerializer();

        public static Dictionary<string, object> ParseObject(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                string text = File.ReadAllText(path, Encoding.UTF8);
                return Serializer.Deserialize<Dictionary<string, object>>(text);
            }
            catch
            {
                return null;
            }
        }

        public static Dictionary<string, object> GetDictionary(
            Dictionary<string, object> parent,
            string key)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value))
                return value as Dictionary<string, object>;
            return new Dictionary<string, object>();
        }

        public static string GetString(
            Dictionary<string, object> parent,
            string key,
            string fallback)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value) && value != null)
                return Convert.ToString(value);
            return fallback;
        }

        public static int GetInt(
            Dictionary<string, object> parent,
            string key,
            int fallback)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value) && value != null)
            {
                int result;
                if (Int32.TryParse(Convert.ToString(value), out result))
                    return result;
            }
            return fallback;
        }

        public static long GetLong(
            Dictionary<string, object> parent,
            string key,
            long fallback)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value) && value != null)
            {
                long result;
                if (Int64.TryParse(Convert.ToString(value), out result))
                    return result;
            }
            return fallback;
        }

        public static double GetDouble(
            Dictionary<string, object> parent,
            string key,
            double fallback)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value) && value != null)
            {
                double result;
                if (Double.TryParse(Convert.ToString(value), out result))
                    return result;
            }
            return fallback;
        }

        public static bool GetBool(
            Dictionary<string, object> parent,
            string key,
            bool fallback)
        {
            object value;
            if (parent != null && parent.TryGetValue(key, out value) && value != null)
            {
                bool result;
                if (Boolean.TryParse(Convert.ToString(value), out result))
                    return result;
            }
            return fallback;
        }

        public static string ToJson(object value)
        {
            return Serializer.Serialize(value);
        }
    }

    internal sealed class NativeWorker
    {
        private NativeWorkerConfig config;
        private readonly string runtimeDirectory;
        private readonly string stateDirectory;
        private readonly string logDirectory;
        private readonly string configPath;
        private readonly string statusPath;
        private readonly string operationStatePath;
        private readonly string historyPath;
        private readonly string refillRequestPath;
        private readonly string pidPath;
        private readonly string stopPath;
        private readonly string lockPath;
        private FileStream lockStream;
        private DateTimeOffset startedAt;
        private bool activityEnabled;
        private long activityKeysSent;
        private string activityLastResult = "";
        private readonly Dictionary<string, DateTimeOffset> sideCooldowns =
            new Dictionary<string, DateTimeOffset>(StringComparer.OrdinalIgnoreCase);
        private readonly List<object> operationHistory = new List<object>();
        private Dictionary<string, string> assetNameLookup;
        private Dictionary<string, string> assetIdLookup;
        private Dictionary<string, string> imageHashLookup;
        private readonly Dictionary<string, string> rewardLabelLookup =
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        private string webViewLocalStorageDirectory;
        private string webViewCacheDirectory;
        private Dictionary<string, object> lastAction;
        private int preferredTargetProcessId;
        private long? cachedCharacterCount;
        private DateTimeOffset cachedCharacterCountAt;
        private int cachedCharacterCountProcessId;
        private string lastStatusFingerprint = "";
        private DateTimeOffset lastStatusWriteAt;
        private string manualRequestId = "";
        private string manualRequestStatus = "";
        private long manualRequestedIncrement;
        private long manualBaseCount;
        private long manualTargetCount;
        private long manualLastCount;
        private long manualSentCount;
        private string manualError = "";
        private bool manualModeEngaged;
        private DateTimeOffset manualRefillStartedAt;
        private DateTimeOffset manualRefillLastProgressLogAt;
        private long manualRefillLastLoggedSent;

        public NativeWorker()
        {
            runtimeDirectory = PortableRuntime.RuntimeDirectory;
            stateDirectory = Path.Combine(runtimeDirectory, "state");
            logDirectory = Path.Combine(runtimeDirectory, "logs");
            configPath = PortableRuntime.ConfigPath;
            statusPath = PortableRuntime.StatusFile;
            operationStatePath = Path.Combine(stateDirectory, "operation-state.json");
            historyPath = Path.Combine(stateDirectory, "history.jsonl");
            refillRequestPath = Path.Combine(stateDirectory, "refill-request.json");
            pidPath = PortableRuntime.PidFile;
            stopPath = Path.Combine(stateDirectory, "stop.requested");
            lockPath = Path.Combine(stateDirectory, "worker.lock");
        }

        public int Run()
        {
            Directory.CreateDirectory(stateDirectory);
            Directory.CreateDirectory(logDirectory);
            config = NativeWorkerConfig.Load(configPath);
            activityEnabled = config.ActivityEnabled;
            preferredTargetProcessId = config.ProcessId;
            webViewLocalStorageDirectory = ResolveWebViewLocalStorageDirectory();
            webViewCacheDirectory = ResolveWebViewCacheDirectory();
            LoadAssetNameLookup();
            LoadAssetImageLookup();
            startedAt = DateTimeOffset.Now;

            try
            {
                lockStream = new FileStream(
                    lockPath,
                    FileMode.OpenOrCreate,
                    FileAccess.ReadWrite,
                    FileShare.None);
            }
            catch
            {
                return 0;
            }

            try
            {
                WritePidFile();
                RestoreOperationState();
                Log("INFO", "worker_started", new Dictionary<string, object> {
                    {"activity_enabled", activityEnabled},
                    {"config_path", configPath},
                    {"runtime", "native-csharp"}
                });

                DateTimeOffset deadline = config.MaxRuntimeMinutes > 0
                    ? startedAt.AddMinutes(config.MaxRuntimeMinutes)
                    : DateTimeOffset.MaxValue;
                string lastAppState = null;

                while (!File.Exists(stopPath) && DateTimeOffset.Now < deadline)
                {
                    TargetInfo target = FindTarget();
                    if (target == null)
                    {
                        SetStatus(
                            "blocked",
                            "启动 Wuwa Tappo 或检查主窗口标题",
                            "app_window_not_found",
                            null,
                            false,
                            null,
                            0,
                            new List<object>());
                        if (lastAppState != "missing")
                            Log("WARN", "app_window_not_found", new Dictionary<string, object>());
                        lastAppState = "missing";
                        if (!WaitWithStop(config.IdlePollIntervalSeconds * 1000))
                            break;
                        continue;
                    }

                    lastAppState = "found";
                    RefreshManualRefillState();
                    List<RewardButtonInfo> buttons = FindRewardButtons(target.Root);
                    long? characterCount = ReadCharacterCountCached(target);
                    bool manualRefillActive =
                        manualRequestStatus == "pending" ||
                        manualRequestStatus == "running";
                    string phase = manualRefillActive
                        ? "manual_refilling"
                        : (buttons.Count == 0 ? "waiting_for_chest" : "candidate_detected");
                    string message = buttons.Count == 0
                        ? "当前未检测到可开启宝箱"
                        : "检测到宝箱，等待按钮可点击";
                    if (manualRefillActive)
                    {
                        message = manualTargetCount > 0
                            ? String.Format(
                                "一次性补足：{0} / {1}",
                                manualLastCount,
                                manualTargetCount)
                            : "一次性补足请求已接收";
                    }
                    if (buttons.Count == 0 &&
                        !manualRefillActive &&
                        activityEnabled &&
                        characterCount.HasValue &&
                        characterCount.Value < config.CharacterThreshold)
                    {
                        message = String.Format(
                            "正在补足字符数：{0} / {1}",
                            characterCount.Value,
                            config.CharacterThreshold);
                    }
                    SetStatus(
                        phase,
                        message,
                        null,
                        target,
                        true,
                        characterCount,
                        buttons.Count,
                        BuildButtonStatus(buttons));

                    if (buttons.Count == 0)
                    {
                        if (manualRefillActive)
                        {
                            if (!characterCount.HasValue)
                            {
                                UpdateManualRefillState(
                                    "failed",
                                    manualLastCount,
                                    manualSentCount,
                                    "character_count_unavailable");
                                activityLastResult = "manual_character_count_unavailable";
                                continue;
                            }
                            if (manualTargetCount <= 0)
                            {
                                long? freshCount = FindCharacterCount(target.Root);
                                if (freshCount.HasValue)
                                {
                                    characterCount = freshCount;
                                    cachedCharacterCount = freshCount;
                                    cachedCharacterCountAt = DateTimeOffset.Now;
                                    cachedCharacterCountProcessId = target.ProcessId;
                                }
                                manualBaseCount = characterCount.Value;
                                manualTargetCount = manualBaseCount + manualRequestedIncrement;
                                manualLastCount = manualBaseCount;
                                manualSentCount = 0;
                                UpdateManualRefillState("running", manualLastCount, manualSentCount, "");
                            }
                            if (characterCount.Value >= manualTargetCount)
                            {
                                UpdateManualRefillState(
                                    "completed",
                                    characterCount.Value,
                                    manualSentCount,
                                    "");
                                activityLastResult = "manual_refill_completed";
                                continue;
                            }

                            ActivityResult manualResult = RunActivityBurst(manualTargetCount);
                            characterCount = manualResult.CountAfter;
                            manualSentCount += manualResult.PressesSent;
                            if (characterCount.HasValue)
                                manualLastCount = characterCount.Value;
                            if (manualResult.InjectionFailed)
                            {
                                UpdateManualRefillState(
                                    "failed",
                                    manualLastCount,
                                    manualSentCount,
                                    String.IsNullOrWhiteSpace(manualResult.ErrorMessage)
                                        ? "input_injection_failed"
                                        : manualResult.ErrorMessage);
                            }
                            else if (manualResult.Reason == "input_not_counted")
                            {
                                UpdateManualRefillState(
                                    "needs_focus",
                                    manualLastCount,
                                    manualSentCount,
                                    "input_not_counted");
                            }
                            else if (manualLastCount >= manualTargetCount)
                            {
                                UpdateManualRefillState(
                                    "completed",
                                    manualLastCount,
                                    manualSentCount,
                                    "");
                            }
                            else
                            {
                                UpdateManualRefillState(
                                    "running",
                                    manualLastCount,
                                    manualSentCount,
                                    "");
                            }
                            activityLastResult = manualResult.Reason;
                            continue;
                        }

                        if (!manualModeEngaged &&
                            activityEnabled &&
                            characterCount.HasValue &&
                            characterCount.Value >= config.CharacterThreshold)
                        {
                            activityLastResult = "character_threshold_reached";
                            SetStatus(
                                "waiting_for_chest",
                                String.Format(
                                    "字符数已达到 {0} 目标，自动计数暂停",
                                    config.CharacterThreshold),
                                null,
                                target,
                                true,
                                characterCount,
                                0,
                                new List<object>());
                            if (!WaitWithStop(config.IdlePollIntervalSeconds * 1000))
                                break;
                            continue;
                        }

                        if (activityEnabled && !manualModeEngaged)
                        {
                            ActivityResult result = RunActivityBurst(config.CharacterThreshold);
                            characterCount = result.CountAfter;
                            if (result.InjectionFailed)
                            {
                                activityEnabled = false;
                                activityLastResult = "input_injection_failed";
                                SetStatus(
                                    "blocked",
                                    "Windows 输入注入失败，自动计数已停用",
                                    "activity_disabled",
                                    target,
                                    true,
                                    characterCount,
                                    0,
                                    new List<object>());
                            }
                            else if (result.Reason == "input_not_counted")
                            {
                                activityEnabled = false;
                                activityLastResult = result.Reason;
                                SetStatus(
                                    "blocked",
                                    "自动输入没有增加字符数，补次数已停用",
                                    "activity_disabled",
                                    target,
                                    true,
                                    characterCount,
                                    0,
                                    new List<object>());
                            }
                            else
                            {
                                activityLastResult = result.Reason;
                            }
                        }
                        else
                        {
                            if (!WaitWithStop(config.IdlePollIntervalSeconds * 1000))
                                break;
                        }
                        continue;
                    }

                    if (ReadRewardPopupInfo(target.Root) != null)
                    {
                        SetStatus(
                            "cooldown",
                            "奖励弹窗尚未结束，已延后下一次操作",
                            null,
                            target,
                            true,
                            characterCount,
                            buttons.Count,
                            BuildButtonStatus(buttons));
                        if (!WaitWithStop(config.ActionSettleMilliseconds > 0 ? config.ActionSettleMilliseconds : 1000))
                            break;
                        continue;
                    }

                    int openedThisCycle = 0;
                    for (int index = 0; index < config.MaxOpensPerCycle; index++)
                    {
                        target = FindTarget();
                        if (target == null)
                            break;
                        buttons = FindRewardButtons(target.Root);
                        RewardButtonInfo button = buttons.FirstOrDefault(delegate(RewardButtonInfo item) {
                            return item.Enabled &&
                                !item.Offscreen &&
                                item.Side != "ambiguous" &&
                                IsActionAllowed(item.Side);
                        });
                        if (button == null)
                            break;

                        bool confirmed = ExecuteChestOpen(target, button);
                        if (!confirmed)
                            break;
                        openedThisCycle++;
                        if (index + 1 < config.MaxOpensPerCycle && config.ActionSettleMilliseconds > 0)
                        {
                            if (!WaitWithStop(config.ActionSettleMilliseconds))
                                break;
                        }
                    }

                    if (!WaitWithStop(config.PollIntervalSeconds * 1000))
                        break;
                }
            }
            catch (Exception ex)
            {
                Log("ERROR", "worker_crashed", new Dictionary<string, object> {
                    {"reason", ex.GetType().Name},
                    {"message", ex.Message}
                });
                SetStatus("blocked", ex.Message, "worker_crashed", null, false, null, 0, new List<object>());
                return 1;
            }
            finally
            {
                SetStatus("stopped", "后台任务已停止", null, null, false, null, 0, new List<object>());
                Log("INFO", "worker_stopped", new Dictionary<string, object>());
                try
                {
                    if (File.Exists(pidPath))
                        File.Delete(pidPath);
                }
                catch
                {
                }
                if (lockStream != null)
                {
                    lockStream.Dispose();
                    lockStream = null;
                }
            }
            return 0;
        }

        private bool IsActionAllowed(string side)
        {
            DateTimeOffset until;
            return !sideCooldowns.TryGetValue(side, out until) || until <= DateTimeOffset.Now;
        }

        private bool ExecuteChestOpen(TargetInfo target, RewardButtonInfo button)
        {
            string operationId = Guid.NewGuid().ToString("N");
            Dictionary<string, object> pending = new Dictionary<string, object> {
                {"operation_id", operationId},
                {"side", button.Side},
                {"button_class", button.ClassName},
                {"button_rect", button.RectText},
                {"detected_at", DateTimeOffset.Now.ToString("o")},
                {"attempted_at", null},
                {"confirmed_at", null},
                {"status", "submitting"},
                {"evidence", null}
            };
            WriteOperationState(pending);
            Log("INFO", "chest_open_submitting", new Dictionary<string, object> {
                {"operation_id", operationId},
                {"side", button.Side},
                {"button_class", button.ClassName},
                {"button_rect", button.RectText}
            });
            SetStatus(
                "submitting",
                "正在开启宝箱",
                null,
                target,
                true,
                FindCharacterCount(target.Root),
                FindRewardButtons(target.Root).Count,
                new List<object>());

            string evidence = "confirmation_timeout";
            RewardPopupInfo popupInfo = null;
            bool confirmed = false;
            HashSet<int> newAssetIdsBefore = ReadLatestNewAssetIds();
            Dictionary<string, string> cacheBefore = SnapshotRewardCache();
            DateTime cacheWatchStarted = DateTime.UtcNow.AddSeconds(-1);
            try
            {
                InvokePattern invoke = (InvokePattern)button.Element.GetCurrentPattern(InvokePattern.Pattern);
                pending["attempted_at"] = DateTimeOffset.Now.ToString("o");
                WriteOperationState(pending);
                SetStatus(
                    "confirming",
                    "正在确认开箱结果",
                    null,
                    target,
                    true,
                    FindCharacterCount(target.Root),
                    0,
                    new List<object>());
                invoke.Invoke();

                DateTimeOffset deadline = DateTimeOffset.Now.AddSeconds(config.ConfirmationTimeoutSeconds);
                while (DateTimeOffset.Now < deadline && !File.Exists(stopPath))
                {
                    Thread.Sleep(config.ConfirmationPollMilliseconds);
                    TargetInfo currentTarget = FindTarget();
                    if (currentTarget == null)
                    {
                        evidence = "target_window_disappeared";
                        break;
                    }
                    popupInfo = ReadRewardPopupInfo(currentTarget.Root);
                    if (popupInfo != null)
                    {
                        confirmed = true;
                        evidence = "reward_popup";
                        break;
                    }
                    List<RewardButtonInfo> currentButtons = FindRewardButtons(currentTarget.Root);
                    bool stillPresent = currentButtons.Any(delegate(RewardButtonInfo item) {
                        return button.Side == "single" ? false : item.Side == button.Side;
                    });
                    if (!stillPresent)
                    {
                        confirmed = true;
                        evidence = button.Side == "single"
                            ? "single_button_removed"
                            : "side_button_removed";
                        popupInfo = WaitForRewardPopupInfo(
                            currentTarget.Root,
                            Math.Min(4000, config.ConfirmationTimeoutSeconds * 1000));
                        break;
                    }
                }
            }
            catch
            {
                evidence = "invoke_failed";
            }

            DateTimeOffset cooldownUntil = DateTimeOffset.Now.AddSeconds(
                confirmed ? config.ActionCooldownSeconds : config.UnknownOutcomeCooldownSeconds);
            sideCooldowns[button.Side] = cooldownUntil;
            pending["status"] = confirmed ? "confirmed" : "unknown";
            pending["confirmed_at"] = DateTimeOffset.Now.ToString("o");
            pending["evidence"] = evidence;
            int rewardAssetId = confirmed
                ? WaitForNewRewardAssetId(newAssetIdsBefore, 2500)
                : 0;
            string cacheItemName = confirmed
                ? WaitForRewardCacheItemName(cacheBefore, cacheWatchStarted, 3000)
                : "";
            string itemSource;
            string itemName = ResolveRewardItemName(
                popupInfo,
                rewardAssetId,
                cacheItemName,
                out itemSource);
            pending["item_name"] = itemName;
            pending["item_source"] = itemSource;
            pending["asset_id"] = rewardAssetId > 0 ? (object)rewardAssetId : null;
            pending["popup_label"] = popupInfo == null ? "" : popupInfo.Label;
            AddOperation(pending);
            lastAction = new Dictionary<string, object> {
                {"operation_id", operationId},
                {"side", button.Side},
                {"status", confirmed ? "confirmed" : "unknown"},
                {"evidence", evidence},
                {"item_name", itemName},
                {"item_source", itemSource},
                {"asset_id", rewardAssetId > 0 ? (object)rewardAssetId : null},
                {"time", DateTimeOffset.Now.ToString("o")}
            };
            SetStatus(
                confirmed ? "cooldown" : "blocked",
                confirmed
                    ? (String.IsNullOrWhiteSpace(itemName)
                        ? "已开启宝箱，但应用未提供具体物品名称"
                        : "已开启：" + itemName)
                    : "开箱结果未知，不会立即重试",
                confirmed ? null : "operation_unknown",
                target,
                true,
                FindCharacterCount(target.Root),
                FindRewardButtons(target.Root).Count,
                new List<object>());
            Log(confirmed ? "INFO" : "WARN",
                confirmed ? "chest_open_confirmed" : "chest_open_unconfirmed",
                new Dictionary<string, object> {
                    {"operation_id", operationId},
                    {"side", button.Side},
                    {"evidence", evidence},
                    {"item_name", itemName},
                    {"item_source", itemSource},
                    {"asset_id", rewardAssetId > 0 ? (object)rewardAssetId : null}
                });
            AppendHistory(new Dictionary<string, object> {
                {"time", DateTimeOffset.Now.ToString("o")},
                {"operation_id", operationId},
                {"side", button.Side},
                {"item_name", itemName},
                {"item_source", itemSource},
                {"asset_id", rewardAssetId > 0 ? (object)rewardAssetId : null},
                {"popup_label", popupInfo == null ? "" : popupInfo.Label},
                {"status", confirmed ? "confirmed" : "unknown"},
                {"result", confirmed ? "opened" : "unknown"},
                {"evidence", evidence}
            });
            return confirmed;
        }

        private ActivityResult RunActivityBurst(long stopAtCount)
        {
            ActivityResult result = new ActivityResult();
            TargetInfo target = FindTarget();
            if (target == null)
            {
                result.Reason = "app_window_not_found";
                return result;
            }
            result.CountBefore = FindCharacterCount(target.Root);
            if (!result.CountBefore.HasValue)
            {
                result.Reason = "character_count_unavailable";
                return result;
            }

            int remaining = config.ActivityBurstSize;
            long? lastObserved = result.CountBefore;
            int recoveryAttempts = 0;
            while (remaining > 0)
            {
                if (File.Exists(stopPath))
                {
                    result.Reason = "stop_requested";
                    break;
                }
                int batchSize = Math.Min(500, remaining);
                if (stopAtCount > 0 && lastObserved.HasValue)
                {
                    long needed = Math.Max(1, stopAtCount - lastObserved.Value);
                    batchSize = (int)Math.Min(batchSize, needed);
                }
                try
                {
                    NativeInput.SendKeys((byte)config.ActivityVirtualKey, batchSize);
                    result.PressesSent += batchSize;
                    activityKeysSent += batchSize;
                    remaining -= batchSize;
                    long? observedCount;
                    if (!WaitForCharacterProgress(
                        target.Root,
                        lastObserved,
                        1200,
                        out observedCount))
                    {
                        if (File.Exists(stopPath))
                        {
                            result.Reason = "stop_requested";
                            break;
                        }

                        bool recovered = false;
                        while (recoveryAttempts < 2 && !File.Exists(stopPath))
                        {
                            string recoveryError = "";
                            if (!TryActivatePetDock(target.Root, out recoveryError))
                            {
                                Log("WARN", "activity_uia_recovery_failed", new Dictionary<string, object> {
                                    {"error", recoveryError}
                                });
                                break;
                            }
                            recoveryAttempts++;
                            activityLastResult = "uia_recovery";
                            Log("WARN", "activity_uia_recovery", new Dictionary<string, object> {
                                {"attempt", recoveryAttempts},
                                {"count", lastObserved},
                                {"sent", result.PressesSent}
                            });
                            if (WaitForCharacterProgress(
                                target.Root,
                                lastObserved,
                                2000,
                                out observedCount))
                            {
                                lastObserved = observedCount;
                                cachedCharacterCount = observedCount;
                                cachedCharacterCountAt = DateTimeOffset.Now;
                                cachedCharacterCountProcessId = target.ProcessId;
                                recovered = true;
                                break;
                            }
                            if (!WaitWithStop(250))
                                break;
                        }

                        if (recovered)
                            continue;
                        result.Reason = File.Exists(stopPath)
                            ? "stop_requested"
                            : "input_not_counted";
                        break;
                    }
                    if (observedCount.HasValue)
                    {
                        lastObserved = observedCount;
                        cachedCharacterCount = observedCount;
                        cachedCharacterCountAt = DateTimeOffset.Now;
                        cachedCharacterCountProcessId = target.ProcessId;
                        if (stopAtCount > 0 && observedCount.Value >= stopAtCount)
                            break;
                    }
                }
                catch (Exception ex)
                {
                    result.InjectionFailed = true;
                    result.ErrorMessage = ex.Message;
                    result.Reason = "input_injection_failed";
                    break;
                }
                if (config.ActivityIntervalMilliseconds > 0 &&
                    !WaitWithStop(config.ActivityIntervalMilliseconds))
                {
                    result.Reason = "stop_requested";
                    break;
                }
            }
            result.Reason = String.IsNullOrEmpty(result.Reason) ? "pulse_complete" : result.Reason;
            target = FindTarget();
            if (target != null)
            {
                if (FindRewardButtons(target.Root).Count > 0)
                    result.Reason = "reward_ready";
                result.CountAfter = lastObserved ?? FindCharacterCount(target.Root);
                if (result.CountAfter.HasValue)
                {
                    cachedCharacterCount = result.CountAfter;
                    cachedCharacterCountAt = DateTimeOffset.Now;
                    cachedCharacterCountProcessId = target.ProcessId;
                }
            }
            if (!result.InjectionFailed &&
                result.Reason != "reward_ready" &&
                result.Reason != "stop_requested" &&
                result.CountBefore.HasValue &&
                result.CountAfter.HasValue &&
                result.CountAfter.Value <= result.CountBefore.Value)
            {
                result.Reason = "input_not_counted";
            }
            return result;
        }

        private static bool TryActivatePetDock(
            AutomationElement root,
            out string error)
        {
            error = "";
            try
            {
                PropertyCondition condition = new PropertyCondition(
                    AutomationElement.ClassNameProperty,
                    "pet-dock-content");
                AutomationElement dock = root.FindFirst(
                    TreeScope.Descendants,
                    condition);
                if (dock == null)
                {
                    error = "pet_dock_not_found";
                    return false;
                }

                InvokePattern invoke = dock.GetCurrentPattern(
                    InvokePattern.Pattern) as InvokePattern;
                if (invoke == null)
                {
                    error = "pet_dock_invoke_unavailable";
                    return false;
                }
                invoke.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private bool WaitForCharacterProgress(
            AutomationElement root,
            long? previousCount,
            int timeoutMilliseconds,
            out long? latestCount)
        {
            latestCount = previousCount;
            DateTime deadline = DateTime.Now.AddMilliseconds(Math.Max(50, timeoutMilliseconds));
            long stableValue = previousCount.HasValue ? previousCount.Value : -1;
            int stableReads = 0;
            while (DateTime.Now < deadline)
            {
                if (!WaitWithStop(5))
                    return false;
                long? observed = FindCharacterCount(root);
                if (!observed.HasValue)
                    continue;
                if (previousCount.HasValue && observed.Value <= previousCount.Value)
                    continue;
                latestCount = observed;
                if (observed.Value == stableValue)
                    stableReads++;
                else
                {
                    stableValue = observed.Value;
                    stableReads = 0;
                }
                if (stableReads >= 0)
                    return true;
            }
            return false;
        }

        private TargetInfo FindTarget()
        {
            Process[] processes = Process.GetProcessesByName(config.ProcessName);
            List<Process> candidates = processes
                .Where(delegate(Process process) {
                    try { return process.MainWindowHandle != IntPtr.Zero; }
                    catch { return false; }
                })
                .ToList();
            Process selected = null;
            if (preferredTargetProcessId > 0)
            {
                selected = candidates.FirstOrDefault(delegate(Process process) {
                    return process.Id == preferredTargetProcessId;
                });
            }

            if (selected == null && !String.IsNullOrWhiteSpace(config.ExecutablePath))
            {
                string preferredPath = config.ExecutablePath;
                try { preferredPath = Path.GetFullPath(preferredPath); } catch { }
                selected = candidates.FirstOrDefault(delegate(Process process) {
                    try
                    {
                        return String.Equals(
                            Path.GetFullPath(process.MainModule.FileName),
                            preferredPath,
                            StringComparison.OrdinalIgnoreCase);
                    }
                    catch
                    {
                        return false;
                    }
                });
            }

            if (selected == null &&
                candidates.Count == 1 &&
                String.IsNullOrWhiteSpace(config.ExecutablePath))
            {
                selected = candidates[0];
            }

            if (selected == null && candidates.Count > 1)
            {
                foreach (Process candidate in candidates)
                {
                    if (!HasExpectedWebViewProfile(candidate.Id))
                        continue;
                    selected = candidate;
                    preferredTargetProcessId = candidate.Id;
                    break;
                }
            }

            if (selected == null)
            {
                if (!String.IsNullOrWhiteSpace(config.ExecutablePath) ||
                    candidates.Count != 1)
                    return null;
                selected = candidates[0];
            }
            if (selected == null)
                return null;
            preferredTargetProcessId = selected.Id;
            try
            {
                AutomationElement root = AutomationElement.FromHandle(selected.MainWindowHandle);
                if (root == null)
                    return null;
                return new TargetInfo {
                    ProcessId = selected.Id,
                    Handle = selected.MainWindowHandle,
                    Root = root
                };
            }
            catch
            {
                return null;
            }
        }

        private bool HasExpectedWebViewProfile(int processId)
        {
            string marker = GetWebViewProfileMarker();
            if (String.IsNullOrWhiteSpace(marker))
                return false;
            try
            {
                string query = String.Format(
                    "SELECT CommandLine FROM Win32_Process WHERE ParentProcessId={0} AND Name='msedgewebview2.exe'",
                    processId);
                using (ManagementObjectSearcher searcher =
                    new ManagementObjectSearcher(query))
                using (ManagementObjectCollection matches = searcher.Get())
                {
                    foreach (ManagementObject item in matches)
                    {
                        try
                        {
                            string commandLine = Convert.ToString(item["CommandLine"]);
                            if (!String.IsNullOrWhiteSpace(commandLine) &&
                                commandLine.IndexOf(marker, StringComparison.OrdinalIgnoreCase) >= 0)
                                return true;
                        }
                        finally
                        {
                            item.Dispose();
                        }
                    }
                }
            }
            catch
            {
            }
            return false;
        }

        private string GetWebViewProfileMarker()
        {
            if (String.IsNullOrWhiteSpace(webViewLocalStorageDirectory))
                return "";
            try
            {
                string normalized = webViewLocalStorageDirectory.Replace('/', '\\');
                int markerIndex = normalized.IndexOf(
                    "\\EBWebView\\",
                    StringComparison.OrdinalIgnoreCase);
                if (markerIndex <= 0)
                    return "";
                int bundleStart = normalized.LastIndexOf('\\', markerIndex - 1) + 1;
                return normalized.Substring(
                    bundleStart,
                    markerIndex - bundleStart + "\\EBWebView".Length);
            }
            catch
            {
                return "";
            }
        }

        private List<RewardButtonInfo> FindRewardButtons(AutomationElement root)
        {
            List<RewardButtonInfo> result = new List<RewardButtonInfo>();
            try
            {
                PropertyCondition condition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Button);
                CacheRequest cacheRequest = new CacheRequest();
                cacheRequest.AutomationElementMode = AutomationElementMode.Full;
                cacheRequest.Add(AutomationElement.ClassNameProperty);
                cacheRequest.Add(AutomationElement.IsEnabledProperty);
                cacheRequest.Add(AutomationElement.IsOffscreenProperty);
                cacheRequest.Add(AutomationElement.BoundingRectangleProperty);
                using (cacheRequest.Activate())
                {
                    AutomationElementCollection elements = root.FindAll(
                        TreeScope.Descendants,
                        condition);
                    foreach (AutomationElement element in elements)
                    {
                        try
                        {
                            AutomationElement.AutomationElementInformation current =
                                element.Cached;
                            if (current.ClassName.IndexOf(
                                "reward-btn",
                                StringComparison.OrdinalIgnoreCase) < 0)
                                continue;
                            result.Add(new RewardButtonInfo {
                                Element = element,
                                ClassName = current.ClassName,
                                Enabled = current.IsEnabled,
                                Offscreen = current.IsOffscreen,
                                Side = FindAncestorSide(element),
                                Left = current.BoundingRectangle.Left,
                                RectText = String.Format(
                                    "{0:0.0},{1:0.0},{2:0.0},{3:0.0}",
                                    current.BoundingRectangle.Left,
                                    current.BoundingRectangle.Top,
                                    current.BoundingRectangle.Width,
                                    current.BoundingRectangle.Height)
                            });
                        }
                        catch
                        {
                        }
                    }
                }
            }
            catch
            {
            }

            if (result.Count == 1 && String.IsNullOrEmpty(result[0].Side))
                result[0].Side = "single";
            else if (result.Count > 1)
            {
                string[] available = new string[] { "left", "right" }
                    .Where(delegate(string side) {
                        return !result.Any(delegate(RewardButtonInfo item) { return item.Side == side; });
                    })
                    .ToArray();
                foreach (RewardButtonInfo item in result
                    .Where(delegate(RewardButtonInfo button) { return String.IsNullOrEmpty(button.Side); })
                    .OrderBy(delegate(RewardButtonInfo button) { return button.Left; }))
                {
                    item.Side = available.Length > 0 ? available[0] : "ambiguous";
                    if (available.Length > 0)
                        available = available.Skip(1).ToArray();
                }
            }
            return result
                .OrderBy(delegate(RewardButtonInfo button) {
                    return button.Side == "left" ? 0 : button.Side == "right" ? 1 : 2;
                })
                .ThenBy(delegate(RewardButtonInfo button) { return button.Left; })
                .ToList();
        }

        private string FindAncestorSide(AutomationElement element)
        {
            AutomationElement current = element;
            for (int depth = 0; depth < 12 && current != null; depth++)
            {
                try
                {
                    string className = current.Current.ClassName ?? "";
                    if (className.IndexOf("reward-anchor--left", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "left";
                    if (className.IndexOf("reward-anchor--right", StringComparison.OrdinalIgnoreCase) >= 0)
                        return "right";
                    current = TreeWalker.ControlViewWalker.GetParent(current);
                }
                catch
                {
                    break;
                }
            }
            return "";
        }

        private long? FindCharacterCount(AutomationElement root)
        {
            try
            {
                PropertyCondition condition = new PropertyCondition(
                    AutomationElement.ControlTypeProperty,
                    ControlType.Text);
                AutomationElementCollection elements = root.FindAll(
                    TreeScope.Descendants,
                    condition);
                foreach (AutomationElement element in elements)
                {
                    try
                    {
                        string name = (element.Current.Name ?? "").Trim();
                        string normalized = name.Replace(",", "");
                        long value;
                        if (!Int64.TryParse(normalized, out value))
                            continue;
                        AutomationElement parent = TreeWalker.ControlViewWalker.GetParent(element);
                        for (int depth = 0; depth < 6 && parent != null; depth++)
                        {
                            if ((parent.Current.ClassName ?? "").IndexOf(
                                "pet-dock-content",
                                StringComparison.OrdinalIgnoreCase) >= 0)
                                return value;
                            parent = TreeWalker.ControlViewWalker.GetParent(parent);
                        }
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return null;
        }

        private long? ReadCharacterCountCached(TargetInfo target)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (cachedCharacterCount.HasValue &&
                cachedCharacterCountProcessId == target.ProcessId &&
                now - cachedCharacterCountAt < TimeSpan.FromSeconds(10))
                return cachedCharacterCount;

            long? value = FindCharacterCount(target.Root);
            if (value.HasValue)
            {
                cachedCharacterCount = value;
                cachedCharacterCountProcessId = target.ProcessId;
                cachedCharacterCountAt = now;
            }
            return value;
        }

        private RewardPopupInfo ReadRewardPopupInfo(AutomationElement root)
        {
            try
            {
                AutomationElementCollection elements = root.FindAll(
                    TreeScope.Descendants,
                    System.Windows.Automation.Condition.TrueCondition);
                AutomationProperty fullDescriptionProperty =
                    AutomationProperty.LookupById(30159);
                List<string> candidates = new List<string>();
                foreach (AutomationElement element in elements)
                {
                    try
                    {
                        string className = element.Current.ClassName ?? "";
                        if (className.IndexOf("chest-reward-popup", StringComparison.OrdinalIgnoreCase) < 0 &&
                            className.IndexOf("chest-drop-motion-root", StringComparison.OrdinalIgnoreCase) < 0 &&
                            className.IndexOf("prize-image", StringComparison.OrdinalIgnoreCase) < 0)
                            continue;

                        AddPopupCandidate(candidates, element.Current.Name);
                        AddPopupCandidate(candidates, element.Current.HelpText);
                        AddPopupCandidate(candidates, element.Current.AutomationId);
                        AddPopupCandidate(candidates, element.Current.ItemStatus);
                        try
                        {
                            AddPopupCandidate(
                                candidates,
                                Convert.ToString(element.GetCurrentPropertyValue(
                                    fullDescriptionProperty,
                                    true)));
                        }
                        catch
                        {
                        }
                    }
                    catch
                    {
                    }
                }
                if (candidates.Count == 0)
                    return null;
                string resolved = "";
                foreach (string candidate in candidates)
                {
                    if (TryResolveRewardLabel(candidate, out resolved))
                        break;
                }
                return new RewardPopupInfo {
                    Label = candidates[0],
                    ItemName = resolved,
                    Candidates = candidates
                };
            }
            catch
            {
            }
            return null;
        }

        private RewardPopupInfo WaitForRewardPopupInfo(
            AutomationElement root,
            int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(Math.Max(0, timeoutMilliseconds));
            do
            {
                TargetInfo target = FindTarget();
                if (target != null)
                {
                    RewardPopupInfo info = ReadRewardPopupInfo(target.Root);
                    if (info != null)
                        return info;
                }
                if (DateTime.Now >= deadline)
                    break;
                Thread.Sleep(250);
            }
            while (!File.Exists(stopPath));
            return null;
        }

        private static void AddPopupCandidate(List<string> candidates, string value)
        {
            string text = (value ?? "").Trim();
            if (String.IsNullOrWhiteSpace(text) || candidates.Contains(text))
                return;
            candidates.Add(text);
        }

        private string ResolveRewardItemName(
            RewardPopupInfo popup,
            int assetId,
            string cacheItemName,
            out string source)
        {
            source = "unresolved";
            if (popup != null && !String.IsNullOrWhiteSpace(popup.ItemName))
            {
                source = "popup_asset";
                return popup.ItemName;
            }
            if (!String.IsNullOrWhiteSpace(cacheItemName))
            {
                source = "webview_cache_image";
                return cacheItemName;
            }

            string resolvedById;
            if (assetId > 0 &&
                assetIdLookup != null &&
                assetIdLookup.TryGetValue(assetId.ToString(), out resolvedById))
            {
                source = "webview_new_asset_id";
                return resolvedById;
            }

            if (popup == null)
                return "未识别物品";

            foreach (string candidate in popup.Candidates ?? new List<string>())
            {
                string resolved;
                if (TryResolveRewardLabel(candidate, out resolved))
                {
                    source = "popup_asset";
                    return resolved;
                }
            }

            source = "popup_label";
            return NormalizeRewardLabel(popup.Label);
        }

        private static string NormalizeRewardLabel(string label)
        {
            string value = (label ?? "").Trim();
            if (String.IsNullOrWhiteSpace(value))
                return "未识别物品";
            string normalized = value
                .Replace(" ", "")
                .Replace("!", "")
                .Replace("！", "")
                .ToLowerInvariant();
            if (normalized == "new")
                return "宝箱物品（名称未提供）";
            if (normalized == "newcharacter" || value == "新角色！")
                return "新角色（名称未提供）";
            if (normalized == "newaccessory" || value == "新配件！")
                return "新配件（名称未提供）";
            if (normalized == "newemote" || value == "新表情！")
                return "新表情（名称未提供）";
            if (normalized == "duplicatecollectibles" || value == "重复收集物")
                return "重复收集物（名称未提供）";
            return value;
        }

        private bool TryResolveRewardLabel(string label, out string itemName)
        {
            itemName = "";
            string value = (label ?? "").Trim().Trim('"', '\'');
            if (String.IsNullOrWhiteSpace(value))
                return false;

            int queryIndex = value.IndexOf('?');
            if (queryIndex >= 0)
                value = value.Substring(0, queryIndex);
            value = value.Replace('\\', '/');
            int slashIndex = value.LastIndexOf('/');
            if (slashIndex >= 0)
                value = value.Substring(slashIndex + 1);

            string key = Uri.UnescapeDataString(value).Trim();
            if (key.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                key = key.Substring(0, key.Length - 4);

            if (assetNameLookup != null && assetNameLookup.TryGetValue(key, out itemName))
                return true;
            if (rewardLabelLookup.TryGetValue(value, out itemName))
                return true;
            if (rewardLabelLookup.TryGetValue(key, out itemName))
                return true;
            return false;
        }

        private List<object> BuildButtonStatus(List<RewardButtonInfo> buttons)
        {
            return buttons.Select(delegate(RewardButtonInfo button) {
                return (object)new Dictionary<string, object> {
                    {"side", button.Side},
                    {"class_name", button.ClassName},
                    {"enabled", button.Enabled},
                    {"offscreen", button.Offscreen},
                    {"rect", button.RectText}
                };
            }).ToList();
        }

        private void SetStatus(
            string phase,
            string message,
            string error,
            TargetInfo target,
            bool appFound,
            long? characterCount,
            int buttonCount,
            List<object> buttons)
        {
            DateTimeOffset now = DateTimeOffset.Now;
            string fingerprint = String.Join("|", new string[] {
                phase ?? "",
                message ?? "",
                error ?? "",
                target == null ? "" : target.ProcessId.ToString(),
                appFound.ToString(),
                characterCount.HasValue ? characterCount.Value.ToString() : "",
                buttonCount.ToString(),
                activityEnabled.ToString(),
                activityKeysSent.ToString(),
                activityLastResult ?? "",
                manualRequestStatus ?? "",
                manualLastCount.ToString(),
                manualTargetCount.ToString(),
                manualSentCount.ToString(),
                lastAction == null ? "" : JsonUtil.ToJson(lastAction),
                JsonUtil.ToJson(buttons ?? new List<object>())
            });
            if (fingerprint == lastStatusFingerprint &&
                now - lastStatusWriteAt < TimeSpan.FromSeconds(15))
                return;

            Dictionary<string, object> status = new Dictionary<string, object> {
                {"schema_version", 1},
                {"worker_pid", Process.GetCurrentProcess().Id},
                {"phase", phase},
                {"started_at", startedAt.ToString("o")},
                {"updated_at", DateTimeOffset.Now.ToString("o")},
                {"dry_run", false},
                {"app_found", appFound},
                {"app_process_id", target == null ? null : (object)target.ProcessId},
                {"window_handle", target == null ? null : (object)target.Handle.ToInt64()},
                {"last_scan_at", DateTimeOffset.Now.ToString("o")},
                {"reward_button_count", buttonCount},
                {"buttons", buttons},
                {"character_count", characterCount},
                {"activity_enabled", activityEnabled},
                {"activity_keys_sent", activityKeysSent},
                {"activity_last_result", activityLastResult},
                {"manual_refill_status", manualRequestStatus},
                {"manual_refill_last_count", manualLastCount},
                {"manual_refill_target_count", manualTargetCount},
                {"manual_refill_sent_count", manualSentCount},
                {"reward_image_name_count", imageHashLookup == null ? 0 : imageHashLookup.Count},
                {"reward_asset_id_count", assetIdLookup == null ? 0 : assetIdLookup.Count},
                {"last_action", lastAction},
                {"last_error", error},
                {"message", message}
            };
            WriteJsonAtomic(statusPath, status);
            lastStatusFingerprint = fingerprint;
            lastStatusWriteAt = now;
        }

        private void WritePidFile()
        {
            WriteJsonAtomic(pidPath, new Dictionary<string, object> {
                {"worker_pid", Process.GetCurrentProcess().Id},
                {"started_at", startedAt.ToString("o")}
            });
        }

        private void RestoreOperationState()
        {
            Dictionary<string, object> state = JsonUtil.ParseObject(operationStatePath);
            if (state == null)
                return;

            object sidesObject;
            if (state.TryGetValue("sides", out sidesObject))
            {
                Dictionary<string, object> sides = sidesObject as Dictionary<string, object>;
                if (sides != null)
                {
                    foreach (KeyValuePair<string, object> pair in sides)
                    {
                        Dictionary<string, object> side = pair.Value as Dictionary<string, object>;
                        string untilText = JsonUtil.GetString(side, "cooldown_until", "");
                        DateTimeOffset until;
                        if (DateTimeOffset.TryParse(untilText, out until))
                            sideCooldowns[pair.Key] = until;
                    }
                }
            }

            object historyObject;
            if (state.TryGetValue("operations", out historyObject))
            {
                object[] items = historyObject as object[];
                if (items != null)
                    operationHistory.AddRange(items.Take(50));
            }

            object pendingObject;
            if (state.TryGetValue("pending", out pendingObject) &&
                pendingObject is Dictionary<string, object>)
            {
                Dictionary<string, object> pending = (Dictionary<string, object>)pendingObject;
                string side = JsonUtil.GetString(pending, "side", "single");
                sideCooldowns[side] = DateTimeOffset.Now.AddSeconds(
                    config.UnknownOutcomeCooldownSeconds);
                pending["status"] = "unknown";
                pending["confirmed_at"] = DateTimeOffset.Now.ToString("o");
                pending["evidence"] = "recovered_pending_after_restart";
                AddOperation(pending);
                AppendHistory(new Dictionary<string, object> {
                    {"time", DateTimeOffset.Now.ToString("o")},
                    {"operation_id", JsonUtil.GetString(pending, "operation_id", "")},
                    {"side", side},
                    {"item_name", JsonUtil.GetString(pending, "item_name", "")},
                    {"item_source", "recovered_pending"},
                    {"asset_id", null},
                    {"popup_label", JsonUtil.GetString(pending, "popup_label", "")},
                    {"status", "unknown"},
                    {"result", "unknown"},
                    {"evidence", "recovered_pending_after_restart"}
                });
                Log("WARN", "pending_operation_recovered", new Dictionary<string, object> {
                    {"side", side}
                });
            }
        }

        private void WriteOperationState(Dictionary<string, object> pending)
        {
            Dictionary<string, object> sides = new Dictionary<string, object>();
            foreach (KeyValuePair<string, DateTimeOffset> pair in sideCooldowns)
            {
                sides[pair.Key] = new Dictionary<string, object> {
                    {"cooldown_until", pair.Value.ToString("o")}
                };
            }
            Dictionary<string, object> state = new Dictionary<string, object> {
                {"schema_version", 1},
                {"pending", pending},
                {"sides", sides},
                {"operations", operationHistory.ToArray()}
            };
            WriteJsonAtomic(operationStatePath, state);
        }

        private void AddOperation(Dictionary<string, object> operation)
        {
            operationHistory.Add(operation);
            if (operationHistory.Count > 50)
                operationHistory.RemoveAt(0);
            WriteOperationState(null);
        }

        private void AppendHistory(Dictionary<string, object> record)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(historyPath));
                File.AppendAllText(
                    historyPath,
                    JsonUtil.ToJson(record) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private void LoadAssetNameLookup()
        {
            assetNameLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            assetIdLookup = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            rewardLabelLookup.Clear();
            try
            {
                string catalogPath = Path.Combine(runtimeDirectory, @"gallery\catalog.json");
                Dictionary<string, object> catalog = JsonUtil.ParseObject(catalogPath);
                if (catalog == null)
                    return;
                object itemsObject;
                if (!catalog.TryGetValue("items", out itemsObject))
                    return;
                IEnumerable items = itemsObject as IEnumerable;
                if (items == null)
                    return;
                foreach (object itemObject in items)
                {
                    Dictionary<string, object> item = itemObject as Dictionary<string, object>;
                    if (item == null)
                        continue;
                    string assetName = JsonUtil.GetString(item, "asset_name", "").Trim();
                    if (assetName.EndsWith(".png", StringComparison.OrdinalIgnoreCase))
                        assetName = assetName.Substring(0, assetName.Length - 4);
                    string nameZh = JsonUtil.GetString(item, "name_zh", "").Trim();
                    string nameEn = JsonUtil.GetString(item, "name_en", "").Trim();
                    string assetId = JsonUtil.GetString(item, "asset_id", "").Trim();
                    if (!String.IsNullOrWhiteSpace(assetName) && !String.IsNullOrWhiteSpace(nameZh))
                    {
                        assetNameLookup[assetName] = nameZh;
                        rewardLabelLookup[assetName] = nameZh;
                    }
                    if (!String.IsNullOrWhiteSpace(nameZh))
                        rewardLabelLookup[nameZh] = nameZh;
                    if (!String.IsNullOrWhiteSpace(nameEn))
                        rewardLabelLookup[nameEn] = nameZh;
                    if (!String.IsNullOrWhiteSpace(assetId) && !String.IsNullOrWhiteSpace(nameZh))
                        assetIdLookup[assetId] = nameZh;
                }
            }
            catch
            {
            }
        }

        private void LoadAssetImageLookup()
        {
            imageHashLookup = new Dictionary<string, string>(
                StringComparer.OrdinalIgnoreCase);
            try
            {
                string indexPath = Path.Combine(
                    runtimeDirectory,
                    @"gallery\image-hashes.json");
                Dictionary<string, object> root = JsonUtil.ParseObject(indexPath);
                Dictionary<string, object> items = JsonUtil.GetDictionary(root, "items");
                foreach (KeyValuePair<string, object> pair in items)
                {
                    Dictionary<string, object> item =
                        pair.Value as Dictionary<string, object>;
                    if (item == null || JsonUtil.GetBool(item, "ambiguous", false))
                        continue;
                    string name = JsonUtil.GetString(item, "name_zh", "").Trim();
                    if (!String.IsNullOrWhiteSpace(pair.Key) &&
                        !String.IsNullOrWhiteSpace(name))
                        imageHashLookup[pair.Key] = name;
                }
            }
            catch
            {
            }
        }

        private string ResolveWebViewLocalStorageDirectory()
        {
            if (!String.IsNullOrWhiteSpace(config.WebViewLocalStorageDirectory))
                return Path.GetFullPath(config.WebViewLocalStorageDirectory);

            string localAppData = Environment.GetFolderPath(
                Environment.SpecialFolder.LocalApplicationData);
            string bundleDirectory = Path.Combine(
                localAppData,
                "com.kuro.wuwatappo.cn",
                "EBWebView",
                "Default",
                "Local Storage",
                "leveldb");
            return bundleDirectory;
        }

        private string ResolveWebViewCacheDirectory()
        {
            if (String.IsNullOrWhiteSpace(webViewLocalStorageDirectory))
                return "";
            try
            {
                return Path.GetFullPath(Path.Combine(
                    webViewLocalStorageDirectory,
                    "..",
                    "..",
                    "Cache",
                    "Cache_Data"));
            }
            catch
            {
                return "";
            }
        }

        private HashSet<int> ReadLatestNewAssetIds()
        {
            List<int> ids = ReadLatestNewAssetIds(webViewLocalStorageDirectory);
            return new HashSet<int>(ids);
        }

        private List<int> ReadLatestNewAssetIds(string directory)
        {
            if (String.IsNullOrWhiteSpace(directory) || !Directory.Exists(directory))
                return new List<int>();

            try
            {
                List<string> logFiles = Directory.GetFiles(directory, "*.log")
                    .OrderByDescending(delegate(string path) {
                        return File.GetLastWriteTimeUtc(path);
                    })
                    .ToList();
                foreach (string path in logFiles)
                {
                    List<int> ids;
                    if (TryReadLatestNewAssetIds(path, out ids))
                        return ids;
                }

                List<string> tableFiles = Directory.GetFiles(directory, "*.ldb")
                    .OrderByDescending(delegate(string path) {
                        return File.GetLastWriteTimeUtc(path);
                    })
                    .ToList();
                foreach (string path in tableFiles)
                {
                    List<int> ids;
                    if (TryReadLatestNewAssetIds(path, out ids))
                        return ids;
                }
            }
            catch
            {
            }
            return new List<int>();
        }

        private static bool TryReadLatestNewAssetIds(string path, out List<int> latest)
        {
            latest = null;
            byte[] bytes;
            try
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                {
                    if (stream.Length <= 0 || stream.Length > 16 * 1024 * 1024)
                        return false;
                    bytes = new byte[(int)stream.Length];
                    int offset = 0;
                    while (offset < bytes.Length)
                    {
                        int read = stream.Read(bytes, offset, bytes.Length - offset);
                        if (read <= 0)
                            break;
                        offset += read;
                    }
                    if (offset != bytes.Length)
                        return false;
                }
            }
            catch
            {
                return false;
            }

            byte[] key = Encoding.UTF8.GetBytes("bag_new_asset_ids");
            int searchOffset = 0;
            while (searchOffset < bytes.Length)
            {
                int keyOffset = FindBytes(bytes, key, searchOffset);
                if (keyOffset < 0)
                    break;

                List<int> parsed;
                if (TryParseAssetIdArray(bytes, keyOffset + key.Length, out parsed))
                    latest = parsed;
                searchOffset = keyOffset + key.Length;
            }
            return latest != null;
        }

        private static bool TryParseAssetIdArray(
            byte[] bytes,
            int startOffset,
            out List<int> ids)
        {
            ids = null;
            int limit = Math.Min(bytes.Length, startOffset + 2048);
            int arrayStart = -1;
            for (int index = startOffset; index < limit; index++)
            {
                if (bytes[index] == (byte)'[')
                {
                    arrayStart = index;
                    break;
                }
            }
            if (arrayStart < 0)
                return false;

            int arrayEnd = -1;
            for (int index = arrayStart + 1; index < limit; index++)
            {
                if (bytes[index] == (byte)']')
                {
                    arrayEnd = index;
                    break;
                }
            }
            if (arrayEnd < 0)
                return false;

            string text = Encoding.ASCII.GetString(
                bytes,
                arrayStart,
                arrayEnd - arrayStart + 1);
            ids = new List<int>();
            int current = 0;
            bool hasDigits = false;
            for (int index = 1; index < text.Length - 1; index++)
            {
                char character = text[index];
                if (character >= '0' && character <= '9')
                {
                    current = checked(current * 10 + (character - '0'));
                    hasDigits = true;
                    continue;
                }
                if (character == ',')
                {
                    if (!hasDigits)
                        return false;
                    ids.Add(current);
                    current = 0;
                    hasDigits = false;
                    continue;
                }
                if (Char.IsWhiteSpace(character))
                    continue;
                return false;
            }
            if (hasDigits)
                ids.Add(current);
            return true;
        }

        private static int FindBytes(byte[] source, byte[] needle, int startOffset)
        {
            if (source == null || needle == null || needle.Length == 0)
                return -1;
            int lastStart = source.Length - needle.Length;
            for (int index = Math.Max(0, startOffset); index <= lastStart; index++)
            {
                bool match = true;
                for (int offset = 0; offset < needle.Length; offset++)
                {
                    if (source[index + offset] == needle[offset])
                        continue;
                    match = false;
                    break;
                }
                if (match)
                    return index;
            }
            return -1;
        }

        private int WaitForNewRewardAssetId(
            HashSet<int> idsBefore,
            int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(Math.Max(0, timeoutMilliseconds));
            while (!File.Exists(stopPath))
            {
                HashSet<int> idsAfter = ReadLatestNewAssetIds();
                foreach (int assetId in idsAfter)
                {
                    if (!idsBefore.Contains(assetId))
                        return assetId;
                }
                if (DateTime.Now >= deadline)
                    break;
                Thread.Sleep(200);
            }
            return 0;
        }

        private Dictionary<string, string> SnapshotRewardCache()
        {
            Dictionary<string, string> snapshot =
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (String.IsNullOrWhiteSpace(webViewCacheDirectory) ||
                !Directory.Exists(webViewCacheDirectory))
                return snapshot;
            try
            {
                foreach (string path in Directory.GetFiles(webViewCacheDirectory, "f_*"))
                {
                    try
                    {
                        FileInfo file = new FileInfo(path);
                        snapshot[file.Name] = file.Length.ToString() + ":" +
                            file.LastWriteTimeUtc.Ticks.ToString();
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return snapshot;
        }

        private string WaitForRewardCacheItemName(
            Dictionary<string, string> before,
            DateTime watchStartedUtc,
            int timeoutMilliseconds)
        {
            DateTime deadline = DateTime.UtcNow.AddMilliseconds(
                Math.Max(0, timeoutMilliseconds));
            while (!File.Exists(stopPath))
            {
                string itemName = FindRewardCacheItemName(before, watchStartedUtc);
                if (!String.IsNullOrWhiteSpace(itemName))
                    return itemName;
                if (DateTime.UtcNow >= deadline)
                    break;
                Thread.Sleep(200);
            }
            return "";
        }

        private string FindRewardCacheItemName(
            Dictionary<string, string> before,
            DateTime watchStartedUtc)
        {
            if (String.IsNullOrWhiteSpace(webViewCacheDirectory) ||
                !Directory.Exists(webViewCacheDirectory) ||
                imageHashLookup == null ||
                imageHashLookup.Count == 0)
                return "";

            try
            {
                List<FileInfo> candidates = Directory
                    .GetFiles(webViewCacheDirectory, "f_*")
                    .Select(delegate(string path) { return new FileInfo(path); })
                    .Where(delegate(FileInfo file) {
                        return file.LastWriteTimeUtc >= watchStartedUtc.AddSeconds(-2);
                    })
                    .OrderByDescending(delegate(FileInfo file) {
                        return file.LastWriteTimeUtc;
                    })
                    .ThenByDescending(delegate(FileInfo file) {
                        return file.Name;
                    })
                    .Take(40)
                    .ToList();
                foreach (FileInfo file in candidates)
                {
                    try
                    {
                        string signature = file.Length.ToString() + ":" +
                            file.LastWriteTimeUtc.Ticks.ToString();
                        string previous;
                        if (before.TryGetValue(file.Name, out previous) &&
                            previous == signature)
                            continue;

                        string hash = ComputeFileSha256(file.FullName);
                        string itemName;
                        if (!String.IsNullOrWhiteSpace(hash) &&
                            imageHashLookup.TryGetValue(hash, out itemName))
                            return itemName;
                    }
                    catch
                    {
                    }
                }
            }
            catch
            {
            }
            return "";
        }

        private static string ComputeFileSha256(string path)
        {
            try
            {
                using (FileStream stream = new FileStream(
                    path,
                    FileMode.Open,
                    FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete))
                using (SHA256 sha256 = SHA256.Create())
                {
                    byte[] hash = sha256.ComputeHash(stream);
                    StringBuilder text = new StringBuilder(hash.Length * 2);
                    foreach (byte value in hash)
                        text.Append(value.ToString("X2"));
                    return text.ToString();
                }
            }
            catch
            {
                return "";
            }
        }

        private void RefreshManualRefillState()
        {
            Dictionary<string, object> request = JsonUtil.ParseObject(refillRequestPath);
            if (request == null)
            {
                manualRequestId = "";
                manualRequestStatus = "";
                manualModeEngaged = false;
                return;
            }

            string requestId = JsonUtil.GetString(request, "request_id", "");
            string status = JsonUtil.GetString(request, "status", "");
            if (String.IsNullOrWhiteSpace(status))
                return;
            bool newRequest = !String.Equals(
                requestId,
                manualRequestId,
                StringComparison.OrdinalIgnoreCase);
            if (newRequest)
            {
                manualRequestId = requestId;
                manualRequestedIncrement = JsonUtil.GetLong(
                    request,
                    "requested_increment",
                    0);
                manualBaseCount = JsonUtil.GetLong(request, "base_count", 0);
                manualTargetCount = JsonUtil.GetLong(request, "target_count", 0);
                manualLastCount = JsonUtil.GetLong(request, "last_count", 0);
                manualSentCount = JsonUtil.GetLong(request, "sent_count", 0);
                manualError = JsonUtil.GetString(request, "error", "");
                manualRefillStartedAt = DateTimeOffset.Now;
                manualRefillLastProgressLogAt = DateTimeOffset.MinValue;
                manualRefillLastLoggedSent = 0;
                if (status == "pending")
                {
                    Log("INFO", "manual_refill_requested", new Dictionary<string, object> {
                        {"request_id", requestId},
                        {"requested_increment", manualRequestedIncrement}
                    });
                }
                else if (status == "running")
                {
                    LogManualRefillEvent("manual_refill_started", status, manualError);
                    manualRefillLastProgressLogAt = DateTimeOffset.Now;
                    manualRefillLastLoggedSent = manualSentCount;
                }
                if (status == "pending")
                {
                    manualBaseCount = 0;
                    manualTargetCount = 0;
                    manualLastCount = 0;
                    manualSentCount = 0;
                    manualError = "";
                }
            }
            manualModeEngaged = true;
            if (status == "cancel_requested")
            {
                manualRequestStatus = "cancelled";
                UpdateManualRefillState("cancelled", manualLastCount, manualSentCount, "");
                return;
            }
            manualRequestStatus = status;
        }

        private void UpdateManualRefillState(
            string status,
            long lastCount,
            long sentCount,
            string error)
        {
            string previousStatus = manualRequestStatus;
            manualRequestStatus = status;
            manualLastCount = lastCount;
            manualSentCount = sentCount;
            manualError = error ?? "";
            Dictionary<string, object> request = new Dictionary<string, object> {
                {"schema_version", 1},
                {"request_id", manualRequestId},
                {"status", status},
                {"requested_increment", manualRequestedIncrement},
                {"base_count", manualBaseCount},
                {"target_count", manualTargetCount},
                {"last_count", lastCount},
                {"sent_count", sentCount},
                {"updated_at", DateTimeOffset.Now.ToString("o")},
                {"error", manualError}
            };
            WriteJsonAtomic(refillRequestPath, request);

            if (status == "running" &&
                !String.Equals(previousStatus, "running", StringComparison.OrdinalIgnoreCase))
            {
                LogManualRefillEvent("manual_refill_started", status, error);
                manualRefillLastProgressLogAt = DateTimeOffset.Now;
                manualRefillLastLoggedSent = sentCount;
            }
            else if (status == "running")
            {
                MaybeLogManualRefillProgress();
            }
            else if (status == "completed" &&
                     !String.Equals(previousStatus, status, StringComparison.OrdinalIgnoreCase))
            {
                LogManualRefillEvent("manual_refill_completed", status, error);
            }
            else if (status == "cancelled" &&
                     !String.Equals(previousStatus, status, StringComparison.OrdinalIgnoreCase))
            {
                LogManualRefillEvent("manual_refill_cancelled", status, error);
            }
            else if (status == "failed" &&
                     !String.Equals(previousStatus, status, StringComparison.OrdinalIgnoreCase))
            {
                LogManualRefillEvent("manual_refill_failed", status, error);
            }
            else if (status == "needs_focus" &&
                     !String.Equals(previousStatus, status, StringComparison.OrdinalIgnoreCase))
            {
                LogManualRefillEvent("manual_refill_needs_focus", status, error);
            }
        }

        private void MaybeLogManualRefillProgress()
        {
            DateTimeOffset now = DateTimeOffset.Now;
            if (manualRefillLastProgressLogAt != DateTimeOffset.MinValue &&
                now - manualRefillLastProgressLogAt < TimeSpan.FromSeconds(10) &&
                manualSentCount - manualRefillLastLoggedSent < 5000)
            {
                return;
            }
            manualRefillLastProgressLogAt = now;
            manualRefillLastLoggedSent = manualSentCount;
            LogManualRefillEvent("manual_refill_progress", "running", manualError);
        }

        private void LogManualRefillEvent(
            string eventName,
            string status,
            string error)
        {
            using (Process currentProcess = Process.GetCurrentProcess())
            {
                double elapsedSeconds = manualRefillStartedAt == DateTimeOffset.MinValue
                    ? 0
                    : Math.Max(0, (DateTimeOffset.Now - manualRefillStartedAt).TotalSeconds);
                bool warning = status == "failed" || status == "needs_focus";
                Log(
                    warning ? "WARN" : "INFO",
                    eventName,
                    new Dictionary<string, object> {
                        {"request_id", manualRequestId},
                        {"status", status},
                        {"requested_increment", manualRequestedIncrement},
                        {"base_count", manualBaseCount},
                        {"target_count", manualTargetCount},
                        {"last_count", manualLastCount},
                        {"sent_count", manualSentCount},
                        {"elapsed_seconds", Math.Round(elapsedSeconds, 1)},
                        {"working_set_mb", Math.Round(
                            currentProcess.WorkingSet64 / 1048576.0,
                            1)},
                        {"worker_cpu_seconds", Math.Round(
                            currentProcess.TotalProcessorTime.TotalSeconds,
                            1)},
                        {"error", error ?? ""}
                    });
            }
        }

        private bool WaitWithStop(int milliseconds)
        {
            DateTime deadline = DateTime.Now.AddMilliseconds(milliseconds);
            while (DateTime.Now < deadline)
            {
                if (File.Exists(stopPath))
                    return false;
                int remaining = (int)Math.Max(1, (deadline - DateTime.Now).TotalMilliseconds);
                Thread.Sleep(Math.Min(250, remaining));
            }
            return !File.Exists(stopPath);
        }

        private void Log(string level, string eventName, Dictionary<string, object> data)
        {
            try
            {
                Dictionary<string, object> entry = new Dictionary<string, object> {
                    {"time", DateTimeOffset.Now.ToString("o")},
                    {"level", level},
                    {"event", eventName},
                    {"worker_pid", Process.GetCurrentProcess().Id},
                    {"data", data ?? new Dictionary<string, object>()}
                };
                File.AppendAllText(
                    Path.Combine(logDirectory, "autochest.jsonl"),
                    JsonUtil.ToJson(entry) + Environment.NewLine,
                    new UTF8Encoding(false));
            }
            catch
            {
            }
        }

        private static void WriteJsonAtomic(string path, object value)
        {
            string directory = Path.GetDirectoryName(path);
            Directory.CreateDirectory(directory);
            string temporary = path + "." + Process.GetCurrentProcess().Id + ".tmp";
            File.WriteAllText(temporary, JsonUtil.ToJson(value), new UTF8Encoding(false));
            if (File.Exists(path))
                File.Replace(temporary, path, null);
            else
                File.Move(temporary, path);
        }

        private sealed class TargetInfo
        {
            public int ProcessId;
            public IntPtr Handle;
            public AutomationElement Root;
        }

        private sealed class RewardButtonInfo
        {
            public AutomationElement Element;
            public string ClassName;
            public string Side;
            public bool Enabled;
            public bool Offscreen;
            public double Left;
            public string RectText;
        }

        private sealed class ActivityResult
        {
            public string Reason = "";
            public string ErrorMessage = "";
            public int PressesSent;
            public long? CountBefore;
            public long? CountAfter;
            public bool InjectionFailed;
        }

        private sealed class RewardPopupInfo
        {
            public string Label = "";
            public string ItemName = "";
            public List<string> Candidates = new List<string>();
        }
    }

    internal static class NativeInput
    {
        private const uint INPUT_KEYBOARD = 1;
        private const uint KEYEVENTF_KEYUP = 0x0002;

        [StructLayout(LayoutKind.Sequential)]
        private struct KEYBDINPUT
        {
            public ushort wVk;
            public ushort wScan;
            public uint dwFlags;
            public uint time;
            public UIntPtr dwExtraInfo;
        }

        // Win32 INPUT on x86 is 28 bytes: 4-byte type + 24-byte union.
        [StructLayout(LayoutKind.Explicit, Size = 24)]
        private struct INPUTUNION
        {
            [FieldOffset(0)]
            public KEYBDINPUT ki;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct INPUT
        {
            public uint type;
            public INPUTUNION u;
        }

        [DllImport("user32.dll", SetLastError = true)]
        private static extern uint SendInput(uint inputCount, INPUT[] inputs, int inputSize);

        public static void SendKey(byte virtualKey)
        {
            INPUT[] inputs = new INPUT[2];
            inputs[0].type = INPUT_KEYBOARD;
            inputs[0].u.ki.wVk = virtualKey;
            inputs[1].type = INPUT_KEYBOARD;
            inputs[1].u.ki.wVk = virtualKey;
            inputs[1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            uint sent = SendInput((uint)inputs.Length, inputs, Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length)
                throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
        }

        public static void SendKeys(byte virtualKey, int count)
        {
            int keyCount = Math.Max(1, count);
            INPUT[] inputs = new INPUT[keyCount * 2];
            for (int index = 0; index < keyCount; index++)
            {
                int offset = index * 2;
                inputs[offset].type = INPUT_KEYBOARD;
                inputs[offset].u.ki.wVk = virtualKey;
                inputs[offset + 1].type = INPUT_KEYBOARD;
                inputs[offset + 1].u.ki.wVk = virtualKey;
                inputs[offset + 1].u.ki.dwFlags = KEYEVENTF_KEYUP;
            }
            uint sent = SendInput(
                (uint)inputs.Length,
                inputs,
                Marshal.SizeOf(typeof(INPUT)));
            if (sent != inputs.Length)
                throw new InvalidOperationException("SendInput failed: " + Marshal.GetLastWin32Error());
        }

    }
}
