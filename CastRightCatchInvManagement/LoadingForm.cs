using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    /// <summary>Startup window with a spinner so a long open is visibly still working.</summary>
    internal sealed class LoadingForm : Form
    {
        private readonly Label _status;
        private readonly WaitSpinner _spinner;

        /// <summary>Centered navy splash shown before sign-in and the workspace.</summary>
        public LoadingForm()
        {
            Text = "Cast Right Catch Inventory";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            ControlBox = true;
            WindowChrome.Apply(this);
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(360, 220);
            BackColor = Theme.NavyDark;
            Font = Theme.Body;
            ForeColor = Theme.Cream;

            var gold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };

            _spinner = new WaitSpinner
            {
                Size = new Size(48, 48),
                Location = new Point(156, 36)
            };

            var title = new Label
            {
                Text = "CAST RIGHT CATCH",
                Font = Theme.BrandTitle,
                ForeColor = Theme.Cream,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 96),
                Size = new Size(320, 28)
            };

            _status = new Label
            {
                Text = "Starting…",
                Font = Theme.Small,
                ForeColor = Theme.GoldLight,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 132),
                Size = new Size(320, 44)
            };

            Controls.Add(_status);
            Controls.Add(title);
            Controls.Add(_spinner);
            Controls.Add(gold);
        }

        /// <summary>Update the status line from any thread without throwing if the splash closed.</summary>
        public void SetStatus(string text)
        {
            // User may have closed the splash while data was still loading.
            if (IsDisposed)
                return;
            // LoadData and settings run off the UI thread.
            if (InvokeRequired)
            {
                BeginInvoke(() => SetStatus(text));
                return;
            }

            _status.Text = string.IsNullOrWhiteSpace(text) ? "Starting…" : text;
            _status.Refresh();
        }

    }

    /// <summary>Gold arc spinner used on the startup window and while email is sending.</summary>
    internal sealed class WaitSpinner : Control
    {
        private readonly System.Windows.Forms.Timer _timer;
        private float _angle;

        /// <summary>16ms timer drives a rotating gold arc.</summary>
        public WaitSpinner()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Theme.NavyDark;
            _timer = new System.Windows.Forms.Timer { Interval = 16 };
            _timer.Tick += (_, _) =>
            {
                _angle = (_angle + 8f) % 360f;
                Invalidate();
            };
        }

        /// <summary>Start the arc animation once the native handle exists.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            _timer.Start();
        }

        /// <summary>Stop the animation timer with the control.</summary>
        protected override void Dispose(bool disposing)
        {
            // Drop the animation timer with the control so it does not tick after close.
            if (disposing)
                _timer.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Dim gold ring plus a rotating gold sweep.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.Clear(BackColor);
            int pad = 4;
            var bounds = new Rectangle(pad, pad, Width - pad * 2, Height - pad * 2);
            using var track = new Pen(Color.FromArgb(50, Theme.Gold), 4)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawArc(track, bounds, 0, 360);
            using var sweep = new Pen(Theme.Gold, 4)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round
            };
            e.Graphics.DrawArc(sweep, bounds, _angle, 110);
        }
    }

    /// <summary>Modal spinner while a long action (SMTP send) runs off the UI thread.</summary>
    internal sealed class WaitForm : Form
    {
        /// <summary>Show a blocking spinner, run work on a worker thread, then return the result.</summary>
        public static T Run<T>(IWin32Window? owner, string status, Func<T> work)
        {
            using var form = new WaitForm(status);
            T? result = default;
            Exception? error = null;
            form.Shown += async (_, _) =>
            {
                try
                {
                    result = await Task.Run(work);
                }
                catch (Exception ex)
                {
                    // Capture so the caller sees the original exception after the dialog closes.
                    error = ex;
                }

                // Work finished (or failed); close unless the user already dismissed it.
                if (!form.IsDisposed)
                    form.Close();
            };
            form.ShowDialog(owner);
            // Re-throw after the UI is gone so callers can show their own error.
            if (error != null)
                throw error;
            return result!;
        }

        /// <summary>Navy modal with spinner; no close box so the work cannot be cancelled mid-send.</summary>
        private WaitForm(string status)
        {
            Text = "Please wait";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = false;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(340, 180);
            BackColor = Theme.NavyDark;
            Font = Theme.Body;
            ForeColor = Theme.Cream;

            var gold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };
            var spinner = new WaitSpinner
            {
                Size = new Size(40, 40),
                Location = new Point(150, 28)
            };
            var label = new Label
            {
                Text = string.IsNullOrWhiteSpace(status) ? "Working…" : status,
                Font = Theme.Body,
                ForeColor = Theme.GoldLight,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Location = new Point(20, 84),
                Size = new Size(300, 56)
            };
            Controls.Add(label);
            Controls.Add(spinner);
            Controls.Add(gold);
        }
    }
}
