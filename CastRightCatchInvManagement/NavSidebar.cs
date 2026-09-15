using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    /// <summary>Navy left nav, including the Current/Old database toggle.</summary>
    internal sealed class NavSidebar : Panel
    {
        private readonly Dictionary<AppPage, CrcNavButton> _buttons = new();
        private readonly List<NavDropGroup> _groups = new();
        private readonly List<Control> _navItems = new();
        private readonly Workspace _workspace;
        private readonly ToolTip _tips = new() { ShowAlways = true };
        private NavyScrollPanel _navHost = null!;
        private int _menuRevision = -1;
        private CrcToggleSwitch _dbToggle = null!;
        private Label _lblCurrentDb = null!;
        private Label _lblOldDb = null!;

        /// <summary>Build brand, Current/Old switch, page list, Settings, and the signed-in user row.</summary>
        public NavSidebar(Workspace workspace)
        {
            _workspace = workspace;
            Dock = DockStyle.Left;
            Width = 236;
            BackColor = Theme.NavyDark;
            Theme.EnableDoubleBuffer(this);

            var brand = BuildBrandHeader();
            var userRow = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 40,
                BackColor = Theme.NavyDark,
                Name = "userRow"
            };
            var logout = new Button
            {
                Name = "btnLogOut",
                Text = "Log out",
                Dock = DockStyle.Right,
                Width = 78,
                FlatStyle = FlatStyle.Flat,
                Font = Theme.Small,
                ForeColor = Theme.GoldLight,
                BackColor = Theme.NavyDark,
                Cursor = Cursors.Hand,
                TabStop = false
            };
            logout.FlatAppearance.BorderSize = 0;
            logout.FlatAppearance.MouseOverBackColor = Theme.NavyHover;
            logout.FlatAppearance.MouseDownBackColor = Theme.NavyMid;
            logout.Click += (_, _) => Accounts.LogOutAndRestart();
            new ToolTip { ShowAlways = true }.SetToolTip(logout, "Sign out on this PC");
            var userLabel = new Label
            {
                Name = "lblNavUser",
                Dock = DockStyle.Fill,
                Font = Theme.Small,
                ForeColor = Theme.GoldLight,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 8, 0)
            };
            userRow.Controls.Add(userLabel);
            userRow.Controls.Add(logout);

            var settingsRow = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = Theme.NavyDark
            };
            var help = new CrcControlsButton();
            BindPageButton(help, AppPage.Help);
            _buttons[AppPage.Help] = help;
            help.Dock = DockStyle.Right;
            help.Width = 52;
            help.Name = "btnHelp";

            var settings = BuildButton(AppPage.Settings, "Settings");
            settings.Dock = DockStyle.Fill;
            settings.Name = "btnSettings";
            settingsRow.Controls.Add(settings);
            settingsRow.Controls.Add(help);

            _navHost = new NavyScrollPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.NavyDark,
                Padding = new Padding(10, 8, 14, 8)
            };

            _navHost.Resize += (_, _) => LayoutNav(_navHost, _navItems);
            RebuildMenu();

            var dbSwitch = BuildDatabaseSwitch();

            Controls.Add(_navHost);
            Controls.Add(settingsRow);
            Controls.Add(userRow);
            Controls.Add(dbSwitch);
            Controls.Add(brand);

            AppLock.Changed += RefreshState;
            RefreshState();
        }

        /// <summary>Unsubscribe from AppLock.Changed so a closed extra does not refresh.</summary>
        protected override void Dispose(bool disposing)
        {
            // Drop the static event so closed extras do not refresh.
            if (disposing)
                AppLock.Changed -= RefreshState;
            base.Dispose(disposing);
        }

        /// <summary>Enable/hide buttons by folder lock, table access, and Admin/Review roles.</summary>
        public void RefreshState()
        {
            if (IsDisposed)
                return;

            // AppLock.Changed can fire from a background settings load.
            if (InvokeRequired)
            {
                BeginInvoke(RefreshState);
                return;
            }

            bool unlocked = AppLock.HasFolder();
            if (_menuRevision != MenuLayout.Revision)
                RebuildMenu();

            foreach (var pair in _buttons)
            {
                bool isSettings = pair.Key == AppPage.Settings || pair.Key == AppPage.Help;
                bool isAdminPage = pair.Key == AppPage.Admin;
                if (isAdminPage)
                {
                    // Admin is staff-only; hide it from regular users.
                    bool staff = AppState.IsAdmin || AppState.IsIt;
                    pair.Value.Visible = staff;
                    pair.Value.Enabled = unlocked && staff;
                    ApplyOpenMark(pair.Key, pair.Value);
                    continue;
                }

                if (pair.Key == AppPage.PendingChanges)
                {
                    // Review is only for users who can approve queued edits.
                    bool review = DataAccess.CanReview();
                    pair.Value.Visible = review;
                    pair.Value.Enabled = unlocked && review;
                    ApplyOpenMark(pair.Key, pair.Value);
                    continue;
                }

                bool allowed = TableAccess.CanPage(pair.Key);
                pair.Value.Enabled = (unlocked || isSettings) && allowed;
                ApplyOpenMark(pair.Key, pair.Value);
                // Settings/Help stay listed so a folder can still be chosen.
                if (!isSettings && pair.Key != AppPage.Help)
                    pair.Value.Visible = allowed;
            }

            foreach (var group in _groups)
            {
                group.Visible = group.ShouldShow();
                group.ApplyAccess(unlocked);
                group.SyncExpanded();
            }

            LayoutNav(_navHost, _navItems);

            // Role or denials changed while this page was open; bounce to Home.
            if (_workspace.CurrentPage is AppPage current && !TableAccess.CanPage(current))
            {
                Navigator.GoTo(AppPage.Dashboard, _workspace, reuseOpenWindow: false);
                return;
            }

            if (Controls.Find("lblNavUser", true).FirstOrDefault() is Label userLabel)
            {
                string name = AppState.CurrentDisplayName.Length > 0
                    ? AppState.CurrentDisplayName
                    : AppState.CurrentUsername;
                userLabel.Text = AppState.SignedIn
                    ? (AppState.IsIt ? "IT  ·  " : AppState.IsAdmin ? "Admin  ·  " : "Signed in  ·  ") + name
                    : "";
            }

            SyncDatabaseSwitch(unlocked);
        }

        /// <summary>Stack visible nav items and size the custom scrollbar to the content.</summary>
        private static void LayoutNav(NavyScrollPanel host, List<Control> items)
        {
            int y = 4;
            int width = Math.Max(160, host.ContentWidth);
            foreach (var item in items)
            {
                // Hidden by access; skip so remaining items pack upward.
                if (!item.Visible)
                    continue;
                item.Location = new Point(0, y);
                item.Width = width;
                y += item.Height + 4;
            }

            host.SetContentHeight(y);
        }

        /// <summary>Seal and wordmark; click anywhere goes Home.</summary>
        private Panel BuildBrandHeader()
        {
            var brand = new Panel
            {
                Dock = DockStyle.Top,
                Height = 150,
                BackColor = Theme.NavyDark
            };
            Theme.EnableDoubleBuffer(brand);
            brand.Paint += (_, e) =>
            {
                using var pen = new Pen(Theme.Gold, 1);
                e.Graphics.DrawLine(pen, 18, brand.Height - 1, brand.Width - 18, brand.Height - 1);
            };

            // Header still reads without the seal if the PNG is missing.
            if (BrandAssets.Seal != null)
            {
                var pic = new PictureBox
                {
                    Image = BrandAssets.Seal,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point(18, 18),
                    Size = new Size(64, 64),
                    BackColor = Color.Transparent,
                    Cursor = Cursors.Hand
                };
                brand.Controls.Add(pic);
            }

            var title = new Label
            {
                Text = "CAST RIGHT",
                Font = Theme.BrandTitle,
                ForeColor = Theme.Cream,
                AutoSize = true,
                Location = new Point(90, 26),
                BackColor = Color.Transparent
            };
            var sub = new Label
            {
                Text = "Catch Co.",
                Font = Theme.BrandItalic,
                ForeColor = Theme.GoldLight,
                AutoSize = true,
                Location = new Point(90, 50),
                BackColor = Color.Transparent
            };
            var product = new Label
            {
                Text = "INVENTORY MANAGER",
                Font = Theme.Caption,
                ForeColor = Color.FromArgb(170, Theme.CreamDark),
                AutoSize = true,
                Location = new Point(18, 100),
                BackColor = Color.Transparent
            };

            brand.Controls.Add(title);
            brand.Controls.Add(sub);
            brand.Controls.Add(product);

            brand.Cursor = Cursors.Hand;
            void GoHome(object? _, EventArgs e) => Navigator.GoTo(AppPage.Dashboard);
            brand.Click += GoHome;
            foreach (Control child in brand.Controls)
            {
                child.Cursor = Cursors.Hand;
                child.Click += GoHome;
            }

            new ToolTip { ShowAlways = true }.SetToolTip(brand, "Home");
            return brand;
        }

        /// <summary>Current / Old toggle at the top of the nav list.</summary>
        private Panel BuildDatabaseSwitch()
        {
            var host = new Panel
            {
                Dock = DockStyle.Top,
                Height = 42,
                BackColor = Theme.NavyDark,
                Padding = new Padding(10, 4, 10, 8)
            };

            _lblCurrentDb = new Label
            {
                Text = "Current",
                Font = Theme.Caption,
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            _lblCurrentDb.Click += (_, _) => DataFiles.SetViewingOldInventory(false);

            _dbToggle = new CrcToggleSwitch();
            _dbToggle.Toggled += (_, _) => DataFiles.SetViewingOldInventory(_dbToggle.On);
            new ToolTip { ShowAlways = true }.SetToolTip(
                _dbToggle,
                "Off is the current database. On shows Old Inventory together with current work.");

            _lblOldDb = new Label
            {
                Text = "Old",
                Font = Theme.Caption,
                AutoSize = true,
                Cursor = Cursors.Hand,
                BackColor = Color.Transparent
            };
            _lblOldDb.Click += (_, _) => DataFiles.SetViewingOldInventory(true);

            host.Controls.Add(_lblCurrentDb);
            host.Controls.Add(_dbToggle);
            host.Controls.Add(_lblOldDb);
            host.Resize += (_, _) => LayoutDatabaseSwitch(host);
            LayoutDatabaseSwitch(host);
            SyncDatabaseSwitch(AppLock.HasFolder());
            return host;
        }

        /// <summary>Center the switch and park Current/Old labels on either side.</summary>
        private void LayoutDatabaseSwitch(Control host)
        {
            int inner = Math.Max(80, host.ClientSize.Width - host.Padding.Horizontal);
            int y = host.Padding.Top + 4;
            int switchX = host.Padding.Left + (inner - _dbToggle.Width) / 2;
            _dbToggle.Location = new Point(switchX, y);

            _lblCurrentDb.Location = new Point(
                Math.Max(host.Padding.Left, switchX - _lblCurrentDb.Width - 8),
                y + (_dbToggle.Height - _lblCurrentDb.Height) / 2);
            _lblOldDb.Location = new Point(
                switchX + _dbToggle.Width + 8,
                y + (_dbToggle.Height - _lblOldDb.Height) / 2);
        }

        /// <summary>Disable the switch until a folder exists; gold the selected Current/Old label.</summary>
        private void SyncDatabaseSwitch(bool unlocked)
        {
            if (_dbToggle == null)
                return;

            bool old = unlocked && AppState.ViewingOldInventory;
            _dbToggle.Enabled = unlocked;
            _dbToggle.SetOn(old);
            _lblCurrentDb.Enabled = unlocked;
            _lblOldDb.Enabled = unlocked;
            _lblCurrentDb.ForeColor = unlocked && !old ? Theme.GoldLight : Color.FromArgb(140, Theme.Cream);
            _lblOldDb.ForeColor = old ? Theme.GoldLight : Color.FromArgb(140, Theme.Cream);
        }

        /// <summary>Recreate folder dropdowns and page buttons from the shared menu layout.</summary>
        private void RebuildMenu()
        {
            foreach (var item in _navItems)
            {
                _navHost.Strip.Controls.Remove(item);
                item.Dispose();
            }

            _navItems.Clear();
            _groups.Clear();
            foreach (var key in _buttons.Keys.Where(page =>
                         page != AppPage.Settings && page != AppPage.Help).ToList())
                _buttons.Remove(key);

            var layout = MenuLayout.Load();
            _menuRevision = MenuLayout.Revision;

            void AddButton(AppPage page, string text)
            {
                var btn = BuildButton(page, text);
                btn.Width = 216;
                _navItems.Add(btn);
                _navHost.Strip.Controls.Add(btn);
            }

            foreach (var node in layout.Root)
            {
                // Admin turned this item off in the menu editor.
                if (!node.On)
                    continue;
                if (node.IsFolder)
                {
                    var drop = BuildDrop(node, 0);
                    // Folder had no visible children after access filtering.
                    if (drop == null)
                        continue;
                    drop.Width = 216;
                    drop.ExpandedChanged += () => LayoutNav(_navHost, _navItems);
                    drop.Register(_buttons);
                    _navItems.Add(drop);
                    _navHost.Strip.Controls.Add(drop);
                    CollectDrops(drop);
                    continue;
                }

                // Stale key from an older catalog version.
                if (!MenuLayout.TryPage(node.Key, out var page))
                    continue;
                AddButton(page, node.Title);
            }

            AddButton(AppPage.Admin, "Admin");
            LayoutNav(_navHost, _navItems);
        }

        /// <summary>Build one dropdown; nested folders stop at depth 2.</summary>
        private NavDropGroup? BuildDrop(MenuNode node, int depth)
        {
            var drop = new NavDropGroup(node.Title, _workspace, depth);
            foreach (var child in node.Children)
            {
                // Admin turned this child off in the menu editor.
                if (!child.On)
                    continue;
                if (child.IsFolder)
                {
                    // Cap nesting so the navy rail stays readable.
                    if (depth >= 2)
                        continue;
                    var nested = BuildDrop(child, depth + 1);
                    if (nested == null)
                        continue;
                    nested.ExpandedChanged += () =>
                    {
                        drop.Relayout();
                        LayoutNav(_navHost, _navItems);
                    };
                    drop.AddNested(nested);
                    continue;
                }

                if (!MenuLayout.TryPage(child.Key, out var page))
                    continue;
                drop.AddPage(new NavMenuItem(page, child.Title, MenuLayout.OpenAction(page)));
            }

            return drop.HasContent ? drop : null;
        }

        /// <summary>Flatten nested dropdowns so RefreshState can update every group.</summary>
        private void CollectDrops(NavDropGroup drop)
        {
            _groups.Add(drop);
            foreach (var nested in drop.Nested)
                CollectDrops(nested);
        }

        /// <summary>Plain sidebar button that navigates in this workspace.</summary>
        private CrcNavButton BuildButton(AppPage page, string text)
        {
            var btn = new CrcNavButton { Text = text, Height = 38 };
            BindPageButton(btn, page);
            _buttons[page] = btn;
            return btn;
        }

        /// <summary>Left-click navigates here; middle/right-click can open another window.</summary>
        private void BindPageButton(CrcNavButton btn, AppPage page)
        {
            BindOpenWindow(btn, page, _workspace);
            btn.Click += (_, _) => Navigator.GoTo(page, _workspace);
        }

        /// <summary>Gold bar if this window shows the page; dim bar if another window does.</summary>
        private void ApplyOpenMark(AppPage page, CrcNavButton btn)
        {
            bool here = page == _workspace.CurrentPage;
            btn.Selected = here;
            btn.OpenElsewhere = !here && Navigator.IsOpen(page);
            string tip = page == AppPage.Help ? "Controls" : "";
            if (btn.OpenElsewhere)
            {
                // Tell the user a click will focus the other window, not duplicate it.
                tip = tip.Length == 0
                    ? "Already open — click to show that window"
                    : tip + " — already open, click to show that window";
            }

            _tips.SetToolTip(btn, tip);
        }

        /// <summary>Middle-click or right-click → Open in another window. If it is already open, that window comes forward.</summary>
        internal static void BindOpenWindow(Control control, AppPage page, Workspace workspace)
        {
            control.MouseDown += (_, e) =>
            {
                // Middle-click is the browser-style "open in another window".
                if (e.Button == MouseButtons.Middle)
                    Navigator.OpenDetached(page, workspace);
            };
            var menu = new ContextMenuStrip();
            var openItem = menu.Items.Add(
                "Open in another window",
                null,
                (_, _) => Navigator.OpenDetached(page, workspace));
            menu.Opening += (_, e) =>
            {
                // Locked pages (no folder / no access) cannot open extras.
                if (!control.Enabled)
                {
                    e.Cancel = true;
                    return;
                }

                bool elsewhere = Navigator.IsShownElsewhere(page, workspace);
                openItem.Text = elsewhere ? "Show window" : "Open in another window";

                while (menu.Items.Count > 1)
                    menu.Items.RemoveAt(menu.Items.Count - 1);

                var open = Navigator.ListOpenPages();
                if (open.Count == 0)
                    return;

                menu.Items.Add(new ToolStripSeparator());
                var heading = menu.Items.Add("Open windows");
                heading.Enabled = false;
                foreach (var openPage in open)
                {
                    var target = openPage;
                    string title = UiStyle.PageTitle(target);
                    // Mark the page already shown in the window that opened the menu.
                    if (workspace.CurrentPage == target)
                        title += " — this window";
                    menu.Items.Add(title, null, (_, _) => Navigator.TryFocus(target));
                }
            };
            control.ContextMenuStrip = menu;
        }
    }

    /// <summary>
    /// Navy sidebar list with a thin gold-tinted scrollbar instead of the system bar.
    /// </summary>
    internal sealed class NavyScrollPanel : Panel
    {
        private const int BarWidth = 6;
        private const int BarPad = 4;
        private int _offset;
        private int _contentHeight;
        private bool _drag;
        private int _dragStartY;
        private int _dragStartOffset;
        private bool _hoverBar;

        public Panel Strip { get; } = new()
        {
            BackColor = Theme.NavyDark
        };

        /// <summary>Custom-paint a gold thumb instead of the system scrollbar.</summary>
        public NavyScrollPanel()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.Selectable,
                true);
            TabStop = false;
            Theme.EnableDoubleBuffer(this);
            Theme.EnableDoubleBuffer(Strip);
            Controls.Add(Strip);
            MouseDown += (_, _) => TryFocus();
            Strip.MouseDown += (_, _) => TryFocus();
            Strip.ControlAdded += (_, e) =>
            {
                // New nav buttons need wheel forwarding too.
                if (e.Control != null)
                    Wire(e.Control);
            };
        }

        public int ContentWidth =>
            Math.Max(1, ClientSize.Width - Padding.Left - Padding.Right - BarWidth - BarPad);

        /// <summary>Tell the scroller how tall the stacked nav items are.</summary>
        public void SetContentHeight(int height)
        {
            _contentHeight = Math.Max(0, height);
            Clamp();
            LayoutStrip();
            Invalidate();
        }

        /// <summary>Reclamp scroll offset when the sidebar height changes.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Clamp();
            LayoutStrip();
            Invalidate();
        }

        /// <summary>Scroll the navy list 48px per wheel notch.</summary>
        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * 48);
        }

        /// <summary>Start a thumb drag when the pointer is on the gold bar.</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            // Only drag when the pointer is on the thumb, not the track.
            if (e.Button != MouseButtons.Left || !ThumbBounds.Contains(e.Location))
                return;
            _drag = true;
            _dragStartY = e.Y;
            _dragStartOffset = _offset;
            Capture = true;
        }

        /// <summary>Hover-highlight the thumb, or convert pointer travel into scroll offset while dragging.</summary>
        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = ThumbBounds.Contains(e.Location) || TrackBounds.Contains(e.Location);
            // Brighten the thumb only while the pointer is on the bar.
            if (hover != _hoverBar)
            {
                _hoverBar = hover;
                Invalidate();
            }

            // Hover-only move should not scroll.
            if (!_drag)
                return;

            int range = Overflow;
            int travel = Math.Max(1, TrackBounds.Height - ThumbBounds.Height);
            int delta = e.Y - _dragStartY;
            _offset = _dragStartOffset + (int)(delta * (range / (double)travel));
            Clamp();
            LayoutStrip();
            Invalidate();
        }

        /// <summary>End a thumb drag and release mouse capture.</summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Capture = false;
        }

        /// <summary>Drop the hover gold unless a thumb drag is still captured.</summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            // Keep the gold hover while the thumb is captured.
            if (_hoverBar && !_drag)
            {
                _hoverBar = false;
                Invalidate();
            }
        }

        /// <summary>Draw the dim track and gold thumb when content overflows.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Content fits; hide the custom bar.
            if (Overflow <= 0)
                return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var dim = new SolidBrush(Color.FromArgb(40, Theme.Cream)))
                g.FillRoundedBar(TrackBounds, dim);

            Color thumbColor = _hoverBar || _drag
                ? Color.FromArgb(210, Theme.Gold)
                : Color.FromArgb(120, Theme.GoldLight);
            using var fill = new SolidBrush(thumbColor);
            g.FillRoundedBar(ThumbBounds, fill);
        }

        private int ViewHeight => Math.Max(1, ClientSize.Height - Padding.Vertical);

        private int Overflow => Math.Max(0, _contentHeight - ViewHeight);

        private Rectangle TrackBounds
        {
            get
            {
                int x = ClientSize.Width - BarPad - BarWidth;
                int y = Padding.Top;
                int h = Math.Max(BarWidth, ClientSize.Height - Padding.Vertical);
                return new Rectangle(x, y, BarWidth, h);
            }
        }

        private Rectangle ThumbBounds
        {
            get
            {
                var track = TrackBounds;
                int overflow = Overflow;
                // No thumb when content fits the viewport.
                if (overflow <= 0)
                    return Rectangle.Empty;
                int thumbH = Math.Max(22, (int)(track.Height * (ViewHeight / (double)Math.Max(ViewHeight, _contentHeight))));
                int travel = Math.Max(0, track.Height - thumbH);
                int y = track.Y + (int)(travel * (_offset / (double)overflow));
                return new Rectangle(track.X, y, track.Width, thumbH);
            }
        }

        /// <summary>Nudge the list by delta pixels and clamp to the overflow range.</summary>
        private void ScrollBy(int delta)
        {
            _offset += delta;
            Clamp();
            LayoutStrip();
            Invalidate();
        }

        /// <summary>Keep the scroll offset between 0 and the overflow so the list cannot bounce.</summary>
        private void Clamp() =>
            _offset = Math.Max(0, Math.Min(_offset, Overflow));

        /// <summary>Position the inner strip so _offset is the pixels scrolled off the top.</summary>
        private void LayoutStrip()
        {
            Strip.Location = new Point(Padding.Left, Padding.Top - _offset);
            Strip.Size = new Size(ContentWidth, Math.Max(ViewHeight, _contentHeight));
        }

        /// <summary>Take focus so mouse-wheel messages reach this panel.</summary>
        private void TryFocus()
        {
            if (!ContainsFocus)
                Focus();
        }

        /// <summary>Forward child mouse-wheel and click so nested buttons still scroll the list.</summary>
        private void Wire(Control control)
        {
            control.MouseDown -= ChildDown;
            control.MouseDown += ChildDown;
            control.MouseWheel -= ChildWheel;
            control.MouseWheel += ChildWheel;
            control.ControlAdded -= ChildAdded;
            control.ControlAdded += ChildAdded;
            foreach (Control child in control.Controls)
                Wire(child);
        }

        /// <summary>Focus the scroller when a nested button is clicked so wheel still works.</summary>
        private void ChildDown(object? sender, MouseEventArgs e) => TryFocus();

        /// <summary>Buttons eat wheel messages; forward them to the navy list.</summary>
        private void ChildWheel(object? sender, MouseEventArgs e) =>
            ScrollBy(-Math.Sign(e.Delta) * 48);

        /// <summary>Wire nested children added after the parent was already hooked.</summary>
        private void ChildAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control != null)
                Wire(e.Control);
        }
    }

    /// <summary>Rounded gold thumb painter for the navy scrollbar.</summary>
    internal static class NavyScrollPaint
    {
        /// <summary>Capsule fill: ellipse when short, rounded rect when tall.</summary>
        public static void FillRoundedBar(this Graphics g, Rectangle bounds, Brush? brush = null)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || brush == null)
                return;
            // Thumb is circular at the minimum height.
            if (bounds.Height <= bounds.Width)
            {
                g.FillEllipse(brush, bounds);
                return;
            }

            using var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, bounds.Width, bounds.Width, 180, 180);
            path.AddArc(bounds.X, bounds.Bottom - bounds.Width, bounds.Width, bounds.Width, 0, 180);
            path.CloseFigure();
            g.FillPath(brush, path);
        }
    }

    /// <summary>One dropdown child: nested page, plus an optional standalone Open action.</summary>
    internal readonly struct NavMenuItem
    {
        public NavMenuItem(AppPage page, string text, Action? open)
        {
            Page = page;
            Text = text;
            Open = open;
        }

        public AppPage Page { get; }
        public string Text { get; }
        public Action? Open { get; }
    }

    /// <summary>Expandable sidebar group of pages, and nested dropdowns.</summary>
    internal sealed class NavDropGroup : Panel
    {
        private readonly List<AppPage> _pages = new();
        private readonly List<CrcNavButton> _pageButtons = new();
        private readonly List<NavDropGroup> _nested = new();
        private readonly List<Control> _order = new();
        private readonly Workspace _workspace;
        private readonly CrcNavButton _header;
        private readonly Button _arrow;
        private readonly Panel _headerRow;
        private readonly Panel _children;
        private readonly int _headerHeight;
        private bool _expanded;
        private bool _manualOpen;

        public event Action? ExpandedChanged;

        public IReadOnlyList<NavDropGroup> Nested => _nested;

        public bool HasContent => _pages.Count > 0 || _nested.Count > 0;

        /// <summary>Header plus nested page buttons; nested folders indent by depth.</summary>
        public NavDropGroup(string headerText, Workspace workspace, int depth)
        {
            _workspace = workspace;
            _headerHeight = depth == 0 ? 38 : 34;
            BackColor = Theme.NavyDark;
            Height = _headerHeight;

            _arrow = new Button
            {
                Dock = DockStyle.Right,
                Width = 30,
                FlatStyle = FlatStyle.Flat,
                TabStop = false,
                Cursor = Cursors.Hand
            };
            _arrow.FlatAppearance.BorderSize = 0;
            _arrow.FlatAppearance.MouseOverBackColor = Theme.NavyHover;
            _arrow.FlatAppearance.MouseDownBackColor = Theme.NavyMid;
            _arrow.BackColor = Theme.NavyDark;
            _arrow.Paint += PaintArrow;
            _arrow.Click += (_, _) => ToggleOpen();

            _header = new CrcNavButton
            {
                Text = headerText,
                Dock = DockStyle.Fill,
                Height = _headerHeight,
                Padding = new Padding(16 + (depth * 12), 0, 8, 0)
            };
            _header.Click += (_, _) => ToggleOpen();

            _headerRow = new Panel { Dock = DockStyle.Top, Height = _headerHeight, BackColor = Theme.NavyDark };
            _headerRow.Controls.Add(_header);
            _headerRow.Controls.Add(_arrow);

            _children = new Panel
            {
                Dock = DockStyle.Top,
                Height = 0,
                BackColor = Theme.NavyDark,
                Visible = false
            };
            _children.Resize += (_, _) => LayoutChildren();

            Controls.Add(_children);
            Controls.Add(_headerRow);
        }

        /// <summary>Add a page button; New Purchase/Sale use OpenNew instead of nested GoTo.</summary>
        public void AddPage(NavMenuItem item)
        {
            var btn = new CrcNavButton
            {
                Text = item.Text,
                Height = 34,
                Padding = new Padding(28, 0, 8, 0)
            };
            var page = item.Page;
            var open = item.Open;
            NavSidebar.BindOpenWindow(btn, page, _workspace);
            btn.Click += (_, _) =>
            {
                Navigator.Activate(_workspace);
                // AddPurchase/SalesOrder open a dedicated window, not a nested page.
                if (open != null)
                    open();
                else
                    Navigator.GoTo(page, _workspace);
            };
            _pages.Add(page);
            _pageButtons.Add(btn);
            _order.Add(btn);
            _children.Controls.Add(btn);
        }

        /// <summary>Nest another dropdown under this one.</summary>
        public void AddNested(NavDropGroup nested)
        {
            _nested.Add(nested);
            _order.Add(nested);
            _children.Controls.Add(nested);
        }

        /// <summary>Expose child page buttons to the sidebar so RefreshState can mark them.</summary>
        public void Register(Dictionary<AppPage, CrcNavButton> buttons)
        {
            for (int i = 0; i < _pages.Count; i++)
                buttons[_pages[i]] = _pageButtons[i];
            foreach (var nested in _nested)
                nested.Register(buttons);
        }

        /// <summary>True when at least one child page is allowed for this user.</summary>
        public bool ShouldShow() =>
            _pages.Any(PageAllowed) || _nested.Any(group => group.ShouldShow());

        /// <summary>Enable children by table access and hide empty folders.</summary>
        public void ApplyAccess(bool unlocked)
        {
            int visibleChildren = 0;
            for (int i = 0; i < _pages.Count; i++)
            {
                bool allowed = PageAllowed(_pages[i]);
                _pageButtons[i].Enabled = unlocked && allowed;
                // Folder header stays disabled when every child is denied.
                if (allowed)
                    visibleChildren++;
            }

            foreach (var nested in _nested)
            {
                nested.ApplyAccess(unlocked);
                // Nested folders with no allowed pages stay collapsed/hidden.
                if (nested.ShouldShow())
                    visibleChildren++;
            }

            _header.Enabled = unlocked && visibleChildren > 0;
            _arrow.Enabled = _header.Enabled;
            Relayout();
        }

        /// <summary>Re-measure after access changes without toggling open/closed.</summary>
        public void SyncExpanded()
        {
            Relayout();
        }

        /// <summary>Expand only when the user opened this folder; height follows allowed children.</summary>
        public void Relayout()
        {
            bool related = IsCurrentRelated();
            _header.Selected = related;
            _header.OpenElsewhere = !related && HasOpenElsewhere();
            _arrow.Enabled = _header.Enabled;
            _arrow.BackColor = Theme.NavyDark;

            bool expand = _manualOpen;
            _expanded = expand;
            SuspendLayout();
            _children.SuspendLayout();
            // Show the host before measuring. Control.Visible is false while any
            // ancestor is hidden, so height must come from access, not Visible.
            _children.Visible = expand;
            LayoutChildren();
            int childHeight = expand ? Math.Max(4, ChildStackHeight()) : 0;
            _children.Height = childHeight;
            Height = _headerHeight + childHeight;
            _children.ResumeLayout(true);
            ResumeLayout(true);
            _arrow.Invalidate();
            ExpandedChanged?.Invoke();
        }

        /// <summary>Review uses CanReview; other pages use table denials.</summary>
        private static bool PageAllowed(AppPage page)
        {
            if (page == AppPage.PendingChanges)
                return DataAccess.CanReview();
            return TableAccess.CanPage(page);
        }

        /// <summary>Hide children the current user cannot open.</summary>
        private bool ChildShouldShow(Control child, int pageIndex)
        {
            if (child is NavDropGroup nested)
                return nested.ShouldShow();
            return pageIndex < _pages.Count && PageAllowed(_pages[pageIndex]);
        }

        /// <summary>Pixel height of allowed children used when the folder is expanded.</summary>
        private int ChildStackHeight()
        {
            int y = 4;
            int pageIndex = 0;
            foreach (var child in _order)
            {
                bool show = ChildShouldShow(child, pageIndex);
                // Nested folders are not in _pages; only page buttons consume an index.
                if (child is CrcNavButton)
                    pageIndex++;
                // Denied pages do not take vertical space.
                if (!show)
                    continue;
                y += child is CrcNavButton ? 34 : Math.Max(child.Height, 34);
            }

            return y;
        }

        /// <summary>Stack allowed children; denied pages stay invisible in place.</summary>
        private void LayoutChildren()
        {
            int y = 4;
            int width = Math.Max(Width, Math.Max(216, _children.ClientSize.Width));
            int pageIndex = 0;
            foreach (var child in _order)
            {
                bool show = ChildShouldShow(child, pageIndex);
                if (child is CrcNavButton)
                    pageIndex++;
                child.Visible = show;
                // Keep denied items out of the hit-test stack.
                if (!show)
                    continue;
                child.Location = new Point(0, y);
                child.Width = width;
                // Nested dropdowns keep the height Relayout already measured.
                if (child is CrcNavButton)
                    child.Height = 34;
                y += child.Height;
            }
        }

        /// <summary>User-driven expand/collapse; does not auto-open for the current page.</summary>
        private void ToggleOpen()
        {
            _manualOpen = !_manualOpen;
            Relayout();
        }

        /// <summary>True when this folder (or a nested one) contains the page shown in this window.</summary>
        private bool IsCurrentRelated() =>
            _workspace.CurrentPage is AppPage page &&
            (_pages.Contains(page) || _nested.Any(group => group.IsCurrentRelated()));

        /// <summary>True when a child page is open in a different workspace.</summary>
        private bool HasOpenElsewhere() =>
            _pages.Any(page => Navigator.IsOpen(page) && page != _workspace.CurrentPage) ||
            _nested.Any(group => group.HasOpenElsewhere());

        /// <summary>Chevron points down when expanded, right when collapsed.</summary>
        private void PaintArrow(object? sender, PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            int cx = _arrow.Width / 2;
            int cy = _arrow.Height / 2;
            Point[] pts = _expanded
                ? new[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 4) }
                : new[] { new Point(cx - 2, cy - 5), new Point(cx + 4, cy), new Point(cx - 2, cy + 5) };

            using var brush = new SolidBrush(
                _header.Enabled ? Theme.GoldLight : Color.FromArgb(70, Theme.Cream));
            e.Graphics.FillPolygon(brush, pts);
        }
    }

    /// <summary>Gold pill switch. Off = current database, on = Old Inventory (all terms).</summary>
    internal sealed class CrcToggleSwitch : Control
    {
        private bool _on;

        public event EventHandler? Toggled;

        public bool On => _on;

        /// <summary>46×24 gold pill; Off is Current database, On is Old Inventory.</summary>
        public CrcToggleSwitch()
        {
            Size = new Size(46, 24);
            Cursor = Cursors.Hand;
            TabStop = false;
            DoubleBuffered = true;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.UserPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.SupportsTransparentBackColor |
                ControlStyles.ResizeRedraw,
                true);
            BackColor = Color.Transparent;
            AccessibleName = "Old Inventory";
            AccessibleRole = AccessibleRole.CheckButton;
        }

        /// <summary>Set the switch without raising Toggled (used when syncing from AppState).</summary>
        public void SetOn(bool on)
        {
            if (_on == on)
                return;
            _on = on;
            Invalidate();
        }

        /// <summary>Toggle Old Inventory on left-click when the switch is enabled.</summary>
        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            // Disabled until a data folder or server is available.
            if (!Enabled || e.Button != MouseButtons.Left)
                return;

            _on = !_on;
            Invalidate();
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Repaint faded when no folder is selected.</summary>
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        /// <summary>Navy or gold track with a cream knob on the Current/Old side.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (Parent != null)
            {
                // Transparent back-color is unreliable; paint the navy rail behind the pill.
                using var clear = new SolidBrush(Parent.BackColor);
                g.FillRectangle(clear, ClientRectangle);
            }

            var track = new Rectangle(1, 1, Math.Max(2, Width - 2), Math.Max(2, Height - 2));
            using var path = RoundedRect(track, track.Height / 2f);
            Color trackColor = !Enabled
                ? Color.FromArgb(50, Theme.Cream)
                : _on ? Theme.Gold : Theme.NavyMid;
            using var fill = new SolidBrush(trackColor);
            g.FillPath(fill, path);

            int pad = 2;
            int kn = Math.Max(8, Height - pad * 2 - 1);
            int kx = _on ? Width - pad - kn - 1 : pad;
            using var knob = new SolidBrush(Enabled ? Theme.Cream : Color.FromArgb(120, Theme.Cream));
            g.FillEllipse(knob, kx, pad, kn, kn);
        }

        /// <summary>Closed pill path for the Current/Old track.</summary>
        private static GraphicsPath RoundedRect(Rectangle bounds, float radius)
        {
            float d = radius * 2f;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    /// <summary>Sidebar page button. Selected items get a gold leading bar.</summary>
    internal class CrcNavButton : Button
    {
        private bool _selected;
        private bool _openElsewhere;

        public bool Selected
        {
            get => _selected;
            set
            {
                _selected = value;
                ApplyColors();
                Invalidate();
            }
        }

        public bool OpenElsewhere
        {
            get => _openElsewhere;
            set
            {
                _openElsewhere = value;
                ApplyColors();
                Invalidate();
            }
        }

        /// <summary>Flat navy button; gold leading bar is painted when Selected or OpenElsewhere.</summary>
        public CrcNavButton()
        {
            FlatStyle = FlatStyle.Flat;
            FlatAppearance.BorderSize = 0;
            FlatAppearance.MouseOverBackColor = Theme.NavyHover;
            FlatAppearance.MouseDownBackColor = Theme.NavyMid;
            UseVisualStyleBackColor = false;
            TextAlign = ContentAlignment.MiddleLeft;
            Padding = new Padding(16, 0, 8, 0);
            Font = Theme.NavFont;
            Cursor = Cursors.Hand;
            TabStop = false;
            ApplyColors();
        }

        /// <summary>Refresh faded vs cream colors when table access changes.</summary>
        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            ApplyColors();
        }

        /// <summary>Gold leading bar when this page is selected here or open elsewhere.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            // Idle buttons have no gold leading bar.
            if (!_selected && !_openElsewhere)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            Color mark = _selected ? Theme.Gold : Color.FromArgb(140, Theme.Gold);
            using var gold = new SolidBrush(mark);
            e.Graphics.FillRectangle(gold, 0, 8, 4, Height - 16);
        }

        /// <summary>Selected is gold on navy-mid; disabled is faded; otherwise cream on navy.</summary>
        private void ApplyColors()
        {
            if (_selected)
            {
                BackColor = Theme.NavyMid;
                ForeColor = Theme.GoldLight;
            }
            else if (!Enabled)
            {
                BackColor = Theme.NavyDark;
                ForeColor = Color.FromArgb(70, Theme.Cream);
            }
            else
            {
                BackColor = Theme.NavyDark;
                ForeColor = Theme.Cream;
            }
        }
    }

    /// <summary>Compact keyboard glyph that opens the Controls (Help) page.</summary>
    internal sealed class CrcControlsButton : CrcNavButton
    {
        /// <summary>Icon-only Help button on the Settings row.</summary>
        public CrcControlsButton()
        {
            Text = "";
            Padding = new Padding(0);
            TextAlign = ContentAlignment.MiddleCenter;
            AccessibleName = "Controls";
        }

        /// <summary>Keyboard glyph in the current ForeColor.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlsGlyph.Paint(e.Graphics, ClientRectangle, ForeColor);
        }
    }

    /// <summary>Monitor glyph used for IT chrome when a text label would not fit.</summary>
    internal sealed class CrcItButton : CrcNavButton
    {
        /// <summary>Icon-only IT button (unused on the current Settings row).</summary>
        public CrcItButton()
        {
            Text = "";
            Padding = new Padding(0);
            TextAlign = ContentAlignment.MiddleCenter;
            AccessibleName = "IT";
        }

        /// <summary>Monitor glyph in the current ForeColor.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            MonitorGlyph.Paint(e.Graphics, ClientRectangle, ForeColor);
        }
    }

    /// <summary>Small keyboard outline drawn on the Controls nav button.</summary>
    internal static class ControlsGlyph
    {
        /// <summary>Draw a rounded keyboard with a space bar in the given color.</summary>
        public static void Paint(Graphics g, Rectangle bounds, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int w = 22;
            int h = 14;
            int x = bounds.X + (bounds.Width - w) / 2;
            int y = bounds.Y + (bounds.Height - h) / 2;

            using var pen = new Pen(color, 1.6f);
            using var fill = new SolidBrush(color);
            using var body = Theme.RoundRect(new Rectangle(x, y, w, h), 2);
            g.DrawPath(pen, body);

            void Key(float kx, float ky, float kw, float kh)
            {
                g.FillRectangle(fill, x + kx, y + ky, kw, kh);
            }

            Key(3, 3, 2.2f, 2.1f);
            Key(6.4f, 3, 2.2f, 2.1f);
            Key(9.8f, 3, 2.2f, 2.1f);
            Key(13.2f, 3, 2.2f, 2.1f);
            Key(16.6f, 3, 2.2f, 2.1f);

            Key(4.2f, 6.2f, 2.2f, 2.1f);
            Key(7.6f, 6.2f, 2.2f, 2.1f);
            Key(11f, 6.2f, 2.2f, 2.1f);
            Key(14.4f, 6.2f, 2.2f, 2.1f);

            Key(6.6f, 9.5f, 8.8f, 2.1f);
        }
    }

    /// <summary>Small monitor-and-stand outline for the IT nav button.</summary>
    internal static class MonitorGlyph
    {
        /// <summary>Draw a rounded screen, bezel, and stand in the given color.</summary>
        public static void Paint(Graphics g, Rectangle bounds, Color color)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;

            int w = 20;
            int h = 14;
            int x = bounds.X + (bounds.Width - w) / 2;
            int y = bounds.Y + (bounds.Height - h) / 2 - 1;

            using var pen = new Pen(color, 1.6f);
            using var fill = new SolidBrush(color);
            using var screen = Theme.RoundRect(new Rectangle(x, y, w, h), 2);
            g.DrawPath(pen, screen);
            g.DrawRectangle(pen, x + 3, y + 3, w - 6, h - 6);

            int standX = x + w / 2;
            g.DrawLine(pen, standX, y + h, standX, y + h + 4);
            g.DrawLine(pen, standX - 5, y + h + 5, standX + 5, y + h + 5);
        }
    }

    /// <summary>Icon Back / Forward next to the page title. Hidden with the header on Home.</summary>
    internal sealed class NavHistoryBar : Panel
    {
        private readonly Workspace _workspace;
        private readonly HistoryIconButton _back;
        private readonly HistoryIconButton _forward;
        private readonly ToolTip _tips = new() { ShowAlways = true };

        /// <summary>Back and Forward chevrons bound to this workspace's history stacks.</summary>
        public NavHistoryBar(Workspace workspace)
        {
            _workspace = workspace;
            Width = 92;
            MinimumSize = new Size(84, 40);
            BackColor = Theme.Paper;
            Padding = new Padding(16, 20, 8, 16);
            Theme.EnableDoubleBuffer(this);

            _back = new HistoryIconButton(back: true);
            _back.Click += (_, _) =>
            {
                // Faded chevron still receives clicks; ignore them.
                if (_workspace.CanGoBack)
                    Navigator.GoBack(_workspace);
            };
            _tips.SetToolTip(_back, "Back  ·  mouse side button or Alt+Left");

            _forward = new HistoryIconButton(back: false);
            _forward.Click += (_, _) =>
            {
                if (_workspace.CanGoForward)
                    Navigator.GoForward(_workspace);
            };
            _tips.SetToolTip(_forward, "Forward  ·  mouse side button or Alt+Right");

            Controls.Add(_forward);
            Controls.Add(_back);
            Resize += (_, _) => LayoutButtons();
            LayoutButtons();
            Sync();
        }

        /// <summary>Gold when this window has back/forward history; faded otherwise.</summary>
        public void Sync()
        {
            _back.Usable = _workspace.CanGoBack;
            _forward.Usable = _workspace.CanGoForward;
        }

        /// <summary>Place the two 32px chevrons side by side in the header.</summary>
        private void LayoutButtons()
        {
            int size = 32;
            int y = Math.Max(Padding.Top, (ClientSize.Height - size) / 2);
            _back.SetBounds(Padding.Left, y, size, size);
            _forward.SetBounds(Padding.Left + size + 4, y, size, size);
        }
    }

    /// <summary>Chevron icon. Gold when it can navigate, dark and faded when it cannot.</summary>
    internal sealed class HistoryIconButton : Control
    {
        private readonly bool _back;
        private bool _usable;
        private bool _hover;
        private bool _down;

        /// <summary>back draws a left chevron; otherwise a right chevron.</summary>
        public HistoryIconButton(bool back)
        {
            _back = back;
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            Size = new Size(32, 32);
            TabStop = false;
            Cursor = Cursors.Default;
        }

        public bool Usable
        {
            get => _usable;
            set
            {
                // Skip a redundant invalidate while the header refreshes often.
                if (_usable == value)
                    return;
                _usable = value;
                Cursor = value ? Cursors.Hand : Cursors.Default;
                Invalidate();
            }
        }

        /// <summary>Brighten the chevron while the pointer is over a usable button.</summary>
        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        /// <summary>Clear hover and pressed gold when the pointer leaves.</summary>
        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            _down = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        /// <summary>Raise Click only when this window has history in that direction.</summary>
        protected override void OnClick(EventArgs e)
        {
            // Empty history must not fire Click subscribers.
            if (!_usable)
                return;
            base.OnClick(e);
        }

        /// <summary>Show the pressed navy chevron on a usable left-click.</summary>
        protected override void OnMouseDown(MouseEventArgs e)
        {
            // Pressed gold is only for a live history target.
            if (e.Button == MouseButtons.Left && _usable)
            {
                _down = true;
                Invalidate();
            }

            base.OnMouseDown(e);
        }

        /// <summary>Clear the pressed state even if the pointer is no longer over the button.</summary>
        protected override void OnMouseUp(MouseEventArgs e)
        {
            _down = false;
            Invalidate();
            base.OnMouseUp(e);
        }

        /// <summary>Draw a gold, hover, pressed, or faded chevron.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;

            Color color = !_usable
                ? Color.FromArgb(110, Theme.Ink)
                : _down
                    ? Theme.Navy
                    : _hover
                        ? Theme.GoldLight
                        : Theme.Gold;

            using var pen = new Pen(color, 2.2f)
            {
                StartCap = LineCap.Round,
                EndCap = LineCap.Round,
                LineJoin = LineJoin.Round
            };

            float cx = Width / 2f;
            float cy = Height / 2f;
            float dx = 6.5f;
            float dy = 8f;
            // Left chevron for back; right chevron for forward.
            if (_back)
            {
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(cx + dx, cy - dy),
                    new PointF(cx - dx, cy),
                    new PointF(cx + dx, cy + dy)
                });
            }
            else
            {
                e.Graphics.DrawLines(pen, new[]
                {
                    new PointF(cx - dx, cy - dy),
                    new PointF(cx + dx, cy),
                    new PointF(cx - dx, cy + dy)
                });
            }
        }
    }
}
