using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    /// <summary>Navy left nav, including the Current/Old database toggle.</summary>
    internal sealed class NavSidebar : Panel
    {
        private readonly Dictionary<AppPage, CrcNavButton> _buttons = new();
        private readonly List<NavDropGroup> _groups = new();
        private readonly Workspace _workspace;
        private CrcToggleSwitch _dbToggle = null!;
        private Label _lblCurrentDb = null!;
        private Label _lblOldDb = null!;

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
                Height = 36,
                BackColor = Theme.NavyDark,
                Name = "userRow"
            };
            var userLabel = new Label
            {
                Name = "lblNavUser",
                Dock = DockStyle.Fill,
                Font = Theme.Small,
                ForeColor = Theme.GoldLight,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(18, 0, 12, 0)
            };
            userRow.Controls.Add(userLabel);

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
            var helpTip = new ToolTip { ShowAlways = true };
            helpTip.SetToolTip(help, "Controls");

            var settings = BuildButton(AppPage.Settings, "Settings");
            settings.Dock = DockStyle.Fill;
            settings.Name = "btnSettings";
            settingsRow.Controls.Add(settings);
            settingsRow.Controls.Add(help);

            var navHost = new NavyScrollPanel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.NavyDark,
                Padding = new Padding(10, 8, 14, 8)
            };

            var items = new List<Control>();
            void AddButton(AppPage page, string text)
            {
                var btn = BuildButton(page, text);
                btn.Width = 216;
                items.Add(btn);
                navHost.Strip.Controls.Add(btn);
            }

            AddButton(AppPage.Dashboard, "Dashboard");

            var purchaseGroup = new NavDropGroup(
                "Purchases",
                AppPage.PurchaseSales,
                "Purchase Form",
                AppPage.AddPurchase,
                workspace,
                AddPurchase.OpenNew);
            purchaseGroup.Width = 216;
            purchaseGroup.ExpandedChanged += () => LayoutNav(navHost, items);
            purchaseGroup.Register(_buttons);
            items.Add(purchaseGroup);
            navHost.Strip.Controls.Add(purchaseGroup);
            _groups.Add(purchaseGroup);

            var salesGroup = new NavDropGroup(
                "Sales",
                AppPage.Sales,
                "Sales Form",
                AppPage.AddSale,
                workspace,
                AddSale.OpenNew,
                "Create Sales Order",
                AppPage.SalesOrder);
            salesGroup.Width = 216;
            salesGroup.ExpandedChanged += () => LayoutNav(navHost, items);
            salesGroup.Register(_buttons);
            items.Add(salesGroup);
            navHost.Strip.Controls.Add(salesGroup);
            _groups.Add(salesGroup);

            AddButton(AppPage.Customers, "Customers");
            AddButton(AppPage.Vendors, "Vendors");
            AddButton(AppPage.ItemCodes, "Item Codes");

            var invoiceGroup = new NavDropGroup(
                "Invoices",
                AppPage.Invoicing,
                "Create Invoice",
                AppPage.InvoicePdf,
                workspace);
            invoiceGroup.Width = 216;
            invoiceGroup.ExpandedChanged += () => LayoutNav(navHost, items);
            invoiceGroup.Register(_buttons);
            items.Add(invoiceGroup);
            navHost.Strip.Controls.Add(invoiceGroup);
            _groups.Add(invoiceGroup);

            AddButton(AppPage.Debits, "Debits");
            AddButton(AppPage.Credits, "Credits");
            AddButton(AppPage.Banking, "Banking");
            AddButton(AppPage.Reports, "Reports");
            AddButton(AppPage.Admin, "Admin");

            navHost.Resize += (_, _) => LayoutNav(navHost, items);
            LayoutNav(navHost, items);

            var dbSwitch = BuildDatabaseSwitch();

            Controls.Add(navHost);
            Controls.Add(settingsRow);
            Controls.Add(userRow);
            Controls.Add(dbSwitch);
            Controls.Add(brand);

            AppLock.Changed += RefreshState;
            RefreshState();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                AppLock.Changed -= RefreshState;
            base.Dispose(disposing);
        }

        public void RefreshState()
        {
            if (IsDisposed)
                return;

            if (InvokeRequired)
            {
                BeginInvoke(RefreshState);
                return;
            }

            bool unlocked = AppLock.HasFolder();
            foreach (var pair in _buttons)
            {
                bool isSettings = pair.Key == AppPage.Settings || pair.Key == AppPage.Help;
                bool isAdminPage = pair.Key == AppPage.Admin;
                if (isAdminPage)
                {
                    bool staff = AppState.IsAdmin || AppState.IsIt;
                    pair.Value.Visible = staff;
                    pair.Value.Enabled = unlocked && staff;
                    pair.Value.Selected = _workspace.CurrentPage == AppPage.Admin;
                    continue;
                }

                pair.Value.Enabled = (unlocked || isSettings) && TableAccess.CanPage(pair.Key);
                pair.Value.Selected = pair.Key == _workspace.CurrentPage;
            }

            foreach (var group in _groups)
                group.SyncExpanded();

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

        private static void LayoutNav(NavyScrollPanel host, List<Control> items)
        {
            int y = 4;
            int width = Math.Max(160, host.ContentWidth);
            foreach (var item in items)
            {
                item.Location = new Point(0, y);
                item.Width = width;
                y += item.Height + 4;
            }

            host.SetContentHeight(y);
        }

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

            if (BrandAssets.Seal != null)
            {
                var pic = new PictureBox
                {
                    Image = BrandAssets.Seal,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point(18, 18),
                    Size = new Size(64, 64),
                    BackColor = Color.Transparent
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
            return brand;
        }

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

        private CrcNavButton BuildButton(AppPage page, string text)
        {
            var btn = new CrcNavButton { Text = text, Height = 38 };
            BindPageButton(btn, page);
            _buttons[page] = btn;
            return btn;
        }

        private void BindPageButton(CrcNavButton btn, AppPage page)
        {
            btn.MouseDown += (_, e) =>
            {
                if (e.Button != MouseButtons.Middle)
                    return;
                Navigator.OpenDetached(page);
            };
            btn.Click += (_, _) => Navigator.GoTo(page, _workspace);
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
            MouseEnter += (_, _) => TryFocus();
            Strip.MouseEnter += (_, _) => TryFocus();
            Strip.ControlAdded += (_, e) =>
            {
                if (e.Control != null)
                    Wire(e.Control);
            };
        }

        public int ContentWidth =>
            Math.Max(1, ClientSize.Width - Padding.Left - Padding.Right - BarWidth - BarPad);

        public void SetContentHeight(int height)
        {
            _contentHeight = Math.Max(0, height);
            Clamp();
            LayoutStrip();
            Invalidate();
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Clamp();
            LayoutStrip();
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            ScrollBy(-Math.Sign(e.Delta) * 48);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left || !ThumbBounds.Contains(e.Location))
                return;
            _drag = true;
            _dragStartY = e.Y;
            _dragStartOffset = _offset;
            Capture = true;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            bool hover = ThumbBounds.Contains(e.Location) || TrackBounds.Contains(e.Location);
            if (hover != _hoverBar)
            {
                _hoverBar = hover;
                Invalidate();
            }

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

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            _drag = false;
            Capture = false;
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            base.OnMouseLeave(e);
            if (_hoverBar && !_drag)
            {
                _hoverBar = false;
                Invalidate();
            }
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
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
                if (overflow <= 0)
                    return Rectangle.Empty;
                int thumbH = Math.Max(22, (int)(track.Height * (ViewHeight / (double)Math.Max(ViewHeight, _contentHeight))));
                int travel = Math.Max(0, track.Height - thumbH);
                int y = track.Y + (int)(travel * (_offset / (double)overflow));
                return new Rectangle(track.X, y, track.Width, thumbH);
            }
        }

        private void ScrollBy(int delta)
        {
            _offset += delta;
            Clamp();
            LayoutStrip();
            Invalidate();
        }

        private void Clamp() =>
            _offset = Math.Max(0, Math.Min(_offset, Overflow));

        private void LayoutStrip()
        {
            Strip.Location = new Point(Padding.Left, Padding.Top - _offset);
            Strip.Size = new Size(ContentWidth, Math.Max(ViewHeight, _contentHeight));
        }

        private void TryFocus()
        {
            if (!ContainsFocus)
                Focus();
        }

        private void Wire(Control control)
        {
            control.MouseEnter -= ChildEnter;
            control.MouseEnter += ChildEnter;
            control.MouseWheel -= ChildWheel;
            control.MouseWheel += ChildWheel;
            control.ControlAdded -= ChildAdded;
            control.ControlAdded += ChildAdded;
            foreach (Control child in control.Controls)
                Wire(child);
        }

        private void ChildEnter(object? sender, EventArgs e) => TryFocus();

        private void ChildWheel(object? sender, MouseEventArgs e) =>
            ScrollBy(-Math.Sign(e.Delta) * 48);

        private void ChildAdded(object? sender, ControlEventArgs e)
        {
            if (e.Control != null)
                Wire(e.Control);
        }
    }

    internal static class NavyScrollPaint
    {
        public static void FillRoundedBar(this Graphics g, Rectangle bounds, Brush? brush = null)
        {
            if (bounds.Width <= 0 || bounds.Height <= 0 || brush == null)
                return;
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

    /// <summary>Expandable sidebar group (Purchases, Sales, Invoices).</summary>
    internal sealed class NavDropGroup : Panel
    {
        private readonly AppPage _headerPage;
        private readonly AppPage _childPage;
        private readonly AppPage? _extraPage;
        private readonly Workspace _workspace;
        private readonly CrcNavButton _header;
        private readonly Button _arrow;
        private readonly Panel _children;
        private readonly CrcNavButton _child;
        private readonly CrcNavButton? _extra;
        private readonly int _childCount;
        private bool _expanded;
        private bool _manualOpen;

        public event Action? ExpandedChanged;

        public NavDropGroup(
            string headerText,
            AppPage headerPage,
            string childText,
            AppPage childPage,
            Workspace workspace,
            Action? openChild = null,
            string? extraText = null,
            AppPage extraPage = AppPage.Dashboard,
            Action? extraOpen = null)
        {
            _headerPage = headerPage;
            _childPage = childPage;
            _workspace = workspace;
            _childCount = string.IsNullOrWhiteSpace(extraText) ? 1 : 2;
            _extraPage = _childCount == 2 ? extraPage : null;
            BackColor = Theme.NavyDark;
            Height = 38;

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
            _arrow.Click += (_, _) =>
            {
                if (IsCurrentRelated())
                    return;
                _manualOpen = !_manualOpen;
                SyncExpanded();
            };

            _header = new CrcNavButton { Text = headerText, Dock = DockStyle.Fill, Height = 38 };
            _header.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Middle)
                    Navigator.OpenDetached(_headerPage);
            };
            _header.Click += (_, _) => Navigator.GoTo(_headerPage, _workspace);

            var headerRow = new Panel { Dock = DockStyle.Top, Height = 38, BackColor = Theme.NavyDark };
            headerRow.Controls.Add(_header);
            headerRow.Controls.Add(_arrow);

            _child = new CrcNavButton
            {
                Text = childText,
                Height = 34,
                Dock = DockStyle.Top
            };
            _child.Padding = new Padding(28, 0, 8, 0);
            _child.MouseDown += (_, e) =>
            {
                if (e.Button == MouseButtons.Middle)
                    Navigator.OpenDetached(_childPage);
            };
            _child.Click += (_, _) =>
            {
                Navigator.Activate(_workspace);
                if (openChild != null)
                    openChild();
                else
                    Navigator.GoTo(_childPage, _workspace);
            };

            _children = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.NavyDark,
                Visible = false,
                Padding = new Padding(0, 4, 0, 0)
            };
            if (_extraPage != null)
            {
                _extra = new CrcNavButton
                {
                    Text = extraText,
                    Height = 34,
                    Dock = DockStyle.Top
                };
                _extra.Padding = new Padding(28, 0, 8, 0);
                var extraTarget = _extraPage.Value;
                _extra.MouseDown += (_, e) =>
                {
                    if (e.Button == MouseButtons.Middle)
                        Navigator.OpenDetached(extraTarget);
                };
                _extra.Click += (_, _) =>
                {
                    Navigator.Activate(_workspace);
                    if (extraOpen != null)
                        extraOpen();
                    else
                        Navigator.GoTo(extraTarget, _workspace);
                };
                _children.Controls.Add(_extra);
            }

            _children.Controls.Add(_child);

            Controls.Add(_children);
            Controls.Add(headerRow);
        }

        public void Register(Dictionary<AppPage, CrcNavButton> buttons)
        {
            buttons[_headerPage] = _header;
            buttons[_childPage] = _child;
            if (_extra != null && _extraPage != null)
                buttons[_extraPage.Value] = _extra;
        }

        public void SyncExpanded()
        {
            bool related = IsCurrentRelated();
            if (related)
                _manualOpen = false;

            _arrow.Enabled = _header.Enabled;
            _arrow.BackColor = Theme.NavyDark;

            bool expand = related || _manualOpen;
            if (expand == _expanded)
            {
                Invalidate(true);
                return;
            }

            _expanded = expand;
            _children.Visible = _expanded;
            Height = _expanded ? 38 + 4 + (34 * _childCount) : 38;
            _arrow.Invalidate();
            ExpandedChanged?.Invoke();
        }

        private bool IsCurrentRelated()
        {
            return _workspace.CurrentPage is AppPage page && IsRelated(page);
        }

        private bool IsRelated(AppPage page) =>
            page == _headerPage || page == _childPage || page == _extraPage;

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

        public void SetOn(bool on)
        {
            if (_on == on)
                return;
            _on = on;
            Invalidate();
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (!Enabled || e.Button != MouseButtons.Left)
                return;

            _on = !_on;
            Invalidate();
            Toggled?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            Invalidate();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (Parent != null)
            {
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

        protected override void OnEnabledChanged(EventArgs e)
        {
            base.OnEnabledChanged(e);
            ApplyColors();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (!_selected)
                return;

            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            using var gold = new SolidBrush(Theme.Gold);
            e.Graphics.FillRectangle(gold, 0, 8, 4, Height - 16);
        }

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

    internal sealed class CrcControlsButton : CrcNavButton
    {
        public CrcControlsButton()
        {
            Text = "";
            Padding = new Padding(0);
            TextAlign = ContentAlignment.MiddleCenter;
            AccessibleName = "Controls";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            ControlsGlyph.Paint(e.Graphics, ClientRectangle, ForeColor);
        }
    }

    internal sealed class CrcItButton : CrcNavButton
    {
        public CrcItButton()
        {
            Text = "";
            Padding = new Padding(0);
            TextAlign = ContentAlignment.MiddleCenter;
            AccessibleName = "IT";
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            MonitorGlyph.Paint(e.Graphics, ClientRectangle, ForeColor);
        }
    }

    internal static class ControlsGlyph
    {
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

    internal static class MonitorGlyph
    {
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
}
