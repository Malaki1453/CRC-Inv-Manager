using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Idle: only a centered search field. Typing slides it to the top and reveals the table.
    /// </summary>
    internal sealed class TableSearchStage : Panel
    {
        private const int CompactHeight = 56;
        private const int AnimMs = 260;

        private readonly Label _title;
        private readonly Panel _gold;
        private readonly Label _hint;
        private readonly Label _searchLabel;
        private readonly TextBox _box;
        private readonly Button _datesToggle;
        private readonly Label _fromLabel;
        private readonly NumericDateBox _from;
        private readonly Label _toLabel;
        private readonly NumericDateBox _to;
        private readonly Panel _toolbar;
        private readonly ColumnSearch _columnSearch;
        private readonly System.Windows.Forms.Timer _debounce;
        private readonly System.Windows.Forms.Timer _anim;

        private bool _datesOpen;
        private bool _active;
        private bool _animating;
        private bool _toActive;
        private DateTime _animStarted;
        private Rectangle _boxFrom;
        private int _heightFrom;
        private int _heightTo;

        /// <summary>Build the idle search overlay; the grid stays hidden until the user types or picks dates.</summary>
        public TableSearchStage(string title, Panel toolbar, ColumnSearch columnSearch)
        {
            _toolbar = toolbar;
            _columnSearch = columnSearch;

            Name = "DataSearch";
            Dock = DockStyle.Fill;
            BackColor = Theme.Paper;
            Theme.EnableDoubleBuffer(this);

            _title = new Label
            {
                Text = title,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                AutoSize = true
            };
            _gold = new Panel { BackColor = Theme.Gold, Size = new Size(52, 3) };
            _hint = new Label
            {
                Text = "Search any field",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Height = 22
            };
            _searchLabel = new Label
            {
                Text = "SEARCH",
                Visible = false,
                TextAlign = ContentAlignment.MiddleLeft
            };
            Theme.StyleFieldLabel(_searchLabel);

            _box = new TextBox { PlaceholderText = "Type to find a row", AutoSize = false };
            Theme.StyleField(_box);
            _box.Font = Theme.Body;

            _debounce = new System.Windows.Forms.Timer { Interval = 120 };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                _columnSearch.SetGlobalQuery(_box.Text);
            };
            _fromLabel = new Label
            {
                Text = "FROM",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Theme.StyleFieldLabel(_fromLabel);
            _toLabel = new Label
            {
                Text = "TO",
                TextAlign = ContentAlignment.MiddleLeft
            };
            Theme.StyleFieldLabel(_toLabel);
            _datesToggle = new Button
            {
                Text = "Dates",
                Size = new Size(64, 28),
                Visible = false,
                TextAlign = ContentAlignment.MiddleCenter,
                Padding = Padding.Empty,
                Margin = Padding.Empty
            };
            Theme.StyleOutlineButton(_datesToggle);
            _datesToggle.Click += (_, _) => ToggleDates();
            _from = new NumericDateBox { Font = Theme.Body };
            _to = new NumericDateBox { Font = Theme.Body };
            _from.DateChanged += (_, _) => ApplyDates();
            _to.DateChanged += (_, _) => ApplyDates();

            _box.TextChanged += (_, _) =>
            {
                bool on = HasQuery();
                // Clearing the box must filter immediately so the table hides again.
                if (_box.Text.Trim().Length == 0)
                {
                    _debounce.Stop();
                    _columnSearch.SetGlobalQuery("");
                }
                else
                {
                    // Debounce while typing so every keystroke does not rescan the grid.
                    _debounce.Start();
                }

                SetActive(on);
            };
            _box.KeyDown += (_, e) =>
            {
                // Enter applies the query without waiting for the debounce timer.
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                _debounce.Stop();
                _columnSearch.SetGlobalQuery(_box.Text);
            };

            _anim = new System.Windows.Forms.Timer { Interval = 15 };
            _anim.Tick += (_, _) => TickAnim();

            Controls.Add(_to);
            Controls.Add(_toLabel);
            Controls.Add(_from);
            Controls.Add(_fromLabel);
            Controls.Add(_datesToggle);
            Controls.Add(_box);
            Controls.Add(_searchLabel);
            Controls.Add(_hint);
            Controls.Add(_gold);
            Controls.Add(_title);

            _columnSearch.ColumnsReady += SyncDateVisible;
            SyncDateVisible();

            _toolbar.Visible = false;
            Resize += (_, _) =>
            {
                // Skip layout while the box is sliding so the animation owns the bounds.
                if (!_animating)
                    ApplyLayout();
            };
            VisibleChanged += (_, _) =>
            {
                if (Visible)
                    Post(FocusBox);
            };
        }

        /// <summary>Draw the bottom rule that separates search from the table.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var line = new Pen(Theme.GridLine);
            e.Graphics.DrawLine(line, 0, Height - 1, Width, Height - 1);
        }

        /// <summary>Relayout after this panel is parented so idle bounds use the page size.</summary>
        protected override void OnParentChanged(EventArgs e)
        {
            base.OnParentChanged(e);
            Post(ApplyLayout);
        }

        /// <summary>Apply idle layout and focus the box once the native handle exists.</summary>
        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            ApplyLayout();
            Post(FocusBox);
        }

        /// <summary>Run on the UI thread after the handle exists; otherwise run immediately.</summary>
        private void Post(Action action)
        {
            if (IsDisposed)
                return;
            if (IsHandleCreated)
                BeginInvoke(action);
            else
                action();
        }

        /// <summary>Start the idle↔compact animation when the search/date state actually changes.</summary>
        private void SetActive(bool active)
        {
            // Ignore duplicate "on" while already compact, or "off" while already idle.
            if (_toActive == active && (_animating || _active == active))
                return;

            _toActive = active;
            StartAnim();
        }

        /// <summary>Capture start bounds and dock so the timer can lerp toward compact or idle.</summary>
        private void StartAnim()
        {
            var parent = Parent;
            int parentH = parent?.ClientSize.Height ?? Height;
            _animating = true;
            _animStarted = DateTime.UtcNow;
            Anchor = AnchorStyles.None;

            if (_toActive)
            {
                int startHeight = Dock == DockStyle.Fill ? parentH : Height;
                // A collapsed leftover height would animate from a sliver instead of the full page.
                if (startHeight < 80)
                    startHeight = parentH;
                Dock = DockStyle.Top;
                Height = startHeight;
                ApplyLayout();
                _boxFrom = _box.Bounds;
                _heightFrom = Height;
                _heightTo = CompactHeight;
            }
            else
            {
                // Leaving compact without a date filter must hide the table again.
                if (!HasDates())
                    _columnSearch.SetGlobalQuery("");
                _toolbar.Visible = false;
                _boxFrom = _box.Bounds;
                Dock = DockStyle.Fill;
                ShowChrome(false);
            }

            _anim.Start();
        }

        /// <summary>Ease the box and height toward compact (searching) or idle (empty).</summary>
        private void TickAnim()
        {
            double elapsed = (DateTime.UtcNow - _animStarted).TotalMilliseconds;
            float t = (float)Math.Clamp(elapsed / AnimMs, 0, 1);
            t = 1f - (1f - t) * (1f - t);

            int parentW = Parent?.ClientSize.Width ?? Width;

            if (_toActive)
            {
                Height = Lerp(_heightFrom, _heightTo, t);
                // Date fields only fit after the bar is mostly compact.
                if (ShowDates() && t > 0.55f)
                    LayoutCompact(parentW);
                else
                    _box.Bounds = Lerp(_boxFrom, CompactBoxBounds(parentW), t);
                ShowChrome(t <= 0.55f);
                if (t > 0.72f)
                    _toolbar.Visible = true;
            }
            else
            {
                _box.Bounds = Lerp(_boxFrom, IdleBoxBounds(Width, Height), t);
                ShowChrome(t > 0.45f);
            }

            // Keep interpolating until the ease reaches 1.
            if (t < 1f)
                return;

            _anim.Stop();
            _animating = false;
            _active = _toActive;
            _toolbar.Visible = _active;
            Dock = _active ? DockStyle.Top : DockStyle.Fill;
            if (_active)
                Height = CompactHeight;
            Parent?.PerformLayout();
            ApplyLayout();
            FocusBox();
        }

        /// <summary>Place title, search box, and optional date fields for idle or compact mode.</summary>
        private void ApplyLayout()
        {
            bool can = CanDates();
            bool dates = ShowDates();
            _datesToggle.Visible = can;
            _from.Visible = dates;
            _to.Visible = dates;
            _fromLabel.Visible = dates;
            _toLabel.Visible = dates;
            StyleDatesButton();
            if (_active)
            {
                ShowChrome(false);
                LayoutCompact(Width);
                return;
            }

            ShowChrome(true);
            var idle = IdleBoxBounds(Width, Height);
            _box.Bounds = idle;
            _title.Location = new Point(idle.X, Math.Max(12, idle.Y - 72));
            _gold.Location = new Point(idle.X, Math.Max(12, idle.Y - 32));
            if (can)
                PlaceDatesToggle(_box.Right + 8);
            if (dates)
            {
                LayoutIdleDates(idle);
                _hint.Bounds = new Rectangle(idle.X, idle.Bottom + 52, idle.Width, 22);
            }
            else
                _hint.Bounds = new Rectangle(idle.X, idle.Bottom + 12, idle.Width, 22);
        }

        /// <summary>Show page title and hint when idle; show the SEARCH label when compact.</summary>
        private void ShowChrome(bool idle)
        {
            _title.Visible = idle;
            _gold.Visible = idle;
            _hint.Visible = idle;
            _searchLabel.Visible = !idle;
            if (!idle)
                _searchLabel.Bounds = new Rectangle(16, 16, 78, 22);
        }

        /// <summary>Centered search box used before any query is typed.</summary>
        private static Rectangle IdleBoxBounds(int width, int height)
        {
            int w = Math.Max(280, Math.Min(560, width - 80));
            int h = 36;
            int x = Math.Max(20, (width - w) / 2);
            int y = Math.Max(80, height / 2 - 8);
            return new Rectangle(x, y, w, h);
        }

        /// <summary>Compact search box along the top bar after the overlay collapses.</summary>
        private static Rectangle CompactBoxBounds(int width) =>
            new(96, 12, Math.Max(120, width - 116), 28);

        private bool CanDates() => _columnSearch.HasDateColumns;

        private bool ShowDates() => CanDates() && _datesOpen;

        private bool HasDates() => _from.Value != null || _to.Value != null;

        private bool HasQuery() => _box.Text.Trim().Length > 0 || HasDates();

        /// <summary>Push the from/to range into column search and expand if a date is set.</summary>
        private void ApplyDates()
        {
            _columnSearch.SetDateRange(_from.Value, _to.Value);
            SetActive(HasQuery());
        }

        /// <summary>Relayout when columns finish building so the Dates button appears only on date tables.</summary>
        private void SyncDateVisible()
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(SyncDateVisible);
                return;
            }

            ApplyLayout();
        }

        /// <summary>Show or hide from/to fields; closing them also clears the date filter.</summary>
        private void ToggleDates()
        {
            // Tables without date columns have no range to filter.
            if (!CanDates())
                return;
            _datesOpen = !_datesOpen;
            if (!_datesOpen)
            {
                _from.ClearDate();
                _to.ClearDate();
                _columnSearch.SetDateRange(null, null);
                SetActive(HasQuery());
            }

            ApplyLayout();
            if (_datesOpen)
                _from.Focus();
        }

        /// <summary>Gold fill while date fields are open; outline when they are hidden.</summary>
        private void StyleDatesButton()
        {
            if (_datesOpen)
                Theme.StyleGoldButton(_datesToggle);
            else
                Theme.StyleOutlineButton(_datesToggle);
            _datesToggle.Text = "Dates";
        }

        /// <summary>Lay out search, Dates, and from/to along the compact top bar.</summary>
        private void LayoutCompact(int width)
        {
            bool dates = ShowDates();
            int dateW = 118;
            int labelW = 40;
            int btnW = 64;
            int right = 16;
            if (dates)
            {
                _to.Bounds = new Rectangle(width - right - dateW, 12, dateW, 28);
                _toLabel.Bounds = new Rectangle(_to.Left - labelW, 16, labelW, 22);
                _from.Bounds = new Rectangle(_toLabel.Left - 8 - dateW, 12, dateW, 28);
                _fromLabel.Bounds = new Rectangle(_from.Left - labelW, 16, labelW, 22);
                _box.Bounds = new Rectangle(96, 12, Math.Max(80, _fromLabel.Left - 8 - btnW - 12 - 96), 28);
                PlaceDatesToggle(_fromLabel.Left - 8 - btnW);
            }
            else if (CanDates())
            {
                _box.Bounds = new Rectangle(96, 12, Math.Max(80, width - right - btnW - 12 - 96), 28);
                PlaceDatesToggle(width - right - btnW);
            }
            else
                _box.Bounds = CompactBoxBounds(width);
        }

        /// <summary>Align the Dates button with the search box vertically.</summary>
        private void PlaceDatesToggle(int x)
        {
            int h = 28;
            int y = _box.Top + (_box.Height - h) / 2;
            _datesToggle.Bounds = new Rectangle(x, y, 64, h);
        }

        /// <summary>Place from/to under the idle search box when dates are expanded before typing.</summary>
        private void LayoutIdleDates(Rectangle idle)
        {
            int dateW = Math.Max(110, (idle.Width - 56) / 2);
            _fromLabel.Bounds = new Rectangle(idle.X, idle.Bottom + 12, 40, 18);
            _from.Bounds = new Rectangle(idle.X, idle.Bottom + 28, dateW, 28);
            _toLabel.Bounds = new Rectangle(idle.X + dateW + 16, idle.Bottom + 12, 40, 18);
            _to.Bounds = new Rectangle(idle.X + dateW + 16, idle.Bottom + 28, dateW, 28);
        }

        /// <summary>Put keyboard focus in the search box when the page is shown.</summary>
        private void FocusBox()
        {
            if (!IsDisposed && _box.CanFocus)
                _box.Focus();
        }

        private static int Lerp(int from, int to, float t) =>
            from + (int)Math.Round((to - from) * t);

        /// <summary>Interpolate rectangle bounds for the search-box slide.</summary>
        private static Rectangle Lerp(Rectangle from, Rectangle to, float t) =>
            new(
                Lerp(from.X, to.X, t),
                Lerp(from.Y, to.Y, t),
                Lerp(from.Width, to.Width, t),
                Lerp(from.Height, to.Height, t));
    }
}
