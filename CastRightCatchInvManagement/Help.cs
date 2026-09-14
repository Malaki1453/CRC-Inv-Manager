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
                "Use the left sidebar to move between pages. Back and Forward arrows next to the page title go through pages you opened in this window. Gold means you can use that arrow; a faded arrow means there is nowhere to go. The side buttons on the mouse, Alt+Left, and Alt+Right do the same. Those arrows hide on Home with the header. Log out is at the bottom, next to your name.",
                "A loading window with a spinner shows while the app is opening data. If the main window is slow to appear, that spinner means it is still working.",
                "After sign-in you land on Home, a full-area image with no buttons. Click the logo in the top-left of the sidebar to return there.",
                DataLink.UseInventoryServer
                    ? "Connect to the inventory server (enter its IP address) before the rest of the workspace unlocks. A local data folder is still available if you need it."
                    : "Choose a data folder before the rest of the workspace unlocks.",
                "Green and red notices appear in the bottom-right. They close on their own or with ×.");

            AddSection(stack, "Windows", "Windows",
                "Middle-click a sidebar tab, or right-click and choose Open in another window, to open that page in another window. If that page is already open, that window comes to the front instead. Each page is still one shared form, so Sales and Create Invoice stay linked.",
                "Left-click a tab to show it in this window, or to switch to the window that already has it. Tabs open in another window have a muted gold mark. Extra windows list every open window at the top; right-click a tab to see that list too. Each window keeps its own Back and Forward history.",
                "If you close the original window while extras are open, a remaining window becomes the main one.",
                "PDFs open in a separate viewer window, not a sidebar tab. That window comes to the front when it opens; after that you can switch to other windows as usual.");

            AddSection(stack, "Tables", "Tables",
                "Every table page opens on a centered search field. Type to slide it to the top and show matching rows. Matching is not case-sensitive and can be any part of a cell, including a close spelling. On tables with dates, Dates next to the search bar opens From and To. Type MM/DD/YYYY, or shorter like 6/1/26 or 6.1.26; a period can stand in for a slash, including after a one-digit month or day. Two-digit years become MM/DD/YYYY when you leave the box. Only a real calendar date is kept. You can also click a date column header for a from/to filter on that column.",
                "Click a column heading (not the sort arrows) to open a filter box under that header. Type to hide rows that do not contain that text. Matching is not case-sensitive and can be any part of the cell, not the whole value.",
                "You can open and type in more than one column. A row stays visible only if it matches every filter that has text.",
                "Clear a filter box and click away to close it. Leave a value in the box to keep that filter on.",
                "Click the arrows on a heading to sort that column A–Z or Z–A.",
                "Jump to column scrolls the table sideways to that heading without changing the filters.",
                "Every row has Record Status: Live, waiting for confirmation to add, waiting for confirmation to edit, or waiting for confirmation to delete. Waiting rows stay in the table (tinted) until someone Accepts or Rejects them on Review. Purchases starts with PO #, record status, status, ship date, and order date. Sales starts with SO #, record status, status, ship date, and PO #. Invoices starts with SO #, PO #, record status, type, customer, vendor, ship date, due date, status, and paid. Click the + header on the table to add columns. Right-click a column heading to hide it. Set default asks you to confirm, then saves the columns you have showing, and their order, as your default for that table. Default columns puts the table back to your saved default, or the original starting set if you have not saved one. Each signed-in user has their own defaults. The columns you show, hide, and rearrange are remembered the next time you open the app. The table stretches to fill the window.",
                "Double-click a row for View Details. On Purchases that popup shows vendor info from the vendor record, then the order header, then a table of every item on that PO. On Sales it shows customer info, then the order header, then every item on that sales order. On Customers and Vendors it opens identity and history. Other tables show every field on the row. Right-click for Edit Product or other row actions. That details popup is not a workspace window and cannot become the main window.");

            AddSection(stack, "Purchases", "Purchases",
                "Purchases lists current purchase rows, including unfinished ones from earlier terms. Completed purchases move into Old Inventory when you roll the term.",
                "New Purchase is one PO with as many product lines as you need, like Create Invoice. Type a vendor code or name to fill vendor and terms. Type an item code on a line to fill description and country of origin. + adds another product on the same PO. Saving writes a purchase PDF. Right-click Show PDF on Purchases. Double-click a Purchases row to see vendor info, the order header, and every item on that PO.",
                "Saving a purchase writes a purchase PDF. Right-click Show PDF. Incoming vendor invoices are imported on Create Invoice; that also stores the purchase PDF.",
                "Save writes one purchases row per line. Freight Co and the per-lb extras (overhead, freight, forwarder, other) apply to every line on that PO and are stored for tracking, not printed on invoices.",
                "Right-click Create Invoice on a purchase to record the vendor invoice you were given. Cast Right Catch is the receiving company; the vendor is the issuer. Matching PO lines fill Create Invoice.");

            AddSection(stack, "Sales", "Sales",
                "Sales lists sold product rows.",
                "New Sale opens Create Sales Order: one customer PO with as many product lines as you need, like New Purchase. Saving writes the sales rows, a sale PDF, and a sales-order PDF. Right-click Show PDF. SO # is written onto the sale when that order is saved.",
                "Right-click Add to Invoice, or Shift+click a row, to add every line on that customer PO to Create Invoice. If Create Invoice already has a different sale, it is cleared and replaced.",
                "Shift+click a row to add those lines without leaving Sales.",
                "Middle-click a row to work with a sales order. If that sale already has an SO # and a PDF, the PDF opens. If not, Create Sales Order fills from that PO.");

            AddSection(stack, "SalesOrder", "Sales orders",
                "Create Sales Order is one customer PO with as many product lines as you need, like New Purchase. Type a customer code or name to fill the header. Type an item code on a line to fill description and country of origin. Lines hold item, lot, pack, cases, volume, and price. Freight Co is a vendor list for tracking and is stored on the sale; it is not printed on the PDF. Double-click a Sales row to see customer info, the order header, and every item on that sales order.",
                "Ship To uses the customer Address. If none is on file, the field says “Not found, please input manually.”",
                "Enter a customer PO, or middle-click a sale, to add every matching line.",
                "Save Sales Order writes the sales rows, a sale PDF, and a sales-order PDF, stores them in the database, writes the SO # onto those sales lines, and opens the sales-order PDF.");

            AddSection(stack, "Invoices", "Invoices",
                "Invoices lists invoices you issued to customers and invoices vendors issued to you (Type Issued or Received).",
                "On Create Invoice, type a sales order or purchase PO in SO / PO #. Suggestions show the number, customer or vendor, and item count; a purchase row is marked · purchase. Picking one writes only the number. A sales order fills a customer invoice. A purchase PO switches the form to a received invoice and fills vendor, ship-to, and every line from that PO. If that SO # or PO # already has an invoice, the form switches to Edit Invoice, keeps that invoice number, and Save Invoice replaces the old PDF. You can also right-click Add to Invoice or Shift+click sales, or type a customer PO on a line. Right-click Create Invoice on a purchase to fill a received vendor invoice. If another party is already on the draft, it is cleared and replaced. Locked lines cannot be edited.",
                "On a customer invoice, Ship To is the customer address, Sold To is the name and contact, and Sales Rep is our contact. On a received invoice, Sold To and Ship To are this company, the vendor is the issuer, and Contact is the vendor’s contact.",
                "Tax has a # / % button. # is a flat amount. % is a percent of the subtotal after discount. Freight Co next to Freight is a vendor you pick for tracking; it is stored on the invoice and is not printed on the PDF.",
                "Import Invoice PDF on Invoices or Create Invoice reads a PDF and fills the form. Incoming vs outgoing is detected from the page: if we are the sold-to / bill-to party it is a received vendor invoice; if we are the issuer it is an invoice we sent. Layouts vary, so review before Create Invoice. Scans with no text still attach. Create Invoice writes our letterhead PDF into the database. Incoming invoices also write a purchase-order PDF for that PO, stored like a sales-order PDF, and keep the original file with the PO.",
                "PDFs live in the database and open in the app from there. Save as downloads a copy. Print, Replace, and Edit invoice / Edit sales order are on the toolbar. That window is not a workspace tab.");

            AddSection(stack, "Customers", "Customers",
                "The customer table shows name, company, phone, and current balance. Double-click View Details for Identity (including contact, terms, and address), a Banking section (routing number and account last 4), and tabs for Description, Sales, and Bank transactions. Right-click Edit Customer to change the record. Hide Routing Number and Account Number on a group if those users should not see bank details.",
                "Address, email, and phone fill Ship To on invoices and sales orders.");

            AddSection(stack, "Lookups", "Vendors, inventory, and other lists",
                "The vendor table shows name, company, phone, and current balance. Double-click View Details for Identity (including contact name), a Banking section, and tabs for Description, Purchases, and Bank transactions. Right-click Edit Vendor to change the record. Type is a required dropdown. Administrators manage types and lookup filters under Admin management: a filter is a set of types (Logistics can include Trucking) and can be used by more than one field. New Purchase Forwarder and Logistics each pick a filter. Hide Routing Number and Account Number on a group if those users should not see bank details.",
                "Vendors and Inventory are lookup tables used on purchase and sales forms.",
                "Debits and Credits are process tables: unfinished rows stay live, and completed rows move into Old Inventory when you roll the term.",
                "Banking shows imported and live-feed transactions. Accounts labels the bank accounts. Read file imports OFX, QFX, or CSV and skips duplicates. Sync live feed pulls new Plaid lines without opening the bank login.");

            AddSection(stack, "Reports", "Reports",
                "Reports use the current database view. Switch to Old to include archived process rows with live ones. Rows still waiting for confirmation to add are left out of report totals.",
                "Aging has Customers (open invoices) and Vendors (open purchases) tabs. Filter the open table if you want. Customer risk is credit limit, open invoices, and overdue amounts.",
                "Monthly P&L and profit per species compare sale amounts to lot cost (purchase cost / lb × pounds sold). Profit per species lists species totals; click a species to expand item codes. Supplier performance is purchase volume and cost by vendor.",
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
                "When IT adds a user, a random password is created (at least 8 characters, with a capital letter, a number, and a symbol). If SMTP is set, a spinner shows while the login email is sending. A Copy text button is always there so you can paste the username and password into a message yourself. That person must choose a new password on first sign-in. Forgot password tells them to call IT support. Sign-in locks for 15 minutes after every five wrong passwords. Thirty wrong sign-ins flags the account as Locked on User management. IT calls the person (or they call IT), then Clear lock to keep their password, or Set password to give them a new one over the phone.",
                "Stay signed in is one Admin setting for everyone (On or Off, how many days, and idle hours). When it is On, the sign-in screen has a checkbox to remember this PC. Log out at the bottom of the sidebar (or Sign out in Settings) ends it on this PC.");

            AddSection(stack, "Settings", "Settings",
                "Your account: username, name, email, password, and Sign out. Log out is also at the bottom of the sidebar. Any signed-in user can change their own account. Call IT support to reset a forgotten password.",
                "Information for invoices (business name, address, phone, email, terms) prints on PDFs. Only an administrator can edit it. Everyone can still see it.",
                DataLink.UseInventoryServer
                    ? "Data lives on the inventory server this company hosts. Clients never open the database files; they send and receive data on an encrypted stream. The server uses local SQLite until you point it at Digital Ocean Postgres (--postgres or CRC_POSTGRES). On the sign-in screen, enter the server IP (and port if it is not 7443). The address is remembered on this PC. Roll to Next Term is administrator-only."
                    : "Point every computer at the same shared data folder so they share the database and these settings. Choose the folder on the sign-in screen. crc_inventory.db and old_inventory.db live there. Roll to Next Term is administrator-only.",
                "The Current / Old toggle is on the sidebar, not in Settings. Current is this term. Old shows archived rows plus current work. Customers, vendors, inventory, accounts, and settings stay in the live database.",
                "Sales order numbers and product (purchase PO) numbers use a pattern such as CRC#### or CRCyy-####. yy / yyyy, mm, and dd are today’s date. # is the running number. Reuse missing numbers fills holes (CRC10 if 10 was deleted). Numbering is administrator-only and shared.",
                "Login email (SMTP) is on Admin → Admin management, not in Settings. One administrator sets the sending mailbox once; it is used for every new user. Gmail is smtp.gmail.com, port 587, with a Gmail app password. Microsoft 365 is smtp.office365.com. A blank host follows the login email. If the email still does not send, use Copy text on the user-saved dialog.",
                "The ? button next to Settings opens this Controls page.");

            AddSection(stack, "Admin", "Admin",
                "IT and administrators see Admin in the sidebar. It has User management, Groups, and Admin management. The Admin management tab is only for administrators.",
                "User management: add and edit users, assign one or more groups, reset passwords, assign IT or administrator, and (administrators only) Data access from the right-click menu. Groups includes built-in Admin and IT. Admin cannot be changed or deleted: Settings and User management, no inventory tables. Only an administrator can change IT permissions. Other groups can be edited by IT and administrators. A user can be in several groups; anything those groups allow wins over what they block. A setting you turn on or off for that user overrides the groups. Reset to groups on Data access clears those overrides.",
                "Pages is a tab on Admin: the tree is the sidebar. Drag a page onto another page to nest it in a new dropdown. Drag onto a folder to put it inside. Drag to the top or bottom of a row to reorder. Add tab creates an empty dropdown. Rename a folder or a page to change the sidebar name; window titles follow the page name. Uncheck a folder to hide everything in it.",
                "Review in the sidebar lists queued adds, edits, and deletes. Confirm-first users still write the row into the table with Record Status set to waiting for confirmation. Accept marks it Live (or deletes it if the request was a delete). Reject undoes the waiting add, restores the previous values on an edit, or clears a waiting delete. People with automatic write access on a visible table can open Review.",
                "Admin management: Stay signed in (on/off toggle, days, idle hours), vendor types and lookup filters, login email (SMTP) for new-user messages, and the live bank feed. Click Save after you change the login email or password so it is stored for every user. Show on the password box only previews a password you just typed; the saved password stays hidden. Only administrators can open this tab or change those settings.",
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
