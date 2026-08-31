namespace CastRightCatchInvManagement
{
    public partial class Help : Form, INavigationPage
    {
        private readonly Dictionary<string, Control> _sections = new(StringComparer.OrdinalIgnoreCase);
        private Panel _scroller = null!;

        public Help()
        {
            InitializeComponent();
            BuildUi();
        }

        public void HighlightCurrentPage() { }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 16, 24);

            _scroller = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(0, 0, 12, 0)
            };

            var stack = new Panel
            {
                Location = new Point(0, 0),
                Width = 800,
                BackColor = Theme.Cream
            };

            var jump = new HorizontalSectionMenu(
                new[]
                {
                    ("General", "General"),
                    ("Windows", "Windows"),
                    ("Tables", "Tables"),
                    ("Purchases", "Purchases"),
                    ("Sales", "Sales"),
                    ("SalesOrder", "Sales orders"),
                    ("Invoices", "Invoices"),
                    ("Customers", "Customers"),
                    ("Lookups", "Other pages"),
                    ("Settings", "Settings")
                },
                Jump);
            jump.Dock = DockStyle.Top;

            AddSection(stack, "General", "General",
                "Use the left sidebar to move between pages.",
                "A data folder must be chosen in Settings before the rest of the workspace unlocks.",
                "Green and red notices appear in the bottom-right. They close on their own or with ×.");

            AddSection(stack, "Windows", "Windows",
                "Middle-click a sidebar tab to open that page in another window. Each page is still one shared form, so Sales and Create Invoice stay linked.",
                "Left-click a tab in a window to show that page there.",
                "If you close the original window while extras are open, a remaining window becomes the main one.");

            AddSection(stack, "Tables", "Tables",
                "Upload CSV imports rows into this page’s table in the database. Headings must match that page.",
                "Click a column heading (not the sort arrows) to open a filter box under that header. Type to hide rows that do not contain that text. Matching is not case-sensitive and can be any part of the cell, not the whole value.",
                "You can open and type in more than one column. A row stays visible only if it matches every filter that has text.",
                "Clear a filter box and click away to close it. Leave a value in the box to keep that filter on.",
                "Click the arrows on a heading to sort that column A–Z or Z–A.",
                "Jump to column scrolls the table sideways to that heading without changing the filters.",
                "Purchases starts with PO #, ship date, and order date. Sales starts with SO #, ship date, and PO #. Click the + header on the table to add columns. Right-click a column heading to hide it. Default columns on the toolbar puts the table back to that starting set. The table stretches to fill the window.",
                "Right-click a row for View Details (a popup with every field) or Edit Product. That details popup is not a workspace window and cannot become the main window.");

            AddSection(stack, "Dashboard", "Dashboard",
                "Command Center is the home overview for the current term.");

            AddSection(stack, "Purchases", "Purchases",
                "Purchases lists purchase rows for the term.",
                "Purchase Form / Add Product creates or edits a purchase line.",
                "Vendor, item code, lot (PO #), costs, and dates on that form save back to purchases.");

            AddSection(stack, "Sales", "Sales",
                "Sales lists sold product rows.",
                "Sales Form / Add Product creates or edits a sale. Create Sales Order on that form builds or opens the sales-order PDF for the customer PO. SO # is written onto the sale when that PDF is created.",
                "Double-click a row to add every line on that customer PO to Create Invoice and open that page, unless Create Invoice is already open in another window.",
                "Shift+click a row to add those lines without leaving Sales.",
                "Middle-click a row to work with a sales order. If that sale already has an SO # and a PDF, the PDF opens. If not, Create Sales Order fills from that PO.");

            AddSection(stack, "SalesOrder", "Sales orders",
                "Create Sales Order is a pick ticket: customer, ship-to, warehouse, freight, item, lot, cases, and volume.",
                "Ship To uses the customer Address. If none is on file, the field says “Not found, please input manually.”",
                "Enter a customer PO, or middle-click a sale, to add every matching line.",
                "Create Sales Order opens an existing PDF when those sales already have one. Otherwise it builds a PDF, stores it in the database, writes the SO # onto those sales lines, and opens the file.");

            AddSection(stack, "Invoices", "Invoices",
                "Invoices lists invoice records. Create Invoice builds a PDF.",
                "Double-click or Shift+click sales to fill lines, or type a customer PO on a line. Locked lines cannot be edited.",
                "Ship To is the customer address. Sold To is the name and contact.",
                "Tax has a # / % button. # is a flat amount. % is a percent of the subtotal after discount.",
                "Create Invoice stores the PDF in the database and also writes a copy into Stored Invoices.");

            AddSection(stack, "Customers", "Customers",
                "The customer table shows name, company, phone, and current balance. Right-click View Details for the rest of the record, or Edit Customer to change it.",
                "Double-click a customer to open history: sales with that customer, notes, and a payment-history slot for when the bank is connected.",
                "Address, email, and phone fill Ship To on invoices and sales orders.");

            AddSection(stack, "Lookups", "Vendors, item codes, and other lists",
                "The vendor table shows name, company, phone, and current balance. Right-click View Details or Edit Vendor for the rest.",
                "Vendors and Item Codes are lookup tables used on purchase and sales forms.",
                "Debits, Credits, and Banking are term tables like the other data pages.",
                "Reports will use the current term’s data.");

            AddSection(stack, "Settings", "Settings",
                "Information for invoices prints on PDFs: business name, address, phone, email, EIN, and terms.",
                "Point every computer at the same shared data folder. Inventory, settings, and invoice/sales-order PDFs live in crc_inventory.db. Copies of those PDFs are also written to Stored Invoices and Stored Sales Orders so you can open them in Explorer. This PC only stores the folder path. A network share is more reliable than OneDrive or Dropbox if two people might work at the same time. Roll to Next Term starts a new term without deleting earlier rows.",
                "User email is this Windows user’s sales-rep address. It is stored in the shared database, so the same person keeps it on any computer.",
                "Sales order numbers can use a pattern such as CRC#### and a start number. CRC#### starts at CRC0001. CRC#### with start 1000 starts at CRC1000. Leave the pattern blank to keep 10001, 10002, and so on.",
                "Product numbers (purchase PO #) use the same kind of pattern. Leave that pattern blank to keep CRC26-10001, CRC26-10002, and so on.",
                "The ? button next to Settings opens this Controls page.");

            LayoutStack(stack);
            stack.Resize += (_, _) => LayoutStack(stack);
            _scroller.Resize += (_, _) =>
            {
                int width = Math.Max(520, _scroller.ClientSize.Width - 8);
                stack.Width = width;
                LayoutStack(stack);
            };
            _scroller.Controls.Add(stack);

            Controls.Add(_scroller);
            Controls.Add(jump);
        }

        private void AddSection(Control host, string key, string title, params string[] lines)
        {
            var card = new CardPanel
            {
                Padding = new Padding(20, 16, 20, 16),
                Margin = new Padding(0)
            };

            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 14)
            };

            var body = new Label
            {
                Text = string.Join(Environment.NewLine + Environment.NewLine, lines),
                Font = Theme.Body,
                ForeColor = Theme.Ink,
                AutoSize = false,
                Location = new Point(20, 48)
            };

            card.Controls.Add(heading);
            card.Controls.Add(body);
            card.Tag = body;
            host.Controls.Add(card);
            _sections[key] = card;
        }

        private static void LayoutStack(Panel stack)
        {
            int y = 0;
            int width = Math.Max(480, stack.Width);
            foreach (Control card in stack.Controls)
            {
                card.Left = 0;
                card.Top = y;
                card.Width = width;
                if (card.Tag is Label body)
                {
                    body.MaximumSize = new Size(Math.Max(200, width - 48), 0);
                    body.Width = Math.Max(200, width - 48);
                    body.Height = Math.Max(40, TextRenderer.MeasureText(
                        body.Text,
                        body.Font,
                        new Size(body.Width, int.MaxValue),
                        TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height + 8);
                    card.Height = body.Top + body.Height + 20;
                }

                y += card.Height + 12;
            }

            stack.Height = y + 8;
        }

        private void Jump(string key)
        {
            if (!_sections.TryGetValue(key, out var section))
                return;

            _scroller.ScrollControlIntoView(section);
            section.Focus();
        }
    }

    internal sealed class HorizontalSectionMenu : Panel
    {
        private readonly Button _toggle;
        private readonly Panel _strip;
        private readonly FlowLayoutPanel _chips;
        private readonly Button _left;
        private readonly Button _right;
        private bool _expanded;
        private int _offset;

        public HorizontalSectionMenu((string Key, string Text)[] items, Action<string> jump)
        {
            Height = 64;
            BackColor = Theme.Cream;
            Padding = new Padding(0, 8, 20, 10);

            var heading = new Label
            {
                Text = "Controls",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = false,
                TextAlign = ContentAlignment.MiddleLeft,
                Dock = DockStyle.Left,
                Width = 118
            };

            _toggle = new Button
            {
                Text = "Sections   ›",
                Dock = DockStyle.Left,
                Width = 136,
                TabStop = false
            };
            Theme.StyleOutlineButton(_toggle);
            _toggle.Margin = new Padding(0, 4, 12, 4);
            _toggle.Click += (_, _) => SetExpanded(!_expanded);

            _left = MakeArrow("‹");
            _left.Dock = DockStyle.Left;
            _left.Click += (_, _) => ScrollBy(-180);

            _right = MakeArrow("›");
            _right.Dock = DockStyle.Right;
            _right.Click += (_, _) => ScrollBy(180);

            _strip = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Cream,
                AutoScroll = false,
                Visible = false,
                Padding = new Padding(12, 0, 12, 0)
            };
            Theme.EnableDoubleBuffer(_strip);

            _chips = new FlowLayoutPanel
            {
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink,
                WrapContents = false,
                FlowDirection = FlowDirection.LeftToRight,
                BackColor = Theme.Cream,
                Padding = new Padding(4, 4, 16, 4),
                Location = new Point(12, 0)
            };

            foreach (var item in items)
            {
                var btn = new Button
                {
                    Text = item.Text,
                    AutoSize = true,
                    Height = 32,
                    Margin = new Padding(0, 2, 12, 2),
                    Padding = new Padding(12, 0, 12, 0),
                    TabStop = false,
                    Tag = item.Key
                };
                Theme.StyleOutlineButton(btn);
                btn.Click += (_, _) => jump((string)btn.Tag!);
                _chips.Controls.Add(btn);
            }

            _strip.Controls.Add(_chips);
            _strip.MouseWheel += StripWheel;
            _chips.MouseWheel += StripWheel;
            foreach (Control child in _chips.Controls)
                child.MouseWheel += StripWheel;

            _strip.Resize += (_, _) =>
            {
                CenterChips();
                ClampOffset();
                UpdateArrows();
            };
            _chips.SizeChanged += (_, _) =>
            {
                CenterChips();
                ClampOffset();
                UpdateArrows();
            };

            Controls.Add(_strip);
            Controls.Add(_right);
            Controls.Add(_left);
            Controls.Add(_toggle);
            Controls.Add(heading);

            _left.Visible = false;
            _right.Visible = false;
        }

        private void SetExpanded(bool expanded)
        {
            _expanded = expanded;
            _toggle.Text = _expanded ? "Sections   ‹" : "Sections   ›";
            _strip.Visible = _expanded;
            if (!_expanded)
                _offset = 0;
            CenterChips();
            ClampOffset();
            UpdateArrows();
        }

        private void ScrollBy(int delta)
        {
            _offset += delta;
            ClampOffset();
            UpdateArrows();
        }

        private void ClampOffset()
        {
            int view = Math.Max(0, _strip.ClientSize.Width - _strip.Padding.Horizontal);
            int max = Math.Max(0, _chips.Width - view);
            _offset = Math.Max(0, Math.Min(max, _offset));
            _chips.Left = _strip.Padding.Left - _offset;
        }

        private void CenterChips()
        {
            int y = Math.Max(0, (_strip.ClientSize.Height - _chips.Height) / 2);
            _chips.Top = y;
        }

        private void StripWheel(object? sender, MouseEventArgs e)
        {
            if (!_expanded)
                return;
            ScrollBy(-Math.Sign(e.Delta) * 80);
            if (e is HandledMouseEventArgs handled)
                handled.Handled = true;
        }

        private void UpdateArrows()
        {
            int view = Math.Max(0, _strip.ClientSize.Width - _strip.Padding.Horizontal);
            bool overflow = _expanded && _chips.Width > view + 2;
            _left.Visible = overflow;
            _right.Visible = overflow;
        }

        private static Button MakeArrow(string text)
        {
            var btn = new Button
            {
                Text = text,
                Width = 36,
                TabStop = false,
                Visible = false
            };
            Theme.StyleOutlineButton(btn);
            btn.FlatAppearance.BorderSize = 0;
            return btn;
        }
    }
}
