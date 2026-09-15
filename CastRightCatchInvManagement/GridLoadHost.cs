namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Wraps a DataGridView with a spinner and faded colors while rows load off the UI thread.
    /// A second request while busy waits until the current load finishes, then runs once more.
    /// </summary>
    internal sealed class GridLoadHost : Panel
    {
        private readonly DataGridView _grid;
        private readonly WaitSpinner _spinner;
        private bool _busy;
        private Action? _pending;

        public GridLoadHost(DataGridView grid)
        {
            _grid = grid;
            Dock = grid.Dock;
            Name = "GridLoadHost";
            BackColor = Theme.Paper;
            _grid.Dock = DockStyle.Fill;
            _spinner = new WaitSpinner
            {
                Size = new Size(48, 48),
                BackColor = Theme.Paper,
                Visible = false
            };
            Controls.Add(_grid);
            Controls.Add(_spinner);
            Resize += (_, _) => CenterSpinner();
        }

        /// <summary>
        /// Wrap the grid for spinner/fade. Parent is DataBody (DataStack row 2), which only holds the table.
        /// Search is row 0 and the buttons are row 1 — wrapping here cannot cover them.
        /// </summary>
        public static GridLoadHost Ensure(DataGridView grid)
        {
            // Already wrapped from a previous FillGrid; do not nest hosts (spinner inside spinner).
            if (grid.Parent is GridLoadHost existing)
                return existing;

            // `grid.Parent` is WinForms Control.Parent of the DataGridView.
            // After ApplyDataPage, that is DataBody (table cell only), NOT DataSearch and NOT DataToolbar.
            var parent = grid.Parent;
            // Constructor ran before ApplyDataPage sited the grid; wrapping now would leave the host with no parent.
            if (parent == null)
                return new GridLoadHost(grid);

            parent.Controls.Remove(grid);
            var host = new GridLoadHost(grid);
            host.Dock = DockStyle.Fill;
            parent.Controls.Add(host);
            return host;
        }

        /// <summary>Fade the grid and show the spinner.</summary>
        public void BeginLoad()
        {
            Theme.FadeGrid(_grid, true);
            _spinner.Visible = true;
            _spinner.BringToFront();
            CenterSpinner();
        }

        /// <summary>Restore full color and hide the spinner when rows are in.</summary>
        public void EndLoad()
        {
            // Host already torn down with the page; skip StyleGrid on a disposed control.
            if (IsDisposed)
                return;
            Theme.FadeGrid(_grid, false);
            _spinner.Visible = false;
        }

        /// <summary>Read on a worker, then apply on the UI thread that started the load.</summary>
        public static void Run<T>(DataGridView grid, Func<CancellationToken, T> load, Action<T> apply)
        {
            var host = Ensure(grid);
            // One load at a time. Queue a single follow-up if the table changes mid-read.
            if (host._busy)
            {
                host._pending = () => Run(grid, load, apply);
                return;
            }

            host._busy = true;
            host.BeginLoad();
            var ui = SynchronizationContext.Current;
            Task.Run(() =>
            {
                T result = default!;
                bool ok = false;
                try
                {
                    result = load(CancellationToken.None);
                    ok = true;
                }
                catch
                {
                    ok = false;
                }

                void Finish()
                {
                    try
                    {
                        // Worker succeeded and the page is still open: push rows onto the grid.
                        if (ok && !grid.IsDisposed)
                            apply(result);
                    }
                    finally
                    {
                        host._busy = false;
                        // Restore full color / hide spinner unless the page closed mid-load.
                        if (!host.IsDisposed)
                            host.EndLoad();
                        var again = host._pending;
                        host._pending = null;
                        again?.Invoke();
                    }
                }

                PostToUi(grid, ui, Finish);
            });
        }

        private static void PostToUi(DataGridView grid, SynchronizationContext? ui, Action apply)
        {
            void Go()
            {
                // Page closed while the worker was reading; do not touch a disposed grid.
                if (grid.IsDisposed)
                    return;
                apply();
            }

            // Prefer the UI SynchronizationContext captured when Run started (WinForms message pump).
            if (ui != null)
            {
                ui.Post(_ => Go(), null);
                return;
            }

            // No context (tests / early ctor): Invoke if the HWND already exists.
            if (grid.IsHandleCreated)
            {
                grid.BeginInvoke(Go);
                return;
            }

            EventHandler? once = null;
            once = (_, _) =>
            {
                grid.HandleCreated -= once;
                Go();
            };
            grid.HandleCreated += once;
            // Handle was created between the check above and the subscribe; run now to avoid waiting forever.
            if (grid.IsHandleCreated)
            {
                grid.HandleCreated -= once;
                Go();
            }
        }

        private void CenterSpinner()
        {
            _spinner.Location = new Point(
                Math.Max(0, (ClientSize.Width - _spinner.Width) / 2),
                Math.Max(0, (ClientSize.Height - _spinner.Height) / 2));
        }
    }
}
