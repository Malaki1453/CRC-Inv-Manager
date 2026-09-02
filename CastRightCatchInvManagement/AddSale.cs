using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Create or edit a sale line. Can open Create Sales Order for the customer PO.</summary>
    public partial class AddSale : Form, INavigationPage
    {
        private TextBox _so = null!;
        private TextBox _po = null!;
        private ComboBox _lot = null!;
        private ComboBox _customer = null!;
        private TextBox _customerCode = null!;
        private TextBox _customerName = null!;
        private TextBox _terms = null!;
        private ComboBox _item = null!;
        private TextBox _description = null!;
        private TextBox _coo = null!;
        private TextBox _packSize = null!;
        private TextBox _cs = null!;
        private TextBox _volume = null!;
        private TextBox _price = null!;
        private TextBox _amount = null!;
        private DateTimePicker _ship = null!;
        private DateTimePicker _due = null!;
        private TextBox _invoice = null!;
        private TextBox _paid = null!;
        private ComboBox _status = null!;
        private Label _modeLabel = null!;
        private Button _save = null!;
        private Button _another = null!;
        private bool _loading;
        private bool _calculating;
        private bool _editing;
        private string _editPo = "";
        private string _editItem = "";
        private string _editCustomer = "";

        internal static Dictionary<string, string>? PendingEdit { get; set; }
        internal static bool StartNew { get; set; }

        public AddSale()
        {
            InitializeComponent();
            BuildUi();
        }

        /// <summary>Open this page as a blank sale.</summary>
        public static void OpenNew()
        {
            PendingEdit = null;
            StartNew = true;
            Navigator.GoTo(AppPage.AddSale);
        }

        /// <summary>Open this page with an existing sale row loaded for edit.</summary>
        public static void OpenEdit(Dictionary<string, string> record)
        {
            PendingEdit = record;
            StartNew = false;
            Navigator.GoTo(AppPage.AddSale);
        }

        /// <summary>
        /// Shown or refreshed: reload customer/item/lot lists, then apply a pending edit or a new blank.
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
                ResetForm(keepCustomer: false);
                return;
            }

        }

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
            _save.Click += (_, _) => SaveSale();

            _another = new Button
            {
                Text = "Add Another",
                Size = new Size(130, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleNavyButton(_another);
            _another.Click += (_, _) => SaveSale(keepCustomer: true);

            var pdf = new Button
            {
                Text = "Create Sales Order",
                Size = new Size(168, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleNavyButton(pdf);
            pdf.Click += (_, _) => CreateSalesOrderPdf();

            var clear = new Button
            {
                Text = "Clear",
                Size = new Size(90, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetForm(keepCustomer: false);

            actions.Controls.Add(_save);
            actions.Controls.Add(_another);
            actions.Controls.Add(pdf);
            actions.Controls.Add(clear);
            actions.Resize += (_, _) =>
            {
                _save.Location = new Point(Math.Max(440, actions.Width - 168), 8);
                _another.Location = new Point(Math.Max(300, actions.Width - 308), 8);
                pdf.Location = new Point(Math.Max(120, actions.Width - 486), 8);
                clear.Location = new Point(Math.Max(8, actions.Width - 586), 8);
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
                Text = "Enter the customer PO, then pick a lot from Purchases to fill the product.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 28
            };

            scroll.Controls.Add(BuildStatusCard());
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(BuildProductCard());
            scroll.Controls.Add(Spacer());
            scroll.Controls.Add(BuildOrderCard());

            Controls.Add(scroll);
            Controls.Add(actions);
            Controls.Add(intro);
            Controls.Add(_modeLabel);

            LoadLookups();
            ResetForm(keepCustomer: false);
        }

        private CardPanel BuildOrderCard()
        {
            var card = MakeCard("Order", 168);
            _po = AddText(card, "PO #", 20, 48, 180);
            _so = AddText(card, "SO #", 220, 48, 120);
            _so.ReadOnly = true;
            _so.PlaceholderText = "Set on Create Sales Order";
            _customer = AddCombo(card, "CUSTOMER", 360, 48, 280);
            _customerCode = AddText(card, "CUSTOMER CODE", 20, 100, 140);
            _customerName = AddText(card, "CUSTOMER", 180, 100, 280);
            _terms = AddText(card, "CUSTOMER TERMS", 480, 100, 160);
            _customer.SelectedIndexChanged += (_, _) => ApplyCustomer();
            return card;
        }

        private CardPanel BuildProductCard()
        {
            var card = MakeCard("Product", 168);
            _item = AddCombo(card, "ITEM CODE", 20, 48, 180);
            _lot = AddCombo(card, "LOT #", 220, 48, 220);
            _description = AddText(card, "DESCRIPTION", 460, 48, 360);
            _coo = AddText(card, "COO", 20, 100, 100);
            _packSize = AddText(card, "PACK SIZE", 140, 100, 110);
            _cs = AddText(card, "CS", 270, 100, 90);
            _volume = AddText(card, "VOLUME", 380, 100, 120);
            _price = AddText(card, "SELL PRICE / LB", 520, 100, 130);
            _amount = AddText(card, "AMOUNT", 670, 100, 130);
            _amount.ReadOnly = true;
            _amount.BackColor = Theme.GridAlt;
            _lot.SelectedIndexChanged += (_, _) => ApplyLot();
            _lot.Leave += (_, _) => ApplyLot();
            _item.SelectedIndexChanged += (_, _) => ApplyItem();
            _volume.TextChanged += (_, _) => RecalcAmount();
            _price.TextChanged += (_, _) => RecalcAmount();
            _cs.TextChanged += (_, _) => RecalcVolume();
            _packSize.TextChanged += (_, _) => RecalcVolume();
            return card;
        }

        private CardPanel BuildStatusCard()
        {
            var card = MakeCard("Dates && status", 120);
            _ship = AddDate(card, "SHIP DATE", 20, 48, 150);
            _due = AddDate(card, "DUE DATE", 190, 48, 150);
            _invoice = AddText(card, "INVOICE #", 360, 48, 130);
            _paid = AddText(card, "PAID", 510, 48, 110);
            _status = AddCombo(card, "STATUS", 640, 48, 140);
            _status.DropDownStyle = ComboBoxStyle.DropDownList;
            SelectStatus(_status, "Pending");
            return card;
        }

        /// <summary>
        /// Refill lot (purchase PO #s), customer, and item-code lists, keeping the current picks.
        /// </summary>
        private void LoadLookups()
        {
            _loading = true;
            string lot = _lot.Text.Trim();
            string customer = CurrentCode(_customer);
            string item = CurrentCode(_item);

            _lot.Items.Clear();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var record in DataFiles.ReadRecords(DataFiles.PurchaseSales))
            {
                string value = DataFiles.GetRecord(record, "PO #").Trim();
                if (value.Length == 0 || !seen.Add(value))
                    continue;
                _lot.Items.Add(value);
            }

            _customer.Items.Clear();
            foreach (var record in DataFiles.ReadRecords(DataFiles.Customers))
            {
                _customer.Items.Add(new CodeChoice(
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

            if (lot.Length > 0)
                _lot.Text = lot;
            SelectCode(_customer, customer);
            SelectCode(_item, item);
            _loading = false;
        }

        /// <summary>When a lot / purchase PO is chosen, copy item, description, pack, cases, and volume from that purchase.</summary>
        private void ApplyLot()
        {
            if (_loading)
                return;

            var purchase = DataFiles.FindPurchaseByPo(_lot.Text);
            if (purchase == null)
                return;

            SelectCode(_item, DataFiles.GetRecord(purchase, "Item Code"));
            _description.Text = DataFiles.GetRecord(purchase, "Description");
            _coo.Text = DataFiles.GetRecord(purchase, "COO");
            _packSize.Text = DataFiles.GetRecord(purchase, "Pack Size");
            _cs.Text = DataFiles.GetRecord(purchase, "CS");
            string volume = DataFiles.GetRecord(purchase, "Volume Received");
            if (volume.Length == 0)
                volume = DataFiles.GetRecord(purchase, "Volume");
            _volume.Text = volume;
            RecalcAmount();
        }

        /// <summary>When a customer is chosen, fill code, name, and terms.</summary>
        private void ApplyCustomer()
        {
            if (_loading || _customer.SelectedItem is not CodeChoice choice)
                return;

            _customerCode.Text = choice.Code;
            _customerName.Text = choice.Name;
            if (!string.IsNullOrWhiteSpace(choice.Extra))
                _terms.Text = choice.Extra;
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

        /// <summary>If volume is empty, set it to pack size × cases, then refresh amount.</summary>
        private void RecalcVolume()
        {
            if (_calculating)
                return;
            decimal pack = ParseNumber(_packSize.Text);
            decimal cs = ParseNumber(_cs.Text);
            if (pack <= 0 || cs <= 0)
            {
                RecalcAmount();
                return;
            }

            _calculating = true;
            if (string.IsNullOrWhiteSpace(_volume.Text))
                _volume.Text = (pack * cs).ToString("0.##", CultureInfo.InvariantCulture);
            _calculating = false;
            RecalcAmount();
        }

        /// <summary>Amount = volume × sell price / lb.</summary>
        private void RecalcAmount()
        {
            if (_calculating)
                return;
            _calculating = true;
            decimal amount = ParseNumber(_volume.Text) * ParseNumber(_price.Text);
            _amount.Text = amount.ToString("0.00", CultureInfo.InvariantCulture);
            _calculating = false;
        }

        /// <summary>
        /// Write this line to sales. Edit replaces the original PO + item + customer; add inserts a new row.
        /// <paramref name="keepCustomer"/> true leaves customer fields filled after add.
        /// </summary>
        private void SaveSale(bool keepCustomer = false)
        {
            if (!AppLock.HasFolder())
            {
                ToastAlert.Error(this, "Select a data folder in Settings first.");
                return;
            }

            string po = _po.Text.Trim();
            if (po.Length == 0)
            {
                ToastAlert.Error(this, "Enter the customer PO #.");
                return;
            }

            RecalcAmount();
            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Lot #"] = _lot.Text.Trim(),
                ["PO #"] = po,
                ["SO #"] = _so.Text.Trim(),
                ["Customer Code"] = _customerCode.Text.Trim(),
                ["Customer"] = _customerName.Text.Trim(),
                ["Customer Terms"] = _terms.Text.Trim(),
                ["Item Code"] = CurrentCode(_item),
                ["Description"] = _description.Text.Trim(),
                ["COO"] = _coo.Text.Trim(),
                ["Pack Size"] = _packSize.Text.Trim(),
                ["CS"] = _cs.Text.Trim(),
                ["Volume"] = _volume.Text.Trim(),
                ["Sell Price / LB"] = _price.Text.Trim(),
                ["Amount"] = _amount.Text.Trim(),
                ["Ship Date"] = DateText(_ship),
                ["Due Date"] = DateText(_due),
                ["Invoice #"] = _invoice.Text.Trim(),
                ["Paid"] = _paid.Text.Trim(),
                ["Status"] = _status.Text.Trim()
            };

            try
            {
                if (_editing)
                {
                    bool updated = DataFiles.ReplaceMatchingRow(
                        DataFiles.Sales,
                        record =>
                            DataFiles.NormalizePo(DataFiles.SalePo(record)) ==
                            DataFiles.NormalizePo(_editPo) &&
                            DataFiles.GetRecord(record, "Item Code").Trim()
                                .Equals(_editItem, StringComparison.OrdinalIgnoreCase) &&
                            DataFiles.GetRecord(record, "Customer Code").Trim()
                                .Equals(_editCustomer, StringComparison.OrdinalIgnoreCase),
                        DataFiles.NamedRow(DataFiles.Sales, fields));
                    if (!updated)
                    {
                        ToastAlert.Error(this, "Could not find that product line to update.");
                        return;
                    }

                    _editPo = po;
                    _editItem = CurrentCode(_item);
                    _editCustomer = _customerCode.Text.Trim();
                    ToastAlert.Success(this, "The product was updated.");
                    return;
                }

                DataFiles.AppendNamedRow(DataFiles.Sales, fields);
                ToastAlert.Success(this, "The product was added.");
                ResetForm(keepCustomer);
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        /// <summary>
        /// Save this sale, then open or build the sales-order PDF for this customer PO.
        /// Writes the SO # back onto this form when the PDF is created.
        /// </summary>
        private void CreateSalesOrderPdf()
        {
            string po = _po.Text.Trim();
            if (po.Length == 0)
            {
                ToastAlert.Error(this, "Enter the customer PO # first.");
                return;
            }

            if (_customerCode.Text.Trim().Length == 0 && _customerName.Text.Trim().Length == 0)
            {
                ToastAlert.Error(this, "Choose a customer first.");
                return;
            }

            SaveSale(keepCustomer: true);
            var prefill = new InvoiceSalePrefill
            {
                Po = po,
                So = _so.Text.Trim(),
                ItemCode = CurrentCode(_item),
                CustomerCode = _customerCode.Text.Trim(),
                CustomerName = _customerName.Text.Trim()
            };

            var order = Navigator.Ensure<SalesOrder>(AppPage.SalesOrder);
            order.BeginFromSale(prefill, error =>
            {
                if (error != null)
                {
                    ToastAlert.Error(this, error);
                    return;
                }

                string? so = order.CreateOrOpenPdf();
                if (!string.IsNullOrWhiteSpace(so))
                    _so.Text = so;
            });
        }

        /// <summary>Copy an existing sale row into the form and switch to edit mode.</summary>
        private void LoadRecord(Dictionary<string, string> record)
        {
            _loading = true;
            _editPo = DataFiles.SalePo(record);
            _editItem = DataFiles.GetRecord(record, "Item Code");
            _editCustomer = DataFiles.GetRecord(record, "Customer Code");
            _so.Text = DataFiles.GetRecord(record, "SO #");
            _po.Text = _editPo;
            _lot.Text = DataFiles.SaleLot(record);
            SelectCode(_customer, _editCustomer);
            _customerCode.Text = _editCustomer;
            _customerName.Text = DataFiles.GetRecord(record, "Customer");
            _terms.Text = DataFiles.GetRecord(record, "Customer Terms");
            SelectCode(_item, _editItem);
            _description.Text = DataFiles.GetRecord(record, "Description");
            _coo.Text = DataFiles.GetRecord(record, "COO");
            _packSize.Text = DataFiles.GetRecord(record, "Pack Size");
            _cs.Text = DataFiles.GetRecord(record, "CS");
            _volume.Text = DataFiles.GetRecord(record, "Volume");
            _price.Text = DataFiles.GetRecord(record, "Sell Price / LB");
            _amount.Text = DataFiles.GetRecord(record, "Amount");
            SetDate(_ship, DataFiles.GetRecord(record, "Ship Date"));
            SetDate(_due, DataFiles.GetRecord(record, "Due Date"));
            _invoice.Text = DataFiles.GetRecord(record, "Invoice #");
            _paid.Text = DataFiles.GetRecord(record, "Paid");
            SelectStatus(_status, DataFiles.GetRecord(record, "Status"));
            _loading = false;
            SetMode(true);
            RecalcAmount();
        }

        /// <summary>Clear the form for another line. Optionally keep the customer.</summary>
        private void ResetForm(bool keepCustomer)
        {
            string customer = keepCustomer ? CurrentCode(_customer) : "";
            string customerName = keepCustomer ? _customerName.Text : "";
            string terms = keepCustomer ? _terms.Text : "";
            string code = keepCustomer ? _customerCode.Text : "";
            string po = keepCustomer ? _po.Text : "";

            _loading = true;
            _so.Text = "";
            _po.Text = po;
            _lot.Text = "";
            _item.SelectedIndex = -1;
            _description.Text = "";
            _coo.Text = "";
            _packSize.Text = "";
            _cs.Text = "";
            _volume.Text = "";
            _price.Text = "";
            _amount.Text = "";
            Uncheck(_ship);
            Uncheck(_due);
            _invoice.Text = "";
            _paid.Text = "";
            SelectStatus(_status, "Pending");

            if (keepCustomer)
            {
                SelectCode(_customer, customer);
                _customerCode.Text = code;
                _customerName.Text = customerName;
                _terms.Text = terms;
            }
            else
            {
                _customer.SelectedIndex = -1;
                _customerCode.Text = "";
                _customerName.Text = "";
                _terms.Text = "";
            }

            _loading = false;
            SetMode(false);
            _po.Focus();
        }

        /// <summary>Switch the heading and save button between Add Product and Edit Product.</summary>
        private void SetMode(bool editing)
        {
            _editing = editing;
            _modeLabel.Text = editing ? "Edit Product" : "Add Product";
            _save.Text = editing ? "Edit Product" : "Add Product";
            _another.Visible = !editing;
        }

        private static CardPanel MakeCard(string title, int height)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = height,
                Padding = new Padding(12, 10, 12, 10)
            };
            card.Controls.Add(new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 10)
            });
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

        private static void Uncheck(DateTimePicker picker) => picker.Checked = false;

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

        private static string CurrentCode(ComboBox box) =>
            box.SelectedItem is CodeChoice choice ? choice.Code : box.Text.Trim();

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
