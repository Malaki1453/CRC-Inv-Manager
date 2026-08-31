namespace CastRightCatchInvManagement
{
    internal sealed class ToastAlert : Panel
    {
        private readonly System.Windows.Forms.Timer _timer;
        private readonly bool _error;
        private Control? _host;

        private ToastAlert(string message, bool error, int milliseconds)
        {
            _error = error;
            Size = new Size(360, 72);
            Anchor = AnchorStyles.Bottom | AnchorStyles.Right;
            BackColor = error ? Theme.DangerFill : Theme.SuccessFill;
            Theme.EnableDoubleBuffer(this);

            var close = new Button
            {
                Text = "×",
                Dock = DockStyle.Right,
                Width = 36,
                FlatStyle = FlatStyle.Flat,
                Font = Theme.BodyBold,
                ForeColor = error ? Theme.Danger : Theme.Success,
                BackColor = BackColor,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            close.FlatAppearance.BorderSize = 0;
            close.FlatAppearance.MouseOverBackColor = error
                ? Color.FromArgb(255, 220, 214)
                : Color.FromArgb(210, 235, 218);
            close.Click += (_, _) => Dismiss();

            var label = new Label
            {
                Text = message,
                Dock = DockStyle.Fill,
                AutoSize = false,
                Font = Theme.BodyBold,
                ForeColor = error ? Theme.Danger : Theme.Success,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 8, 8, 8)
            };

            Controls.Add(label);
            Controls.Add(close);

            _timer = new System.Windows.Forms.Timer { Interval = Math.Max(400, milliseconds) };
            _timer.Tick += (_, _) => Dismiss();
            _timer.Start();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var border = new Pen(_error ? Theme.Danger : Theme.Success, 2);
            e.Graphics.DrawRectangle(border, 1, 1, Width - 3, Height - 3);
        }

        public void Dismiss()
        {
            if (IsDisposed)
                return;

            _timer.Stop();
            _timer.Dispose();
            if (_host != null)
                _host.Resize -= OnHostResize;
            Parent?.Controls.Remove(this);
            Dispose();
        }

        public static void Success(Control host, string message) => Show(host, message, error: false);

        public static void Error(Control host, string message) => Show(host, message, error: true);

        private static void Show(Control host, string message, bool error)
        {
            foreach (var old in host.Controls.OfType<ToastAlert>().ToList())
                old.Dismiss();

            var toast = new ToastAlert(message, error, 1000);
            toast._host = host;
            host.Controls.Add(toast);
            toast.Place();
            toast.BringToFront();
            host.Resize += toast.OnHostResize;
        }

        private void OnHostResize(object? sender, EventArgs e) => Place();

        private void Place()
        {
            if (_host == null || _host.IsDisposed)
                return;

            int x = Math.Max(12, _host.ClientSize.Width - Width - 20);
            int y = Math.Max(12, _host.ClientSize.Height - Height - 20);
            Location = new Point(x, y);
        }
    }
}
