namespace CastRightCatchInvManagement
{
    /// <summary>
    /// For users with Stay signed in: close the app after Admin's idle hours with no mouse or keyboard use.
    /// </summary>
    internal static class IdleWatch
    {
        private static DateTime _lastActivityUtc = DateTime.UtcNow;
        private static System.Windows.Forms.Timer? _timer;
        private static Filter? _filter;
        private static bool _closing;

        /// <summary>Start watching once the main window is up. No-op if Stay signed in is off.</summary>
        public static void Start()
        {
            if (!AppState.StaySignedIn)
                return;
            if (_timer != null)
            {
                NoteActivity();
                return;
            }

            _lastActivityUtc = DateTime.UtcNow;
            _filter = new Filter();
            Application.AddMessageFilter(_filter);
            _timer = new System.Windows.Forms.Timer { Interval = 30_000 };
            _timer.Tick += (_, _) => CheckIdle();
            _timer.Start();
        }

        public static void Stop()
        {
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }

            if (_filter != null)
            {
                Application.RemoveMessageFilter(_filter);
                _filter = null;
            }
        }

        public static void NoteActivity() => _lastActivityUtc = DateTime.UtcNow;

        private static void CheckIdle()
        {
            if (_closing || !AppState.StaySignedIn)
                return;
            if (DateTime.UtcNow - _lastActivityUtc < Accounts.IdleCloseAfter)
                return;

            _closing = true;
            Stop();
            Application.Exit();
        }

        private sealed class Filter : IMessageFilter
        {
            private const int WmKeyDown = 0x0100;
            private const int WmSysKeyDown = 0x0104;
            private const int WmLButtonDown = 0x0201;
            private const int WmRButtonDown = 0x0204;
            private const int WmMButtonDown = 0x0207;
            private const int WmMouseWheel = 0x020A;
            private const int WmMouseMove = 0x0200;

            public bool PreFilterMessage(ref Message m)
            {
                switch (m.Msg)
                {
                    case WmKeyDown:
                    case WmSysKeyDown:
                    case WmLButtonDown:
                    case WmRButtonDown:
                    case WmMButtonDown:
                    case WmMouseWheel:
                    case WmMouseMove:
                        NoteActivity();
                        break;
                }

                return false;
            }
        }
    }
}
