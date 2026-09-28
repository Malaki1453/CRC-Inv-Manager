namespace CastRightCatchInvManagement
{
    /// <summary>New/edit vendor page: identity, type, amount, and banking.</summary>
    public partial class AddVendor : Form, INavigationPage
    {
        private TextBox _code = null!;
        private TextBox _name = null!;
        private TextBox _company = null!;
        private TextBox _phone = null!;
        private TextBox _contact = null!;
        private TextBox _terms = null!;
        private ComboBox _type = null!;
        private TextBox _amount = null!;
        private TextBox _balance = null!;
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
        /// <summary>Vendor row queued by OpenEdit until this page is shown.</summary>
        internal static Dictionary<string, string>? PendingEdit { get; set; }

        /// <summary>Build the New Vendor page on a blank record.</summary>
        public AddVendor()
        {
            InitializeComponent();
            BuildUi();
        }

        /// <summary>Open this page as a blank vendor with the next code.</summary>
        public static void OpenNew()
        {
            if (!DataAccess.CanMutate(DataFiles.Vendors))
            {
                MessageBox.Show("This account can only view vendors.", "New Vendor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = null;
            StartNew = true;
            Navigator.GoTo(AppPage.AddVendor);
        }

        /// <summary>Open this page with the selected vendor loaded for edit.</summary>
        public static void OpenEdit(Dictionary<string, string> record)
        {
            if (!DataAccess.CanMutate(DataFiles.Vendors))
            {
                MessageBox.Show("This account can only view vendors.", "Edit Vendor",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = record;
            StartNew = false;
            Navigator.GoTo(AppPage.AddVendor);
        }

        /// <summary>Apply a queued new/edit when this page is shown.</summary>
        public void HighlightCurrentPage()
        {
            FillTypes();
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

        /// <summary>Lay out identity, banking, notes, and save actions.</summary>
        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 20, 28, 24);
            AutoScroll = true;

            _title = new Label
            {
                Text = "New Vendor",
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
            _save = new Button { Text = "Add Vendor", Size = new Size(140, 34), Location = new Point(0, 8) };
            Theme.StyleGoldButton(_save);
            _save.Click += (_, _) => SaveRecord(stayOnForm: false);
            _another = new Button { Text = "Save and Next", Size = new Size(140, 34), Location = new Point(156, 8) };
            Theme.StyleNavyButton(_another);
            _another.Click += (_, _) =>
            {
                if (SaveRecord(stayOnForm: true))
                    _code.Focus();
            };
            var clear = new Button { Text = "Clear", Size = new Size(90, 34), Location = new Point(302, 8) };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetForm();
            footer.Controls.Add(_save);
            footer.Controls.Add(_another);
            footer.Controls.Add(clear);

            var identity = Card("Identity", 220);
            _code = AddField(identity, "CODE", 20, 48, 140);
            _name = AddField(identity, "NAME", 180, 48, 220);
            _company = AddField(identity, "COMPANY", 420, 48, 220);
            _phone = AddField(identity, "PHONE", 660, 48, 160);
            _contact = AddField(identity, "CONTACT NAME", 20, 108, 220);
            _terms = AddField(identity, "TERMS", 260, 108, 140);
            _type = AddCombo(identity, "TYPE", 420, 108, 180);
            _amount = AddField(identity, "AMOUNT", 620, 108, 140);
            _balance = AddField(identity, "CURRENT BALANCE", 20, 168, 160);
            _code.PlaceholderText = "V-1001";
            _name.PlaceholderText = "Vendor name";
            _company.PlaceholderText = "Company";
            _phone.PlaceholderText = "(253) 000-0000";
            _contact.PlaceholderText = "Who we talk to";
            _terms.PlaceholderText = "NET 15";
            _amount.PlaceholderText = "0.00";
            _balance.PlaceholderText = "0.00";
            MoneyFormat.BindInput(_amount);
            MoneyFormat.BindInput(_balance);

            var banking = Card("Banking", 110);
            _routing = AddField(banking, "ROUTING NUMBER", 20, 48, 280);
            _account = AddField(banking, "ACCOUNT NUMBER", 320, 48, 280);
            _routing.PlaceholderText = "9-digit routing number";
            _account.PlaceholderText = "Full account number — only last 4 is shown after save";
            _account.GotFocus += (_, _) =>
            {
                if (_account.Text.Contains('•'))
                    _account.SelectAll();
            };

            var notes = Card("Notes", 140);
            _notes = AddMultiline(notes, "NOTES", 20, 48, 820, 72);
            _notes.PlaceholderText = "Notes about this vendor";

            var stack = new Panel { Dock = DockStyle.Fill, AutoScroll = true };
            notes.Dock = DockStyle.Top;
            banking.Dock = DockStyle.Top;
            identity.Dock = DockStyle.Top;
            stack.Controls.Add(notes);
            stack.Controls.Add(banking);
            stack.Controls.Add(identity);

            Controls.Add(stack);
            Controls.Add(footer);
            Controls.Add(_waitNote);
            Controls.Add(_title);
            FillTypes();
            ResetForm();
        }

        /// <summary>Reload Type names from Admin vendor-type settings.</summary>
        private void FillTypes()
        {
            string selected = ComboText(_type);
            _type.Items.Clear();
            foreach (var name in VendorTypes.Load())
                _type.Items.Add(name);
            SelectCombo(_type, selected);
        }

        /// <summary>Blank the form and assign the next vendor code.</summary>
        public void ResetForm()
        {
            _originalCode = "";
            _storedAccount = "";
            _title.Text = "New Vendor";
            _save.Text = "Add Vendor";
            _another.Visible = true;
            ShowWaitNote("");
            _code.Text = DataFiles.NextVendorCode();
            _name.Text = "";
            _company.Text = "";
            _phone.Text = "";
            _contact.Text = "";
            _terms.Text = "";
            _amount.Text = "";
            _balance.Text = "";
            _routing.Text = "";
            _account.Text = "";
            _notes.Text = "";
            _type.SelectedIndex = -1;
        }

        /// <summary>Fill the editors from a vendors row.</summary>
        private void LoadRecord(Dictionary<string, string> record)
        {
            _originalCode = DataFiles.GetRecord(record, "Code").Trim();
            _title.Text = "Edit Vendor";
            _save.Text = "Save Vendor";
            _another.Visible = false;
            ShowWaitNote(DataFiles.StatusOf(record));
            _code.Text = _originalCode;
            _name.Text = DataFiles.GetRecord(record, "Name");
            _company.Text = DataFiles.GetRecordAny(record, "Company", "Name");
            _phone.Text = DataFiles.GetRecord(record, "Phone");
            _contact.Text = DataFiles.GetRecord(record, "Contact Name");
            _terms.Text = DataFiles.GetRecord(record, "Terms");
            _amount.Text = MoneyFormat.Display(DataFiles.GetRecord(record, "Amount"));
            _balance.Text = MoneyFormat.Display(DataFiles.GetRecord(record, "Current Balance"));
            SelectCombo(_type, DataFiles.GetRecord(record, "Type"));
            _notes.Text = DataFiles.GetRecordAny(record, "Description", "Notes");
            _routing.Text = DataFiles.GetRecord(record, DataFiles.RoutingNumber);
            _storedAccount = DataFiles.DigitsOnly(DataFiles.GetRecord(record, DataFiles.AccountNumber));
            _account.Text = DataFiles.MaskAccountNumber(_storedAccount);
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
            _waitNote.Text = status.Length > 0 ? status + "." : "This vendor is awaiting confirmation.";
        }

        /// <summary>Insert or update the vendor. Type is required.</summary>
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

            string type = ComboText(_type);
            if (type.Length == 0)
            {
                MessageBox.Show("Choose a type.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _type.Focus();
                return false;
            }

            bool exists = DataFiles.ReadAllRecords(DataFiles.Vendors).Any(record =>
                DataFiles.GetRecord(record, "Code").Equals(code, StringComparison.OrdinalIgnoreCase));
            if (exists && !code.Equals(_originalCode, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("That code is already in use.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = code,
                ["Name"] = name,
                ["Company"] = _company.Text.Trim(),
                ["Phone"] = _phone.Text.Trim(),
                ["Contact Name"] = _contact.Text.Trim(),
                ["Terms"] = _terms.Text.Trim(),
                ["Type"] = type,
                ["Amount"] = MoneyFormat.Store(_amount.Text),
                ["Current Balance"] = MoneyFormat.Store(_balance.Text),
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
                        DataFiles.Vendors,
                        record => DataFiles.GetRecord(record, "Code")
                            .Equals(_originalCode, StringComparison.OrdinalIgnoreCase),
                        fields);
                }
                else
                {
                    result = DataFiles.MutateInsert(DataFiles.Vendors, fields);
                }

                if (!result.Ok)
                {
                    ToastAlert.Error(this, result.Message);
                    return false;
                }

                ToastAlert.Success(this, result.Queued
                    ? result.Message
                    : _originalCode.Length > 0 ? "The vendor was saved." : "The vendor was added.");
                ResetForm();
                if (!stayOnForm)
                    Navigator.GoToAndSearch(AppPage.Vendors, name);
                return true;
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return false;
            }
        }

        private static void SelectCombo(ComboBox box, string value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0)
            {
                box.SelectedIndex = -1;
                return;
            }

            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is string item &&
                    item.Equals(value, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            box.Items.Add(value);
            box.SelectedItem = value;
        }

        private static string ComboText(ComboBox box) =>
            (box.SelectedItem as string ?? box.Text ?? "").Trim();

        private static CardPanel Card(string title, int height)
        {
            var card = new CardPanel { Height = height };
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

        private static ComboBox AddCombo(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption, Location = new Point(x, y) };
            Theme.StyleFieldLabel(label);
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(x, y + 16),
                Size = new Size(width, 26)
            };
            Theme.StyleCombo(box);
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
