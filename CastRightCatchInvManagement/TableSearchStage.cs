using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Search UI for data pages. Built in UiStyle.ApplyDataPage as DataStack row 0.
    /// Idle: that row is Percent 100 (centered box); rows 1–2 (buttons, table) are 0.
    /// Compact: row 0 is 56px, row 1 is the buttons, row 2 is the table.
    /// PlaceAboveTableAndButtons owns that switch. Search never Dock-Fills the same
    /// parent as the grid — that is what used to pin the table to the page top.
    /// </summary>
    internal sealed class TableSearchStage : Panel
    {
        private const int CompactHeight = 56;
        /// <summary>DataToolbar is 50px; 8px extra is the gap under the search bar.</summary>
        private const int ToolbarRowHeight = 58;
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

        /// <summary>Dates popover is open (From/To boxes visible).</summary>
        private bool _datesOpen;
        /// <summary>
        /// Compact-search flag. True after the user types a query or sets dates:
        /// DataStack row 0 is 56px, row 1 (buttons) and row 2 (table) are shown.
        /// False = idle overlay: row 0 fills the stack, rows 1–2 are 0, FillGrid skips load.
        /// Used in PlaceAboveTableAndButtons as `if (!_active)` for idle vs compact.
        /// </summary>
        private bool _active;
        /// <summary>Idle-return animation is running (box sliding back to center).</summary>
        private bool _animating;
        /// <summary>
        /// Where SetActive wants to go. True = compact/searching, false = idle overlay.
        /// Can differ from _active while the idle-return animation is still running.
        /// </summary>
        private bool _toActive;
        private DateTime _animStarted;
        private Rectangle _boxFrom;

        /// <summary>True when compact search is showing (user has typed or set dates).</summary>
        public bool IsSearching => _active;

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
                // Page just became visible (tab switch / first show): put the caret in the search box.
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
            PlaceAboveTableAndButtons();
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
            // Panel already torn down with the page; invoking would throw ObjectDisposedException.
            if (IsDisposed)
                return;
            // Native HWND exists: queue so layout/focus runs after the current paint, not mid-constructor.
            if (IsHandleCreated)
                BeginInvoke(action);
            else
                // Handle not created yet (still in ctor / before shown): run now so idle bounds still apply.
                action();
        }

        /// <summary>Start the idle↔compact animation when the search/date state actually changes.</summary>
        private void SetActive(bool active)
        {
            // Same target as now (and not mid-animation): skip so we do not restart layout or reload the grid.
            if (_toActive == active && (_animating || _active == active))
                return;

            _toActive = active;
            StartAnim();
            // active == true: user typed/set dates. Load rows now that the overlay is gone and the grid is visible.
            if (active && FindForm() is INavigationPage page)
                page.HighlightCurrentPage();
        }

        /// <summary>Capture start bounds and dock so the timer can lerp toward compact or idle.</summary>
        private void StartAnim()
        {
            _animating = true;
            _animStarted = DateTime.UtcNow;
            // Keep Dock Fill so this panel stays in DataStack row 0. Do not clear Anchor —
            // AnchorStyles.None let the old overlay float over the grid.

            // _toActive true: going compact (searching). Snap the stack rows; do not animate height over the grid.
            if (_toActive)
            {
                _animating = false;
                _active = true;
                _toolbar.Visible = true;
                ShowChrome(false);
                PlaceAboveTableAndButtons();
                ApplyLayout();
                FocusBox();
                return;
            }

            // Going idle and no date range: clear the grid filter so the table hides with the overlay.
            if (!HasDates())
                _columnSearch.SetGlobalQuery("");
            _toolbar.Visible = false;
            _boxFrom = _box.Bounds;
            ShowChrome(false);

            _anim.Start();
        }

        /// <summary>Ease the box and height toward compact (searching) or idle (empty).</summary>
        private void TickAnim()
        {
            double elapsed = (DateTime.UtcNow - _animStarted).TotalMilliseconds;
            float t = (float)Math.Clamp(elapsed / AnimMs, 0, 1);
            t = 1f - (1f - t) * (1f - t);

            _box.Bounds = Lerp(_boxFrom, IdleBoxBounds(Width, Height), t);
            ShowChrome(t > 0.45f);

            // Animation still running: wait for the next tick.
            if (t < 1f)
                return;

            _anim.Stop();
            _animating = false;
            _active = _toActive;
            _toolbar.Visible = _active;
            PlaceAboveTableAndButtons();
            ApplyLayout();
            FocusBox();
        }

        /// <summary>
        /// Switch DataStack rows after the user types (or clears search).
        /// Parent is DataStack (TableLayoutPanel), not the CardPanel and not DataBody.
        /// Idle: row 0 Percent 100, rows 1–2 height 0 (no buttons, no table).
        /// Compact: row 0 = 56px search, row 1 = buttons (8px gap via margin), row 2 = table.
        /// Each row owns its rectangle, so the table top is the bottom of the buttons, not the page top.
        /// </summary>
        private void PlaceAboveTableAndButtons()
        {
            // `Parent` is WinForms Control.Parent. ApplyDataPage parents this panel to DataStack.
            // Local `stack` is that TableLayoutPanel. It is NOT the Form, NOT DataBody, NOT the CardPanel.
            if (Parent is not TableLayoutPanel stack || stack.Name != "DataStack" || stack.RowCount < 3)
                return;

            Control? toolbar = stack.Controls["DataToolbar"];
            Control? body = stack.Controls["DataBody"];

            stack.SuspendLayout();
            // _active == false: idle overlay. User has not typed a query and has not set dates.
            // Purpose: search cell fills the page. Collapse button and table rows so the grid
            // cannot sit at the form top or steal clicks. FillGrid stays skipped.
            if (!_active)
            {
                stack.RowStyles[0] = new RowStyle(SizeType.Percent, 100f);
                stack.RowStyles[1] = new RowStyle(SizeType.Absolute, 0f);
                stack.RowStyles[2] = new RowStyle(SizeType.Absolute, 0f);
                if (toolbar != null)
                    toolbar.Visible = false;
                if (body != null)
                    body.Visible = false;
                Dock = DockStyle.Fill;
                stack.ResumeLayout(true);
                return;
            }

            // _active == true: compact search. User typed or set dates.
            // Purpose: search is a 56px row at the top of the stack. Buttons are the next row
            // (aligned to the bottom of search + 8px margin). Table is the leftover row under the buttons.
            stack.RowStyles[0] = new RowStyle(SizeType.Absolute, CompactHeight);
            stack.RowStyles[1] = new RowStyle(SizeType.Absolute, ToolbarRowHeight);
            stack.RowStyles[2] = new RowStyle(SizeType.Percent, 100f);
            if (toolbar != null)
                toolbar.Visible = true;
            if (body != null)
                body.Visible = true;
            Dock = DockStyle.Fill;
            stack.ResumeLayout(true);
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
            // Compact bar: SEARCH label + box along the top; no page title.
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
            // This table has date columns (Purchases/Sales/etc.), so show the Dates toggle.
            if (can)
                PlaceDatesToggle(_box.Right + 8);
            // From/To are open: leave room under the box for those fields.
            if (dates)
            {
                LayoutIdleDates(idle);
                _hint.Bounds = new Rectangle(idle.X, idle.Bottom + 52, idle.Width, 22);
            }
            else
                // Dates closed: hint sits just under the box with no From/To row in between.
                _hint.Bounds = new Rectangle(idle.X, idle.Bottom + 12, idle.Width, 22);
        }

        /// <summary>Show page title and hint when idle; show the SEARCH label when compact.</summary>
        private void ShowChrome(bool idle)
        {
            _title.Visible = idle;
            _gold.Visible = idle;
            _hint.Visible = idle;
            _searchLabel.Visible = !idle;
            // Compact bar: pin the SEARCH caption to the left of the box.
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
            // Page closed while columns were still building; skip layout on a disposed panel.
            if (IsDisposed)
                return;
            // ColumnsReady can fire off the UI thread; marshal so we only touch control bounds here.
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
            // Closing Dates must drop the range so leftover From/To do not keep the table filtered.
            if (!_datesOpen)
            {
                _from.ClearDate();
                _to.ClearDate();
                _columnSearch.SetDateRange(null, null);
                SetActive(HasQuery());
            }

            ApplyLayout();
            // Opening Dates: put the caret in From so the user can type immediately.
            if (_datesOpen)
                _from.Focus();
        }

        /// <summary>Gold fill while date fields are open; outline when they are hidden.</summary>
        private void StyleDatesButton()
        {
            // Gold fill = dates popover is open (active filter UI). Outline = Dates is available but closed.
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
            // From/To are showing: shrink the search box so Dates + date fields fit on the right of the bar.
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
                // Table has date columns but From/To are closed: box + Dates button only.
                _box.Bounds = new Rectangle(96, 12, Math.Max(80, width - right - btnW - 12 - 96), 28);
                PlaceDatesToggle(width - right - btnW);
            }
            else
                // Inventory/etc. have no date columns: search box uses the full compact width.
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
            // Skip if the page closed or the box cannot take keyboard focus yet (not shown / disabled).
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
