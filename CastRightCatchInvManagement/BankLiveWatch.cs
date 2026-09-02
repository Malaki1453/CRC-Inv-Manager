namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Pulls the live bank feed on a timer (1 or 3 hours) while the app is open.
    /// Interval is an administrator setting.
    /// </summary>
    internal static class BankLiveWatch
    {
        private static System.Windows.Forms.Timer? _timer;

        public static void Start()
        {
            Stop();
            if (AppState.PlaidSyncHours <= 0)
                return;

            _timer = new System.Windows.Forms.Timer { Interval = 60_000 };
            _timer.Tick += async (_, _) => await TickAsync();
            _timer.Start();
            _ = TickAsync();
        }

        public static void Stop()
        {
            if (_timer == null)
                return;
            _timer.Stop();
            _timer.Dispose();
            _timer = null;
        }

        private static async Task TickAsync()
        {
            int hours = AppState.PlaidSyncHours;
            if (hours <= 0 || !PlaidClient.IsConfigured)
                return;
            if (AppState.PlaidLastSync is DateTime last &&
                DateTime.Now - last < TimeSpan.FromHours(hours))
                return;

            await BankLive.SyncAllQuietAsync();
        }
    }
}
