using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Web.Script.Serialization;

namespace WuwaTappoCdpHost
{
    internal sealed class NativePageProfile
    {
        public string CommonPath = "";
        public string ClickPath = "";
        public string StoreKey = "";
        public string SaveKey = "";
        public string CountMode = "";
    }

    internal sealed class NativePageSnapshot
    {
        public bool LoggedIn;
        public string AccountKey = "";
        public long Available;
        public long Cumulative;
        public long Local;
        public long Consumed;
        public long ServerTotal;
    }

    internal sealed class NativePageBatchResult
    {
        public bool Ok;
        public string Error = "";
        public int Delta;
        public NativePageSnapshot Before;
        public NativePageSnapshot After;
    }

    internal static class NativePageRefill
    {
        private static readonly JavaScriptSerializer Serializer =
            new JavaScriptSerializer();

        public static NativePageProfile Discover(
            CdpClient client,
            CancellationToken cancellationToken)
        {
            Dictionary<string, object> value = EvaluateObject(
                client,
                ReadScript("discover.js"),
                cancellationToken);
            if (!GetBool(value, "ok", false))
                throw new InvalidOperationException(
                    "页面原生计数能力发现失败：" + GetString(value, "error", "unknown"));

            NativePageProfile profile = new NativePageProfile {
                CommonPath = GetString(value, "commonPath", ""),
                ClickPath = GetString(value, "clickPath", ""),
                StoreKey = GetString(value, "storeKey", ""),
                SaveKey = GetString(value, "saveKey", ""),
                CountMode = GetString(value, "countMode", "")
            };
            if (profile.CommonPath == "" || profile.ClickPath == "" ||
                profile.StoreKey == "" || profile.SaveKey == "")
                throw new InvalidOperationException("页面原生计数能力字段不完整。");
            return profile;
        }

        public static NativePageSnapshot ReadSnapshot(
            CdpClient client,
            NativePageProfile profile,
            CancellationToken cancellationToken)
        {
            string script = ReadScript("state.js")
                .Replace("__COMMON_PATH__", Quote(profile.CommonPath))
                .Replace("__STORE_KEY__", Quote(profile.StoreKey));
            Dictionary<string, object> value = EvaluateObject(
                client,
                script,
                cancellationToken);
            if (!GetBool(value, "ok", false))
                throw new InvalidOperationException(
                    "读取页面计数失败：" + GetString(value, "error", "unknown"));

            NativePageSnapshot snapshot = new NativePageSnapshot {
                LoggedIn = GetBool(value, "loggedIn", false),
                AccountKey = GetString(value, "accountKey", ""),
                Available = GetLong(value, "available", 0),
                Cumulative = GetLong(value, "cumulative", 0),
                Local = GetLong(value, "local", 0),
                Consumed = GetLong(value, "consumed", 0),
                ServerTotal = GetLong(value, "serverTotal", 0)
            };
            if (!snapshot.LoggedIn)
                throw new InvalidOperationException("Wuwa Tappo 当前未登录。");
            return snapshot;
        }

