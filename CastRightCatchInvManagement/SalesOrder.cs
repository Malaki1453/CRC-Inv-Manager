using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Sales order: one header and multiple product lines, like New Purchase. Saves a PDF.</summary>
    public partial class SalesOrder : Form, INavigationPage
    {
        private AutoCompleteStringCollection _poSource = new();
        private readonly List<SalesOrderLineRow> _lines = new();
        private readonly List<CustomerChoice> _customerRecords = new();
        private List<LookupSuggest.Hit> _customerHits = new();
        private LookupSuggest? _customerCodeSuggest;
        private LookupSuggest? _customerNameSuggest;

        private TextBox _customer = null!;
        private TextBox _customerCode = null!;
        private TextBox _address = null!;
        private TextBox _contact = null!;
        private TextBox _email = null!;
        private TextBox _contactPhone = null!;
        private TextBox _warehouse = null!;
        private DateTimePicker _releaseDate = null!;
        private TextBox _customerPo = null!;
        private TextBox _soNo = null!;
        private DateTimePicker _orderDate = null!;
        private ComboBox _freightCo = null!;
        private TextBox _freightTerms = null!;
        private TextBox _terms = null!;
        private DateTimePicker _due = null!;
        private ComboBox _status = null!;
        private Panel _lineHost = null!;
        private Label _modeLabel = null!;
        private Label _totalCases = null!;
        private Label _totalVolume = null!;
        private Label _totalAmount = null!;
        private Button _save = null!;
        private Button _another = null!;
        private List<LookupSuggest.Hit> _itemHits = new();
        private readonly HashSet<string> _loadedItems = new(StringComparer.OrdinalIgnoreCase);
        private bool _busyAdding;
        private bool _fillingPo;
        private bool _editing;
        private string _editPo = "";
        private string _editCustomer = "";

        internal static Dictionary<string, string>? PendingEdit { get; set; }
        internal static bool StartNew { get; set; }

        public SalesOrder()
        {
            InitializeComponent();
            BuildUi();
        }

        public static void OpenNew()
        {
            if (!DataAccess.CanMutate(DataFiles.Sales))
            {
                MessageBox.Show("This account can only view sales.", "Sales Order",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = null;
            StartNew = true;
            Navigator.GoTo(AppPage.SalesOrder);
        }

        public static void OpenEdit(Dictionary<string, string> record)
        {
            if (!DataAccess.CanMutate(DataFiles.Sales))
            {
                MessageBox.Show("This account can only view sales.", "Edit Sale",
                    MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            PendingEdit = record;
            StartNew = false;
            Navigator.GoTo(AppPage.SalesOrder);
        }

        public void HighlightCurrentPage()
        {
            RefreshLookups();
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
                ResetDraft(keepCustomer: false);
                return;
            }

            if (_lines.Count == 0)
                AddLine();
        }

        /// <summary>
        /// Start or continue a pick ticket from a sale. Clears the draft if the customer does not match,
        /// then adds every unused sale line on that PO.
        /// </summary>
        internal void BeginFromSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            if (OrderHasCustomer() && !PrefillMatchesCustomer(prefill))
                ResetDraft();
            TryAddSale(prefill, done);
        }

        /// <summary>Build or open the sales-order PDF and return the SO # written on the form (or null).</summary>
        internal string? CreateOrOpenPdf()
        {
            CreateSalesOrder();
            return _soNo.Text.Trim();
        }

        /// <summary>
        /// Add matching sale lines for this PO/SO. Fails if the ticket already has a different customer.
        /// <paramref name="done"/> gets an error string, or null on success.
        /// </summary>
        internal void TryAddSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            if (_lines.Count == 0)
                AddLine();

            if (OrderHasCustomer() && !PrefillMatchesCustomer(prefill))
            {
                done("Customer does not match. Please remove old data or pick a different sale.");
                return;
            }

            if (prefill.So.Length > 0)
                _soNo.Text = prefill.So;

            string key = prefill.Po.Length > 0 ? prefill.Po : prefill.So;
            string code = OrderHasCustomer() ? CurrentCustomerCode() : prefill.CustomerCode;
            string name = OrderHasCustomer() ? CurrentCustomerName() : prefill.CustomerName;
            StartAddItems(key, code, name, done);
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

            RefreshLookups();
            ResetDraft(keepCustomer: false);
        }

        private CardPanel BuildHeader()
        {
            var card = new CardPanel { Height = 268, Padding = new Padding(16, 10, 16, 10) };

            _modeLabel = new Label
            {
                Text = "Sales Order",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 8)
            };
            card.Controls.Add(_modeLabel);

            _soNo = AddField(card, "SO #", 20, 36, 130);
            _customerCode = AddField(card, "CUSTOMER CODE", 164, 36, 160);
            _customer = AddField(card, "CUSTOMER", 338, 36, 250);
            _customerPo = AddField(card, "CUSTOMER PO", 602, 36, 170);

            _terms = AddField(card, "TERMS", 20, 86, 140);
            _orderDate = AddDate(card, "ORDER DATE", 174, 86, 130);
            _releaseDate = AddDate(card, "SHIP DATE", 318, 86, 130);
            _due = AddDate(card, "DUE DATE", 462, 86, 130);
            _status = AddCombo(card, "STATUS", 606, 86, 130);
            _status.DropDownStyle = ComboBoxStyle.DropDownList;
            SelectStatus(_status, "Open");

            _warehouse = AddField(card, "WAREHOUSE", 20, 136, 150);
            _freightCo = AddCombo(card, "FREIGHT CO", 184, 136, 160);
            _freightTerms = AddField(card, "FREIGHT TERMS", 358, 136, 150);
            _contact = AddField(card, "CONTACT", 522, 136, 150);

            _email = AddField(card, "EMAIL", 20, 186, 200);
            _contactPhone = AddField(card, "PHONE", 234, 186, 140);

            _address = AddMultiline(card, "SHIP TO", 388, 186, 360, 48);
            _address.PlaceholderText = "Not found, please input manually";

            _customerCodeSuggest = new LookupSuggest(_customerCode, () => _customerHits, codeFirst: true, ApplyCustomerHit);
            _customerNameSuggest = new LookupSuggest(_customer, () => _customerHits, codeFirst: false, ApplyCustomerHit);

            _customerPo.Leave += (_, _) => RequestPoFill();
            _customerPo.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                RequestPoFill();
            };

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
                Text = "Type a customer code or name to fill the order. Type an item code on a line to fill that product. Add lines for more products on this sales order.",
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
                var slots = SalesOrderLineLayout.Slots(labels.Width);
                DrawHeader(e.Graphics, "ITEM", slots.Item);
                DrawHeader(e.Graphics, "LOT #", slots.Lot);
                DrawHeader(e.Graphics, "DESCRIPTION", slots.Description);
                DrawHeader(e.Graphics, "COO", slots.Coo);
                DrawHeader(e.Graphics, "PACK", slots.UnitSize);
                DrawHeader(e.Graphics, "CS", slots.Cases);
                DrawHeader(e.Graphics, "VOLUME", slots.Volume);
                DrawHeader(e.Graphics, "PRICE / LB", slots.Price);
                DrawHeader(e.Graphics, "AMOUNT", slots.Amount);
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

        private SalesOrderLineRow AddLine()
        {
            var row = new SalesOrderLineRow();
            row.AttachLookups(() => _itemHits);
            row.SetPo(_customerPo.Text.Trim());
            row.Changed += (_, _) => UpdateTotals();
            row.RemoveRequested += (_, _) => RemoveLine(row);
            _lines.Add(row);
            _lineHost.Controls.Add(row);
            LayoutLines();
            row.FocusItem();
            UpdateTotals();
            return row;
        }

        private void RemoveLine(SalesOrderLineRow row)
        {
            _lines.Remove(row);
            _lineHost.Controls.Remove(row);
            row.Dispose();
            if (_lines.Count == 0)
                AddLine();
            LayoutLines();
            RefreshPoSuggestions();
            UpdateTotals();
        }

        private void LayoutLines()
        {
            int y = _lineHost.Padding.Top;
            int width = Math.Max(640, _lineHost.ClientSize.Width - _lineHost.Padding.Horizontal - 8);
            foreach (var row in _lines)
            {
                row.SetBounds(_lineHost.Padding.Left, y, width, SalesOrderLineRow.RowHeight);
                y += SalesOrderLineRow.RowHeight + 6;
            }

            _lineHost.AutoScrollMinSize = new Size(0, y + 8);
        }

        private SalesOrderLineRow GetOpenLine()
        {
            if (_lines.Count > 0 && !_lines[^1].HasContent())
                return _lines[^1];
            return AddLine();
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

        private CardPanel BuildFooter()
        {
            var card = new CardPanel { Height = 92, Padding = new Padding(16, 10, 16, 10) };

            _save = new Button
            {
                Text = "Save Sales Order",
                Size = new Size(168, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleGoldButton(_save);
            _save.Click += (_, _) => CreateSalesOrder();

            _another = new Button
            {
                Text = "Add Another",
                Size = new Size(130, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleNavyButton(_another);
            _another.Click += (_, _) => CreateSalesOrder(keepCustomer: true);

            var clear = new Button
            {
                Text = "Clear",
                Size = new Size(90, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetDraft(keepCustomer: false);

            _totalCases = TotalLabel(card, "TOTAL CASES", 20, 16);
            _totalVolume = TotalLabel(card, "TOTAL VOLUME", 140, 16);
            _totalAmount = TotalLabel(card, "TOTAL", 280, 16);
            _totalAmount.Font = Theme.SectionTitle;
            _totalAmount.ForeColor = Theme.Navy;

            card.Controls.Add(_save);
            card.Controls.Add(_another);
            card.Controls.Add(clear);
            card.Resize += (_, _) =>
            {
                _save.Location = new Point(Math.Max(400, card.Width - 192), 28);
                _another.Location = new Point(Math.Max(260, card.Width - 332), 28);
                clear.Location = new Point(Math.Max(160, card.Width - 432), 28);
            };

            return card;
        }

        private void RefreshLookups()
        {
            _customerRecords.Clear();
            _customerHits = new List<LookupSuggest.Hit>();
            foreach (var record in DataFiles.VisibleRecords(DataFiles.Customers))
            {
                var choice = new CustomerChoice(
                    DataFiles.GetRecord(record, "Code"),
                    DataFiles.GetRecord(record, "Name"),
                    DataFiles.GetRecord(record, "Terms"),
                    DataFiles.GetRecord(record, "Contact Name"),
                    DataFiles.GetRecord(record, "Address"),
                    DataFiles.GetRecord(record, "Email"),
                    DataFiles.GetRecord(record, "Phone"));
                if (choice.Code.Length == 0 && choice.Name.Length == 0)
                    continue;
                _customerRecords.Add(choice);
                _customerHits.Add(new LookupSuggest.Hit(choice.Code, choice.Name, choice.Terms));
            }

            _itemHits = new List<LookupSuggest.Hit>();
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
            RefreshPoSuggestions();
        }

        private void RefreshPoSuggestions()
        {
            _poSource = DataFiles.InvoicePoSuggestions(
                CurrentCustomerCode(),
                CurrentCustomerName());
            _customerPo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _customerPo.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _customerPo.AutoCompleteCustomSource = _poSource;
        }

        private string CurrentCustomerCode() => _customerCode.Text.Trim();

        private string CurrentCustomerName() => _customer.Text.Trim();

        private bool OrderHasCustomer()
        {
            return CurrentCustomerCode().Length > 0 ||
                   CurrentCustomerName().Length > 0 ||
                   _lines.Any(row => row.HasContent());
        }

        private void ApplyCustomerHit(LookupSuggest.Hit hit)
        {
            CustomerChoice? match = null;
            foreach (var record in _customerRecords)
            {
                if (record.Code.Equals(hit.Code, StringComparison.OrdinalIgnoreCase) ||
                    (hit.Name.Length > 0 && record.Name.Equals(hit.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    match = record;
                    break;
                }
            }

            if (match != null)
                FillCustomer(match, match.Terms, overwrite: true);
            else
                FillCustomer(hit.Code, hit.Name, hit.Extra, "", "", "", "", overwrite: true);
        }

        private bool PrefillMatchesCustomer(InvoiceSalePrefill prefill)
        {
            string code = CurrentCustomerCode();
            string name = CurrentCustomerName();
            if (prefill.CustomerCode.Length > 0 && code.Length > 0)
                return prefill.CustomerCode.Equals(code, StringComparison.OrdinalIgnoreCase);
            if (prefill.CustomerName.Length > 0 && name.Length > 0)
                return prefill.CustomerName.Equals(name, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private Dictionary<string, int> UsedLineCounts()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in _lines.Select(row => row.GetLine()))
            {
                string key = LineIdentity(line);
                if (key.Length == 0)
                    continue;
                used[key] = used.GetValueOrDefault(key) + 1;
            }

            return used;
        }

        private static string LineIdentity(SalesOrderLine line)
        {
            return LineIdentity(line.PoNumber, line.ItemCode, line.LotNumber, line.Cases, line.Volume);
        }

        private static string LineIdentity(Dictionary<string, string> record)
        {
            return LineIdentity(
                DataFiles.SalePo(record),
                DataFiles.GetRecord(record, "Item Code"),
                DataFiles.SaleLot(record),
                DataFiles.GetRecord(record, "CS"),
                DataFiles.GetRecord(record, "Volume"));
        }

        private static string LineIdentity(string? po, string? item, string? lot, string? cases, string? volume)
        {
            string poKey = DataFiles.NormalizePo(po);
            string itemKey = (item ?? "").Trim().ToUpperInvariant();
            if (poKey.Length == 0 && itemKey.Length == 0)
                return "";

            return string.Join("|",
                poKey,
                itemKey,
                (lot ?? "").Trim().ToUpperInvariant(),
                (cases ?? "").Trim(),
                (volume ?? "").Trim());
        }

        private void RequestPoFill()
        {
            if (_fillingPo)
                return;

            string po = _customerPo.Text.Trim();
            if (po.Length == 0)
                return;

            StartAddItems(
                po,
                CurrentCustomerCode(),
                CurrentCustomerName(),
                error =>
                {
                    if (error != null)
                        ToastAlert.Error(this, error);
                    else
                        ToastAlert.Success(this, "The information was added.");
                });
        }

        /// <summary>
        /// Look up sale rows for this PO/SO and append each unused line onto the pick ticket,
        /// filling customer and ship-to from the first match.
        /// </summary>
        private void StartAddItems(string? key, string customerCode, string customerName, Action<string?> done)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                done("This sale has no PO or SO number.");
                return;
            }

            if (_busyAdding)
            {
                done("Still adding lines to the sales order.");
                return;
            }

            _busyAdding = true;
            var used = UsedLineCounts();
            string currentSo = _soNo.Text.Trim();

            Task.Run(() =>
            {
                try
                {
                    var sources = DataFiles.FindSalesOrderSourcesForKey(key, customerCode, customerName);
                    if (string.IsNullOrWhiteSpace(customerCode) &&
                        string.IsNullOrWhiteSpace(customerName) &&
                        sources.Count > 0)
                    {
                        string firstCode = DataFiles.GetRecord(sources[0], "Customer Code");
                        string firstName = DataFiles.GetRecord(sources[0], "Customer");
                        sources = sources
                            .Where(record => DataFiles.MatchesCustomer(record, firstCode, firstName))
                            .ToList();
                    }

                    var available = sources
                        .Where(record =>
                        {
                            string existingSo = DataFiles.GetRecord(record, "SO #").Trim();
                            return existingSo.Length == 0 ||
                                   existingSo.Equals(currentSo, StringComparison.OrdinalIgnoreCase);
                        })
                        .ToList();

                    var remaining = new List<Dictionary<string, string>>();
                    foreach (var record in available)
                    {
                        string identity = LineIdentity(record);
                        if (identity.Length > 0 &&
                            used.TryGetValue(identity, out int count) &&
                            count > 0)
                        {
                            used[identity] = count - 1;
                            continue;
                        }

                        remaining.Add(record);
                    }

                    string existingSo = sources
                        .Select(record => DataFiles.GetRecord(record, "SO #").Trim())
                        .FirstOrDefault(value => value.Length > 0) ?? "";

                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed)
                        {
                            _busyAdding = false;
                            done("Could not add that sale.");
                            return;
                        }

                        if (sources.Count == 0)
                        {
                            _busyAdding = false;
                            done("No sales were found for that PO.");
                            return;
                        }

                        if (available.Count == 0)
                        {
                            _busyAdding = false;
                            done(existingSo.Length > 0
                                ? $"This PO already has sales order {existingSo}."
                                : "No open sales were found for that PO.");
                            return;
                        }

                        if (remaining.Count == 0)
                        {
                            _busyAdding = false;
                            done("This PO is already on the sales order.");
                            return;
                        }

                        AddRecordsInBatches(remaining, 0, done);
                    }));
                }
                catch (Exception ex)
                {
                    BeginInvoke(new Action(() =>
                    {
                        _busyAdding = false;
                        done(ex.Message);
                    }));
                }
            });
        }

        private void AddRecordsInBatches(
            List<Dictionary<string, string>> sources,
            int index,
            Action<string?> done)
        {
            const int batchSize = 25;
            _lineHost.SuspendLayout();
            try
            {
                int end = Math.Min(index + batchSize, sources.Count);
                for (int i = index; i < end; i++)
                {
                    var row = GetOpenLine();
                    row.FillFromRecord(sources[i]);
                    if (i == 0)
                        ApplySaleHeader(sources[i]);
                }

                index = end;
            }
            finally
            {
                _lineHost.ResumeLayout(true);
            }

            LayoutLines();
            UpdateTotals();

            if (index < sources.Count)
            {
                BeginInvoke(new Action(() => AddRecordsInBatches(sources, index, done)));
                return;
            }

            if (_lines.Count == 0 || _lines[^1].HasContent())
                AddLine();
            RefreshPoSuggestions();
            _busyAdding = false;
            done(null);
        }

        private void ResetDraft(bool keepCustomer = false)
        {
            string customerName = keepCustomer ? _customer.Text : "";
            string customerCode = keepCustomer ? _customerCode.Text : "";
            string terms = keepCustomer ? _terms.Text : "";
            string po = keepCustomer ? _customerPo.Text : "";

            ClearLines();
            _loadedItems.Clear();
            _editing = false;
            _editPo = "";
            _editCustomer = "";
            _customer.Text = keepCustomer ? customerName : "";
            _customerCode.Text = keepCustomer ? customerCode : "";
            _address.Text = keepCustomer ? _address.Text : "";
            _contact.Text = keepCustomer ? _contact.Text : "";
            _email.Text = keepCustomer ? _email.Text : "";
            _contactPhone.Text = keepCustomer ? _contactPhone.Text : "";
            if (!keepCustomer)
            {
                _address.Text = "";
                _contact.Text = "";
                _email.Text = "";
                _contactPhone.Text = "";
                _warehouse.Text = "";
                VendorChoice.Select(_freightCo, "");
                _freightTerms.Text = "";
            }

            _customerPo.Text = po;
            _terms.Text = terms;
            RefreshLookups();
            _soNo.Text = DataFiles.NextSalesOrderNumber();
            _orderDate.Value = DateTime.Today;
            _releaseDate.Value = DateTime.Today;
            _due.Value = DateTime.Today;
            SelectStatus(_status, "Open");
            SetMode(false);
            AddLine();
            UpdateTotals();
        }

        private void ApplySaleHeader(Dictionary<string, string> record)
        {
            string code = DataFiles.GetRecordAny(record, "Customer Code", "Cust ID");
            string name = DataFiles.GetRecordAny(record, "Customer", "Customer Name");
            string terms = DataFiles.GetRecordAny(record, "Customer Terms");
            string so = DataFiles.GetRecordAny(record, "SO #", "SO NO", "SO Number");
            string po = DataFiles.SalePo(record);

            CustomerChoice? match = null;
            foreach (var choice in _customerRecords)
            {
                bool byCode = code.Length > 0 && choice.Code.Equals(code, StringComparison.OrdinalIgnoreCase);
                bool byName = name.Length > 0 && choice.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
                if (!byCode && !byName)
                    continue;
                match = choice;
                break;
            }

            if (match != null)
                FillCustomer(match, terms, overwrite: false);
            else
                FillCustomer(code, name, terms, "", "", "", "", overwrite: false);

            if (po.Length > 0 && _customerPo.Text.Trim().Length == 0)
            {
                _fillingPo = true;
                _customerPo.Text = po;
                _fillingPo = false;
            }

            if (so.Length > 0 && _soNo.Text.Trim().Length == 0)
                _soNo.Text = so;

            string ship = DataFiles.GetRecordAny(record, "Ship Date");
            if (DateTime.TryParse(ship, out var shipDate))
                _releaseDate.Value = shipDate;

            if (_warehouse.Text.Trim().Length == 0)
                _warehouse.Text = DataFiles.GetRecordAny(record, "Location");

            if (VendorChoice.TextOf(_freightCo).Length == 0)
            {
                string freight = DataFiles.GetRecordAny(
                    record,
                    DataFiles.FreightCompanyColumn,
                    "Forwarder",
                    "Logistics");
                VendorChoice.Select(_freightCo, freight);
            }

            if (_freightTerms.Text.Trim().Length == 0 && terms.Length > 0)
                _freightTerms.Text = terms;
        }

        private void FillCustomer(CustomerChoice choice, string terms, bool overwrite)
        {
            FillCustomer(
                choice.Code,
                choice.Name,
                terms.Length > 0 ? terms : choice.Terms,
                choice.Contact,
                choice.Address,
                choice.Email,
                choice.Phone,
                overwrite);
        }

        private void FillCustomer(
            string code,
            string name,
            string terms,
            string contact,
            string address,
            string email,
            string phone,
            bool overwrite)
        {
            void Put(TextBox box, string value)
            {
                if (value.Length == 0)
                    return;
                if (overwrite || box.Text.Trim().Length == 0)
                    box.Text = value;
            }

            if (code.Length > 0)
                _customerCode.Text = code;
            Put(_contact, contact);
            if (overwrite)
                _address.Text = address;
            else
                Put(_address, address);
            Put(_email, email);
            Put(_contactPhone, phone);
            if (name.Length > 0 && (overwrite || _customer.Text.Trim().Length == 0))
                _customer.Text = name;
            if (terms.Length > 0 && (overwrite || _terms.Text.Trim().Length == 0))
                _terms.Text = terms;
            if (terms.Length > 0 && (overwrite || _freightTerms.Text.Trim().Length == 0))
                _freightTerms.Text = terms;
            RefreshPoSuggestions();
        }

        private SalesOrderDraft CollectDraft()
        {
            return new SalesOrderDraft
            {
                SoNumber = _soNo.Text.Trim(),
                OrderDate = _orderDate.Value.Date,
                ReleaseDate = _releaseDate.Value.Date,
                CustomerCode = _customerCode.Text.Trim(),
                CustomerName = _customer.Text.Trim(),
                Address = _address.Text.Trim(),
                CustomerPhone = _contactPhone.Text.Trim(),
                Contact = _contact.Text.Trim(),
                Email = _email.Text.Trim(),
                ContactPhone = _contactPhone.Text.Trim(),
                Warehouse = _warehouse.Text.Trim(),
                CustomerPo = _customerPo.Text.Trim(),
                Terms = _terms.Text.Trim(),
                Status = _status.Text.Trim(),
                DueDate = _due.Value.Date,
                FreightCompany = VendorChoice.TextOf(_freightCo),
                FreightTerms = _freightTerms.Text.Trim(),
                Lines = _lines.Select(row =>
                {
                    var line = row.GetLine();
                    if (line.PoNumber.Length == 0)
                        line.PoNumber = _customerPo.Text.Trim();
                    return line;
                }).ToList()
            };
        }

        private void UpdateTotals()
        {
            decimal cases = 0;
            decimal volume = 0;
            decimal amount = 0;
            foreach (var row in _lines)
            {
                var line = row.GetLine();
                cases += SalesOrderLineRow.ParseNumber(line.Cases);
                volume += SalesOrderLineRow.ParseNumber(line.Volume);
                amount += SalesOrderLineRow.ParseNumber(line.Amount);
            }

            _totalCases.Text = cases.ToString("0.###", CultureInfo.InvariantCulture);
            _totalVolume.Text = volume.ToString("0.###", CultureInfo.InvariantCulture);
            _totalAmount.Text = amount.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// If a PDF already exists for this SO #, open it. Otherwise draw a new PDF, store it,
        /// write the SO # onto matching sales, and open the viewer.
        /// </summary>
        private void CreateSalesOrder(bool keepCustomer = false)
        {
            if (!AppLock.HasFolder())
            {
                ToastAlert.Error(this, "Select a data folder in Settings first.");
                return;
            }

            var draft = CollectDraft();
            if (string.IsNullOrWhiteSpace(draft.CustomerPo))
            {
                ToastAlert.Error(this, "Enter a customer PO #.");
                return;
            }

            if (string.IsNullOrWhiteSpace(draft.SoNumber))
            {
                ToastAlert.Error(this, "Enter a sales order number.");
                return;
            }

            if (string.IsNullOrWhiteSpace(draft.CustomerName) && string.IsNullOrWhiteSpace(draft.CustomerCode))
            {
                ToastAlert.Error(this, "Pick a customer.");
                return;
            }

            var lines = draft.Lines
                .Where(line => line.ItemCode.Length > 0 || line.Description.Length > 0)
                .ToList();
            if (lines.Count == 0)
            {
                ToastAlert.Error(this, "Add at least one product line.");
                return;
            }

            string po = draft.CustomerPo.Trim();
            foreach (var line in lines)
            {
                if (line.PoNumber.Length == 0)
                    line.PoNumber = po;
            }

            var savedItems = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            MutateResult? last = null;
            try
            {
                foreach (var line in lines)
                {
                    var values = BuildSaleValues(draft, line);
                    string item = line.ItemCode;
                    bool exists = _editing &&
                                  _loadedItems.Contains(item) &&
                                  po.Equals(_editPo, StringComparison.OrdinalIgnoreCase);
                    last = exists
                        ? DataFiles.MutateUpdate(
                            DataFiles.Sales,
                            record =>
                                DataFiles.NormalizePo(DataFiles.SalePo(record)) ==
                                DataFiles.NormalizePo(_editPo) &&
                                DataFiles.GetRecord(record, "Item Code").Trim()
                                    .Equals(item, StringComparison.OrdinalIgnoreCase) &&
                                DataFiles.GetRecord(record, "Customer Code").Trim()
                                    .Equals(_editCustomer, StringComparison.OrdinalIgnoreCase),
                            values)
                        : DataFiles.MutateInsert(DataFiles.Sales, values);
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
                            ["Item Code"] = oldItem,
                            ["Customer Code"] = _editCustomer
                        };
                        last = DataFiles.MutateDelete(DataFiles.Sales, doomed);
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
                SaleDocument.SaveFromPo(po);
            }
            catch
            {
                // keep the saved rows even if the sale PDF cannot be written
            }

            try
            {
                var pos = lines.Select(line => line.PoNumber).Where(p => p.Length > 0).ToList();
                pos.Add(po);
                string soNumber = draft.SoNumber;
                string? existingSo = DataFiles.FindExistingSalesOrderNumber(
                    pos,
                    draft.CustomerCode,
                    draft.CustomerName);
                if (!string.IsNullOrWhiteSpace(existingSo) && !_editing)
                    soNumber = existingSo;
                draft.SoNumber = soNumber;
                _soNo.Text = soNumber;
                string pdfPath = SalesOrderDocument.Save(draft);
                DataFiles.AssignSalesOrderNumber(
                    pos,
                    draft.CustomerCode,
                    draft.CustomerName,
                    soNumber,
                    draft.FreightCompany);
                DataFiles.OpenPdf(pdfPath, DataFiles.PdfKindSalesOrder, soNumber);
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            ToastAlert.Success(this, last is { Queued: true }
                ? last.Value.Message
                : _editing ? "The sales order was updated." : "The sales order was saved.");

            if (keepCustomer)
            {
                ResetDraft(keepCustomer: true);
                return;
            }

            if (_editing)
            {
                _editPo = po;
                _editCustomer = draft.CustomerCode;
                _loadedItems.Clear();
                foreach (string item in savedItems)
                    _loadedItems.Add(item);
                return;
            }

            ResetDraft(keepCustomer: false);
        }

        private Dictionary<string, string> BuildSaleValues(SalesOrderDraft draft, SalesOrderLine line)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["PO #"] = line.PoNumber.Length > 0 ? line.PoNumber : draft.CustomerPo,
                ["SO #"] = draft.SoNumber,
                ["Customer Code"] = draft.CustomerCode,
                ["Customer"] = draft.CustomerName,
                ["Customer Terms"] = draft.Terms,
                ["Item Code"] = line.ItemCode,
                ["Lot #"] = line.LotNumber,
                ["Description"] = line.Description,
                ["COO"] = line.Coo,
                ["Pack Size"] = line.UnitSize,
                ["CS"] = line.Cases,
                ["Volume"] = line.Volume,
                ["Sell Price / LB"] = line.Price,
                ["Amount"] = line.Amount,
                ["Ship Date"] = CsvIO.Date(draft.ReleaseDate),
                ["Due Date"] = CsvIO.Date(draft.DueDate),
                ["Status"] = draft.Status,
                [DataFiles.FreightCompanyColumn] = draft.FreightCompany
            };
        }

        private void LoadOrder(Dictionary<string, string> record)
        {
            string po = DataFiles.SalePo(record);
            var rows = DataFiles.FindSalesByPo(po);
            if (rows.Count == 0)
                rows.Add(record);

            ClearLines();
            _loadedItems.Clear();
            _editPo = po;
            _editCustomer = DataFiles.GetRecord(rows[0], "Customer Code");
            ApplySaleHeader(rows[0]);
            _customerPo.Text = po;
            string so = DataFiles.GetRecord(rows[0], "SO #");
            if (so.Length > 0)
                _soNo.Text = so;
            string due = DataFiles.GetRecord(rows[0], "Due Date");
            if (DateTime.TryParse(due, out var dueDate))
                _due.Value = dueDate;
            SelectStatus(_status, DataFiles.GetRecord(rows[0], "Status"));
            _terms.Text = DataFiles.GetRecord(rows[0], "Customer Terms");

            foreach (var row in rows)
            {
                AddLine();
                var line = _lines[^1];
                line.FillFromRecord(row);
                string item = DataFiles.GetRecord(row, "Item Code").Trim();
                if (item.Length > 0)
                    _loadedItems.Add(item);
            }

            SetMode(true);
            UpdateTotals();
        }

        private void SetMode(bool editing)
        {
            _editing = editing;
            if (_modeLabel != null)
                _modeLabel.Text = editing ? "Edit Sales Order" : "Sales Order";
            if (_save != null)
                _save.Text = editing ? "Save Changes" : "Save Sales Order";
            if (_another != null)
                _another.Visible = !editing;
        }

        private static void SelectStatus(ComboBox box, string? value)
        {
            if (box.Items.Count == 0)
                box.Items.AddRange(new object[] { "Open", "Pending", "Shipped", "Invoiced", "Paid", "Complete" });

            string pick = (value ?? "").Trim();
            if (pick.Length == 0)
                pick = "Open";
            box.SelectedItem = pick;
            if (box.SelectedIndex < 0)
            {
                box.Items.Add(pick);
                box.SelectedItem = pick;
            }
        }

        private ComboBox AddCombo(Control parent, string caption, int x, int y, int width)
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

        private TextBox AddField(Control parent, string caption, int x, int y, int width)
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

        private DateTimePicker AddDate(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var box = new DateTimePicker
            {
                Format = DateTimePickerFormat.Short,
                Location = new Point(x, y + 16),
                Size = new Size(width, 26),
                Font = Theme.Body
            };
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }

        private TextBox AddMultiline(Control parent, string caption, int x, int y, int width, int height)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.None,
                Location = new Point(x, y + 16),
                Size = new Size(width, height)
            };
            Theme.StyleField(box);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }

        private static Label TotalLabel(Control parent, string caption, int x, int y)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var value = new Label
            {
                Text = "0",
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(x, y + 16)
            };
            parent.Controls.Add(label);
            parent.Controls.Add(value);
            return value;
        }

        private sealed class CustomerChoice
        {
            public string Code { get; }
            public string Name { get; }
            public string Terms { get; }
            public string Contact { get; }
            public string Address { get; }
            public string Email { get; }
            public string Phone { get; }

            public CustomerChoice(
                string code,
                string name,
                string terms,
                string contact,
                string address,
                string email,
                string phone)
            {
                Code = code;
                Name = name;
                Terms = terms;
                Contact = contact;
                Address = address;
                Email = email;
                Phone = phone;
            }

            public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Code : $"{Name} ({Code})";
        }
    }
}
