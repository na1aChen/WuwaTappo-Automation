using System;
using System.Collections.Generic;
using System.IO;
using WuwaTappoCdpHost;

namespace WuwaTappoCdpHostTests
{
    internal static class CdpHostSelfTestProgram
    {
        private static int Main()
        {
            int passed = 0;
            try
            {
                string[] rootPaths = {
                    Path.GetPathRoot(AppDomain.CurrentDomain.BaseDirectory) ?? "C:\\",
                    @"C:\",
                    @"E:\"
                };
                foreach (string rootPath in rootPaths)
                {
                    if (!String.IsNullOrWhiteSpace(rootPath) &&
                        !String.IsNullOrWhiteSpace(CdpHostForm.SplitParent(rootPath)))
                    {
                        throw new InvalidOperationException(
                            "根目录没有终止：" + rootPath);
                    }
                    passed++;
                    Console.WriteLine("PASS 根目录终止 " + rootPath);
                }

                string current = AppDomain.CurrentDomain.BaseDirectory;
                HashSet<string> seen = new HashSet<string>(
                    StringComparer.OrdinalIgnoreCase);
                string parent = CdpHostForm.SplitParent(current);
                int depth = 0;
                while (!String.IsNullOrWhiteSpace(parent))
                {
                    if (!seen.Add(parent))
                    {
                        throw new InvalidOperationException(
                            "祖先路径出现循环：" + parent);
                    }
                    if (depth >= 32)
                    {
                        throw new InvalidOperationException(
                            "祖先路径超过 32 层仍未终止。");
                    }
                    parent = CdpHostForm.SplitParent(parent);
                    depth++;
                }
                passed++;
                Console.WriteLine(
                    "PASS 便携目录祖先遍历终止 depth=" + depth);

                RunRecoveryPolicyTests(ref passed);

                AssertTrue(
                    ProductBranding.Author == "by Phrolova2405",
                    "统一作者水印");
                passed++;
                AssertTrue(
                    ProductBranding.MainDisplayName.EndsWith(
                        ProductBranding.Author,
                        StringComparison.Ordinal),
                    "主程序名称包含作者水印");
                passed++;
                AssertTrue(
                    ProductBranding.HostDisplayName.EndsWith(
                        ProductBranding.Author,
                        StringComparison.Ordinal),
                    "Host 名称包含作者水印");
                passed++;

                Console.WriteLine("SUMMARY passed=" + passed + " failed=0");
                return 0;
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine("FAIL " + ex);
                Console.WriteLine("SUMMARY passed=" + passed + " failed=1");
                return 1;
            }
        }

        private static void RunRecoveryPolicyTests(ref int passed)
        {
            AssertTrue(
                NativeRecoveryPolicy.IsPreflightConflict("store-state-changed"),
                "识别预检冲突");
            passed++;

            AssertTrue(
                NativeRecoveryPolicy.IsPostSaveUnconfirmed(
                    "post-save-state-not-confirmed"),
                "识别保存后未确认");
            passed++;

            AssertTrue(
                NativeRecoveryPolicy.IsIncrementUnconfirmed(
                    "increment-not-confirmed"),
                "识别增量中断");
            passed++;

            AssertTrue(
                NativeRecoveryPolicy.CanRetryPreflight(4),
                "第 5 次预检恢复仍可重试");
            passed++;

            AssertTrue(
                !NativeRecoveryPolicy.CanRetryPreflight(5),
                "连续 5 次预检失败后停止");
            passed++;

            AssertTrue(
                NativeRecoveryPolicy.CanRecoverSession(2),
                "第 3 次会话恢复仍可继续");
            passed++;

            AssertTrue(
                !NativeRecoveryPolicy.CanRecoverSession(3),
                "连续 3 次会话恢复后停止");
            passed++;

            NativePageSnapshot before = Snapshot(
                100000,
                200000,
                5000,
                896000,
                "account-a");
            NativePageSnapshot after = Snapshot(
                101000,
                201000,
                6000,
                896000,
                "account-a");
            AssertTrue(
                NativeRecoveryPolicy.IsBatchConfirmed(before, after, 1000),
                "保存后达到双计数目标即确认");
            passed++;

            NativePageSnapshot delayed = Snapshot(
                before.Available,
                before.Cumulative,
                before.Local,
                before.Consumed,
                before.AccountKey);
            AssertTrue(
                !NativeRecoveryPolicy.IsBatchConfirmed(before, delayed, 1000),
                "延迟回写前不确认");
            passed++;

            AssertTrue(
                NativeRecoveryPolicy.IsStableNoCoreChange(before, delayed),
                "保存回滚且核心计数未前进时可安全重基准");
            passed++;

            NativePageSnapshot partialAfter = Snapshot(
                100400,
                200400,
                0,
                896000,
                "account-a");
            AssertTrue(
                !NativeRecoveryPolicy.IsStableNoCoreChange(
                    before,
                    partialAfter),
                "部分核心计数变化时不允许回滚重试");
            passed++;

            AssertTrue(
                NativeRecoveryPolicy.Remaining(
                    500000,
                    510000,
                    Snapshot(400000, 450000, 0, 0, "account-a")) == 100000,
                "按可用与累计计数的较大缺口计算剩余量");
            passed++;

            NativePageSnapshot changedAccount = Snapshot(
                101000,
                201000,
                6000,
                896000,
                "account-b");
            AssertTrue(
                NativeRecoveryPolicy.IsAccountChanged(before, changedAccount),
                "账号摘要变化禁止继续");
            passed++;

            NativePageSnapshot partial = Snapshot(
                100400,
                200400,
                0,
                896000,
                "account-a");
            AssertTrue(
                NativeRecoveryPolicy.CanRebase(before, before, partial),
                "部分增量且 local 归零后可精确重基准");
            passed++;

            NativePageSnapshot externallyAdvanced = Snapshot(
                101250,
                201250,
                0,
                896100,
                "account-a");
            AssertTrue(
                NativeRecoveryPolicy.CanRebase(
                    before,
                    partial,
                    externallyAdvanced),
                "页面额外增量可重基准且不重复旧批次");
            passed++;

            NativePageSnapshot regressed = Snapshot(
                99999,
                200500,
                0,
                896000,
                "account-a");
            AssertTrue(
                !NativeRecoveryPolicy.CanRebase(before, before, regressed),
                "可用计数倒退时拒绝重基准");
            passed++;

            AssertTrue(
                !NativeRecoveryPolicy.CanRebase(
                    before,
                    before,
                    changedAccount),
                "账号变化时拒绝重基准");
            passed++;

            string detail = NativeRecoveryPolicy.DescribeDelta(
                "post-save-state-not-confirmed",
                3,
                101000,
                201000,
                before,
                delayed);
            AssertTrue(
                detail.Contains("attempt=3") &&
                detail.Contains("expected_available=101000") &&
                detail.Contains("actual_available=100000") &&
                detail.Contains("expected_cumulative=201000") &&
                detail.Contains("actual_cumulative=200000") &&
                !detail.Contains("account-a"),
                "恢复日志包含差分且不包含账号摘要");
            passed++;
        }

        private static NativePageSnapshot Snapshot(
            long available,
            long cumulative,
            long local,
            long consumed,
            string accountKey)
        {
            return new NativePageSnapshot {
                LoggedIn = true,
                AccountKey = accountKey,
                Available = available,
                Cumulative = cumulative,
                Local = local,
                Consumed = consumed,
                ServerTotal = 0
            };
        }

        private static void AssertTrue(bool condition, string name)
        {
            if (!condition)
                throw new InvalidOperationException(name);
            Console.WriteLine("PASS " + name);
        }
    }
}
