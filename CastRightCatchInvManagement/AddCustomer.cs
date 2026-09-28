namespace CastRightCatchInvManagement
{
    /// <summary>New Customer page: identity, billing/shipping addresses, and banking.</summary>
    public partial class AddCustomer : Form, INavigationPage
    {
        private TextBox _code = null!;
        private TextBox _name = null!;
        private TextBox _company = null!;
        private TextBox _phone = null!;
        private TextBox _contact = null!;
        private TextBox _email = null!;
        private TextBox _terms = null!;
        private TextBox _credit = null!;
        private TextBox _balance = null!;
        private TextBox _established = null!;
        private TextBox _billing = null!;
        private CheckBox _shipDifferent = null!;
        private TextBox _shipping = null!;
        private Panel _shippingHost = null!;
        private TextBox _routing = null!;
        private TextBox _account = null!;
        private TextBox _notes = null!;
        private Button _save = null!;
        private Button _another = null!;
        private Label _title = null!;
        private Label _waitNote = null!;
        private string _originalCode = "";
        private string _storedAccount = "";
        /// <summary>True when OpenNew should wipe the last draft on the next show.</summary>
        internal static bool StartNew { get; set; }
        /// <summary>Customer row queued by OpenEdit until this page is shown.</summary>
        internal static Dictionary<string, string>? PendingEdit { get; set; }

        /// <summary>Build the New Customer page on a blank record.</summary>
        public AddCustomer()
        {
            InitializeComponent();
            BuildUi();
        }

        /// <summary>Open this page as a blank customer with the next code.</summary>
        public static void OpenNew()
        {
            if (!DataAccess.CanMutate(DataFiles.Customers))
            {
                MessageBox.Show("This account can only view customers.", "New Customer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = null;
            StartNew = true;
            Navigator.GoTo(AppPage.AddCustomer);
        }

        /// <summary>Open this page with the selected customer loaded for edit.</summary>
        public static void OpenEdit(Dictionary<string, string> record)
        {
            if (!DataAccess.CanMutate(DataFiles.Customers))
            {
                MessageBox.Show("This account can only view customers.", "Edit Customer",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = record;
            StartNew = false;
            Navigator.GoTo(AppPage.AddCustomer);
        }

        /// <summary>Apply a queued new/edit when this page is shown.</summary>
        public void HighlightCurrentPage()
        {
            if (PendingEdit != null)
            {
                var record = PendingEdit;
                PendingEdit = null;
                StartNew = false;
                LoadRecord(record);
                return;
            }

            if (!StartNew)
                return;
            StartNew = false;
            ResetForm();
        }

        /// <summary>Lay out identity, addresses, banking, and save actions.</summary>
        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 20, 28, 24);
            AutoScroll = true;

            _title = new Label
            {
                Text = "New Customer",
                Dock = DockStyle.Top,
                Height = 40
            };
            Theme.StylePageTitle(_title);
            _waitNote = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(12, 6, 12, 6),
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                BackColor = Theme.WaitEditFill,
                Visible = false
            };

            var footer = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Theme.Cream
            };
            _save = new Button { Text = "Add Customer", Size = new Size(150, 34), Location = new Point(0, 8) };
            Theme.StyleGoldButton(_save);
            _save.Click += (_, _) => SaveRecord(stayOnForm: false);
            _another = new Button { Text = "Save and Next", Size = new Size(140, 34), Location = new Point(166, 8) };
            Theme.StyleNavyButton(_another);
            _another.Click += (_, _) =>
            {
                if (SaveRecord(stayOnForm: true))
                    _code.Focus();
            };
            var clear = new Button { Text = "Clear", Size = new Size(90, 34), Location = new Point(312, 8) };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetForm();
            footer.Controls.Add(_save);
            footer.Controls.Add(_another);
            footer.Controls.Add(clear);

            var identity = BuildIdentity();
            var addresses = BuildAddresses();
            var banking = BuildBanking();
            var notes = BuildNotes();

            var stack = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            notes.Dock = DockStyle.Top;
            banking.Dock = DockStyle.Top;
            addresses.Dock = DockStyle.Top;
            identity.Dock = DockStyle.Top;
            stack.Controls.Add(notes);
            stack.Controls.Add(banking);
            stack.Controls.Add(addresses);
            stack.Controls.Add(identity);

            Controls.Add(stack);
            Controls.Add(footer);
            Controls.Add(_waitNote);
            Controls.Add(_title);
            ResetForm();
        }

        /// <summary>Code, name, company, contact, and credit fields.</summary>
        private CardPanel BuildIdentity()
        {
            var card = Card("Identity", 220);
            _code = AddField(card, "CODE", 20, 48, 140);
            _name = AddField(card, "NAME", 180, 48, 220);
            _company = AddField(card, "COMPANY", 420, 48, 220);
            _phone = AddField(card, "PHONE", 660, 48, 160);
            _contact = AddField(card, "CONTACT NAME", 20, 108, 220);
            _email = AddField(card, "EMAIL", 260, 108, 220);
            _terms = AddField(card, "TERMS", 500, 108, 140);
            _credit = AddField(card, "CREDIT LIMIT", 660, 108, 160);
            _balance = AddField(card, "CURRENT BALANCE", 20, 168, 160);
            _established = AddField(card, "ESTABLISHED", 200, 168, 160);
            _code.PlaceholderText = "C-1001";
            _name.PlaceholderText = "Customer name";
            _company.PlaceholderText = "Company";
            _phone.PlaceholderText = "(253) 000-0000";
            _contact.PlaceholderText = "Who we talk to";
            _email.PlaceholderText = "name@company.com";
            _terms.PlaceholderText = "NET 15";
            _credit.PlaceholderText = "0.00";
            _balance.PlaceholderText = "0.00";
            MoneyFormat.BindInput(_credit);
            MoneyFormat.BindInput(_balance);
            return card;
        }

        /// <summary>Billing address plus an optional separate shipping address.</summary>
        private CardPanel BuildAddresses()
        {
            var card = Card("Addresses", 250);
            _billing = AddMultiline(card, "BILLING ADDRESS", 20, 48, 820, 72);
            _shipDifferent = new CheckBox
            {
                Text = "Shipping address is different",
                AutoSize = true,
                Location = new Point(20, 132),
                ForeColor = Theme.Ink,
                Font = Theme.Body
            };
            _shipDifferent.CheckedChanged += (_, _) => SyncShipping();
            card.Controls.Add(_shipDifferent);

            _shippingHost = new Panel
            {
                Location = new Point(12, 158),
                Size = new Size(840, 84),
                Visible = false
            };
            _shipping = AddMultiline(_shippingHost, "SHIPPING ADDRESS", 8, 0, 820, 72);
            card.Controls.Add(_shippingHost);
            _billing.PlaceholderText = "Street, city, state, ZIP";
            _shipping.PlaceholderText = "Street, city, state, ZIP";
            return card;
        }

        /// <summary>Routing and account numbers, same as the customer edit dialog.</summary>
        private CardPanel BuildBanking()
        {
            var card = Card("Banking", 110);
            _routing = AddField(card, "ROUTING NUMBER", 20, 48, 280);
            _account = AddField(card, "ACCOUNT NUMBER", 320, 48, 280);
            _routing.PlaceholderText = "9-digit routing number";
            _account.PlaceholderText = "Full account number — only last 4 is shown after save";
            _account.GotFocus += (_, _) =>
            {
                if (_account.Text.Contains('•'))
                    _account.SelectAll();
            };
            return card;
        }

        /// <summary>Free-form notes stored on Description and Notes.</summary>
        private CardPanel BuildNotes()
        {
            var card = Card("Notes", 140);
            _notes = AddMultiline(card, "NOTES", 20, 48, 820, 72);
            _notes.PlaceholderText = "Notes about this customer";
            return card;
        }

        /// <summary>Show shipping fields only when the box is checked.</summary>
        private void SyncShipping()
        {
            _shippingHost.Visible = _shipDifferent.Checked;
        }

        /// <summary>Blank the form and assign the next customer code.</summary>
        public void ResetForm()
        {
            DataFiles.EnsureCustomerColumns();
            _originalCode = "";
            _storedAccount = "";
            _title.Text = "New Customer";
            _save.Text = "Add Customer";
            _another.Visible = true;
            ShowWaitNote("");
            _code.Text = DataFiles.NextCustomerCode();
            _name.Text = "";
            _company.Text = "";
            _phone.Text = "";
            _contact.Text = "";
            _email.Text = "";
            _terms.Text = "";
            _credit.Text = "";
            _balance.Text = "";
            _established.Text = "";
            _billing.Text = "";
            _shipping.Text = "";
            _shipDifferent.Checked = false;
            _routing.Text = "";
            _account.Text = "";
            _notes.Text = "";
            SyncShipping();
        }

        /// <summary>Fill the editors from a customers row.</summary>
        private void LoadRecord(Dictionary<string, string> record)
        {
            DataFiles.EnsureCustomerColumns();
            _originalCode = DataFiles.GetRecord(record, "Code").Trim();
            _title.Text = "Edit Customer";
            _save.Text = "Save Customer";
            _another.Visible = false;
            ShowWaitNote(DataFiles.StatusOf(record));
            _code.Text = _originalCode;
            _name.Text = DataFiles.GetRecord(record, "Name");
            _company.Text = DataFiles.GetRecord(record, "Company");
            _phone.Text = DataFiles.GetRecord(record, "Phone");
            _contact.Text = DataFiles.GetRecord(record, "Contact Name");
            _email.Text = DataFiles.GetRecord(record, "Email");
            _terms.Text = DataFiles.GetRecord(record, "Terms");
            _credit.Text = MoneyFormat.Display(DataFiles.GetRecord(record, "Credit Limit"));
            _balance.Text = MoneyFormat.Display(DataFiles.GetRecord(record, "Current Balance"));
            _established.Text = DataFiles.GetRecord(record, "Established");
            _billing.Text = DataFiles.CustomerBillingAddress(record);
            string shipping = DataFiles.GetRecord(record, DataFiles.ShippingAddressColumn).Trim();
            _shipping.Text = shipping;
            _shipDifferent.Checked = shipping.Length > 0 &&
                !shipping.Equals(_billing.Text.Trim(), StringComparison.OrdinalIgnoreCase);
            _notes.Text = DataFiles.GetRecordAny(record, "Description", "Notes");
            _routing.Text = DataFiles.GetRecord(record, DataFiles.RoutingNumber);
            _storedAccount = DataFiles.DigitsOnly(DataFiles.GetRecord(record, DataFiles.AccountNumber));
            _account.Text = DataFiles.MaskAccountNumber(_storedAccount);
            SyncShipping();
        }

        /// <summary>Gold bar when this record is still waiting in Review.</summary>
        private void ShowWaitNote(string status)
        {
            bool waiting = DataFiles.IsWaiting(
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    [DataFiles.RecordStatus] = status
                });
            _waitNote.Visible = waiting;
            if (!waiting)
                return;
            _waitNote.BackColor = status.Equals(DataFiles.RecordWaitingEdit, StringComparison.OrdinalIgnoreCase)
                ? Theme.WaitEditFill
                : Theme.WaitAddFill;
            _waitNote.Text = status.Length > 0 ? status + "." : "This customer is awaiting confirmation.";
        }

        /// <summary>Insert the customer. Returns false when validation or the write fails.</summary>
        private bool SaveRecord(bool stayOnForm)
        {
            if (!AppLock.HasFolder())
            {
                MessageBox.Show("Select a data folder in Settings first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string code = _code.Text.Trim();
            string name = _name.Text.Trim();
            if (code.Length == 0 || name.Length == 0)
            {
                MessageBox.Show("Enter a code and a name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            bool exists = DataFiles.ReadAllRecords(DataFiles.Customers).Any(record =>
                DataFiles.GetRecord(record, "Code").Equals(code, StringComparison.OrdinalIgnoreCase));
            if (exists && !code.Equals(_originalCode, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("That code is already in use.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string billing = _billing.Text.Trim();
            string shipping = _shipDifferent.Checked ? _shipping.Text.Trim() : "";
            if (shipping.Equals(billing, StringComparison.OrdinalIgnoreCase))
                shipping = "";

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = code,
                ["Name"] = name,
                ["Company"] = _company.Text.Trim(),
                ["Phone"] = _phone.Text.Trim(),
                ["Contact Name"] = _contact.Text.Trim(),
                ["Email"] = _email.Text.Trim(),
                ["Terms"] = _terms.Text.Trim(),
                ["Credit Limit"] = MoneyFormat.Store(_credit.Text),
                ["Current Balance"] = MoneyFormat.Store(_balance.Text),
                ["Established"] = _established.Text.Trim(),
                ["Address"] = billing,
                [DataFiles.ShippingAddressColumn] = shipping,
                ["Notes"] = _notes.Text.Trim(),
                ["Description"] = _notes.Text.Trim(),
                [DataFiles.RoutingNumber] = DataFiles.DigitsOnly(_routing.Text),
                [DataFiles.AccountNumber] = DataFiles.ResolveAccountNumber(_account.Text, _storedAccount)
            };

            try
            {
                MutateResult result;
                if (_originalCode.Length > 0)
                {
                    result = DataFiles.MutateUpdate(
                        DataFiles.Customers,
                        record => DataFiles.GetRecord(record, "Code")
                            .Equals(_originalCode, StringComparison.OrdinalIgnoreCase),
                        fields);
                }
                else
                {
                    result = DataFiles.MutateInsert(DataFiles.Customers, fields);
                }
                if (!result.Ok)
                {
                    ToastAlert.Error(this, result.Message);
                    return false;
                }

                ToastAlert.Success(this, result.Queued
                    ? result.Message
                    : _originalCode.Length > 0 ? "The customer was saved." : "The customer was added.");
                ResetForm();
                if (!stayOnForm)
                    Navigator.GoToAndSearch(AppPage.Customers, name);
                return true;
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return false;
            }
        }

        private static CardPanel Card(string title, int height)
        {
            var card = new CardPanel
            {
                Height = height,
                Margin = new Padding(0, 0, 0, 12),
                Padding = new Padding(0)
            };
            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 12)
            };
            card.Controls.Add(heading);
            return card;
        }

        private static TextBox AddField(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption, Location = new Point(x, y) };
            Theme.StyleFieldLabel(label);
            var box = new TextBox
            {
                Location = new Point(x, y + 16),
                Size = new Size(width, 26)
            };
            Theme.StyleField(box);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }

        private static TextBox AddMultiline(Control parent, string caption, int x, int y, int width, int height)
        {
            var label = new Label { Text = caption, Location = new Point(x, y) };
            Theme.StyleFieldLabel(label);
            var box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Location = new Point(x, y + 16),
                Size = new Size(width, height)
            };
            Theme.StyleField(box);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }
    }
}