        public static NativePageBatchResult Boost(
            CdpClient client,
            NativePageProfile profile,
            NativePageSnapshot before,
            int delta,
            CancellationToken cancellationToken)
        {
            string script = ReadScript("boost.js")
                .Replace("__COMMON_PATH__", Quote(profile.CommonPath))
                .Replace("__STORE_KEY__", Quote(profile.StoreKey))
                .Replace("__CLICK_PATH__", Quote(profile.ClickPath))
                .Replace("__SAVE_KEY__", Quote(profile.SaveKey))
                .Replace("__COUNT_MODE__", Quote(profile.CountMode))
                .Replace("__ACCOUNT_KEY__", Quote(before.AccountKey))
                .Replace("__DELTA__", delta.ToString(CultureInfo.InvariantCulture))
                .Replace("__BEFORE_AVAILABLE__", before.Available.ToString(CultureInfo.InvariantCulture))
                .Replace("__BEFORE_CUMULATIVE__", before.Cumulative.ToString(CultureInfo.InvariantCulture))
                .Replace("__BEFORE_LOCAL__", before.Local.ToString(CultureInfo.InvariantCulture))
                .Replace("__BEFORE_CONSUMED__", before.Consumed.ToString(CultureInfo.InvariantCulture));
            Dictionary<string, object> value = EvaluateObject(
                client,
                script,
                cancellationToken);

            NativePageBatchResult result = new NativePageBatchResult {
                Ok = GetBool(value, "ok", false),
                Error = GetString(value, "error", ""),
                Delta = GetInt(value, "delta", delta),
                Before = before
            };
            Dictionary<string, object> after = GetDictionary(value, "after");
            if (after != null)
            {
                result.After = new NativePageSnapshot {
                    LoggedIn = GetBool(after, "loggedIn", false),
                    AccountKey = before.AccountKey,
                    Available = GetLong(after, "available", 0),
                    Cumulative = GetLong(after, "cumulative", 0),
                    Local = GetLong(after, "local", 0),
                    Consumed = GetLong(after, "consumed", 0),
                    ServerTotal = GetLong(after, "serverTotal", 0)
                };
            }
            return result;
        }

        private static Dictionary<string, object> EvaluateObject(
            CdpClient client,
            string script,
            CancellationToken cancellationToken)
        {
            Dictionary<string, object> response = client.EvaluateAwaitAsync(
                    script,
                    cancellationToken)
                .GetAwaiter()
                .GetResult();
            Dictionary<string, object> payload = GetDictionary(response, "result");
            Dictionary<string, object> result = GetDictionary(payload, "result");
            object value;
            if (result == null ||
                !result.TryGetValue("value", out value) ||
                value == null)
                throw new InvalidOperationException("页面脚本没有返回结果。");
            Dictionary<string, object> values = value as Dictionary<string, object>;
            if (values == null)
                throw new InvalidOperationException("页面脚本返回格式无效。");
            return values;
        }

        private static string ReadScript(string fileName)
        {
            string path = Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                "runtime",
                "native",
                fileName);
            if (!File.Exists(path))
                throw new FileNotFoundException("找不到页面桥接脚本：" + path);
            return File.ReadAllText(path);
        }

        private static string Quote(string value)
        {
            return Serializer.Serialize(value ?? "");
        }

        private static Dictionary<string, object> GetDictionary(
            Dictionary<string, object> parent,
            string key)
        {
            object value;
            return parent != null &&
                   parent.TryGetValue(key, out value)
                ? value as Dictionary<string, object>
                : null;
        }

        private static string GetString(
            Dictionary<string, object> values,
            string key,
            string fallback)
        {
            object value;
            return values != null &&
                   values.TryGetValue(key, out value) &&
                   value != null
                ? Convert.ToString(value, CultureInfo.InvariantCulture)
                : fallback;
        }

        private static bool GetBool(
            Dictionary<string, object> values,
            string key,
            bool fallback)
        {
            object value;
            if (values == null ||
                !values.TryGetValue(key, out value) ||
                value == null)
                return fallback;
            try
            {
                return Convert.ToBoolean(value, CultureInfo.InvariantCulture);
            }
            catch
            {
                return fallback;
            }
        }

        private static int GetInt(
            Dictionary<string, object> values,
            string key,
            int fallback)
        {
            long value = GetLong(values, key, fallback);
            return value > Int32.MaxValue || value < Int32.MinValue
                ? fallback
                : (int)value;
        }

        private static long GetLong(
            Dictionary<string, object> values,
            string key,
            long fallback)
        {
            object value;
            if (values == null ||
                !values.TryGetValue(key, out value) ||
                value == null)
                return fallback;
            long parsed;
            return Int64.TryParse(
                Convert.ToString(value, CultureInfo.InvariantCulture),
                NumberStyles.Integer,
                CultureInfo.InvariantCulture,
                out parsed)
                ? parsed
                : fallback;
        }
    }
}
