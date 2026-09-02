namespace CastRightCatchInvManagement
{
    /// <summary>In-app Controls page. Opened from the ? button next to Settings.</summary>
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
                    ("Reports", "Reports"),
                    ("SignIn", "Sign in"),
                    ("Settings", "Settings"),
                    ("Admin", "Admin")
                },
                Jump);
            jump.Dock = DockStyle.Top;

            AddSection(stack, "General", "General",
                "Use the left sidebar to move between pages.",
                DataLink.UseInventoryServer
                    ? "Connect to the inventory server (enter its IP address) before the rest of the workspace unlocks. A local data folder is still available if you need it."
                    : "Choose a data folder before the rest of the workspace unlocks.",
                "Green and red notices appear in the bottom-right. They close on their own or with ×.");

            AddSection(stack, "Windows", "Windows",
                "Middle-click a sidebar tab to open that page in another window. Each page is still one shared form, so Sales and Create Invoice stay linked.",
                "Left-click a tab in a window to show that page there.",
                "If you close the original window while extras are open, a remaining window becomes the main one.",
                "PDFs open in a separate viewer window, not a sidebar tab.");

            AddSection(stack, "Tables", "Tables",
                "Click a column heading (not the sort arrows) to open a filter box under that header. Type to hide rows that do not contain that text. Matching is not case-sensitive and can be any part of the cell, not the whole value.",
                "You can open and type in more than one column. A row stays visible only if it matches every filter that has text.",
                "Clear a filter box and click away to close it. Leave a value in the box to keep that filter on.",
                "Click the arrows on a heading to sort that column A–Z or Z–A.",
                "Jump to column scrolls the table sideways to that heading without changing the filters.",
                "Purchases starts with PO #, status, ship date, and order date. Sales starts with SO #, status, ship date, and PO #. Invoices starts with SO #, customer, ship date, due date, status, and paid. Click the + header on the table to add columns. Right-click a column heading to hide it. Default columns on the toolbar puts the table back to that starting set. The columns you show, hide, and rearrange are remembered the next time you open the app. The table stretches to fill the window.",
                "Right-click a row for View Details (a popup with every field) or Edit Product. That details popup is not a workspace window and cannot become the main window.");

            AddSection(stack, "Dashboard", "Dashboard",
                "Command Center shows totals from the current database view. Switch to Old to include archived process rows with live ones.",
                "Total Revenue is the sum of sale amounts. If you cannot open Sales, it uses invoice amounts instead.",
                "Outstanding is unpaid invoice balances. Late Fees is the unpaid amount on invoices past their due date — there is no late-fee percentage yet. Deals is how many distinct customer POs (or sales orders) are on Sales.");

            AddSection(stack, "Purchases", "Purchases",
                "Purchases lists current purchase rows, including unfinished ones from earlier terms. Completed purchases move into Old Inventory when you roll the term.",
                "Purchase Form / Add Product creates or edits a purchase line.",
                "Vendor, item code, lot (PO #), costs, and dates on that form save back to purchases.");

            AddSection(stack, "Sales", "Sales",
                "Sales lists sold product rows.",
                "Sales Form / Add Product creates or edits a sale. Create Sales Order on that form builds or opens the sales-order PDF for the customer PO. SO # is written onto the sale when that PDF is created.",
                "Double-click a row to add every line on that customer PO to Create Invoice and open that page, unless Create Invoice is already open in another window.",
                "Shift+click a row to add those lines without leaving Sales.",
                "Middle-click a row to work with a sales order. If that sale already has an SO # and a PDF, the PDF opens in the app. If not, Create Sales Order fills from that PO.");

            AddSection(stack, "SalesOrder", "Sales orders",
                "Create Sales Order is a pick ticket: customer, ship-to, warehouse, freight, item, lot, cases, and volume.",
                "Ship To uses the customer Address. If none is on file, the field says “Not found, please input manually.”",
                "Enter a customer PO, or middle-click a sale, to add every matching line.",
                "Create Sales Order opens an existing PDF when those sales already have one. Otherwise it builds a PDF, stores it in the database, writes the SO # onto those sales lines, and opens it in the PDF window.");

            AddSection(stack, "Invoices", "Invoices",
                "Invoices lists invoice records. Create Invoice builds a PDF.",
                "Double-click or Shift+click sales to fill lines, or type a customer PO on a line. Locked lines cannot be edited.",
                "Ship To is the customer address. Sold To is the name and contact.",
                "Tax has a # / % button. # is a flat amount. % is a percent of the subtotal after discount.",
                "Create Invoice stores the PDF in the database, writes a copy into Stored Invoices, and opens it in the PDF window. Double-click an invoice row to open that PDF. If it has no PDF, you can create one from the sales on that invoice.",
                "PDFs open in their own window. Save to database keeps markups. Save as copies a file. Print, Replace, and Edit invoice / Edit sales order are on the toolbar. That window is not a workspace tab.");

            AddSection(stack, "Customers", "Customers",
                "The customer table shows name, company, phone, and current balance. Right-click View Details for the rest of the record, or Edit Customer to change it.",
                "Edit Customer and the double-click history window both show Identity (including contact, terms, and address) and tabs for Description, Sales, and Bank transactions.",
                "Address, email, and phone fill Ship To on invoices and sales orders.");

            AddSection(stack, "Lookups", "Vendors, item codes, and other lists",
                "The vendor table shows name, company, phone, and current balance. Right-click View Details or Edit Vendor for the rest. Edit Vendor and the double-click history window both show Identity and tabs for Description, Purchases, and Bank transactions.",
                "Vendors and Item Codes are lookup tables used on purchase and sales forms.",
                "Debits and Credits are process tables: unfinished rows stay live, and completed rows move into Old Inventory when you roll the term.",
                "Banking shows imported and live-feed transactions. Accounts labels the bank accounts. Read file imports OFX, QFX, or CSV and skips duplicates. Sync live feed pulls new Plaid lines without opening the bank login.");

            AddSection(stack, "Reports", "Reports",
                "Reports use the current database view. Switch to Old to include archived process rows with live ones.",
                "Aging lists open invoices by days past due. Customer risk is credit limit, open invoices, and overdue amounts.",
                "Monthly P&L and profit per species compare sale amounts to lot cost (purchase cost / lb × pounds sold). Supplier performance is purchase volume and cost by vendor.",
                "Commission tracker lists deals by PO / SO. There is no commission percentage in Settings yet, so it shows sale volume only.",
                "Export CSV downloads the open report as a spreadsheet file. The file starts with the report name, scope, and the summary totals from the top of the page, then the table. CSV is used instead of PDF so you can sort and filter in Excel.");

            AddSection(stack, "SignIn", "Sign in",
                DataLink.UseInventoryServer
                    ? "The app asks you to sign in after you connect to the inventory server, unless you checked Stay signed in on this PC."
                    : "The app asks you to sign in after you choose a data folder, unless you checked Stay signed in on this PC.",
                DataLink.UseInventoryServer
                    ? "The first IT user is created on the server PC (CrcInventoryServer --bootstrap). Clients cannot create it."
                    : "If there is no IT user yet, the sign-in screen lets you create one. That person is always IT.",
                "IT users and administrators see Admin in the sidebar. User management is a tab on that page.",
                "When IT adds a user, a 6-character password is emailed. That person must choose a new password and three security questions on first sign-in. Forgot password uses those questions.",
                "Stay signed in is one Admin setting for everyone (On or Off, how many days, and idle hours). When it is On, the sign-in screen has a checkbox to remember this PC. Sign out in Settings ends it on this PC.");

            AddSection(stack, "Settings", "Settings",
                "Your account: username, name, email, password, security questions, and Sign out. Any signed-in user can change their own account.",
                "Information for invoices (business name, address, phone, email, EIN, terms) prints on PDFs. Only an administrator can edit it. Everyone can still see it.",
                DataLink.UseInventoryServer
                    ? "Data lives on the inventory server this company hosts. Clients never open the database files; they send and receive data on an encrypted stream. On the sign-in screen, enter the server IP (and port if it is not 7443). The address is remembered on this PC. Roll to Next Term is administrator-only."
                    : "Point every computer at the same shared data folder so they share the database and these settings. Choose the folder on the sign-in screen. crc_inventory.db and old_inventory.db live there. Roll to Next Term is administrator-only.",
                "The Current / Old toggle is on the sidebar, not in Settings. Current is this term. Old shows archived rows plus current work. Customers, vendors, item codes, accounts, and settings stay in the live database.",
                "Sales order numbers and product (purchase PO) numbers use a pattern such as CRC#### or CRCyy-####. yy / yyyy, mm, and dd are today’s date. # is the running number. Reuse missing numbers fills holes (CRC10 if 10 was deleted). Numbering is administrator-only and shared.",
                "SMTP in Settings is for login emails when IT adds a user. Administrator-only.",
                "The ? button next to Settings opens this Controls page.");

            AddSection(stack, "Admin", "Admin",
                "IT and administrators see Admin in the sidebar. It has two tabs: User management and Admin management. The Admin management tab is only for administrators.",
                "User management: add and edit users, reset passwords, assign IT or administrator, and (administrators only) table access from the right-click menu. Blocked pages disappear from that person’s sidebar. Administrators and IT still see every table.",
                "Admin management: Stay signed in (on/off toggle, days, idle hours) and the live bank feed. Only administrators can open this tab, connect the bank, sync, or change those settings.",
                "People with Banking can still see imported transactions and read a bank file. They cannot sync the live feed or log into the bank.",
                "API keys are a one-time setup under Admin management → API keys. Create an account at dashboard.plaid.com, copy Client ID and Secret from Team Settings → Keys, save them (sandbox while testing), then Connect bank. Sandbox test login is user_good / pass_good. After Plaid approves a live app, switch to Development or Production.");

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

        /// <summary>One Controls heading plus body paragraphs, registered for the jump chips.</summary>
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

        /// <summary>Scroll the Controls page so the named section is at the top.</summary>
        private void Jump(string key)
        {
            if (!_sections.TryGetValue(key, out var section))
                return;

            _scroller.ScrollControlIntoView(section);
            section.Focus();
        }
    }

    /// <summary>Jump chips across the top of Controls.</summary>
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
