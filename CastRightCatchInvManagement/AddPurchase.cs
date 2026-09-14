using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// New Purchase: one PO header and multiple product lines, like Create Invoice.
    /// Vendor code/name and item code fill the rest. Saves one purchases row per line.
    /// </summary>
    public partial class AddPurchase : Form, INavigationPage
    {
        private readonly List<PurchaseLineRow> _lines = new();
        private readonly HashSet<string> _loadedItems = new(StringComparer.OrdinalIgnoreCase);

        private TextBox _po = null!;
        private TextBox _vendor = null!;
        private TextBox _vendorName = null!;
        private TextBox _location = null!;
        private TextBox _vendorTerms = null!;
        private DateTimePicker _agreement = null!;
        private DateTimePicker _expectedShip = null!;
        private DateTimePicker _vendorDue = null!;
        private DateTimePicker _ship = null!;
        private DateTimePicker _arrival = null!;
        private TextBox _forwarder = null!;
        private TextBox _logistics = null!;
        private ComboBox _status = null!;
        private ComboBox _freightCo = null!;
        private TextBox _overhead = null!;
        private TextBox _freight = null!;
        private TextBox _forwarderLb = null!;
        private TextBox _other = null!;
        private Panel _lineHost = null!;
        private Label _modeLabel = null!;
        private Label _totalVolume = null!;
        private Label _totalCost = null!;
        private Button _save = null!;
        private Button _another = null!;
        private List<LookupSuggest.Hit> _vendorHits = new();
        private List<LookupSuggest.Hit> _forwarderHits = new();
        private List<LookupSuggest.Hit> _logisticsHits = new();
        private List<LookupSuggest.Hit> _itemHits = new();
        private LookupSuggest? _vendorCodeSuggest;
        private LookupSuggest? _vendorNameSuggest;
        private LookupSuggest? _forwarderSuggest;
        private LookupSuggest? _logisticsSuggest;
        private bool _editing;
        private string _editPo = "";

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

        /// <summary>Open this page with every product on that PO loaded for edit.</summary>
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

        public void HighlightCurrentPage()
        {
            LoadLookups();
            if (PendingEdit != null)
            {
                var record = PendingEdit;
                PendingEdit = null;
                StartNew = false;
                LoadOrder(record);
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
            if (_lines.Count == 0)
                AddLine();
        }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 20);
            AutoScroll = false;

            var footer = BuildFooter();
            footer.Dock = DockStyle.Bottom;

            var header = BuildHeader();
            header.Dock = DockStyle.Top;

            var lines = BuildLinesCard();
            lines.Dock = DockStyle.Fill;

            Controls.Add(lines);
            Controls.Add(header);
            Controls.Add(footer);

            LoadLookups();
            ResetForm(keepVendor: false);
        }

        private CardPanel BuildHeader()
        {
            var card = new CardPanel { Height = 268, Padding = new Padding(16, 10, 16, 10) };

            _modeLabel = new Label
            {
                Text = "New Purchase",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 8)
            };
            card.Controls.Add(_modeLabel);

            _po = AddField(card, "PO #", 20, 36, 130);
            _vendor = AddField(card, "VENDOR CODE", 164, 36, 160);
            _vendorName = AddField(card, "VENDOR", 338, 36, 250);
            _location = AddField(card, "LOCATION", 602, 36, 170);

            _vendorTerms = AddField(card, "VENDOR TERMS", 20, 86, 160);
            _agreement = AddDate(card, "AGREEMENT DATE", 194, 86, 140);
            _expectedShip = AddDate(card, "EXPECTED SHIP DATE", 348, 86, 150);
            _vendorDue = AddDate(card, "VENDOR DUE DATE", 512, 86, 140);
            _status = AddCombo(card, "STATUS", 666, 86, 130);
            _status.DropDownStyle = ComboBoxStyle.DropDownList;
            SelectStatus(_status, "Pending");

            _ship = AddDate(card, "SHIP DATE", 20, 136, 140);
            _arrival = AddDate(card, "ARRIVAL DATE", 174, 136, 140);
            _forwarder = AddField(card, "FORWARDER", 328, 136, 150);
            _logistics = AddField(card, "LOGISTICS", 492, 136, 150);
            _freightCo = AddCombo(card, "FREIGHT CO", 656, 136, 180);

            _overhead = AddField(card, "OVERHEAD / LB", 20, 186, 120);
            _freight = AddField(card, "FREIGHT / LB", 154, 186, 120);
            _forwarderLb = AddField(card, "FORWARDER / LB", 288, 186, 130);
            _other = AddField(card, "OTHER / LB", 432, 186, 110);

            _vendorCodeSuggest = new LookupSuggest(_vendor, () => _vendorHits, codeFirst: true, ApplyVendorHit);
            _vendorNameSuggest = new LookupSuggest(_vendorName, () => _vendorHits, codeFirst: false, ApplyVendorHit);
            _forwarderSuggest = new LookupSuggest(_forwarder, () => _forwarderHits, codeFirst: false, hit => ApplyNameHit(_forwarder, hit));
            _logisticsSuggest = new LookupSuggest(_logistics, () => _logisticsHits, codeFirst: false, hit => ApplyNameHit(_logistics, hit));
            foreach (var box in new[] { _overhead, _freight, _forwarderLb, _other })
                box.TextChanged += (_, _) => RecalcLines();

            return card;
        }

        private CardPanel BuildLinesCard()
        {
            var card = new CardPanel { Padding = new Padding(1) };

            var addBar = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = Theme.Paper,
                Padding = new Padding(12, 8, 12, 8)
            };
            var add = new Button
            {
                Text = "+",
                Width = 36,
                Dock = DockStyle.Left
            };
            Theme.StyleGoldButton(add);
            add.Click += (_, _) => AddLine();
            var hint = new Label
            {
                Text = "Type a vendor code or name to fill the order. Type an item code on a line to fill that product. Add lines for more products on this PO.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };
            addBar.Controls.Add(hint);
            addBar.Controls.Add(add);

            var labels = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Theme.Navy
            };
            labels.Resize += (_, _) => labels.Invalidate();
            labels.Paint += (_, e) =>
            {
                var slots = PurchaseLineLayout.Slots(labels.Width);
                DrawHeader(e.Graphics, "ITEM", slots.Item);
                DrawHeader(e.Graphics, "DESCRIPTION", slots.Description);
                DrawHeader(e.Graphics, "COO", slots.Coo);
                DrawHeader(e.Graphics, "PACK", slots.Pack);
                DrawHeader(e.Graphics, "CS", slots.Cases);
                DrawHeader(e.Graphics, "VOLUME", slots.Volume);
                DrawHeader(e.Graphics, "PRICE / LB", slots.Price);
                DrawHeader(e.Graphics, "TOTAL / LB", slots.TotalPerLb);
                DrawHeader(e.Graphics, "TOTAL", slots.Total);
            };

            _lineHost = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Paper,
                Padding = new Padding(8, 8, 8, 8)
            };
            _lineHost.Resize += (_, _) => LayoutLines();

            card.Controls.Add(_lineHost);
            card.Controls.Add(labels);
            card.Controls.Add(addBar);
            return card;
        }

        private void AddLine()
        {
            var row = new PurchaseLineRow();
            row.AttachLookups(() => _itemHits);
            row.SetSharedCosts(SharedOverhead(), SharedFreight(), SharedForwarder(), SharedOther());
            row.Changed += (_, _) => UpdateTotals();
            row.RemoveRequested += (_, _) => RemoveLine(row);
            _lines.Add(row);
            _lineHost.Controls.Add(row);
            LayoutLines();
            row.FocusItem();
            UpdateTotals();
        }

        private void RemoveLine(PurchaseLineRow row)
        {
            _lines.Remove(row);
            _lineHost.Controls.Remove(row);
            row.Dispose();
            if (_lines.Count == 0)
                AddLine();
            LayoutLines();
            UpdateTotals();
        }

        private void LayoutLines()
        {
            int y = _lineHost.Padding.Top;
            int width = Math.Max(640, _lineHost.ClientSize.Width - _lineHost.Padding.Horizontal - 8);
            foreach (var row in _lines)
            {
                row.SetBounds(_lineHost.Padding.Left, y, width, PurchaseLineRow.RowHeight);
                y += PurchaseLineRow.RowHeight + 6;
            }

            _lineHost.AutoScrollMinSize = new Size(0, y + 8);
        }

        private CardPanel BuildFooter()
        {
            var card = new CardPanel { Height = 78, Padding = new Padding(16, 10, 16, 10) };

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

            _totalVolume = TotalLabel(card, "TOTAL VOLUME", 20, 16);
            _totalCost = TotalLabel(card, "TOTAL COST", 160, 16);
            _totalCost.Font = Theme.SectionTitle;
            _totalCost.ForeColor = Theme.Navy;

            card.Controls.Add(_save);
            card.Controls.Add(_another);
            card.Controls.Add(clear);
            card.Resize += (_, _) =>
            {
                _save.Location = new Point(Math.Max(360, card.Width - 174), 22);
                _another.Location = new Point(Math.Max(220, card.Width - 314), 22);
                clear.Location = new Point(Math.Max(120, card.Width - 414), 22);
            };

            return card;
        }

        private void LoadLookups()
        {
            _vendorHits.Clear();
            _forwarderHits.Clear();
            _logisticsHits.Clear();
            foreach (var record in DataFiles.VisibleRecords(DataFiles.Vendors))
            {
                string code = DataFiles.GetRecord(record, "Code").Trim();
                string name = DataFiles.GetRecordAny(record, "Name", "Company").Trim();
                if (code.Length == 0 && name.Length == 0)
                    continue;
                var hit = new LookupSuggest.Hit(code, name, DataFiles.GetRecord(record, "Terms"));
                _vendorHits.Add(hit);
                if (VendorTypes.MatchesSlot(record, VendorTypes.SlotPurchaseForwarder))
                    _forwarderHits.Add(hit);
                if (VendorTypes.MatchesSlot(record, VendorTypes.SlotPurchaseLogistics))
                    _logisticsHits.Add(hit);
            }

            _itemHits.Clear();
            foreach (var record in DataFiles.VisibleRecords(DataFiles.ItemCodes))
            {
                string code = DataFiles.GetRecord(record, "Code").Trim();
                string description = DataFiles.GetRecord(record, "Description").Trim();
                string species = DataFiles.GetRecord(record, "Species").Trim();
                if (description.Length == 0)
                    description = species;
                if (code.Length == 0 && description.Length == 0)
                    continue;
                _itemHits.Add(new LookupSuggest.Hit(
                    code,
                    description,
                    DataFiles.GetRecord(record, "COO"),
                    species));
            }

            if (_freightCo != null)
                VendorChoice.Fill(_freightCo);

            foreach (var row in _lines)
                row.AttachLookups(() => _itemHits);
        }

        private void ApplyVendorHit(LookupSuggest.Hit hit)
        {
            _vendor.Text = hit.Code;
            _vendorName.Text = hit.Name;
            if (hit.Extra.Length > 0)
                _vendorTerms.Text = hit.Extra;
        }

        private static void ApplyNameHit(TextBox box, LookupSuggest.Hit hit)
        {
            box.Text = hit.Name.Length > 0 ? hit.Name : hit.Code;
        }

        private void RecalcLines()
        {
            decimal overhead = SharedOverhead();
            decimal freight = SharedFreight();
            decimal forwarder = SharedForwarder();
            decimal other = SharedOther();
            foreach (var row in _lines)
                row.SetSharedCosts(overhead, freight, forwarder, other);
            UpdateTotals();
        }

        private void UpdateTotals()
        {
            decimal volume = 0;
            decimal cost = 0;
            foreach (var row in _lines)
            {
                var line = row.GetLine();
                volume += PurchaseLineRow.ParseNumber(line.Volume);
                cost += PurchaseLineRow.ParseNumber(line.TotalCost);
            }

            _totalVolume.Text = volume.ToString("0.###", CultureInfo.InvariantCulture);
            _totalCost.Text = cost.ToString("0.00", CultureInfo.InvariantCulture);
        }

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

            var lines = _lines.Select(row => row.GetLine())
                .Where(line => line.ItemCode.Length > 0 || line.Description.Length > 0)
                .ToList();
            if (lines.Count == 0)
            {
                ToastAlert.Error(this, "Add at least one product line.");
                return;
            }

            if (string.IsNullOrWhiteSpace(_vendor.Text) &&
                string.IsNullOrWhiteSpace(_vendorName.Text))
            {
                ToastAlert.Error(this, "Pick a vendor.");
                return;
            }

            RecalcLines();
            var savedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MutateResult? last = null;
            try
            {
                foreach (var line in lines)
                {
                    var values = BuildValues(po, line);
                    string item = line.ItemCode;
                    bool exists = _editing &&
                                  _loadedItems.Contains(item) &&
                                  po.Equals(_editPo, StringComparison.OrdinalIgnoreCase);
                    last = exists
                        ? DataFiles.MutateUpdate(
                            DataFiles.PurchaseSales,
                            record =>
                                DataFiles.NormalizePo(DataFiles.GetRecord(record, "PO #")) ==
                                DataFiles.NormalizePo(_editPo) &&
                                DataFiles.GetRecord(record, "Item Code").Trim()
                                    .Equals(item, StringComparison.OrdinalIgnoreCase),
                            values)
                        : DataFiles.MutateInsert(DataFiles.PurchaseSales, values);
                    if (last is not { Ok: true })
                    {
                        ToastAlert.Error(this, last?.Message ?? "Could not save that line.");
                        return;
                    }

                    if (item.Length > 0)
                        savedItems.Add(item);
                }

                if (_editing)
                {
                    foreach (string oldItem in _loadedItems)
                    {
                        if (savedItems.Contains(oldItem) &&
                            po.Equals(_editPo, StringComparison.OrdinalIgnoreCase))
                            continue;
                        var doomed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["PO #"] = _editPo,
                            ["Item Code"] = oldItem
                        };
                        last = DataFiles.MutateDelete(DataFiles.PurchaseSales, doomed);
                        if (last is not { Ok: true })
                        {
                            ToastAlert.Error(this, last?.Message ?? "Could not remove that line.");
                            return;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            try
            {
                PurchaseDocument.SaveFromPo(po);
            }
            catch
            {
                // keep the saved rows even if the PDF cannot be written
            }

            ToastAlert.Success(this, last is { Queued: true }
                ? last.Value.Message
                : _editing ? "The purchase was updated." : "The purchase was saved.");

            if (keepVendor)
            {
                ResetForm(keepVendor: true);
                return;
            }

            if (_editing)
            {
                _editPo = po;
                _loadedItems.Clear();
                foreach (string item in savedItems)
                    _loadedItems.Add(item);
                return;
            }

            ResetForm(keepVendor: false);
        }

        private Dictionary<string, string> BuildValues(string po, PurchaseLine line)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PO #"] = po,
                ["Vendor Code"] = _vendor.Text.Trim(),
                ["Vendor"] = _vendorName.Text.Trim(),
                ["Location"] = _location.Text.Trim(),
                ["Item Code"] = line.ItemCode,
                ["Description"] = line.Description,
                ["COO"] = line.Coo,
                ["Pack Size"] = line.PackSize,
                ["CS"] = line.Cases,
                ["Volume"] = line.Volume,
                ["Price Paid / LB"] = line.Price,
                ["Overhead / LB"] = _overhead.Text.Trim(),
                ["Freight / LB"] = _freight.Text.Trim(),
                [DataFiles.FreightCompanyColumn] = VendorChoice.TextOf(_freightCo),
                ["Forwarder / LB"] = _forwarderLb.Text.Trim(),
                ["Other / LB"] = _other.Text.Trim(),
                ["Total Cost / LB"] = line.TotalPerLb,
                ["Total Cost"] = line.TotalCost,
                ["Agreement Date"] = DateText(_agreement),
                ["Expected Ship Date"] = DateText(_expectedShip),
                ["Vendor Terms"] = _vendorTerms.Text.Trim(),
                ["Vendor Due Date"] = DateText(_vendorDue),
                ["Ship Date"] = DateText(_ship),
                ["Arrival Date"] = DateText(_arrival),
                ["Forwarder"] = _forwarder.Text.Trim(),
                ["Logistics"] = _logistics.Text.Trim(),
                ["Status"] = _status.Text.Trim()
            };
        }

        private void LoadOrder(Dictionary<string, string> record)
        {
            string po = DataFiles.GetRecord(record, "PO #");
            var rows = DataFiles.FindPurchasesByPo(po);
            if (rows.Count == 0)
                rows.Add(record);

            ClearLines();
            _editPo = po;
            _loadedItems.Clear();
            _po.Text = po;
            _vendor.Text = DataFiles.GetRecord(rows[0], "Vendor Code");
            _vendorName.Text = DataFiles.GetRecord(rows[0], "Vendor");
            _location.Text = DataFiles.GetRecord(rows[0], "Location");
            _vendorTerms.Text = DataFiles.GetRecord(rows[0], "Vendor Terms");
            SetDate(_agreement, DataFiles.GetRecord(rows[0], "Agreement Date"));
            SetDate(_expectedShip, DataFiles.GetRecord(rows[0], "Expected Ship Date"));
            SetDate(_vendorDue, DataFiles.GetRecord(rows[0], "Vendor Due Date"));
            SetDate(_ship, DataFiles.GetRecord(rows[0], "Ship Date"));
            SetDate(_arrival, DataFiles.GetRecord(rows[0], "Arrival Date"));
            _forwarder.Text = DataFiles.GetRecord(rows[0], "Forwarder");
            _logistics.Text = DataFiles.GetRecord(rows[0], "Logistics");
            VendorChoice.Select(
                _freightCo,
                DataFiles.GetRecordAny(rows[0], DataFiles.FreightCompanyColumn, "Forwarder", "Logistics"));
            SelectStatus(_status, DataFiles.GetRecord(rows[0], "Status"));
            _overhead.Text = DataFiles.GetRecord(rows[0], "Overhead / LB");
            _freight.Text = DataFiles.GetRecord(rows[0], "Freight / LB");
            _forwarderLb.Text = DataFiles.GetRecord(rows[0], "Forwarder / LB");
            _other.Text = DataFiles.GetRecord(rows[0], "Other / LB");

            foreach (var row in rows)
            {
                AddLine();
                var line = _lines[^1];
                line.FillFromRecord(row);
                string item = DataFiles.GetRecord(row, "Item Code").Trim();
                if (item.Length > 0)
                    _loadedItems.Add(item);
            }

            RecalcLines();
            SetMode(true);
        }

        private void ResetForm(bool keepVendor)
        {
            string vendorCode = keepVendor ? _vendor.Text.Trim() : "";
            string vendorName = keepVendor ? _vendorName.Text.Trim() : "";
            string terms = keepVendor ? _vendorTerms.Text : "";
            string location = keepVendor ? _location.Text : "";
            string freightCo = keepVendor ? VendorChoice.TextOf(_freightCo) : "";

            ClearLines();
            _loadedItems.Clear();
            _po.Text = DataFiles.NextPurchasePo();
            _overhead.Text = "";
            _freight.Text = "";
            _forwarderLb.Text = "";
            _other.Text = "";
            _agreement.Value = DateTime.Today;
            _agreement.Checked = true;
            Uncheck(_expectedShip);
            Uncheck(_vendorDue);
            Uncheck(_ship);
            Uncheck(_arrival);
            _forwarder.Text = "";
            _logistics.Text = "";
            SelectStatus(_status, "Pending");
            VendorChoice.Select(_freightCo, freightCo);

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

            SetMode(false);
            AddLine();
            _po.Focus();
            UpdateTotals();
        }

        private void ClearLines()
        {
            foreach (var row in _lines.ToList())
            {
                _lineHost.Controls.Remove(row);
                row.Dispose();
            }

            _lines.Clear();
        }

        private void SetMode(bool editing)
        {
            _editing = editing;
            _modeLabel.Text = editing ? "Edit Purchase" : "New Purchase";
            _save.Text = editing ? "Save Changes" : "Save Purchase";
            _another.Visible = !editing;
        }

        private decimal SharedOverhead() => PurchaseLineRow.ParseNumber(_overhead.Text);
        private decimal SharedFreight() => PurchaseLineRow.ParseNumber(_freight.Text);
        private decimal SharedForwarder() => PurchaseLineRow.ParseNumber(_forwarderLb.Text);
        private decimal SharedOther() => PurchaseLineRow.ParseNumber(_other.Text);

        private static void DrawHeader(Graphics g, string text, Rectangle slot)
        {
            TextRenderer.DrawText(
                g,
                text,
                Theme.Caption,
                new Rectangle(slot.X, 0, slot.Width, 28),
                Theme.HeaderText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private static Label TotalLabel(Control parent, string caption, int x, int y)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var value = new Label
            {
                Location = new Point(x, y + 16),
                Size = new Size(130, 26),
                Font = Theme.Body,
                ForeColor = Theme.Ink,
                TextAlign = ContentAlignment.MiddleLeft
            };
            parent.Controls.Add(label);
            parent.Controls.Add(value);
            return value;
        }

        private static TextBox AddField(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
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
            if (NumericDateBox.TryParseCell(text, out var date) ||
                DateTime.TryParse(text, out date))
            {
                picker.Value = date.Date;
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

    }
}
