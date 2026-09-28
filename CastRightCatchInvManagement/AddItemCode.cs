namespace CastRightCatchInvManagement
{
    /// <summary>New/edit item-code page. Pack size here auto-fills purchase and sales lines.</summary>
    public partial class AddItemCode : Form, INavigationPage
    {
        private TextBox _code = null!;
        private TextBox _description = null!;
        private TextBox _species = null!;
        private TextBox _scientific = null!;
        private TextBox _coo = null!;
        private ComboBox _farmed = null!;
        private ComboBox _fresh = null!;
        private TextBox _proc = null!;
        private TextBox _pack = null!;
        private TextBox _shelfLife = null!;
        private Label _shelfLabel = null!;
        private TextBox _minProfit = null!;
        private string _loadedMinProfit = "";
        private Button _save = null!;
        private Label _title = null!;
        private string _originalCode = "";

        /// <summary>True when OpenNew should wipe the last draft on the next show.</summary>
        internal static bool StartNew { get; set; }
        /// <summary>Item row queued by OpenEdit until this page is shown.</summary>
        internal static Dictionary<string, string>? PendingEdit { get; set; }

        /// <summary>Build the New Item page on a blank record.</summary>
        public AddItemCode()
        {
            InitializeComponent();
            BuildUi();
        }

        /// <summary>Open this page as a blank item.</summary>
        public static void OpenNew()
        {
            if (!DataAccess.CanMutate(DataFiles.ItemCodes))
            {
                MessageBox.Show("This account can only view inventory.", "New Item",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = null;
            StartNew = true;
            Navigator.GoTo(AppPage.AddItemCode);
        }

        /// <summary>Open this page with the selected catalog row loaded for edit.</summary>
        public static void OpenEdit(Dictionary<string, string> record)
        {
            if (!DataAccess.CanMutate(DataFiles.ItemCodes))
            {
                MessageBox.Show("This account can only view inventory.", "Edit Item",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = record;
            StartNew = false;
            Navigator.GoTo(AppPage.AddItemCode);
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

        /// <summary>Lay out catalog fields and save actions.</summary>
        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 20, 28, 24);

            _title = new Label
            {
                Text = "New Item",
                Dock = DockStyle.Top,
                Height = 40
            };
            Theme.StylePageTitle(_title);

            var footer = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Theme.Cream
            };
            _save = new Button { Text = "Add Item", Size = new Size(130, 34), Location = new Point(0, 8) };
            Theme.StyleGoldButton(_save);
            _save.Click += (_, _) => SaveRecord(stayOnForm: false);
            var another = new Button { Text = "Save and Next", Size = new Size(140, 34), Location = new Point(146, 8) };
            Theme.StyleNavyButton(another);
            another.Click += (_, _) =>
            {
                if (SaveRecord(stayOnForm: true))
                    _code.Focus();
            };
            var clear = new Button { Text = "Clear", Size = new Size(90, 34), Location = new Point(292, 8) };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetForm();
            footer.Controls.Add(_save);
            footer.Controls.Add(another);
            footer.Controls.Add(clear);

            var card = new CardPanel { Dock = DockStyle.Top, Height = 340 };
            var heading = new Label
            {
                Text = "Item",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 12)
            };
            card.Controls.Add(heading);
            _code = AddField(card, "CODE", 20, 48, 160);
            _description = AddField(card, "DESCRIPTION", 200, 48, 360);
            _species = AddField(card, "SPECIES", 580, 48, 220);
            _coo = AddField(card, "COO", 20, 108, 160);
            _proc = AddField(card, "PROC COUNTRY", 200, 108, 180);
            _pack = AddField(card, "PACK SIZE", 400, 108, 140);
            _farmed = AddCombo(card, "FARMED / WILD", 560, 108, 140, "Farmed", "Wild");
            _fresh = AddCombo(card, "FRESH / FROZEN", 720, 108, 140, "Fresh", "Frozen");
            _scientific = AddField(card, "SCIENTIFIC NAME", 20, 168, 400);
            _shelfLabel = new Label { Text = "SHELF LIFE (MONTHS)", Location = new Point(440, 168) };
            Theme.StyleFieldLabel(_shelfLabel);
            _shelfLife = new TextBox
            {
                Location = new Point(440, 184),
                Size = new Size(140, 26),
                PlaceholderText = "12"
            };
            Theme.StyleField(_shelfLife);
            card.Controls.Add(_shelfLabel);
            card.Controls.Add(_shelfLife);
            _minProfit = AddField(card, "MINIMUM PROFIT / LB  ·  ADMIN", 20, 228, 200);
            _minProfit.PlaceholderText = "0.00";
            MoneyFormat.BindInput(_minProfit);
            _minProfit.ReadOnly = !AppState.IsAdmin;
            if (_minProfit.ReadOnly)
                _minProfit.BackColor = Theme.Cream;
            _fresh.SelectedIndexChanged += (_, _) => SyncFrozenFields();
            _code.PlaceholderText = "Item code";
            _description.PlaceholderText = "Product name";
            _species.PlaceholderText = "Species";
            _coo.PlaceholderText = "Country of origin";
            _proc.PlaceholderText = "Processed in";
            _pack.PlaceholderText = "e.g. 10";
            _scientific.PlaceholderText = "Scientific name";

            Controls.Add(card);
            Controls.Add(footer);
            Controls.Add(_title);
            ResetForm();
        }

        /// <summary>Blank the form for a new catalog row.</summary>
        public void ResetForm()
        {
            DataFiles.EnsureItemColumns();
            _originalCode = "";
            _title.Text = "New Item";
            _save.Text = "Add Item";
            _code.Text = "";
            _description.Text = "";
            _species.Text = "";
            _scientific.Text = "";
            _coo.Text = "";
            _proc.Text = "";
            _pack.Text = "";
            _farmed.SelectedIndex = -1;
            _fresh.SelectedIndex = -1;
            _shelfLife.Text = "";
            _loadedMinProfit = "";
            _minProfit.Text = DataFiles.DefaultMinimumProfitText();
            SyncFrozenFields();
        }

        /// <summary>Fill the editors from an inventory row.</summary>
        private void LoadRecord(Dictionary<string, string> record)
        {
            DataFiles.EnsureItemColumns();
            _originalCode = DataFiles.GetRecord(record, "Code").Trim();
            _title.Text = "Edit Item";
            _save.Text = "Save Item";
            _code.Text = _originalCode;
            _description.Text = DataFiles.GetRecord(record, "Description");
            _species.Text = DataFiles.GetRecord(record, "Species");
            _scientific.Text = DataFiles.GetRecord(record, "Scientific Name");
            _coo.Text = DataFiles.GetRecord(record, "COO");
            _proc.Text = DataFiles.GetRecord(record, "Proc Country");
            _pack.Text = DataFiles.GetRecord(record, "Pack Size");
            SelectCombo(_farmed, DataFiles.GetRecord(record, "Farmed / Wild"));
            SelectCombo(_fresh, DataFiles.GetRecord(record, "Fresh / Frozen"));
            _shelfLife.Text = DataFiles.GetRecord(record, DataFiles.ShelfLifeMonthsColumn);
            _loadedMinProfit = DataFiles.GetRecord(record, DataFiles.MinimumProfitColumn);
            _minProfit.Text = _loadedMinProfit.Trim().Length > 0
                ? MoneyFormat.Display(_loadedMinProfit)
                : DataFiles.DefaultMinimumProfitText();
            SyncFrozenFields();
        }

        /// <summary>Insert or update the item. Returns false when validation or the write fails.</summary>
        private bool SaveRecord(bool stayOnForm)
        {
            if (!AppLock.HasFolder())
            {
                MessageBox.Show("Select a data folder in Settings first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string code = _code.Text.Trim();
            if (code.Length == 0)
            {
                MessageBox.Show("Enter an item code.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                _code.Focus();
                return false;
            }

            bool exists = DataFiles.ReadAllRecords(DataFiles.ItemCodes).Any(record =>
                DataFiles.GetRecord(record, "Code").Equals(code, StringComparison.OrdinalIgnoreCase));
            if (exists && !code.Equals(_originalCode, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("That code is already in use.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = code,
                ["Description"] = _description.Text.Trim(),
                ["Species"] = _species.Text.Trim(),
                ["Scientific Name"] = _scientific.Text.Trim(),
                ["COO"] = _coo.Text.Trim(),
                ["Proc Country"] = _proc.Text.Trim(),
                ["Pack Size"] = _pack.Text.Trim(),
                ["Farmed / Wild"] = ComboText(_farmed),
                ["Fresh / Frozen"] = ComboText(_fresh),
                [DataFiles.ShelfLifeMonthsColumn] = FrozenSelected() ? _shelfLife.Text.Trim() : "",
                [DataFiles.MinimumProfitColumn] = StoredMinimumProfit()
            };

            try
            {
                MutateResult result;
                if (_originalCode.Length > 0)
                {
                    result = DataFiles.MutateUpdate(
                        DataFiles.ItemCodes,
                        record => DataFiles.GetRecord(record, "Code")
                            .Equals(_originalCode, StringComparison.OrdinalIgnoreCase),
                        fields);
                }
                else
                {
                    result = DataFiles.MutateInsert(DataFiles.ItemCodes, fields);
                }

                if (!result.Ok)
                {
                    ToastAlert.Error(this, result.Message);
                    return false;
                }

                ToastAlert.Success(this, result.Queued
                    ? result.Message
                    : _originalCode.Length > 0 ? "The item was saved." : "The item was added.");
                ResetForm();
                if (!stayOnForm)
                    Navigator.GoToAndSearch(AppPage.ItemCodes, code);
                return true;
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return false;
            }
        }

        /// <summary>Admin-typed minimum, or the stored/default amount when the field is blank.</summary>
        private string StoredMinimumProfit()
        {
            string text = AppState.IsAdmin ? _minProfit.Text : _loadedMinProfit;
            if (MoneyFormat.TryParse(text, out _) && !string.IsNullOrWhiteSpace(text))
                return MoneyFormat.Store(text);
            return MoneyFormat.Store(DataFiles.DefaultMinimumProfitText());
        }

        /// <summary>Shelf life applies to frozen items only.</summary>
        private void SyncFrozenFields()
        {
            bool frozen = FrozenSelected();
            _shelfLabel.Visible = frozen;
            _shelfLife.Visible = frozen;
            if (frozen && string.IsNullOrWhiteSpace(_shelfLife.Text))
                _shelfLife.Text = "12";
        }

        private bool FrozenSelected() =>
            ComboText(_fresh).Equals("Frozen", StringComparison.OrdinalIgnoreCase);

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

        private static ComboBox AddCombo(Control parent, string caption, int x, int y, int width, params string[] items)
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
            foreach (string item in items)
                box.Items.Add(item);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }
    }
}
