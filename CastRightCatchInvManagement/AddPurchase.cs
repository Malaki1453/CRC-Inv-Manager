using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Create or edit a purchase line. Saves to the live purchases table.</summary>
    public partial class AddPurchase : Form, INavigationPage
    {
        private TextBox _po = null!;
        private ComboBox _vendor = null!;
        private TextBox _vendorName = null!;
        private TextBox _vendorInvoice = null!;
        private TextBox _location = null!;
        private TextBox _vendorTerms = null!;
        private ComboBox _item = null!;
        private TextBox _description = null!;
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
        private bool _loading;
        private bool _calculating;
        private bool _editing;
        private string _editPo = "";
        private string _editItem = "";
        private Label _modeLabel = null!;
        private Button _save = null!;
        private Button _another = null!;

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
            PendingEdit = null;
            StartNew = true;
            Navigator.GoTo(AppPage.AddPurchase);
        }

        /// <summary>Open this page with an existing purchase row loaded for edit.</summary>
        public static void OpenEdit(Dictionary<string, string> record)
        {
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
                Text = "Add Product",
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
                Text = "Add Product",
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                Dock = DockStyle.Top,
                Height = 40
            };
            var intro = new Label
            {
                Text = "Pick a vendor and item code to fill the details. Totals update as you type.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 28
            };

            var order = BuildOrderCard();
            var product = BuildProductCard();
            var cost = BuildCostCard();
            var dates = BuildDatesCard();

            scroll.Controls.Add(dates);
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(cost);
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(product);
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(order);

            Controls.Add(scroll);
            Controls.Add(actions);
            Controls.Add(intro);
            Controls.Add(_modeLabel);

            LoadLookups();
            ResetForm(keepVendor: false);
        }

        private CardPanel BuildOrderCard()
        {
            var card = MakeCard("Order", 168);
            _po = AddText(card, "PO #", 20, 48, 160);
            _vendor = AddCombo(card, "VENDOR CODE", 200, 48, 180);
            _vendorName = AddText(card, "VENDOR", 400, 48, 260);
            _vendorInvoice = AddText(card, "VENDOR INVOICE #", 20, 100, 180);
            _location = AddText(card, "LOCATION", 220, 100, 140);
            _vendorTerms = AddText(card, "VENDOR TERMS", 380, 100, 160);
            _vendor.SelectedIndexChanged += (_, _) => ApplyVendor();
            return card;
        }

        private CardPanel BuildProductCard()
        {
            var card = MakeCard("Product", 168);
            _item = AddCombo(card, "ITEM CODE", 20, 48, 180);
            _description = AddText(card, "DESCRIPTION", 220, 48, 440);
            _coo = AddText(card, "COO", 20, 100, 120);
            _packSize = AddText(card, "PACK SIZE", 160, 100, 120);
            _item.SelectedIndexChanged += (_, _) => ApplyItem();
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

        /// <summary>Refill vendor and item-code dropdowns from the live lookup tables, keeping the current picks.</summary>
        private void LoadLookups()
        {
            _loading = true;
            string vendor = CurrentCode(_vendor);
            string item = CurrentCode(_item);

            _vendor.Items.Clear();
            foreach (var record in DataFiles.ReadRecords(DataFiles.Vendors))
            {
                _vendor.Items.Add(new CodeChoice(
                    DataFiles.GetRecord(record, "Code"),
                    DataFiles.GetRecord(record, "Name"),
                    DataFiles.GetRecord(record, "Terms")));
            }

            _item.Items.Clear();
            foreach (var record in DataFiles.ReadRecords(DataFiles.ItemCodes))
            {
                _item.Items.Add(new CodeChoice(
                    DataFiles.GetRecord(record, "Code"),
                    DataFiles.GetRecord(record, "Description"),
                    DataFiles.GetRecord(record, "COO")));
            }

            SelectCode(_vendor, vendor);
            SelectCode(_item, item);
            _loading = false;
        }

        /// <summary>When a vendor code is chosen, fill vendor name and terms from that lookup.</summary>
        private void ApplyVendor()
        {
            if (_loading || _vendor.SelectedItem is not CodeChoice choice)
                return;

            _vendorName.Text = choice.Name;
            if (!string.IsNullOrWhiteSpace(choice.Extra))
                _vendorTerms.Text = choice.Extra;
        }

        /// <summary>When an item code is chosen, fill description and country of origin.</summary>
        private void ApplyItem()
        {
            if (_loading || _item.SelectedItem is not CodeChoice choice)
                return;

            if (!string.IsNullOrWhiteSpace(choice.Name))
                _description.Text = choice.Name;
            if (!string.IsNullOrWhiteSpace(choice.Extra))
                _coo.Text = choice.Extra;
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

            if (string.IsNullOrWhiteSpace(CurrentCode(_vendor)) &&
                string.IsNullOrWhiteSpace(CurrentCode(_item)))
            {
                ToastAlert.Error(this, "Pick a vendor or item code.");
                return;
            }

            RecalcCost();
            var fields = new[]
            {
                po,
                _vendorInvoice.Text.Trim(),
                CurrentCode(_vendor),
                _vendorName.Text.Trim(),
                _location.Text.Trim(),
                CurrentCode(_item),
                _description.Text.Trim(),
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
                if (_editing)
                {
                    bool updated = DataFiles.ReplaceMatchingRow(
                        DataFiles.PurchaseSales,
                        record =>
                            DataFiles.NormalizePo(DataFiles.GetRecord(record, "PO #")) ==
                            DataFiles.NormalizePo(_editPo) &&
                            DataFiles.GetRecord(record, "Item Code").Trim()
                                .Equals(_editItem, StringComparison.OrdinalIgnoreCase),
                        fields);
                    if (!updated)
                    {
                        ToastAlert.Error(this, "Could not find that product line to update.");
                        return;
                    }

                    _editPo = po;
                    _editItem = CurrentCode(_item);
                    ToastAlert.Success(this, "The product was updated.");
                    return;
                }

                DataFiles.AppendRow(DataFiles.PurchaseSales, fields);
                ToastAlert.Success(this, "The product was added.");
                ResetForm(keepVendor);
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        /// <summary>Switch the heading and save button between Add Product and Edit Product.</summary>
        private void SetMode(bool editing)
        {
            _editing = editing;
            _modeLabel.Text = editing ? "Edit Product" : "Add Product";
            _save.Text = editing ? "Edit Product" : "Add Product";
            _another.Visible = !editing;
        }

        /// <summary>Copy an existing purchase row into the form and switch to edit mode.</summary>
        private void LoadRecord(Dictionary<string, string> record)
        {
            _loading = true;
            _editPo = DataFiles.GetRecord(record, "PO #");
            _editItem = DataFiles.GetRecord(record, "Item Code");
            _po.Text = _editPo;
            SelectCode(_vendor, DataFiles.GetRecord(record, "Vendor Code"));
            _vendorName.Text = DataFiles.GetRecord(record, "Vendor");
            _vendorInvoice.Text = DataFiles.GetRecord(record, "Vendor Invoice #");
            _location.Text = DataFiles.GetRecord(record, "Location");
            _vendorTerms.Text = DataFiles.GetRecord(record, "Vendor Terms");
            SelectCode(_item, _editItem);
            _description.Text = DataFiles.GetRecord(record, "Description");
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
            string vendorCode = keepVendor ? CurrentCode(_vendor) : "";
            string vendorName = keepVendor ? _vendorName.Text : "";
            string terms = keepVendor ? _vendorTerms.Text : "";
            string location = keepVendor ? _location.Text : "";

            _loading = true;
            _po.Text = DataFiles.NextPurchasePo();
            _vendorInvoice.Text = "";
            _item.SelectedIndex = -1;
            _description.Text = "";
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
                SelectCode(_vendor, vendorCode);
                _vendorName.Text = vendorName;
                _vendorTerms.Text = terms;
                _location.Text = location;
            }
            else
            {
                _vendor.SelectedIndex = -1;
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

        private static string CurrentCode(ComboBox box)
        {
            if (box.SelectedItem is CodeChoice choice)
                return choice.Code;
            return box.Text.Trim();
        }

        private static void SelectCode(ComboBox box, string code)
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                box.SelectedIndex = -1;
                return;
            }

            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is CodeChoice choice &&
                    choice.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            box.Text = code;
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

        private sealed class CodeChoice
        {
            public string Code { get; }
            public string Name { get; }
            public string Extra { get; }

            public CodeChoice(string code, string name, string extra)
            {
                Code = code;
                Name = name;
                Extra = extra;
            }

            public override string ToString() =>
                string.IsNullOrWhiteSpace(Name) ? Code : $"{Code}  ·  {Name}";
        }
    }
}
