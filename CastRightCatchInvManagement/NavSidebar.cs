using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    internal sealed class NavSidebar : Panel
    {
        private readonly Dictionary<AppPage, CrcNavButton> _buttons = new();
        private readonly List<NavDropGroup> _groups = new();
        private readonly Workspace _workspace;

        public NavSidebar(Workspace workspace)
        {
            _workspace = workspace;
            Dock = DockStyle.Left;
            Width = 236;
            BackColor = Theme.NavyDark;
            Theme.EnableDoubleBuffer(this);

            var brand = BuildBrandHeader();
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

            var navHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.NavyDark,
                Padding = new Padding(10, 8, 10, 8),
                AutoScroll = true
            };
            Theme.EnableDoubleBuffer(navHost);

            var items = new List<Control>();
            void AddButton(AppPage page, string text)
            {
                var btn = BuildButton(page, text);
                btn.Width = 216;
                btn.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
                items.Add(btn);
                navHost.Controls.Add(btn);
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
            purchaseGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            purchaseGroup.ExpandedChanged += () => LayoutNav(navHost, items);
            purchaseGroup.Register(_buttons);
            items.Add(purchaseGroup);
            navHost.Controls.Add(purchaseGroup);
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
            salesGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            salesGroup.ExpandedChanged += () => LayoutNav(navHost, items);
            salesGroup.Register(_buttons);
            items.Add(salesGroup);
            navHost.Controls.Add(salesGroup);
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
            invoiceGroup.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            invoiceGroup.ExpandedChanged += () => LayoutNav(navHost, items);
            invoiceGroup.Register(_buttons);
            items.Add(invoiceGroup);
            navHost.Controls.Add(invoiceGroup);
            _groups.Add(invoiceGroup);

            AddButton(AppPage.Debits, "Debits");
            AddButton(AppPage.Credits, "Credits");
            AddButton(AppPage.Banking, "Banking");
            AddButton(AppPage.Reports, "Reports");

            navHost.Resize += (_, _) => LayoutNav(navHost, items);
            LayoutNav(navHost, items);

            Controls.Add(navHost);
            Controls.Add(settingsRow);
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
                pair.Value.Enabled = unlocked || isSettings;
                pair.Value.Selected = pair.Key == _workspace.CurrentPage;
            }

            foreach (var group in _groups)
                group.SyncExpanded();
        }

        private static void LayoutNav(Panel host, List<Control> items)
        {
            int y = 4;
            int width = Math.Max(180, host.ClientSize.Width - host.Padding.Horizontal);
            foreach (var item in items)
            {
                item.Location = new Point(0, y);
                item.Width = width;
                y += item.Height + 4;
            }
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
}
