using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Create or edit a purchase line. Saves to the live purchases table.</summary>
    public partial class AddPurchase : Form, INavigationPage
    {
        private TextBox _po = null!;
        private TextBox _vendor = null!;
        private TextBox _vendorName = null!;
        private TextBox _location = null!;
        private TextBox _vendorTerms = null!;
        private TextBox _item = null!;
        private TextBox _species = null!;
        private TextBox _coo = null!;
        private TextBox _packSize = null!;
        private TextBox _cs = null!;
        private TextBox _volume = null!;
        private TextBox _volumeReceived = null!;
        private TextBox _price = null!;
        private TextBox _overhead = null!;
        private TextBox _freight = null!;
        private TextBox _forwarderLb = null!;
        private TextBox _other = null!;
        private TextBox _totalPerLb = null!;
        private TextBox _totalCost = null!;
        private DateTimePicker _agreement = null!;
        private DateTimePicker _expectedShip = null!;
        private DateTimePicker _vendorDue = null!;
        private DateTimePicker _ship = null!;
        private DateTimePicker _arrival = null!;
        private TextBox _forwarder = null!;
        private TextBox _logistics = null!;
        private ComboBox _status = null!;
        private List<Dictionary<string, string>> _vendorRows = new();
        private List<Dictionary<string, string>> _itemRows = new();
        private bool _loading;
        private bool _calculating;
        private bool _editing;
        private string _editPo = "";
        private string _editItem = "";
        private Label _modeLabel = null!;
        private Button _save = null!;
        private Button _another = null!;
        private LookupSearchPanel _lookup = null!;

        internal static Dictionary<string, string>? PendingEdit { get; set; }
        internal static bool StartNew { get; set; }

        public AddPurchase()
        {
            InitializeComponent();
            BuildUi();
        }

        /// <summary>Open this page as a blank purchase. Assigns the next PO #.</summary>
        public static void OpenNew()
        {
            if (!DataAccess.CanMutate(DataFiles.PurchaseSales))
            {
                MessageBox.Show("This account can only view purchases.", "New Purchase",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = null;
            StartNew = true;
            Navigator.GoTo(AppPage.AddPurchase);
        }

        /// <summary>Open this page with an existing purchase row loaded for edit.</summary>
        public static void OpenEdit(Dictionary<string, string> record)
        {
            if (!DataAccess.CanMutate(DataFiles.PurchaseSales))
            {
                MessageBox.Show("This account can only view purchases.", "Edit Product",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = record;
            StartNew = false;
            Navigator.GoTo(AppPage.AddPurchase);
        }

        /// <summary>
        /// Shown or refreshed: reload vendor/item lists, then apply a pending edit, a new blank, or keep the form.
        /// </summary>
        public void HighlightCurrentPage()
        {
            LoadLookups();
            if (PendingEdit != null)
            {
                var record = PendingEdit;
                PendingEdit = null;
                StartNew = false;
                LoadRecord(record);
                return;
            }

            if (StartNew)
            {
                StartNew = false;
                ResetForm(keepVendor: false);
                return;
            }

            if (!_editing && string.IsNullOrWhiteSpace(_po.Text))
                _po.Text = DataFiles.NextPurchasePo();
        }

        /// <summary>Build the scrollable form: order, product, cost, and date cards plus save actions.</summary>
        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 20);

            var actions = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = Theme.Cream
            };
            _save = new Button
            {
                Text = "Save Purchase",
                Size = new Size(150, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleGoldButton(_save);
            _save.Click += (_, _) => SavePurchase();

            _another = new Button
            {
                Text = "Add Another",
                Size = new Size(130, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleNavyButton(_another);
            _another.Click += (_, _) => SavePurchase(keepVendor: true);

            var clear = new Button
            {
                Text = "Clear",
                Size = new Size(90, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetForm(keepVendor: false);

            actions.Controls.Add(_save);
            actions.Controls.Add(_another);
            actions.Controls.Add(clear);
            actions.Resize += (_, _) =>
            {
                _save.Location = new Point(Math.Max(280, actions.Width - 168), 8);
                _another.Location = new Point(Math.Max(100, actions.Width - 308), 8);
                clear.Location = new Point(Math.Max(8, actions.Width - 408), 8);
            };

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(0, 0, 8, 8)
            };
            Theme.EnableDoubleBuffer(scroll);

            _modeLabel = new Label
            {
                Text = "New Purchase",
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                Dock = DockStyle.Top,
                Height = 40
            };
            var intro = new Label
            {
                Text = "Search for a vendor or item. Matching rows appear in the table — pick one to fill the form.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 28
            };

            var order = BuildOrderCard();
            var product = BuildProductCard();
            var cost = BuildCostCard();
            var dates = BuildDatesCard();
            _lookup = BuildLookupCard();

            scroll.Controls.Add(dates);
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(cost);
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(product);
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(order);

            Controls.Add(scroll);
            Controls.Add(actions);
            Controls.Add(_lookup);
            Controls.Add(intro);
            Controls.Add(_modeLabel);

            LoadLookups();
            ResetForm(keepVendor: false);
        }

        private CardPanel BuildOrderCard()
        {
            var card = MakeCard("Order", 168);
            _po = AddText(card, "PO #", 20, 48, 160);
            _vendor = AddText(card, "VENDOR CODE", 200, 48, 180);
            _vendorName = AddText(card, "VENDOR", 400, 48, 260);
            _location = AddText(card, "LOCATION", 20, 100, 180);
            _vendorTerms = AddText(card, "VENDOR TERMS", 220, 100, 200);
            WireApply(_vendor, ApplyVendorFromCode);
            WireApply(_vendorName, ApplyVendorFromName);
            WireApply(_vendorTerms, ApplyVendorFromName);
            return card;
        }

        private CardPanel BuildProductCard()
        {
            var card = MakeCard("Product", 168);
            _item = AddText(card, "ITEM CODE", 20, 48, 180);
            _species = AddText(card, "SPECIES", 220, 48, 440);
            _coo = AddText(card, "COO", 20, 100, 120);
            _packSize = AddText(card, "PACK SIZE", 160, 100, 120);
            WireApply(_item, ApplyItemFromCode);
            WireApply(_species, ApplyItemFromSpecies);
            WireApply(_coo, ApplyItemFromSpecies);
            _packSize.TextChanged += (_, _) => RecalcVolume();
            return card;
        }

        private CardPanel BuildCostCard()
        {
            var card = MakeCard("Quantity && cost", 220);
            _cs = AddText(card, "CS", 20, 48, 90);
            _volume = AddText(card, "VOLUME", 130, 48, 120);
            _volumeReceived = AddText(card, "VOLUME RECEIVED", 270, 48, 140);
            _price = AddText(card, "PRICE PAID / LB", 430, 48, 130);
            _overhead = AddText(card, "OVERHEAD / LB", 20, 100, 120);
            _freight = AddText(card, "FREIGHT / LB", 160, 100, 120);
            _forwarderLb = AddText(card, "FORWARDER / LB", 300, 100, 130);
            _other = AddText(card, "OTHER / LB", 450, 100, 110);
            _totalPerLb = AddText(card, "TOTAL COST / LB", 20, 152, 150);
            _totalCost = AddText(card, "TOTAL COST", 190, 152, 160);
            _totalPerLb.ReadOnly = true;
            _totalCost.ReadOnly = true;
            _totalPerLb.BackColor = Theme.GridAlt;
            _totalCost.BackColor = Theme.GridAlt;

            _cs.TextChanged += (_, _) => RecalcVolume();
            foreach (var box in new[] { _volume, _volumeReceived, _price, _overhead, _freight, _forwarderLb, _other })
                box.TextChanged += (_, _) => RecalcCost();

            return card;
        }

        private CardPanel BuildDatesCard()
        {
            var card = MakeCard("Dates && shipping", 168);
            _agreement = AddDate(card, "AGREEMENT DATE", 20, 48, 150);
            _expectedShip = AddDate(card, "EXPECTED SHIP DATE", 190, 48, 160);
            _vendorDue = AddDate(card, "VENDOR DUE DATE", 370, 48, 150);
            _status = AddCombo(card, "STATUS", 540, 48, 150);
            _status.DropDownStyle = ComboBoxStyle.DropDownList;
            SelectStatus(_status, "Pending");
            _ship = AddDate(card, "SHIP DATE", 20, 100, 150);
            _arrival = AddDate(card, "ARRIVAL DATE", 190, 100, 160);
            _forwarder = AddText(card, "FORWARDER", 370, 100, 150);
            _logistics = AddText(card, "LOGISTICS", 540, 100, 150);
            return card;
        }

        /// <summary>Reload vendor and item-code lists used by the search table.</summary>
        private void LoadLookups()
        {
            _vendorRows = DataFiles.VisibleRecords(DataFiles.Vendors);
            _itemRows = DataFiles.VisibleRecords(DataFiles.ItemCodes);
            _lookup?.SetSources(
                new LookupSource
                {
                    Kind = "Vendor",
                    Rows = _vendorRows,
                    CodeColumn = "Code",
                    NameColumns = new[] { "Name", "Company" },
                    ExtraColumn = "Terms"
                },
                new LookupSource
                {
                    Kind = "Item",
                    Rows = _itemRows,
                    CodeColumn = "Code",
                    NameColumns = new[] { "Species", "Description" },
                    ExtraColumn = "COO"
                });
        }

        private LookupSearchPanel BuildLookupCard()
        {
            var panel = new LookupSearchPanel("Search vendors and items by code, name, species, terms, or any other field");
            panel.Picked += pick =>
            {
                if (pick.Kind.Equals("Vendor", StringComparison.OrdinalIgnoreCase))
                    PickVendor(pick.Code);
                else if (pick.Kind.Equals("Item", StringComparison.OrdinalIgnoreCase))
                    PickItem(pick.Code);
            };
            return panel;
        }

        /// <summary>When a vendor code is entered, fill vendor name and terms from that lookup.</summary>
        private void ApplyVendorFromCode()
        {
            if (_loading)
                return;

            string code = _vendor.Text.Trim();
            if (code.Length == 0)
                return;

            var record = FindByCode(_vendorRows, code);
            if (record == null)
                return;

            string name = DataFiles.GetRecord(record, "Name").Trim();
            if (name.Length == 0)
                name = DataFiles.GetRecord(record, "Company").Trim();
            string terms = DataFiles.GetRecord(record, "Terms");
            _loading = true;
            if (name.Length > 0)
                _vendorName.Text = name;
            if (terms.Length > 0)
                _vendorTerms.Text = terms;
            _loading = false;
        }

        /// <summary>When a vendor name or company is entered, fill the matching vendor code and terms.</summary>
        private void ApplyVendorFromName()
        {
            if (_loading || _vendor.Text.Trim().Length > 0)
                return;

            string needle = _vendorName.Text.Trim();
            if (needle.Length == 0)
                return;

            string? code = null;
            foreach (var record in _vendorRows)
            {
                string name = DataFiles.GetRecord(record, "Name").Trim();
                string company = DataFiles.GetRecord(record, "Company").Trim();
                if (!name.Equals(needle, StringComparison.OrdinalIgnoreCase) &&
                    !company.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    continue;
                string next = DataFiles.GetRecord(record, "Code").Trim();
                if (next.Length == 0)
                    continue;
                if (code != null && !code.Equals(next, StringComparison.OrdinalIgnoreCase))
                    return;
                code = next;
            }

            if (code != null)
                PickVendor(code);
        }

        /// <summary>When an item code is entered, fill species and country of origin.</summary>
        private void ApplyItemFromCode()
        {
            if (_loading)
                return;

            string code = _item.Text.Trim();
            if (code.Length == 0)
                return;

            var record = FindByCode(_itemRows, code);
            if (record == null)
                return;

            string species = DataFiles.GetRecord(record, "Species").Trim();
            if (species.Length == 0)
                species = DataFiles.GetRecord(record, "Description").Trim();
            string coo = DataFiles.GetRecord(record, "COO");
            _loading = true;
            if (species.Length > 0)
                _species.Text = species;
            if (coo.Length > 0)
                _coo.Text = coo;
            _loading = false;
        }

        /// <summary>When a species is entered, fill the matching item code and COO if it is unique.</summary>
        private void ApplyItemFromSpecies()
        {
            if (_loading || _item.Text.Trim().Length > 0)
                return;

            string needle = _species.Text.Trim();
            if (needle.Length == 0)
                return;

            string? code = null;
            foreach (var record in _itemRows)
            {
                string species = DataFiles.GetRecord(record, "Species").Trim();
                string description = DataFiles.GetRecord(record, "Description").Trim();
                if (!species.Equals(needle, StringComparison.OrdinalIgnoreCase) &&
                    !description.Equals(needle, StringComparison.OrdinalIgnoreCase))
                    continue;
                string next = DataFiles.GetRecord(record, "Code").Trim();
                if (next.Length == 0)
                    continue;
                if (code != null && !code.Equals(next, StringComparison.OrdinalIgnoreCase))
                    return;
                code = next;
            }

            if (code != null)
                PickItem(code);
        }

        /// <summary>Volume = pack size × cases. Copies that into Volume Received if it is still empty.</summary>
        private void RecalcVolume()
        {
            if (_calculating)
                return;

            decimal pack = ParseNumber(_packSize.Text);
            decimal cs = ParseNumber(_cs.Text);
            if (pack <= 0 || cs <= 0)
            {
                RecalcCost();
                return;
            }

            _calculating = true;
            string volume = (pack * cs).ToString("0.##", CultureInfo.InvariantCulture);
            _volume.Text = volume;
            if (string.IsNullOrWhiteSpace(_volumeReceived.Text))
                _volumeReceived.Text = volume;
            _calculating = false;
            RecalcCost();
        }

        /// <summary>Total cost / lb is the sum of the per-lb fields. Total cost is that times pounds received (or ordered).</summary>
        private void RecalcCost()
        {
            if (_calculating)
                return;

            _calculating = true;
            decimal perLb = ParseNumber(_price.Text)
                + ParseNumber(_overhead.Text)
                + ParseNumber(_freight.Text)
                + ParseNumber(_forwarderLb.Text)
                + ParseNumber(_other.Text);
            decimal lbs = ParseNumber(_volumeReceived.Text);
            if (lbs <= 0)
                lbs = ParseNumber(_volume.Text);

            _totalPerLb.Text = perLb.ToString("0.####", CultureInfo.InvariantCulture);
            _totalCost.Text = (perLb * lbs).ToString("0.00", CultureInfo.InvariantCulture);
            _calculating = false;
        }

        /// <summary>
        /// Write this line to purchases. Edit replaces the original PO + item; add inserts a new row.
        /// <paramref name="keepVendor"/> true (Add Another) leaves vendor fields filled.
        /// </summary>
        private void SavePurchase(bool keepVendor = false)
        {
            if (!AppLock.HasFolder())
            {
                ToastAlert.Error(this, "Select a data folder in Settings first.");
                return;
            }

            string po = _po.Text.Trim();
            if (po.Length == 0)
            {
                ToastAlert.Error(this, "Enter a PO #.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_vendor.Text) &&
                string.IsNullOrWhiteSpace(_item.Text))
            {
                ToastAlert.Error(this, "Pick a vendor or item code.");
                return;
            }

            RecalcCost();
            var fields = new[]
            {
                po,
                _vendor.Text.Trim(),
                _vendorName.Text.Trim(),
                _location.Text.Trim(),
                _item.Text.Trim(),
                _species.Text.Trim(),
                _coo.Text.Trim(),
                _packSize.Text.Trim(),
                _cs.Text.Trim(),
                _volume.Text.Trim(),
                _volumeReceived.Text.Trim(),
                _price.Text.Trim(),
                _overhead.Text.Trim(),
                _freight.Text.Trim(),
                _forwarderLb.Text.Trim(),
                _other.Text.Trim(),
                _totalPerLb.Text.Trim(),
                _totalCost.Text.Trim(),
                DateText(_agreement),
                DateText(_expectedShip),
                _vendorTerms.Text.Trim(),
                DateText(_vendorDue),
                DateText(_ship),
                DateText(_arrival),
                _forwarder.Text.Trim(),
                _logistics.Text.Trim(),
                _status.Text.Trim()
            };

            try
            {
                var values = DataFiles.RowFromFields(DataFiles.PurchaseSales, fields);
                MutateResult result;
                if (_editing)
                {
                    result = DataFiles.MutateUpdate(
                        DataFiles.PurchaseSales,
                        record =>
                            DataFiles.NormalizePo(DataFiles.GetRecord(record, "PO #")) ==
                            DataFiles.NormalizePo(_editPo) &&
                            DataFiles.GetRecord(record, "Item Code").Trim()
                                .Equals(_editItem, StringComparison.OrdinalIgnoreCase),
                        values);
                }
                else
                {
                    result = DataFiles.MutateInsert(DataFiles.PurchaseSales, values);
                }

                if (!result.Ok)
                {
                    ToastAlert.Error(this, result.Message);
                    return;
                }

                ToastAlert.Success(this, result.Queued
                    ? result.Message
                    : _editing ? "The product was updated." : "The product was added.");
                if (_editing && !result.Queued)
                {
                    _editPo = po;
                    _editItem = _item.Text.Trim();
                    return;
                }

                if (!_editing)
                    ResetForm(keepVendor);
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        /// <summary>Switch the heading and save button between New Purchase and Edit Product.</summary>
        private void SetMode(bool editing)
        {
            _editing = editing;
            _modeLabel.Text = editing ? "Edit Product" : "New Purchase";
            _save.Text = editing ? "Save Changes" : "Save Purchase";
            _another.Visible = !editing;
        }

        /// <summary>Copy an existing purchase row into the form and switch to edit mode.</summary>
        private void LoadRecord(Dictionary<string, string> record)
        {
            _loading = true;
            _editPo = DataFiles.GetRecord(record, "PO #");
            _editItem = DataFiles.GetRecord(record, "Item Code");
            _po.Text = _editPo;
            _vendor.Text = DataFiles.GetRecord(record, "Vendor Code");
            _vendorName.Text = DataFiles.GetRecord(record, "Vendor");
            _location.Text = DataFiles.GetRecord(record, "Location");
            _vendorTerms.Text = DataFiles.GetRecord(record, "Vendor Terms");
            _item.Text = _editItem;
            _species.Text = DataFiles.GetRecord(record, "Description");
            _coo.Text = DataFiles.GetRecord(record, "COO");
            _packSize.Text = DataFiles.GetRecord(record, "Pack Size");
            _cs.Text = DataFiles.GetRecord(record, "CS");
            _volume.Text = DataFiles.GetRecord(record, "Volume");
            _volumeReceived.Text = DataFiles.GetRecord(record, "Volume Received");
            _price.Text = DataFiles.GetRecord(record, "Price Paid / LB");
            _overhead.Text = DataFiles.GetRecord(record, "Overhead / LB");
            _freight.Text = DataFiles.GetRecord(record, "Freight / LB");
            _forwarderLb.Text = DataFiles.GetRecord(record, "Forwarder / LB");
            _other.Text = DataFiles.GetRecord(record, "Other / LB");
            _totalPerLb.Text = DataFiles.GetRecord(record, "Total Cost / LB");
            _totalCost.Text = DataFiles.GetRecord(record, "Total Cost");
            SetDate(_agreement, DataFiles.GetRecord(record, "Agreement Date"));
            SetDate(_expectedShip, DataFiles.GetRecord(record, "Expected Ship Date"));
            SetDate(_vendorDue, DataFiles.GetRecord(record, "Vendor Due Date"));
            SetDate(_ship, DataFiles.GetRecord(record, "Ship Date"));
            SetDate(_arrival, DataFiles.GetRecord(record, "Arrival Date"));
            _forwarder.Text = DataFiles.GetRecord(record, "Forwarder");
            _logistics.Text = DataFiles.GetRecord(record, "Logistics");
            SelectStatus(_status, DataFiles.GetRecord(record, "Status"));
            _loading = false;
            SetMode(true);
            RecalcCost();
        }

        /// <summary>Clear the form for another line. Optionally keep the vendor. Assigns the next PO #.</summary>
        private void ResetForm(bool keepVendor)
        {
            string vendorCode = keepVendor ? _vendor.Text.Trim() : "";
            string vendorName = keepVendor ? _vendorName.Text : "";
            string terms = keepVendor ? _vendorTerms.Text : "";
            string location = keepVendor ? _location.Text : "";

            _loading = true;
            _po.Text = DataFiles.NextPurchasePo();
            _item.Text = "";
            _species.Text = "";
            _coo.Text = "";
            _packSize.Text = "";
            _cs.Text = "";
            _volume.Text = "";
            _volumeReceived.Text = "";
            _price.Text = "";
            _overhead.Text = "";
            _freight.Text = "";
            _forwarderLb.Text = "";
            _other.Text = "";
            _totalPerLb.Text = "";
            _totalCost.Text = "";
            _agreement.Value = DateTime.Today;
            _agreement.Checked = true;
            Uncheck(_expectedShip);
            Uncheck(_vendorDue);
            Uncheck(_ship);
            Uncheck(_arrival);
            _forwarder.Text = "";
            _logistics.Text = "";
            SelectStatus(_status, "Pending");

            if (keepVendor)
            {
                _vendor.Text = vendorCode;
                _vendorName.Text = vendorName;
                _vendorTerms.Text = terms;
                _location.Text = location;
            }
            else
            {
                _vendor.Text = "";
                _vendorName.Text = "";
                _vendorTerms.Text = "";
                _location.Text = "";
            }

            _loading = false;
            SetMode(false);
            _po.Focus();
        }

        private static CardPanel MakeCard(string title, int height)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = height,
                Padding = new Padding(12, 10, 12, 10)
            };
            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 10)
            };
            card.Controls.Add(heading);
            return card;
        }

        private static Panel Spacer() => new()
        {
            Dock = DockStyle.Top,
            Height = 12,
            BackColor = Theme.Cream
        };

        private static TextBox AddText(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var box = new TextBox();
            Theme.StyleField(box);
            box.Location = new Point(x, y + 16);
            box.Size = new Size(width, 26);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }

        private static ComboBox AddCombo(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDown,
                Location = new Point(x, y + 16),
                Size = new Size(width, 26)
            };
            Theme.StyleCombo(box);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }

        private static DateTimePicker AddDate(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var box = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                ShowCheckBox = true,
                Checked = false,
                Location = new Point(x, y + 16),
                Size = new Size(width, 26),
                Font = Theme.Body
            };
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }

        private static string DateText(DateTimePicker picker) =>
            picker.Checked ? CsvIO.Date(picker.Value.Date) : "";

        private static void SetDate(DateTimePicker picker, string text)
        {
            if (DateTime.TryParse(text, out var date))
            {
                picker.Value = date;
                picker.Checked = true;
            }
            else
            {
                picker.Checked = false;
            }
        }

        private static void Uncheck(DateTimePicker picker)
        {
            picker.Checked = false;
        }

        private static void SelectStatus(ComboBox box, string? value)
        {
            if (box.Items.Count == 0)
                box.Items.AddRange(new object[] { "Pending", "Sent", "Confirmed", "Complete" });

            string pick = (value ?? "").Trim();
            if (pick.Equals("Open", StringComparison.OrdinalIgnoreCase))
                pick = "Pending";
            if (pick.Equals("Paid", StringComparison.OrdinalIgnoreCase))
                pick = "Complete";
            box.SelectedItem = pick.Length > 0 ? pick : "Pending";
            if (box.SelectedIndex < 0)
                box.SelectedItem = "Pending";
        }

        private void PickVendor(string code)
        {
            _vendor.Text = code;
            ApplyVendorFromCode();
        }

        private void PickItem(string code)
        {
            _item.Text = code;
            ApplyItemFromCode();
        }

        /// <summary>Enter or leaving a code/name box fills the linked fields when there is a unique match.</summary>
        private void WireApply(TextBox box, Action apply)
        {
            box.Leave += (_, _) => apply();
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                apply();
                e.SuppressKeyPress = true;
            };
        }

        private static Dictionary<string, string>? FindByCode(
            List<Dictionary<string, string>> rows,
            string code)
        {
            foreach (var record in rows)
            {
                if (DataFiles.GetRecord(record, "Code").Trim()
                    .Equals(code, StringComparison.OrdinalIgnoreCase))
                    return record;
            }

            return null;
        }

        private static decimal ParseNumber(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return 0;
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.CurrentCulture, out var value))
                return value;
            if (decimal.TryParse(text, NumberStyles.Any, CultureInfo.InvariantCulture, out value))
                return value;
            return 0;
        }
    }
}
