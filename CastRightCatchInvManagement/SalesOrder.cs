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
        private TextBox _freightCo = null!;
        private ComboBox _freightTerms = null!;
        private TextBox _overhead = null!;
        private TextBox _freight = null!;
        private TextBox _other = null!;
        private Panel _overheadHost = null!;
        private Panel _freightLbHost = null!;
        private Panel _otherHost = null!;
        private DateTimePicker _paymentTerm = null!;
        private string _customerTerms = "";
        private ComboBox _status = null!;
        private Panel _lineHost = null!;
        private Label _modeLabel = null!;
        private Label _totalCases = null!;
        private Label _totalVolume = null!;
        private Label _totalAmount = null!;
        private Button _save = null!;
        private Button _another = null!;
        private List<LookupSuggest.Hit> _itemHits = new();
        private List<LookupSuggest.Hit> _freightHits = new();
        private LookupSuggest? _freightSuggest;
        private LookupSuggest? _shipSuggest;
        private CheckBox _saveAddress = null!;
        private CheckBox _shippingDifferent = null!;
        private TextBox _billing = null!;
        private Panel _shipHost = null!;
        private CardPanel _headerCard = null!;
        private bool _syncingAddresses;
        private readonly HashSet<string> _loadedItems = new(StringComparer.OrdinalIgnoreCase);
        private bool _busyAdding;
        private bool _splittingLot;
        private bool _fillingPo;
        private bool _editing;
        private string _editPo = "";
        private string _editCustomer = "";
        private string _editSo = "";

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
            _headerCard = new CardPanel { Height = 338, Padding = new Padding(16, 10, 16, 10) };
            var card = _headerCard;

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

            _orderDate = AddDate(card, "ORDER DATE", 20, 86, 130);
            _releaseDate = AddDate(card, "SHIP DATE", 164, 86, 130);
            _paymentTerm = AddDate(card, "PAYMENT TERM", 308, 86, 130);
            _orderDate.ValueChanged += (_, _) => ApplyPaymentDue();
            _status = AddCombo(card, "STATUS", 452, 86, 130);
            _status.DropDownStyle = ComboBoxStyle.DropDownList;
            SelectStatus(_status, "Open");

            _warehouse = AddField(card, "WAREHOUSE", 20, 136, 150);
            _freightCo = AddField(card, "FREIGHT CO", 184, 136, 140);
            _freightTerms = AddCombo(card, "FREIGHT TERMS", 338, 136, 130);
            _freightTerms.DropDownStyle = ComboBoxStyle.DropDownList;
            SelectFreightTerms(_freightTerms, "");
            _contact = AddField(card, "CONTACT", 482, 136, 150);

            _email = AddField(card, "EMAIL", 20, 186, 200);
            _contactPhone = AddField(card, "PHONE", 234, 186, 140);

            _billing = AddMultiline(card, "BILLING ADDRESS", 388, 186, 360, 48);
            _billing.PlaceholderText = "Street, city, state, ZIP";
            _shippingDifferent = new CheckBox
            {
                Text = "Shipping is different from billing",
                AutoSize = true,
                Location = new Point(388, 252),
                Font = Theme.Small,
                ForeColor = Theme.Ink
            };
            _shipHost = new Panel
            {
                Location = new Point(388, 274),
                Size = new Size(360, 64),
                Visible = false,
                BackColor = Theme.Cream
            };
            _address = AddMultiline(_shipHost, "SHIP TO", 0, 0, 360, 48);
            _address.PlaceholderText = "Type a warehouse or enter an address";
            _saveAddress = new CheckBox
            {
                Text = "Save shipping address on customer",
                AutoSize = true,
                Location = new Point(388, 256),
                Font = Theme.Small,
                ForeColor = Theme.Ink,
                Visible = false,
                Checked = false
            };
            card.Controls.Add(_shippingDifferent);
            card.Controls.Add(_shipHost);
            card.Controls.Add(_saveAddress);
            _shippingDifferent.CheckedChanged += (_, _) =>
            {
                if (_syncingAddresses)
                    return;
                if (_shippingDifferent.Checked && _address.Text.Trim().Length == 0)
                {
                    var match = CurrentCustomerChoice();
                    if (match != null && match.Warehouses.Count > 0)
                        _address.Text = match.Warehouses[0].Address;
                }

                RelayoutAddressBlock();
                SyncSaveAddressOffer();
            };
            _address.TextChanged += (_, _) => SyncSaveAddressOffer();
            _customer.TextChanged += (_, _) => SyncSaveAddressOffer();
            _customerCode.TextChanged += (_, _) => SyncSaveAddressOffer();
            RelayoutAddressBlock();

            _other = AddCostField(card, "OTHER / LB", 20, 242, 110, out _otherHost);
            _freight = AddCostField(card, "FREIGHT / LB", 154, 242, 120, out _freightLbHost);
            _overhead = AddCostField(card, "OVERHEAD / LB", 288, 242, 120, out _overheadHost);

            _customerCodeSuggest = new LookupSuggest(_customerCode, () => _customerHits, codeFirst: true, ApplyCustomerHit);
            _customerNameSuggest = new LookupSuggest(_customer, () => _customerHits, codeFirst: false, ApplyCustomerHit);
            _shipSuggest = new LookupSuggest(
                _address,
                WarehouseHits,
                codeFirst: true,
                ApplyWarehouseHit,
                minListWidth: 360,
                showWhenEmpty: true);
            _freightSuggest = new LookupSuggest(
                _freightCo,
                () => _freightHits,
                codeFirst: false,
                hit => _freightCo.Text = hit.Name.Length > 0 ? hit.Name : hit.Code);

            foreach (var box in new[] { _overhead, _freight, _other })
            {
                MoneyFormat.BindInput(box);
                box.TextChanged += (_, _) => RecalcLines();
            }

            _freightCo.TextChanged += (_, _) => SyncCostFields();
            SyncCostFields();

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
            var row = CreateLine();
            _lines.Add(row);
            _lineHost.Controls.Add(row);
            LayoutLines();
            row.FocusItem();
            UpdateTotals();
            return row;
        }

        /// <summary>Wire a product row without adding it to the host yet.</summary>
        private SalesOrderLineRow CreateLine()
        {
            var row = new SalesOrderLineRow();
            row.AttachLookups(() => _itemHits);
            row.SetPo(_customerPo.Text.Trim());
            row.CurrentSoNumber = () => _soNo.Text.Trim();
            row.SiblingVolume = () => SiblingVolume(row);
            row.Changed += (_, _) => UpdateTotals();
            row.RemoveRequested += (_, _) => RemoveLine(row);
            row.LotOverflow += (_, e) => OnLotOverflow(row, e);
            row.SetSharedCosts(SharedOverhead(), SharedFreight(), SharedOther());
            return row;
        }

        /// <summary>Queue the leftover cases onto the next line after this lot is capped.</summary>
        private void OnLotOverflow(SalesOrderLineRow source, LotOverflowEventArgs e)
        {
            if (_splittingLot || _busyAdding || IsDisposed)
                return;
            if (IsHandleCreated)
                BeginInvoke(new Action(() => ApplyLotOverflow(source, e)));
            else
                ApplyLotOverflow(source, e);
        }

        /// <summary>
        /// Put leftover cases on the next empty or same-item line, or insert a new line,
        /// so the rest can be sold from another lot.
        /// </summary>
        private void ApplyLotOverflow(SalesOrderLineRow source, LotOverflowEventArgs e)
        {
            if (_splittingLot || IsDisposed || !_lines.Contains(source))
                return;
            if (e.Volume <= 0.0005m && e.Cases <= 0.0005m)
                return;

            _splittingLot = true;
            try
            {
                var leftover = source.GetLine();
                leftover.LotNumber = "";
                leftover.Cases = SalesOrderLineRow.FormatQty(e.Cases);
                leftover.Volume = e.Volume > 0 ? MoneyFormat.Plain(e.Volume) : "";
                leftover.Amount = "";

                int i = _lines.IndexOf(source);
                if (i < 0)
                    return;

                if (i + 1 < _lines.Count)
                {
                    var next = _lines[i + 1];
                    if (!next.HasContent())
                    {
                        next.FillFromLine(leftover);
                        next.FocusLot();
                        LayoutLines();
                        UpdateTotals();
                        return;
                    }

                    if (next.ItemCode.Equals(source.ItemCode, StringComparison.OrdinalIgnoreCase) &&
                        next.LotNumber.Length == 0)
                    {
                        next.AddQuantity(e.Cases, e.Volume);
                        LayoutLines();
                        UpdateTotals();
                        return;
                    }
                }

                var row = CreateLine();
                _lines.Insert(i + 1, row);
                _lineHost.Controls.Add(row);
                row.FillFromLine(leftover);
                row.FocusLot();
                LayoutLines();
                UpdateTotals();
            }
            finally
            {
                _splittingLot = false;
            }
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
                Text = "Save and Next",
                Size = new Size(140, 34),
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
                    DataFiles.PartyDocumentName(record),
                    DataFiles.GetRecord(record, "Terms"),
                    DataFiles.GetRecord(record, "Contact Name"),
                    DataFiles.CustomerBillingAddress(record),
                    DataFiles.CustomerWarehouses(record),
                    DataFiles.GetRecord(record, "Email"),
                    DataFiles.GetRecord(record, "Phone"));
                // Skip customers that have neither a code nor a name to type.
                if (choice.Code.Length == 0 && choice.Name.Length == 0)
                    continue;
                _customerRecords.Add(choice);
                _customerHits.Add(new LookupSuggest.Hit(
                    choice.Code,
                    choice.Name,
                    choice.Terms,
                    DataFiles.PartyCompanyName(record) + " " + DataFiles.PartyDba(record)));
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

            _freightHits.Clear();
            foreach (var record in DataFiles.VisibleRecords(DataFiles.Vendors))
            {
                if (!VendorTypes.MatchesSlot(record, VendorTypes.SlotSalesFreight))
                    continue;
                string code = DataFiles.GetRecord(record, "Code").Trim();
                string name = DataFiles.PartyDocumentName(record);
                if (code.Length == 0 && name.Length == 0)
                    continue;
                _freightHits.Add(new LookupSuggest.Hit(
                    code,
                    name,
                    DataFiles.GetRecord(record, "Terms"),
                    DataFiles.PartyCompanyName(record) + " " + DataFiles.PartyDba(record)));
            }

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

        /// <summary>Warehouse and billing addresses for the customer currently on this ticket.</summary>
        private IReadOnlyList<LookupSuggest.Hit> WarehouseHits()
        {
            var hits = new List<LookupSuggest.Hit>();
            CustomerChoice? match = CurrentCustomerChoice();
            if (match == null)
                return hits;

            if (match.Billing.Length > 0)
            {
                string first = FirstAddressLine(match.Billing);
                hits.Add(new LookupSuggest.Hit("Billing", first, match.Billing, match.Billing));
            }

            foreach (var warehouse in match.Warehouses)
            {
                string first = FirstAddressLine(warehouse.Address);
                string label = warehouse.Name.Length > 0 ? warehouse.Name : first;
                hits.Add(new LookupSuggest.Hit(
                    label,
                    first,
                    warehouse.Address,
                    warehouse.Name + " " + warehouse.Address));
            }

            return hits;
        }

        /// <summary>Write the picked warehouse (or billing) into Ship To.</summary>
        private void ApplyWarehouseHit(LookupSuggest.Hit hit)
        {
            string address = hit.Extra.Length > 0 ? hit.Extra : hit.DisplayName;
            _address.Text = address;
            if (hit.Code.Length > 0 &&
                !hit.Code.Equals("Billing", StringComparison.OrdinalIgnoreCase))
                _warehouse.Text = hit.Code;
            _syncingAddresses = true;
            bool billingPick = hit.Code.Equals("Billing", StringComparison.OrdinalIgnoreCase) ||
                AddressesEqual(address, _billing.Text);
            _shippingDifferent.Checked = !billingPick;
            _syncingAddresses = false;
            RelayoutAddressBlock();
            SyncSaveAddressOffer();
        }

        /// <summary>First line of an address, used as the suggestion subtitle.</summary>
        private static string FirstAddressLine(string text)
        {
            int breakAt = text.IndexOfAny(['\r', '\n']);
            return (breakAt < 0 ? text : text[..breakAt]).Trim();
        }

        /// <summary>Visible customer matching the code or name on this ticket.</summary>
        private CustomerChoice? CurrentCustomerChoice()
        {
            string code = CurrentCustomerCode();
            string name = CurrentCustomerName();
            foreach (var record in _customerRecords)
            {
                if (code.Length > 0 &&
                    record.Code.Equals(code, StringComparison.OrdinalIgnoreCase))
                    return record;
                if (name.Length > 0 && DataFiles.PartyLabelMatches(record.Name, name))
                    return record;
            }

            return null;
        }

        /// <summary>Show the save-on-customer box only for a new delivery address.</summary>
        private void SyncSaveAddressOffer()
        {
            bool show = ShouldOfferSaveAddress();
            _saveAddress.Visible = show;
            if (!show)
                _saveAddress.Checked = false;
            RelayoutAddressBlock();
        }

        /// <summary>Stack Ship To and save-address under billing; grow the header when shipping is open.</summary>
        private void RelayoutAddressBlock()
        {
            int y = 252;
            _shippingDifferent.Location = new Point(388, y);
            y = _shippingDifferent.Bottom + 4;
            _shipHost.Visible = _shippingDifferent.Checked;
            if (_shippingDifferent.Checked)
            {
                _shipHost.Location = new Point(388, y);
                y = _shipHost.Bottom + 4;
            }

            _saveAddress.Location = new Point(388, y);
            int bottom = _shippingDifferent.Bottom;
            if (_shipHost.Visible)
                bottom = Math.Max(bottom, _shipHost.Bottom);
            if (_saveAddress.Visible)
                bottom = Math.Max(bottom, _saveAddress.Bottom);
            _headerCard.Height = Math.Max(338, bottom + 16);
        }

        /// <summary>Billing typed on the ticket.</summary>
        private string CurrentBillingAddress() => _billing.Text.Trim();

        /// <summary>Ship To when it is different, otherwise the billing address.</summary>
        private string CurrentShipToAddress()
        {
            if (_shippingDifferent.Checked && _address.Text.Trim().Length > 0)
                return _address.Text.Trim();
            return CurrentBillingAddress();
        }

        /// <summary>True when Ship To is not billing and not already a warehouse on this customer.</summary>
        private bool ShouldOfferSaveAddress()
        {
            if (!_shippingDifferent.Checked)
                return false;
            if (!DataAccess.CanMutate(DataFiles.Customers))
                return false;
            string ship = NormalizeAddress(_address.Text);
            if (ship.Length == 0)
                return false;
            var match = CurrentCustomerChoice();
            if (match == null)
                return false;
            if (AddressesEqual(ship, match.Billing))
                return false;
            foreach (var warehouse in match.Warehouses)
            {
                if (AddressesEqual(ship, warehouse.Address))
                    return false;
            }

            return true;
        }

        /// <summary>Append Ship To as a warehouse on the live customer row.</summary>
        private MutateResult SaveShipToOnCustomer(string address, string warehouseName)
        {
            address = address.Trim();
            if (address.Length == 0)
                return MutateResult.Deny("Enter a delivery address.");

            string code = CurrentCustomerCode();
            string name = CurrentCustomerName();
            Dictionary<string, string>? found = null;
            foreach (var record in DataFiles.ReadAllRecords(DataFiles.Customers))
            {
                if (code.Length > 0 &&
                    DataFiles.GetRecord(record, "Code").Equals(code, StringComparison.OrdinalIgnoreCase))
                {
                    found = record;
                    break;
                }

                if (name.Length > 0 &&
                    DataFiles.PartyLabelMatches(DataFiles.PartyDocumentName(record), name))
                {
                    found = record;
                    break;
                }
            }

            if (found == null)
                return MutateResult.Deny("Could not find that customer.");

            var warehouses = DataFiles.CustomerWarehouses(found);
            foreach (var warehouse in warehouses)
            {
                if (AddressesEqual(address, warehouse.Address))
                    return MutateResult.Saved();
            }

            warehouses.Add(new DataFiles.CustomerWarehouse(warehouseName.Trim(), address));
            var fields = new Dictionary<string, string>(found, StringComparer.OrdinalIgnoreCase)
            {
                [DataFiles.ShippingAddressColumn] = DataFiles.FormatCustomerWarehouses(warehouses)
            };
            string matchCode = DataFiles.GetRecord(found, "Code");
            return DataFiles.MutateUpdate(
                DataFiles.Customers,
                record => DataFiles.GetRecord(record, "Code")
                    .Equals(matchCode, StringComparison.OrdinalIgnoreCase),
                fields);
        }

        /// <summary>Trim and unify line breaks so typed and stored addresses compare.</summary>
        private static string NormalizeAddress(string? text) =>
            (text ?? "").Replace("\r\n", "\n").Replace('\r', '\n').Trim();

        /// <summary>True when two addresses are the same ignoring case and line-break style.</summary>
        private static bool AddressesEqual(string left, string right) =>
            NormalizeAddress(left).Equals(NormalizeAddress(right), StringComparison.OrdinalIgnoreCase);

        /// <summary>Fill customer fields from a lookup pick, using the customer record when found.</summary>
        private void ApplyCustomerHit(LookupSuggest.Hit hit)
        {
            CustomerChoice? match = null;
            foreach (var record in _customerRecords)
            {
                // Match by code first, then by name when the pick has one.
                if (record.Code.Equals(hit.Code, StringComparison.OrdinalIgnoreCase) ||
                    (hit.Name.Length > 0 && DataFiles.PartyLabelMatches(record.Name, hit.Name)))
                {
                    match = record;
                    break;
                }
            }

            // Known customers also fill contact, address, email, and phone.
            if (match != null)
                FillCustomer(match, overwrite: true);
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
                return DataFiles.PartyLabelMatches(prefill.CustomerName, name);
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
            string po = keepCustomer ? _customerPo.Text : "";

            ClearLines();
            _loadedItems.Clear();
            _editing = false;
            _editPo = "";
            _editSo = "";
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
                _billing.Text = "";
                _syncingAddresses = true;
                _shippingDifferent.Checked = false;
                _syncingAddresses = false;
                _saveAddress.Checked = false;
                _contact.Text = "";
                _email.Text = "";
                _contactPhone.Text = "";
                _warehouse.Text = "";
                _freightCo.Text = "";
                SelectFreightTerms(_freightTerms, "");
                _customerTerms = "";
                _paymentTerm.Value = DateTime.Today;
            }

            _overhead.Text = "";
            _freight.Text = "";
            _other.Text = "";
            _customerPo.Text = po;
            RefreshLookups();
            _soNo.Text = DataFiles.NextSalesOrderNumber();
            _orderDate.Value = DateTime.Today;
            _releaseDate.Value = DateTime.Today;
            SelectStatus(_status, "Open");
            SetMode(false);
            AddLine();
            SyncCostFields();
            UpdateTotals();
            SyncSaveAddressOffer();
        }

        /// <summary>Fill customer, PO, SO, ship date, and freight from the first matching sale.</summary>
        private void ApplySaleHeader(Dictionary<string, string> record)
        {
            string code = DataFiles.GetRecordAny(record, "Customer Code", "Cust ID");
            string name = DataFiles.GetRecordAny(record, "Customer", "Customer Name");
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
                FillCustomer(match, overwrite: false);
            // Unknown sales still get code/name from the sale row itself.
            else
                FillCustomer(code, name, "", "", "", "", "", overwrite: false);

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
            if (_freightCo.Text.Trim().Length == 0)
            {
                string freight = DataFiles.GetRecordAny(record, DataFiles.FreightCompanyColumn);
                if (freight.Length > 0)
                    _freightCo.Text = freight;
            }

            if (string.IsNullOrWhiteSpace(_freightTerms.Text))
            {
                string terms = DataFiles.GetRecord(record, DataFiles.FreightTermsColumn);
                if (terms.Length > 0)
                    SelectFreightTerms(_freightTerms, terms);
            }

            if (_customerTerms.Length == 0)
                SetPaymentFromSale(record);
        }

        /// <summary>Due date is order date plus the days in the customer’s Terms.</summary>
        private void ApplyPaymentDue()
        {
            if (_customerTerms.Length == 0)
                return;
            _paymentTerm.Value = DataFiles.DueDateFromTerms(_orderDate.Value.Date, _customerTerms);
        }

        /// <summary>Restore a stored due date, or compute one from Payment Term days.</summary>
        private void SetPaymentFromSale(Dictionary<string, string> record)
        {
            string payment = DataFiles.GetRecord(record, DataFiles.PaymentTermColumn);
            string due = DataFiles.GetRecord(record, "Due Date");
            if (DateTime.TryParse(due, out var dueDate))
            {
                _customerTerms = payment;
                _paymentTerm.Value = dueDate.Date;
                return;
            }

            if (payment.Length == 0)
                return;
            if (DateTime.TryParse(payment, out var asDate))
            {
                _customerTerms = "";
                _paymentTerm.Value = asDate.Date;
                return;
            }

            _customerTerms = payment;
            ApplyPaymentDue();
        }

        /// <summary>Fill customer fields from a known customer record.</summary>
        private void FillCustomer(CustomerChoice choice, bool overwrite)
        {
            FillCustomer(
                choice.Code,
                choice.Name,
                choice.Terms,
                choice.Contact,
                choice.Address,
                choice.Email,
                choice.Phone,
                overwrite,
                choice.Billing);
        }

        /// <summary>Write customer identity, payment term, and billing, optionally overwriting typed fields.</summary>
        private void FillCustomer(
            string code,
            string name,
            string terms,
            string contact,
            string address,
            string email,
            string phone,
            bool overwrite,
            string billing = "")
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
            if (terms.Length > 0 && (overwrite || _customerTerms.Length == 0))
            {
                _customerTerms = terms;
                ApplyPaymentDue();
            }
            Put(_contact, contact);
            Put(_billing, billing.Length > 0 ? billing : address);
            Put(_email, email);
            Put(_contactPhone, phone);
            if (name.Length > 0 && (overwrite || _customer.Text.Trim().Length == 0))
                _customer.Text = name;
            // Billing is the default. Ship To stays hidden until they check the box.
            if (overwrite)
            {
                _address.Text = "";
                _syncingAddresses = true;
                _shippingDifferent.Checked = false;
                _syncingAddresses = false;
            }

            RefreshPoSuggestions();
            SyncSaveAddressOffer();
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
                Address = CurrentShipToAddress(),
                BillingAddress = CurrentBillingAddress(),
                CustomerPhone = _contactPhone.Text.Trim(),
                Contact = _contact.Text.Trim(),
                Email = _email.Text.Trim(),
                ContactPhone = _contactPhone.Text.Trim(),
                Warehouse = _warehouse.Text.Trim(),
                CustomerPo = _customerPo.Text.Trim(),
                Status = _status.Text.Trim(),
                PaymentTerms = _customerTerms,
                DueDate = _paymentTerm.Value.Date,
                FreightCompany = _freightCo.Text.Trim(),
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
            _totalVolume.Text = MoneyFormat.Plain(volume);
            _totalAmount.Text = MoneyFormat.Display(amount);
            foreach (var row in _lines)
                row.RefreshWarnings();
        }

        /// <summary>Volume on other lines of this ticket that share the same item and purchase lot.</summary>
        private decimal SiblingVolume(SalesOrderLineRow row)
        {
            decimal n = 0;
            string item = row.ItemCode;
            string lot = DataFiles.NormalizePo(row.LotNumber);
            if (item.Length == 0 || lot.Length == 0)
                return 0;
            foreach (var other in _lines)
            {
                if (other == row)
                    continue;
                if (!other.ItemCode.Equals(item, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (!DataFiles.NormalizePo(other.LotNumber).Equals(lot, StringComparison.OrdinalIgnoreCase))
                    continue;
                n += other.VolumeValue;
            }

            return n;
        }

        /// <summary>
        /// If a PDF already exists for this SO #, open it. Otherwise draw a new PDF, store it,
        /// write the SO # onto matching sales, and open the viewer.
        /// </summary>
        private void CreateSalesOrder(bool keepCustomer = false)
        {
            if (!AppLock.HasFolder())
            {
                ToastAlert.Error(this, AppLock.NeedStoreMessage());
                return;
            }

            DataFiles.EnsureSalesColumns();
            RecalcLines();
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

            foreach (var row in _lines)
            {
                if (!row.HasContent())
                    continue;
                row.RefreshWarnings();
                string? floor = row.MinimumProfitError();
                if (floor != null)
                {
                    MessageBox.Show(floor, "Minimum profit", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                string? over = row.VolumeError();
                if (over != null)
                {
                    MessageBox.Show(over, "Lot volume", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
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
                if (_editing)
                    DataFiles.RetireStoredPdf(DataFiles.PdfKindSalesOrder, _editSo, soNumber);
                string pdfPath = SalesOrderDocument.Save(draft);
                DataFiles.AssignSalesOrderNumber(
                    pos,
                    draft.CustomerCode,
                    draft.CustomerName,
                    soNumber,
                    draft.FreightCompany,
                    draft.FreightTerms);
                DataFiles.OpenPdf(pdfPath, DataFiles.PdfKindSalesOrder, soNumber);
            }
            // PDF/store failures should not leave the form looking saved.
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            string extra = "";
            if (_saveAddress.Visible && _saveAddress.Checked)
            {
                var stored = SaveShipToOnCustomer(draft.Address, draft.Warehouse);
                if (stored is { Ok: true, Queued: true })
                    extra = " New address is waiting for review.";
                else if (stored is not { Ok: true })
                    extra = " Address was not saved on the customer.";
                else
                    RefreshLookups();
            }

            ToastAlert.Success(this, (last is { Queued: true }
                ? last.Value.Message
                : _editing ? "The sales order was updated." : "The sales order was saved.") + extra);

            // Add Another starts a new ticket with the same customer on this page.
            if (keepCustomer)
            {
                ResetDraft(keepCustomer: true);
                return;
            }

            ResetDraft(keepCustomer: false);
            Navigator.GoToAndSearch(AppPage.Sales, draft.SoNumber);
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
                [DataFiles.PaymentTermColumn] = draft.PaymentTerms,
                ["Status"] = draft.Status,
                [DataFiles.FreightCompanyColumn] = draft.FreightCompany,
                [DataFiles.FreightTermsColumn] = draft.FreightTerms,
                ["Overhead / LB"] = MoneyFormat.Store(_overhead.Text),
                ["Freight / LB"] = MoneyFormat.Store(_freight.Text),
                ["Other / LB"] = MoneyFormat.Store(_other.Text)
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
            _editSo = DataFiles.GetRecord(rows[0], "SO #").Trim();
            ApplySaleHeader(rows[0]);
            _customerPo.Text = po;
            string so = DataFiles.GetRecord(rows[0], "SO #");
            // Keep the generated SO # when the sale has not been assigned one yet.
            if (so.Length > 0)
                _soNo.Text = so;
            SetPaymentFromSale(rows[0]);
            SelectStatus(_status, DataFiles.GetRecord(rows[0], "Status"));
            _freightCo.Text = DataFiles.GetRecord(rows[0], DataFiles.FreightCompanyColumn);
            SelectFreightTerms(_freightTerms, DataFiles.GetRecord(rows[0], DataFiles.FreightTermsColumn));
            _overhead.Text = MoneyFormat.Display(DataFiles.GetRecord(rows[0], "Overhead / LB"));
            _freight.Text = MoneyFormat.Display(DataFiles.GetRecord(rows[0], "Freight / LB"));
            _other.Text = MoneyFormat.Display(DataFiles.GetRecord(rows[0], "Other / LB"));
            SyncCostFields();


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

            RecalcLines();
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

        /// <summary>Show Freight / LB when Freight Co is filled. Other / LB always stays.</summary>
        private void SyncCostFields()
        {
            bool showFreight = _freightCo.Text.Trim().Length > 0;
            bool showOverhead = HasCost(_overhead.Text);

            if (!showFreight)
                _freight.Text = "";

            _freightLbHost.Visible = showFreight;
            _overheadHost.Visible = showOverhead;

            int x = 20;
            _otherHost.Location = new Point(x, 242);
            x += _otherHost.Width + 14;
            if (showFreight)
            {
                _freightLbHost.Location = new Point(x, 242);
                x += _freightLbHost.Width + 14;
            }

            if (showOverhead)
                _overheadHost.Location = new Point(x, 242);

            RecalcLines();
        }

        private static bool HasCost(string text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return false;
            return MoneyFormat.TryParse(text, out decimal amount) ? amount != 0 : text.Trim().Length > 0;
        }

        /// <summary>Header overhead / lb applied to every product line.</summary>
        private decimal SharedOverhead() =>
            _overheadHost.Visible ? SalesOrderLineRow.ParseNumber(_overhead.Text) : 0;
        /// <summary>Header freight / lb applied to every product line.</summary>
        private decimal SharedFreight() =>
            _freightLbHost.Visible ? SalesOrderLineRow.ParseNumber(_freight.Text) : 0;
        /// <summary>Header other / lb applied to every product line.</summary>
        private decimal SharedOther() => SalesOrderLineRow.ParseNumber(_other.Text);

        /// <summary>Push header per-lb costs onto every line and refresh order totals.</summary>
        private void RecalcLines()
        {
            decimal overhead = SharedOverhead();
            decimal freight = SharedFreight();
            decimal other = SharedOther();
            foreach (var row in _lines)
                row.SetSharedCosts(overhead, freight, other);
            UpdateTotals();
        }

        /// <summary>Labeled cost box in a host panel so / lb fields can show or hide together.</summary>
        private static TextBox AddCostField(
            Control parent,
            string caption,
            int x,
            int y,
            int width,
            out Panel host)
        {
            host = new Panel
            {
                Location = new Point(x, y),
                Size = new Size(width, 42)
            };
            var label = new Label { Text = caption, Location = new Point(0, 0) };
            Theme.StyleFieldLabel(label);
            var box = new TextBox
            {
                Location = new Point(0, 16),
                Size = new Size(width, 26)
            };
            Theme.StyleField(box);
            host.Controls.Add(label);
            host.Controls.Add(box);
            parent.Controls.Add(host);
            return box;
        }

        /// <summary>Select Collect or Delivered, adding unknown stored values so older rows still display.</summary>
        private static void SelectFreightTerms(ComboBox box, string? value)
        {
            if (box.Items.Count == 0)
                box.Items.AddRange(new object[] { "Collect", "Delivered" });

            string pick = (value ?? "").Trim();
            if (pick.Length == 0)
            {
                box.SelectedIndex = -1;
                return;
            }

            foreach (object item in box.Items)
            {
                if (string.Equals(item.ToString(), pick, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedItem = item;
                    return;
                }
            }

            box.Items.Add(pick);
            box.SelectedItem = pick;
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
            public string Billing { get; }
            public IReadOnlyList<DataFiles.CustomerWarehouse> Warehouses { get; }
            public string Address { get; }
            public string Email { get; }
            public string Phone { get; }

            /// <summary>Store one visible customer record for lookup matching.</summary>
            public CustomerChoice(
                string code,
                string name,
                string terms,
                string contact,
                string billing,
                IReadOnlyList<DataFiles.CustomerWarehouse> warehouses,
                string email,
                string phone)
            {
                Code = code;
                Name = name;
                Terms = terms;
                Contact = contact;
                Billing = billing;
                Warehouses = warehouses;
                Address = warehouses.Count > 0 ? warehouses[0].Address : billing;
                Email = email;
                Phone = phone;
            }

            /// <summary>Display name with code, or code alone when the name is blank.</summary>
            public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Code : $"{Name} ({Code})";
        }
    }
}
