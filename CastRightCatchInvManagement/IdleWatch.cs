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
            // Idle close only applies to remembered sessions (Admin policy + checkbox).
            if (!AppState.StaySignedIn)
                return;
            // Already watching this process; just treat startup as activity.
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

        /// <summary>Stop the idle timer and message filter, e.g. before a forced exit.</summary>
        public static void Stop()
        {
            // Timer is null when Start never ran or Stop already ran.
            if (_timer != null)
            {
                _timer.Stop();
                _timer.Dispose();
                _timer = null;
            }

            // Filter is only installed after a stay-signed-in Start.
            if (_filter != null)
            {
                Application.RemoveMessageFilter(_filter);
                _filter = null;
            }
        }

        /// <summary>Record that the user just used the mouse or keyboard.</summary>
        public static void NoteActivity() => _lastActivityUtc = DateTime.UtcNow;

        /// <summary>Exit the app when the Admin idle-hours window has elapsed with no input.</summary>
        private static void CheckIdle()
        {
            // Policy may have been turned off, or we are already exiting.
            if (_closing || !AppState.StaySignedIn)
                return;
            // Still inside the allowed idle window.
            if (DateTime.UtcNow - _lastActivityUtc < Accounts.IdleCloseAfter)
                return;

            _closing = true;
            Stop();
            Application.Exit();
        }

        /// <summary>Treats keyboard and mouse messages as activity for the idle clock.</summary>
        private sealed class Filter : IMessageFilter
        {
            private const int WmKeyDown = 0x0100;
            private const int WmSysKeyDown = 0x0104;
            private const int WmLButtonDown = 0x0201;
            private const int WmRButtonDown = 0x0204;
            private const int WmMButtonDown = 0x0207;
            private const int WmXButtonDown = 0x020B;
            private const int WmMouseWheel = 0x020A;
            private const int WmMouseMove = 0x0200;

            /// <summary>Reset the idle clock on input without swallowing the message.</summary>
            public bool PreFilterMessage(ref Message m)
            {
                switch (m.Msg)
                {
                    // Any keyboard or mouse input means the user is still here.
                    case WmKeyDown:
                    case WmSysKeyDown:
                    case WmLButtonDown:
                    case WmRButtonDown:
                    case WmMButtonDown:
                    case WmXButtonDown:
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
