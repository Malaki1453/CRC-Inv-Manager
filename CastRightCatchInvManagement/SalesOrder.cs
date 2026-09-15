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

        /// <summary>Sales row queued by OpenEdit until this page is shown.</summary>
        internal static Dictionary<string, string>? PendingEdit { get; set; }
        /// <summary>True when OpenNew should wipe the last draft on the next show.</summary>
        internal static bool StartNew { get; set; }

        /// <summary>Build the sales-order page and start on a blank draft.</summary>
        public SalesOrder()
        {
            InitializeComponent();
            BuildUi();
        }

        /// <summary>Open this page as a blank sales order. Assigns the next SO #.</summary>
        public static void OpenNew()
        {
            // View-only accounts cannot insert sales rows.
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

        /// <summary>Open this page with every product on that customer PO loaded for edit.</summary>
        public static void OpenEdit(Dictionary<string, string> record)
        {
            // View-only accounts cannot update sales rows.
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

        /// <summary>Apply a queued new/edit request, or keep at least one blank product line.</summary>
        public void HighlightCurrentPage()
        {
            RefreshLookups();
            // OpenEdit queued a sales row to load.
            if (PendingEdit != null)
            {
                var record = PendingEdit;
                PendingEdit = null;
                StartNew = false;
                LoadOrder(record);
                return;
            }

            // OpenNew asked for a fresh draft instead of the last ticket.
            if (StartNew)
            {
                StartNew = false;
                ResetDraft(keepCustomer: false);
                return;
            }

            // Always keep one empty product line to type into.
            if (_lines.Count == 0)
                AddLine();
        }

        /// <summary>
        /// Start or continue a pick ticket from a sale. Clears the draft if the customer does not match,
        /// then adds every unused sale line on that PO.
        /// </summary>
        internal void BeginFromSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            // A different customer cannot share this pick ticket.
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
            // Always keep one empty product line to type into.
            if (_lines.Count == 0)
                AddLine();

            // Refuse to mix two customers on one sales order.
            if (OrderHasCustomer() && !PrefillMatchesCustomer(prefill))
            {
                done("Customer does not match. Please remove old data or pick a different sale.");
                return;
            }

            // Keep an existing SO # from the sale when the ticket is still blank.
            if (prefill.So.Length > 0)
                _soNo.Text = prefill.So;

            string key = prefill.Po.Length > 0 ? prefill.Po : prefill.So;
            string code = OrderHasCustomer() ? CurrentCustomerCode() : prefill.CustomerCode;
            string name = OrderHasCustomer() ? CurrentCustomerName() : prefill.CustomerName;
            StartAddItems(key, code, name, done);
        }

        /// <summary>Assemble header, product lines, and footer cards for this page.</summary>
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

        /// <summary>Sales-order header: customer, dates, freight, and ship-to.</summary>
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
                // Enter on Customer PO should fill lines, not beep.
                if (e.KeyCode != Keys.Enter)
                    return;
                e.SuppressKeyPress = true;
                RequestPoFill();
            };

            return card;
        }

        /// <summary>Scrollable product table with column headers and an add-line bar.</summary>
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

        /// <summary>Append a blank product line and give it focus.</summary>
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

        /// <summary>Drop a product line, keeping at least one empty row so the ticket can still be typed.</summary>
        private void RemoveLine(SalesOrderLineRow row)
        {
            _lines.Remove(row);
            _lineHost.Controls.Remove(row);
            row.Dispose();
            // A sales order with zero lines cannot be saved; leave a blank row instead.
            if (_lines.Count == 0)
                AddLine();
            LayoutLines();
            RefreshPoSuggestions();
            UpdateTotals();
        }

        /// <summary>Stack product rows in the host and size the scrollbar to the last line.</summary>
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

        /// <summary>Reuse the last empty row, or add a new one, before filling from a sale.</summary>
        private SalesOrderLineRow GetOpenLine()
        {
            // Prefer the trailing blank row over stacking empty lines.
            if (_lines.Count > 0 && !_lines[^1].HasContent())
                return _lines[^1];
            return AddLine();
        }

        /// <summary>Dispose every product row before loading or resetting the draft.</summary>
        private void ClearLines()
        {
            foreach (var row in _lines.ToList())
            {
                _lineHost.Controls.Remove(row);
                row.Dispose();
            }

            _lines.Clear();
        }

        /// <summary>Paint a navy column caption into a line-header slot.</summary>
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

        /// <summary>Totals plus Save, Add Another, and Clear.</summary>
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

        /// <summary>Reload customer, item, freight, and PO suggestions from live tables.</summary>
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
                // Skip customers that have neither a code nor a name to type.
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
                // Description is optional in item codes; species is the fallback label.
                if (description.Length == 0)
                    description = species;
                // An item with no code and no name cannot be looked up.
                if (code.Length == 0 && description.Length == 0)
                    continue;
                _itemHits.Add(new LookupSuggest.Hit(
                    code,
                    description,
                    DataFiles.GetRecord(record, "COO"),
                    species));
            }

            // Freight combo is created with the header; skip if BuildUi has not run yet.
            if (_freightCo != null)
                VendorChoice.Fill(_freightCo);
            foreach (var row in _lines)
                row.AttachLookups(() => _itemHits);
            RefreshPoSuggestions();
        }

        /// <summary>Limit Customer PO autocomplete to this customer’s unused sale POs.</summary>
        private void RefreshPoSuggestions()
        {
            _poSource = DataFiles.InvoicePoSuggestions(
                CurrentCustomerCode(),
                CurrentCustomerName());
            _customerPo.AutoCompleteMode = AutoCompleteMode.SuggestAppend;
            _customerPo.AutoCompleteSource = AutoCompleteSource.CustomSource;
            _customerPo.AutoCompleteCustomSource = _poSource;
        }

        /// <summary>Customer code currently on the form.</summary>
        private string CurrentCustomerCode() => _customerCode.Text.Trim();

        /// <summary>Customer name currently on the form.</summary>
        private string CurrentCustomerName() => _customer.Text.Trim();

        /// <summary>True when the draft already has a customer or product lines.</summary>
        private bool OrderHasCustomer()
        {
            return CurrentCustomerCode().Length > 0 ||
                   CurrentCustomerName().Length > 0 ||
                   _lines.Any(row => row.HasContent());
        }

        /// <summary>Fill customer fields from a lookup pick, using the customer record when found.</summary>
        private void ApplyCustomerHit(LookupSuggest.Hit hit)
        {
            CustomerChoice? match = null;
            foreach (var record in _customerRecords)
            {
                // Match by code first, then by name when the pick has one.
                if (record.Code.Equals(hit.Code, StringComparison.OrdinalIgnoreCase) ||
                    (hit.Name.Length > 0 && record.Name.Equals(hit.Name, StringComparison.OrdinalIgnoreCase)))
                {
                    match = record;
                    break;
                }
            }

            // Known customers also fill contact, address, email, and phone.
            if (match != null)
                FillCustomer(match, match.Terms, overwrite: true);
            // Unknown picks still fill code/name/terms from the suggestion itself.
            else
                FillCustomer(hit.Code, hit.Name, hit.Extra, "", "", "", "", overwrite: true);
        }

        /// <summary>True when the incoming sale is the same customer as this draft, or the draft has none.</summary>
        private bool PrefillMatchesCustomer(InvoiceSalePrefill prefill)
        {
            string code = CurrentCustomerCode();
            string name = CurrentCustomerName();
            // Codes are unique; prefer them over names.
            if (prefill.CustomerCode.Length > 0 && code.Length > 0)
                return prefill.CustomerCode.Equals(code, StringComparison.OrdinalIgnoreCase);
            // Fall back to name when either side has no customer code.
            if (prefill.CustomerName.Length > 0 && name.Length > 0)
                return prefill.CustomerName.Equals(name, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        /// <summary>Count already-added lines by PO/item/lot/qty so the same sale is not appended twice.</summary>
        private Dictionary<string, int> UsedLineCounts()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in _lines.Select(row => row.GetLine()))
            {
                string key = LineIdentity(line);
                // Empty identity is an unfinished row, not a used sale line.
                if (key.Length == 0)
                    continue;
                used[key] = used.GetValueOrDefault(key) + 1;
            }

            return used;
        }

        /// <summary>Identity key for a draft line, used to skip duplicates while adding sales.</summary>
        private static string LineIdentity(SalesOrderLine line)
        {
            return LineIdentity(line.PoNumber, line.ItemCode, line.LotNumber, line.Cases, line.Volume);
        }

        /// <summary>Identity key for a sales-table row, matching <see cref="LineIdentity(SalesOrderLine)"/>.</summary>
        private static string LineIdentity(Dictionary<string, string> record)
        {
            return LineIdentity(
                DataFiles.SalePo(record),
                DataFiles.GetRecord(record, "Item Code"),
                DataFiles.SaleLot(record),
                DataFiles.GetRecord(record, "CS"),
                DataFiles.GetRecord(record, "Volume"));
        }

        /// <summary>Build a PO|item|lot|cases|volume key, or empty when both PO and item are blank.</summary>
        private static string LineIdentity(string? po, string? item, string? lot, string? cases, string? volume)
        {
            string poKey = DataFiles.NormalizePo(po);
            string itemKey = (item ?? "").Trim().ToUpperInvariant();
            // A line with neither PO nor item cannot be matched later.
            if (poKey.Length == 0 && itemKey.Length == 0)
                return "";

            return string.Join("|",
                poKey,
                itemKey,
                (lot ?? "").Trim().ToUpperInvariant(),
                (cases ?? "").Trim(),
                (volume ?? "").Trim());
        }

        /// <summary>When Customer PO leaves focus, append unused sale lines for that PO.</summary>
        private void RequestPoFill()
        {
            // ApplySaleHeader writes the PO without wanting a recursive lookup.
            if (_fillingPo)
                return;

            string po = _customerPo.Text.Trim();
            // An empty Customer PO box is not a lookup request.
            if (po.Length == 0)
                return;

            StartAddItems(
                po,
                CurrentCustomerCode(),
                CurrentCustomerName(),
                error =>
                {
                    // Null means existing sale lines were appended. Empty means a new PO with no sales yet.
                    if (error == null)
                        ToastAlert.Success(this, "The information was added.");
                    // Non-empty errors (wrong customer, missing PO) need a toast; blank is a typed new PO.
                    else if (error.Length > 0)
                        ToastAlert.Error(this, error);
                },
                missingOk: true);
        }

        /// <summary>
        /// Look up sale rows for this PO/SO and append each unused line onto the pick ticket,
        /// filling customer and ship-to from the first match.
        /// </summary>
        private void StartAddItems(
            string? key,
            string customerCode,
            string customerName,
            Action<string?> done,
            bool missingOk = false)
        {
            // Lookup is keyed by customer PO or SO #.
            if (string.IsNullOrWhiteSpace(key))
            {
                done("This sale has no PO or SO number.");
                return;
            }

            // A second add would race the first background lookup.
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
                    // No customer was chosen yet: lock onto the first matching sale's customer.
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
                        // Already on the ticket: consume one copy so duplicate sale rows can still add extras.
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
                        // The form may have closed while the lookup ran.
                        if (IsDisposed)
                        {
                            _busyAdding = false;
                            done("Could not add that sale.");
                            return;
                        }

                        // No matching sale rows for that customer PO.
                        if (sources.Count == 0)
                        {
                            _busyAdding = false;
                            // A new sales order types a customer PO that does not exist yet.
                            done(missingOk ? "" : "No sales were found for that PO.");
                            return;
                        }

                        // Every matching sale already belongs to a different SO.
                        if (available.Count == 0)
                        {
                            _busyAdding = false;
                            done(existingSo.Length > 0
                                ? $"This PO already has sales order {existingSo}."
                                : "No open sales were found for that PO.");
                            return;
                        }

                        // Every matching sale line is already on this ticket.
                        if (remaining.Count == 0)
                        {
                            _busyAdding = false;
                            done("This PO is already on the sales order.");
                            return;
                        }

                        AddRecordsInBatches(remaining, 0, done);
                    }));
                }
                // Store lookup errors must surface on the UI thread.
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

        /// <summary>Fill product rows in batches so a large PO does not freeze the form.</summary>
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
                    // Header (customer, ship-to, SO) comes from the first remaining sale.
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

            // Yield so the UI can paint before the next batch.
            if (index < sources.Count)
            {
                BeginInvoke(new Action(() => AddRecordsInBatches(sources, index, done)));
                return;
            }

            // Leave a blank row after the last filled line.
            if (_lines.Count == 0 || _lines[^1].HasContent())
                AddLine();
            RefreshPoSuggestions();
            _busyAdding = false;
            done(null);
        }

        /// <summary>Clear the draft, optionally keeping the customer for Add Another.</summary>
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
            // A full clear also drops ship-to, contact, and freight.
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

        /// <summary>Fill customer, PO, SO, ship date, and freight from the first matching sale.</summary>
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
                // Skip customers that match neither the sale's code nor name.
                if (!byCode && !byName)
                    continue;
                match = choice;
                break;
            }

            // Known customers also fill contact and address; unknown sales still get code/name.
            if (match != null)
                FillCustomer(match, terms, overwrite: false);
            // Unknown sales still get code/name from the sale row itself.
            else
                FillCustomer(code, name, terms, "", "", "", "", overwrite: false);

            // Do not overwrite a PO the user already typed.
            if (po.Length > 0 && _customerPo.Text.Trim().Length == 0)
            {
                _fillingPo = true;
                _customerPo.Text = po;
                _fillingPo = false;
            }

            // Keep a generated SO # when the sale has not been assigned one yet.
            if (so.Length > 0 && _soNo.Text.Trim().Length == 0)
                _soNo.Text = so;

            string ship = DataFiles.GetRecordAny(record, "Ship Date");
            // Only overwrite the picker when the sale actually has a ship date.
            if (DateTime.TryParse(ship, out var shipDate))
                _releaseDate.Value = shipDate;

            // Sale location becomes warehouse when the ticket has none.
            if (_warehouse.Text.Trim().Length == 0)
                _warehouse.Text = DataFiles.GetRecordAny(record, "Location");

            // Keep a freight company already chosen on this ticket.
            if (VendorChoice.TextOf(_freightCo).Length == 0)
            {
                string freight = DataFiles.GetRecordAny(
                    record,
                    DataFiles.FreightCompanyColumn,
                    "Forwarder",
                    "Logistics");
                VendorChoice.Select(_freightCo, freight);
            }

            // Freight terms default to customer terms when the ticket has none.
            if (_freightTerms.Text.Trim().Length == 0 && terms.Length > 0)
                _freightTerms.Text = terms;
        }

        /// <summary>Fill customer fields from a known customer record.</summary>
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

        /// <summary>Write customer identity and ship-to, optionally overwriting fields the user already typed.</summary>
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
                // Empty source values should not wipe a field the user already typed.
                if (value.Length == 0)
                    return;
                // Lookup picks replace; sale prefill only fills blanks.
                if (overwrite || box.Text.Trim().Length == 0)
                    box.Text = value;
            }

            // Customer code is the lookup key; always write when the sale has one.
            if (code.Length > 0)
                _customerCode.Text = code;
            Put(_contact, contact);
            // Lookup should replace a stale ship-to; sale prefill should not.
            if (overwrite)
                _address.Text = address;
            // Prefill from a sale only fills ship-to when it is still blank.
            else
                Put(_address, address);
            Put(_email, email);
            Put(_contactPhone, phone);
            // Same overwrite-or-blank rule for name, terms, and freight terms.
            if (name.Length > 0 && (overwrite || _customer.Text.Trim().Length == 0))
                _customer.Text = name;
            // Terms only fill when the lookup asked to replace, or the box is still blank.
            if (terms.Length > 0 && (overwrite || _terms.Text.Trim().Length == 0))
                _terms.Text = terms;
            // Freight terms follow the same overwrite-or-blank rule as customer terms.
            if (terms.Length > 0 && (overwrite || _freightTerms.Text.Trim().Length == 0))
                _freightTerms.Text = terms;
            RefreshPoSuggestions();
        }

        /// <summary>Snapshot header fields and product lines for save and PDF output.</summary>
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
                    // Lines typed by hand inherit the header customer PO (stored as Invoice #).
                    if (line.PoNumber.Length == 0)
                        line.PoNumber = _customerPo.Text.Trim();
                    return line;
                }).ToList()
            };
        }

        /// <summary>Sum line cases, volume, and amount for the footer.</summary>
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
            // Sales are stored in the chosen data folder.
            if (!AppLock.HasFolder())
            {
                ToastAlert.Error(this, "Select a data folder in Settings first.");
                return;
            }

            var draft = CollectDraft();
            // Customer PO is the sale-row key.
            if (string.IsNullOrWhiteSpace(draft.CustomerPo))
            {
                ToastAlert.Error(this, "Enter a customer PO #.");
                return;
            }

            // SO # is the stored pick-ticket key and the PDF file key.
            if (string.IsNullOrWhiteSpace(draft.SoNumber))
            {
                ToastAlert.Error(this, "Enter a sales order number.");
                return;
            }

            // A sales order without a customer cannot be posted.
            if (string.IsNullOrWhiteSpace(draft.CustomerName) && string.IsNullOrWhiteSpace(draft.CustomerCode))
            {
                ToastAlert.Error(this, "Pick a customer.");
                return;
            }

            var lines = draft.Lines
                .Where(line => line.ItemCode.Length > 0 || line.Description.Length > 0)
                .ToList();
            // Blank rows are only placeholders for typing.
            if (lines.Count == 0)
            {
                ToastAlert.Error(this, "Add at least one product line.");
                return;
            }

            string po = draft.CustomerPo.Trim();
            foreach (var line in lines)
            {
                // Customer PO writes to Invoice #; fill it from the header when the line has none.
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
                    // Same PO + item already exists: update that row instead of inserting a duplicate.
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
                    // Stop so remaining lines are not written after a failed mutate.
                    if (last is not { Ok: true })
                    {
                        ToastAlert.Error(this, last?.Message ?? "Could not save that line.");
                        return;
                    }

                    // Track saved items so removed lines can be deleted after the loop.
                    if (item.Length > 0)
                        savedItems.Add(item);
                }

                // Lines dropped in the editor must be deleted from the original PO.
                if (_editing)
                {
                    foreach (string oldItem in _loadedItems)
                    {
                        // Still on this PO, so keep the row.
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
                        // Stop so remaining dropped lines are not deleted after a failed mutate.
                        if (last is not { Ok: true })
                        {
                            ToastAlert.Error(this, last?.Message ?? "Could not remove that line.");
                            return;
                        }
                    }
                }
            }
            // Mutate can throw when the store is locked or the row is missing.
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            try
            {
                SaleDocument.SaveFromPo(po);
            }
            // Keep the saved rows even if the sale PDF cannot be written.
            catch
            {
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
                // Reuse a sales-order number already assigned to this PO instead of minting a second one.
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
            // PDF/store failures should not leave the form looking saved.
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            ToastAlert.Success(this, last is { Queued: true }
                ? last.Value.Message
                : _editing ? "The sales order was updated." : "The sales order was saved.");

            // Add Another starts a new ticket with the same customer.
            if (keepCustomer)
            {
                ResetDraft(keepCustomer: true);
                return;
            }

            // Stay in edit mode so a second save still updates these items.
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

        /// <summary>Map header fields plus one product line onto a sales-table row.</summary>
        private Dictionary<string, string> BuildSaleValues(SalesOrderDraft draft, SalesOrderLine line)
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                // PO # stores the purchase lot (line Lot #). The Sales grid displays PO # as "Lot #".
                ["PO #"] = line.LotNumber,
                ["SO #"] = draft.SoNumber,
                ["Customer Code"] = draft.CustomerCode,
                ["Customer"] = draft.CustomerName,
                ["Customer Terms"] = draft.Terms,
                ["Item Code"] = line.ItemCode,
                // Invoice # stores the customer PO. The Sales grid displays Invoice # as "Customer PO".
                ["Invoice #"] = line.PoNumber.Length > 0 ? line.PoNumber : draft.CustomerPo,
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

        /// <summary>Load every product on this customer PO into the form for edit.</summary>
        private void LoadOrder(Dictionary<string, string> record)
        {
            // SalePo reads Invoice # (customer PO). Grid PO # is the purchase lot, shown as "Lot #".
            string po = DataFiles.SalePo(record);
            var rows = DataFiles.FindSalesByPo(po);
            // The clicked row is enough to edit even if the PO lookup returned nothing.
            if (rows.Count == 0)
                rows.Add(record);

            ClearLines();
            _loadedItems.Clear();
            _editPo = po;
            _editCustomer = DataFiles.GetRecord(rows[0], "Customer Code");
            ApplySaleHeader(rows[0]);
            _customerPo.Text = po;
            string so = DataFiles.GetRecord(rows[0], "SO #");
            // Keep the generated SO # when the sale has not been assigned one yet.
            if (so.Length > 0)
                _soNo.Text = so;
            string due = DataFiles.GetRecord(rows[0], "Due Date");
            // Only overwrite the picker when the sale actually has a due date.
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
                // Remember loaded items so a later save can delete ones the user removed.
                if (item.Length > 0)
                    _loadedItems.Add(item);
            }

            SetMode(true);
            UpdateTotals();
        }

        /// <summary>Switch captions and hide Add Another while editing an existing sales order.</summary>
        private void SetMode(bool editing)
        {
            _editing = editing;
            // Controls are created in BuildUi; skip if HighlightCurrentPage ran first.
            if (_modeLabel != null)
                _modeLabel.Text = editing ? "Edit Sales Order" : "Sales Order";
            // Save caption switches to Save Changes while editing an existing ticket.
            if (_save != null)
                _save.Text = editing ? "Save Changes" : "Save Sales Order";
            // Add Another is only for a new ticket, not an in-place edit.
            if (_another != null)
                _another.Visible = !editing;
        }

        /// <summary>Select a status, adding unknown values so older rows still display.</summary>
        private static void SelectStatus(ComboBox box, string? value)
        {
            // Fill the list once; the designer does not own these items.
            if (box.Items.Count == 0)
                box.Items.AddRange(new object[] { "Open", "Pending", "Shipped", "Invoiced", "Paid", "Complete" });

            string pick = (value ?? "").Trim();
            // Blank status on a new ticket (or an old row) defaults to Open.
            if (pick.Length == 0)
                pick = "Open";
            box.SelectedItem = pick;
            // Keep statuses that are no longer in the default list.
            if (box.SelectedIndex < 0)
            {
                box.Items.Add(pick);
                box.SelectedItem = pick;
            }
        }

        /// <summary>Labeled combo placed at a fixed header coordinate.</summary>
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

        /// <summary>Labeled text box placed at a fixed header coordinate.</summary>
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

        /// <summary>Date picker placed at a fixed header coordinate.</summary>
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

        /// <summary>Labeled multiline box used for Ship To.</summary>
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

        /// <summary>Caption plus value label used for footer totals.</summary>
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

        /// <summary>Customer lookup row used to fill code, name, terms, and ship-to.</summary>
        private sealed class CustomerChoice
        {
            public string Code { get; }
            public string Name { get; }
            public string Terms { get; }
            public string Contact { get; }
            public string Address { get; }
            public string Email { get; }
            public string Phone { get; }

            /// <summary>Store one visible customer record for lookup matching.</summary>
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

            /// <summary>Display name with code, or code alone when the name is blank.</summary>
            public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Code : $"{Name} ({Code})";
        }
    }
}
