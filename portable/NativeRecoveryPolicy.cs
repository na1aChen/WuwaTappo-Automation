using System;
using System.Globalization;

namespace WuwaTappoCdpHost
{
    internal static class NativeRecoveryPolicy
    {
        public const int MaxPreflightRetries = 5;
        public const int MaxSessionRecoveries = 3;
        public const int PostSavePollCount = 5;
        public const int PostSavePollDelayMilliseconds = 400;

        public static bool IsPreflightConflict(string error)
        {
            return String.Equals(
                error,
                "store-state-changed",
                StringComparison.Ordinal);
        }

        public static bool IsPostSaveUnconfirmed(string error)
        {
            return String.Equals(
                error,
                "post-save-state-not-confirmed",
                StringComparison.Ordinal);
        }

        public static bool IsIncrementUnconfirmed(string error)
        {
            return String.Equals(
                error,
                "increment-not-confirmed",
                StringComparison.Ordinal);
        }

        public static bool CanRetryPreflight(int failedAttempts)
        {
            return failedAttempts >= 0 &&
                   failedAttempts < MaxPreflightRetries;
        }

        public static bool CanRecoverSession(int recoveries)
        {
            return recoveries >= 0 &&
                   recoveries < MaxSessionRecoveries;
        }

        public static bool IsBatchConfirmed(
            NativePageSnapshot before,
            NativePageSnapshot after,
            int delta)
        {
            return before != null &&
                   after != null &&
                   delta > 0 &&
                   after.LoggedIn &&
                   after.Available >= before.Available + delta &&
                   after.Cumulative >= before.Cumulative + delta;
        }

        public static bool IsStableNoCoreChange(
            NativePageSnapshot before,
            NativePageSnapshot observed)
        {
            return before != null &&
                   observed != null &&
                   observed.LoggedIn &&
                   observed.Available == before.Available &&
                   observed.Cumulative == before.Cumulative;
        }

        public static bool IsAccountChanged(
            NativePageSnapshot start,
            NativePageSnapshot current)
        {
            return start == null ||
                   current == null ||
                   !current.LoggedIn ||
                   !String.Equals(
                       start.AccountKey,
                       current.AccountKey,
                       StringComparison.Ordinal);
        }

        public static bool CanRebase(
            NativePageSnapshot start,
            NativePageSnapshot current,
            NativePageSnapshot observed)
        {
            return !IsAccountChanged(start, observed) &&
                   observed.Available >= current.Available &&
                   observed.Cumulative >= current.Cumulative;
        }

        public static long Remaining(
            long targetAvailable,
            long targetCumulative,
            NativePageSnapshot current)
        {
            if (current == null)
                return targetAvailable;
            long availableRemaining = targetAvailable - current.Available;
            long cumulativeRemaining = targetCumulative - current.Cumulative;
            return Math.Max(0L, Math.Max(availableRemaining, cumulativeRemaining));
        }

        public static string DescribeDelta(
            string reason,
            int attempt,
            long expectedAvailable,
            long expectedCumulative,
            NativePageSnapshot baseline,
            NativePageSnapshot actual)
        {
            return String.Format(
                CultureInfo.InvariantCulture,
                "attempt={0}; reason={1}; expected_available={2}; actual_available={3}; " +
                "expected_cumulative={4}; actual_cumulative={5}; " +
                "baseline_local={6}; actual_local={7}; baseline_consumed={8}; actual_consumed={9}",
                attempt,
                reason ?? "",
                expectedAvailable,
                actual == null ? -1L : actual.Available,
                expectedCumulative,
                actual == null ? -1L : actual.Cumulative,
                baseline == null ? -1L : baseline.Local,
                actual == null ? -1L : actual.Local,
                baseline == null ? -1L : baseline.Consumed,
                actual == null ? -1L : actual.Consumed);
        }
    }
}
