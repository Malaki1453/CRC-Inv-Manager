namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Pulls the live bank feed on a timer (1 or 3 hours) while the app is open.
    /// Interval is an administrator setting.
    /// </summary>
    internal static class BankLiveWatch
    {
        private static System.Windows.Forms.Timer? _timer;

        /// <summary>Start the one-minute poller, or stay idle when auto-sync is off.</summary>
        public static void Start()
        {
            Stop();
            // Auto-sync is an admin setting; skip the timer when it is Off.
            if (AppState.PlaidSyncHours <= 0)
                return;

            _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _timer.Tick += async (_, _) => await TickAsync();
            _timer.Start();
            _ = TickAsync();
        }

        /// <summary>Tear down the poller so a closed app or setting change does not keep syncing.</summary>
        public static void Stop()
        {
            // Nothing to dispose when the watch was never started.
            if (_timer == null)
                return;
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }

        /// <summary>Sync when the configured interval has elapsed and Plaid keys exist.</summary>
        private static async Task TickAsync()
        {
            int hours = AppState.PlaidSyncHours;
            // Keys or auto-sync may have been turned off since the last tick.
            if (hours <= 0 || !PlaidClient.IsConfigured)
                return;
            // Stay quiet until the admin interval (1 or 3 hours) has passed.
            if (AppState.PlaidLastSync is DateTime last &&
                DateTime.Now - last < TimeSpan.FromHours(hours))
                return;

            await BankLive.SyncAllQuietAsync();
        }
    }
}
