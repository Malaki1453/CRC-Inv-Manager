using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Create Invoice form. Builds a PDF into Stored Invoices and marks PDF Created on the invoice row.</summary>
    public partial class InvoicePdf : Form, INavigationPage
    {
        private readonly List<InvoiceLineRow> _lines = new();
        private AutoCompleteStringCollection _poSource = new();

        private TextBox _invoiceNo = null!;
        private DateTimePicker _invoiceDate = null!;
        private TextBox _soNo = null!;
        private ComboBox _customer = null!;
        private TextBox _customerCode = null!;
        private TextBox _terms = null!;
        private DateTimePicker _shipDate = null!;
        private TextBox _shipVia = null!;
        private TextBox _salesRep = null!;
        private TextBox _soldTo = null!;
        private TextBox _shipTo = null!;
        private Panel _lineHost = null!;
        private Label _totalWeight = null!;
        private Label _subTotal = null!;
        private TextBox _discount = null!;
        private TextBox _freight = null!;
        private TextBox _tax = null!;
        private Button _taxMode = null!;
        private Label _heading = null!;
        private Label _soCaption = null!;
        private Label _partyCaption = null!;
        private Label _partyCodeCaption = null!;
        private Label _salesRepCaption = null!;
        private Label _linesHint = null!;
        private bool _taxPercent;
        private Label _invoiceTotal = null!;
        private bool _loadingCustomer;
        private bool _busyAdding;
        private bool _received;

        public InvoicePdf()
        {
            InitializeComponent();
            BuildUi();
            ResetDraft();
        }

        /// <summary>Shown or refreshed: reload customers, keep at least one blank line, redraw totals.</summary>
        public void HighlightCurrentPage()
        {
            RefreshLookups();
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
            RefreshLines();
        }

        /// <summary>
        /// Add matching sale lines for this PO/SO onto the invoice.
        /// If another sale is already on the invoice, the draft is cleared and replaced.
        /// <paramref name="done"/> gets an error string, or null on success.
        /// </summary>
        internal void TryAddSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            string key = prefill.Po.Length > 0 ? prefill.Po : prefill.So;
            if (_received ||
                (InvoiceHasSale() && (!PrefillMatchesCustomer(prefill) || !InvoiceAlreadyHasPo(key))))
                ResetDraft();

            if (_lines.Count == 0)
                AddLine(lockPrevious: false);

            string code = InvoiceHasCustomer() ? CurrentCustomerCode() : prefill.CustomerCode;
            string name = InvoiceHasCustomer() ? CurrentCustomerName() : prefill.CustomerName;
            StartAddItems(key, code, name, done);
        }

        /// <summary>
        /// Fill Create Invoice from a purchase: vendor is the issuer, we are the receiver.
        /// Matching PO lines are added. A different vendor or an issued draft is replaced.
        /// </summary>
        internal void TryAddPurchase(InvoicePurchasePrefill prefill, Action<string?> done)
        {
            string key = prefill.Po.Trim();
            if (key.Length == 0)
            {
                done("This purchase has no PO number.");
                return;
            }

            if (!_received ||
                (InvoiceHasSale() && (!PrefillMatchesVendor(prefill) || !InvoiceAlreadyHasPo(key))))
                ResetDraft(received: true);

            SetReceivedMode(true);
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);

            FillVendor(
                prefill.VendorCode,
                prefill.VendorName,
                prefill.VendorTerms,
                prefill.ShipDate);
            if (key.Length > 0)
                _soNo.Text = key;

            string code = CurrentPartyCode().Length > 0 ? CurrentPartyCode() : prefill.VendorCode;
            string name = CurrentPartyName().Length > 0 ? CurrentPartyName() : prefill.VendorName;
            StartAddPurchases(key, code, name, done);
        }

        /// <summary>
        /// Rebuild a missing invoice PDF from an invoices-grid row: prefer the stored snapshot,
        /// otherwise pull matching sales, then create and store the PDF.
        /// </summary>
        internal void CreatePdfFromInvoice(
            Dictionary<string, string> invoice,
            Action<string?> done)
        {
            if (_busyAdding)
            {
                done("Still adding lines to the invoice.");
                return;
            }

            bool received = DataFiles.IsReceivedInvoice(invoice);
            if (DataFiles.TryInvoiceDraft(invoice, out var stored) && stored.Lines.Count > 0)
            {
                ResetDraft(received: stored.Received || received);
                ApplyDraft(stored);
                FinishCreatedPdf(done);
                return;
            }

            ResetDraft(received: received);
            ApplyInvoiceHeader(invoice);

            string invoiceNo = DataFiles.GetRecord(invoice, "Invoice #").Trim();
            string so = DataFiles.GetRecord(invoice, "SO #").Trim();
            string code = DataFiles.GetRecordAny(invoice, "Customer Code", "Cust ID");
            string name = DataFiles.GetRecordAny(invoice, "Customer", "Customer Name");

            _busyAdding = true;
            Task.Run(() =>
            {
                try
                {
                    var sources = DataFiles.FindInvoiceSourcesForInvoice(
                        invoiceNo, so, code, name);
                    if (sources.Count == 0)
                        sources = DataFiles.FindInvoiceSourcesForInvoice(
                            invoiceNo, so, null, null);
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed)
                        {
                            _busyAdding = false;
                            done("Could not create that invoice PDF.");
                            return;
                        }

                        var remaining = UnusedSources(sources);
                        if (remaining.Count == 0)
                        {
                            _busyAdding = false;
                            FinishCreatedPdf(done);
                            return;
                        }

                        AddRecordsInBatches(remaining, 0, error =>
                        {
                            if (error != null)
                            {
                                done(error);
                                return;
                            }

                            FinishCreatedPdf(done);
                        });
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
        }

        private CardPanel BuildHeader()
        {
            var card = new CardPanel { Height = 214, Padding = new Padding(16, 12, 16, 12) };

            _heading = new Label
            {
                Text = "Invoice",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 8)
            };
            card.Controls.Add(_heading);

            _invoiceNo = AddField(card, "INVOICE NO.", 20, 36, 110);
            _invoiceDate = AddDate(card, "INVOICE DATE", 144, 36, 120);
            _soNo = AddField(card, "SO #", 278, 36, 110);

            _customer = AddCustomer(card, "CUSTOMER", 20, 86, 250);
            _customerCode = AddField(card, "CUST ID", 284, 86, 90);
            _terms = AddField(card, "TERMS", 388, 86, 160);

            _shipDate = AddDate(card, "SHIP DATE", 20, 136, 120);
            _shipVia = AddField(card, "SHIP VIA", 154, 136, 180);
            _salesRep = AddField(card, "SALES REP", 348, 136, 200);

            _soCaption = CaptionAt(card, "SO #");
            _partyCaption = CaptionAt(card, "CUSTOMER");
            _partyCodeCaption = CaptionAt(card, "CUST ID");
            _salesRepCaption = CaptionAt(card, "SALES REP");

            _soldTo = AddMultiline(card, "SOLD TO", 564, 36, 220, 58);
            _shipTo = AddMultiline(card, "SHIP TO", 564, 110, 220, 58);
            _shipTo.PlaceholderText = "Not found, please input manually";

            card.Resize += (_, _) =>
            {
                int right = Math.Max(180, card.Width - 260);
                _soldTo.Left = right;
                _shipTo.Left = right;
                foreach (Control c in card.Controls)
                {
                    if (c is Label lbl && (lbl.Text == "SOLD TO" || lbl.Text == "SHIP TO"))
                        lbl.Left = right;
                }
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
            add.Click += (_, _) => AddLine(lockPrevious: true);
            _linesHint = new Label
            {
                Text = "Enter the customer PO from Sales to fill the line. After that, only that customer’s POs are suggested.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            };
            addBar.Controls.Add(_linesHint);
            addBar.Controls.Add(add);

            var labels = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Theme.Navy
            };
            labels.Resize += (_, _) => LayoutLineLabels(labels);
            labels.Paint += (_, e) =>
            {
                var slots = InvoiceLineLayout.Slots(labels.Width);
                DrawHeader(e.Graphics, "PO #", slots.Po);
                DrawHeader(e.Graphics, "PRODUCT", slots.Product);
                DrawHeader(e.Graphics, "LOT #", slots.Lot);
                DrawHeader(e.Graphics, "ORD", slots.Ordered);
                DrawHeader(e.Graphics, "SHIP", slots.Shipped);
                DrawHeader(e.Graphics, "DESCRIPTION", slots.Description);
                DrawHeader(e.Graphics, "WEIGHT", slots.Weight);
                DrawHeader(e.Graphics, "PRICE", slots.Price);
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

        private CardPanel BuildFooter()
        {
            var card = new CardPanel { Height = 118, Padding = new Padding(16, 10, 16, 10) };

            var create = new Button
            {
                Text = "Create Invoice",
                Size = new Size(150, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleGoldButton(create);
            create.Click += (_, _) => CreateInvoice();

            var clear = new Button
            {
                Text = "Clear",
                Size = new Size(90, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetDraft();

            _totalWeight = TotalLabel(card, "TOTAL WEIGHT", 20, 16);
            _subTotal = TotalLabel(card, "SUB TOTAL", 160, 16);
            _discount = AddField(card, "DISCOUNT", 300, 16, 90);
            _freight = AddField(card, "FREIGHT", 404, 16, 90);
            _tax = AddTaxField(card, 508, 16, 90);
            _invoiceTotal = TotalLabel(card, "INVOICE TOTAL", 20, 64);
            _invoiceTotal.Font = Theme.SectionTitle;
            _invoiceTotal.ForeColor = Theme.Navy;

            _discount.TextChanged += (_, _) => UpdateTotals();
            _freight.TextChanged += (_, _) => UpdateTotals();
            _tax.TextChanged += (_, _) => UpdateTotals();

            card.Controls.Add(create);
            card.Controls.Add(clear);
            create.Location = new Point(620, 64);
            clear.Location = new Point(522, 64);
            card.Resize += (_, _) =>
            {
                create.Location = new Point(Math.Max(360, card.Width - 174), 64);
                clear.Location = new Point(Math.Max(260, card.Width - 272), 64);
            };

            return card;
        }

        private void AddLine(bool lockPrevious)
        {
            if (lockPrevious)
            {
                var open = _lines.LastOrDefault(line => !line.Locked);
                if (open != null && !open.HasContent())
                {
                    open.FocusPo();
                    return;
                }

                open?.Lock();
            }

            var row = new InvoiceLineRow();
            row.SetPoSuggestions(_poSource);
            row.Changed += (_, _) => UpdateTotals();
            row.PoRequested += (_, po) =>
            {
                if (_received)
                {
                    StartAddPurchases(
                        po,
                        CurrentPartyCode(),
                        CurrentPartyName(),
                        error =>
                        {
                            if (error != null)
                                ToastAlert.Error(this, error);
                            else
                                ToastAlert.Success(this, "The information was added.");
                        });
                    return;
                }

                StartAddItems(
                    po,
                    CurrentPartyCode(),
                    CurrentPartyName(),
                    error =>
                    {
                        if (error != null)
                            ToastAlert.Error(this, error);
                        else
                            ToastAlert.Success(this, "The information was added.");
                    });
            };
            row.RemoveRequested += (_, _) => RemoveLine(row);
            _lines.Add(row);
            _lineHost.Controls.Add(row);
            LayoutLines();
            row.FocusPo();
            UpdateTotals();
        }

        private void RemoveLine(InvoiceLineRow row)
        {
            _lines.Remove(row);
            _lineHost.Controls.Remove(row);
            row.Dispose();
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
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
                row.SetBounds(_lineHost.Padding.Left, y, width, InvoiceLineRow.RowHeight);
                y += InvoiceLineRow.RowHeight + 6;
            }

            _lineHost.AutoScrollMinSize = new Size(0, y + 8);
        }

        private void RefreshLines()
        {
            LayoutLines();
            foreach (var row in _lines)
                row.Refresh();
            _lineHost.Refresh();
        }

        private static void LayoutLineLabels(Panel labels)
        {
            labels.Invalidate();
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

        private void RefreshLookups()
        {
            string current = _customer.SelectedItem is CustomerChoice choice ? choice.Code : _customerCode.Text;
            _loadingCustomer = true;
            _customer.Items.Clear();
            string table = _received ? DataFiles.Vendors : DataFiles.Customers;
            foreach (var record in DataFiles.VisibleRecords(table))
            {
                _customer.Items.Add(new CustomerChoice(
                    DataFiles.GetRecord(record, "Code"),
                    DataFiles.GetRecordAny(record, "Name", "Company"),
                    DataFiles.GetRecord(record, "Terms"),
                    DataFiles.GetRecordAny(record, "Contact Name", "Phone"),
                    DataFiles.GetRecordAny(record, "Address", "Company")));
            }

            if (!string.IsNullOrWhiteSpace(current))
            {
                for (int i = 0; i < _customer.Items.Count; i++)
                {
                    if (_customer.Items[i] is CustomerChoice c &&
                        c.Code.Equals(current, StringComparison.OrdinalIgnoreCase))
                    {
                        _customer.SelectedIndex = i;
                        break;
                    }
                }
            }

            _loadingCustomer = false;
            if (!_received && string.IsNullOrWhiteSpace(_salesRep.Text))
                _salesRep.Text = OurContact();

            RefreshPoSuggestions();
        }

        private void RefreshPoSuggestions()
        {
            _poSource = _received
                ? DataFiles.PurchasePoSuggestions(CurrentPartyCode(), CurrentPartyName())
                : DataFiles.InvoicePoSuggestions(CurrentPartyCode(), CurrentPartyName());
            foreach (var row in _lines)
                row.SetPoSuggestions(_poSource);
        }

        private string CurrentPartyCode()
        {
            if (_customer.SelectedItem is CustomerChoice choice && choice.Code.Length > 0)
                return choice.Code;
            return _customerCode.Text.Trim();
        }

        private string CurrentPartyName()
        {
            if (_customer.SelectedItem is CustomerChoice choice)
                return choice.Name;
            return _customer.Text.Trim();
        }

        private string CurrentCustomerCode() => CurrentPartyCode();

        private string CurrentCustomerName() => CurrentPartyName();

        private bool InvoiceHasCustomer()
        {
            return CurrentPartyCode().Length > 0 ||
                   _customer.SelectedItem is CustomerChoice ||
                   _lines.Any(line => line.HasContent());
        }

        private bool InvoiceHasSale()
        {
            return _lines.Any(line => line.Locked || line.HasContent()) ||
                   CurrentCustomerCode().Length > 0 ||
                   _customer.SelectedItem is CustomerChoice;
        }

        private bool InvoiceAlreadyHasPo(string key)
        {
            string want = DataFiles.NormalizePo(key);
            if (want.Length == 0)
                return false;

            foreach (var line in _lines)
            {
                var data = line.GetLine();
                if (DataFiles.NormalizePo(data.PoNumber).Equals(want, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
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

        private HashSet<string> UsedPoItemKeys()
        {
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in _lines)
            {
                var data = line.GetLine();
                string key = PoItemKey(data.PoNumber, data.ProductId);
                if (key.Length > 0)
                    used.Add(key);
            }

            return used;
        }

        private List<Dictionary<string, string>> UnusedSources(
            List<Dictionary<string, string>> sources)
        {
            var used = UsedPoItemKeys();
            return sources
                .Where(record =>
                {
                    string po = DataFiles.SalePo(record);
                    string item = DataFiles.GetRecord(record, "Item Code");
                    string itemKey = PoItemKey(po, item);
                    return itemKey.Length == 0 || !used.Contains(itemKey);
                })
                .ToList();
        }

        private void ApplyInvoiceHeader(Dictionary<string, string> invoice)
        {
            RefreshLookups();
            string number = DataFiles.GetRecord(invoice, "Invoice #").Trim();
            if (number.Length > 0)
                _invoiceNo.Text = number;

            if (_received || DataFiles.IsReceivedInvoice(invoice))
                ApplyVendorFromPurchase(invoice);
            else
                ApplyCustomerFromPurchase(invoice);
            ApplyStoredHeader(invoice);
        }

        private void ApplyDraft(InvoiceDraft draft)
        {
            SetReceivedMode(draft.Received);
            RefreshLookups();
            _invoiceNo.Text = draft.InvoiceNumber;
            _invoiceDate.Value = draft.InvoiceDate == DateTime.MinValue ? DateTime.Today : draft.InvoiceDate;
            _soNo.Text = draft.Received ? draft.PoNumber : draft.SoNumber;
            if (draft.Received)
            {
                FillVendor(draft.VendorCode, draft.VendorName, draft.Terms, draft.ShipDate);
            }
            else
            {
                FillCustomer(draft.CustomerCode, draft.CustomerName, draft.Terms, "", draft.ShipTo);
            }
            if (!draft.Received && draft.CustomerName.Length > 0)
                _customer.Text = draft.CustomerName;
            else if (draft.Received && draft.VendorName.Length > 0)
                _customer.Text = draft.VendorName;
            if (draft.Terms.Length > 0)
                _terms.Text = draft.Terms;
            _shipVia.Text = draft.ShipVia;
            if (draft.SalesRep.Length > 0)
                _salesRep.Text = draft.SalesRep;
            _shipDate.Value = draft.ShipDate == DateTime.MinValue ? DateTime.Today : draft.ShipDate;
            if (draft.SoldTo.Length > 0)
                _soldTo.Text = draft.SoldTo;
            if (draft.ShipTo.Length > 0)
                _shipTo.Text = draft.ShipTo;
            _discount.Text = MoneyField(draft.Discount);
            _freight.Text = MoneyField(draft.Freight);
            _tax.Text = MoneyField(draft.TaxRate);
            _taxPercent = draft.TaxIsPercent;
            if (_taxMode != null)
                _taxMode.Text = _taxPercent ? "%" : "#";

            foreach (var row in _lines.ToList())
            {
                _lineHost.Controls.Remove(row);
                row.Dispose();
            }
            _lines.Clear();
            foreach (var line in draft.Lines)
            {
                AddLine(lockPrevious: false);
                var row = _lines[^1];
                row.FillFromLine(line);
                row.Lock();
            }
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
            UpdateTotals();
        }

        private void ApplyStoredHeader(Dictionary<string, string> invoice)
        {
            string invoiceDate = DataFiles.GetRecord(invoice, DataFiles.InvoiceDateColumn);
            if (DateTime.TryParse(invoiceDate, out var dated))
                _invoiceDate.Value = dated;
            string terms = DataFiles.GetRecord(invoice, "Terms");
            if (terms.Length > 0)
                _terms.Text = terms;
            string via = DataFiles.GetRecord(invoice, "Ship Via");
            if (via.Length > 0)
                _shipVia.Text = via;
            string rep = DataFiles.GetRecord(invoice, "Sales Rep");
            if (rep.Length > 0)
                _salesRep.Text = rep;
            string sold = DataFiles.GetRecord(invoice, "Sold To");
            if (sold.Length > 0)
                _soldTo.Text = sold;
            string shipTo = DataFiles.GetRecord(invoice, "Ship To");
            if (shipTo.Length > 0)
                _shipTo.Text = shipTo;
            string discount = DataFiles.GetRecord(invoice, "Discount");
            if (discount.Length > 0)
                _discount.Text = discount;
            string freight = DataFiles.GetRecord(invoice, "Freight");
            if (freight.Length > 0)
                _freight.Text = freight;
            string tax = DataFiles.GetRecord(invoice, "Tax");
            if (tax.Length > 0)
                _tax.Text = tax;
            string mode = DataFiles.GetRecord(invoice, DataFiles.InvoiceTaxModeColumn);
            _taxPercent = mode.Trim() == "%";
            if (_taxMode != null)
                _taxMode.Text = _taxPercent ? "%" : "#";
        }

        private static string MoneyField(decimal value) =>
            value == 0 ? "" : value.ToString("0.##", CultureInfo.InvariantCulture);

        private void FinishCreatedPdf(Action<string?> done)
        {
            var draft = CollectDraft();
            if (draft.Lines.Count == 0)
            {
                done("No lines were found for this invoice. Add them here, then Create Invoice.");
                return;
            }

            if (!draft.Received &&
                string.IsNullOrWhiteSpace(draft.CustomerName) &&
                string.IsNullOrWhiteSpace(draft.CustomerCode))
            {
                done("This invoice has no customer. Choose one here, then Create Invoice.");
                return;
            }

            if (draft.Received &&
                string.IsNullOrWhiteSpace(draft.VendorName) &&
                string.IsNullOrWhiteSpace(draft.VendorCode))
            {
                done("This invoice has no vendor. Choose one here, then Create Invoice.");
                return;
            }

            try
            {
                SaveInvoicePdf(draft);
                ResetDraft();
                done(null);
            }
            catch (Exception ex)
            {
                done(ex.Message);
            }
        }

        private static string PoItemKey(string? po, string? item)
        {
            string poKey = DataFiles.NormalizePo(po);
            string itemKey = (item ?? "").Trim().ToUpperInvariant();
            if (poKey.Length == 0)
                return "";
            return poKey + "|" + itemKey;
        }

        /// <summary>
        /// Look up sale rows for this PO/SO and append each unused line onto the invoice,
        /// filling sold-to / ship-to from the first match.
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
                done("Still adding lines to the invoice.");
                return;
            }

            _busyAdding = true;

            Task.Run(() =>
            {
                try
                {
                    var sources = DataFiles.FindInvoiceSourcesForKey(key, customerCode, customerName);
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

                    var remaining = UnusedSources(sources);

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

                        if (remaining.Count == 0)
                        {
                            _busyAdding = false;
                            done("This PO is already on the invoice.");
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

        /// <summary>
        /// Look up purchase rows for this PO and append each unused line onto the received invoice.
        /// </summary>
        private void StartAddPurchases(string? key, string vendorCode, string vendorName, Action<string?> done)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                done("This purchase has no PO number.");
                return;
            }

            if (_busyAdding)
            {
                done("Still adding lines to the invoice.");
                return;
            }

            _busyAdding = true;
            Task.Run(() =>
            {
                try
                {
                    var sources = DataFiles.FindPurchaseSourcesForKey(key, vendorCode, vendorName);
                    if (string.IsNullOrWhiteSpace(vendorCode) &&
                        string.IsNullOrWhiteSpace(vendorName) &&
                        sources.Count > 0)
                    {
                        string firstCode = DataFiles.GetRecord(sources[0], "Vendor Code");
                        string firstName = DataFiles.GetRecord(sources[0], "Vendor");
                        sources = sources
                            .Where(record => DataFiles.MatchesVendor(record, firstCode, firstName))
                            .ToList();
                    }

                    var remaining = UnusedSources(sources);
                    BeginInvoke(new Action(() =>
                    {
                        if (IsDisposed)
                        {
                            _busyAdding = false;
                            done("Could not add that purchase.");
                            return;
                        }

                        if (sources.Count == 0)
                        {
                            _busyAdding = false;
                            done("No purchases were found for that PO.");
                            return;
                        }

                        if (remaining.Count == 0)
                        {
                            _busyAdding = false;
                            done("This PO is already on the invoice.");
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
                    {
                        if (_received)
                            ApplyVendorFromPurchase(sources[i]);
                        else
                            ApplyCustomerFromPurchase(sources[i]);
                    }
                    row.Lock();
                }

                index = end;
            }
            finally
            {
                _lineHost.ResumeLayout(true);
            }

            RefreshLines();
            UpdateTotals();

            if (index < sources.Count)
            {
                BeginInvoke(new Action(() => AddRecordsInBatches(sources, index, done)));
                return;
            }

            if (_lines.All(line => line.Locked))
                AddLine(lockPrevious: false);

            RefreshLines();
            RefreshPoSuggestions();
            _busyAdding = false;
            done(null);
        }

        private InvoiceLineRow GetOpenLine()
        {
            var open = _lines.LastOrDefault(line => !line.Locked);
            if (open != null && string.IsNullOrWhiteSpace(open.GetLine().ProductId))
                return open;

            AddLine(lockPrevious: true);
            return _lines.LastOrDefault(line => !line.Locked) ?? _lines[^1];
        }

        /// <summary>Clear header, lines, tax, and totals, then assign the next invoice #.</summary>
        private void ResetDraft(bool received = false)
        {
            foreach (var row in _lines.ToList())
            {
                _lineHost.Controls.Remove(row);
                row.Dispose();
            }

            _lines.Clear();
            SetReceivedMode(received);
            _customer.SelectedIndex = -1;
            _customerCode.Text = "";
            RefreshLookups();
            _invoiceNo.Text = DataFiles.NextNumber(DataFiles.Invoices, "Invoice #", 1001);
            _soNo.Text = received ? "" : DataFiles.NextNumber(DataFiles.Invoices, "SO #", 10001);
            _invoiceDate.Value = DateTime.Today;
            _shipDate.Value = DateTime.Today;
            _terms.Text = received ? "" : AppState.PaymentTerms;
            _shipVia.Text = "";
            _salesRep.Text = received ? "" : OurContact();
            if (received)
            {
                _soldTo.Text = DataFiles.CompanyAddressBlock();
                _shipTo.Text = (AppState.Address ?? "").Trim();
            }
            else
            {
                _soldTo.Text = "";
                _shipTo.Text = "";
            }
            _discount.Text = "";
            _freight.Text = "";
            _tax.Text = "";
            _taxPercent = false;
            if (_taxMode != null)
                _taxMode.Text = "#";
            AddLine(lockPrevious: false);
            UpdateTotals();
        }

        private void SetReceivedMode(bool received)
        {
            _received = received;
            if (_heading != null)
                _heading.Text = received ? "Received invoice" : "Invoice";
            if (_soCaption != null)
                _soCaption.Text = received ? "PO #" : "SO #";
            if (_partyCaption != null)
                _partyCaption.Text = received ? "VENDOR" : "CUSTOMER";
            if (_partyCodeCaption != null)
                _partyCodeCaption.Text = received ? "VEND ID" : "CUST ID";
            if (_salesRepCaption != null)
                _salesRepCaption.Text = received ? "CONTACT" : "SALES REP";
            if (_linesHint != null)
            {
                _linesHint.Text = received
                    ? "Lines come from the vendor purchase PO. We are the receiving company; the vendor is the issuer."
                    : "Enter the customer PO from Sales to fill the line. After that, only that customer’s POs are suggested.";
            }
        }

        private void ApplyCustomer()
        {
            if (_loadingCustomer || _customer.SelectedItem is not CustomerChoice choice)
                return;

            if (_received)
                FillVendor(choice.Code, choice.Name, choice.Terms, null);
            else
                FillCustomer(choice.Code, choice.Name, choice.Terms, choice.Contact, choice.Address);
        }

        private void ApplyVendorFromPurchase(Dictionary<string, string> record)
        {
            string code = DataFiles.GetRecordAny(record, "Vendor Code", "Code");
            string name = DataFiles.GetRecordAny(record, "Vendor", "Name", "Company");
            string terms = DataFiles.GetRecordAny(record, "Vendor Terms", "Terms");
            string ship = DataFiles.GetRecordAny(record, "Arrival Date", "Ship Date");
            DateTime? shipDate = DateTime.TryParse(ship, out var parsed) ? parsed : null;
            FillVendor(code, name, terms, shipDate);
        }

        private void FillVendor(string code, string name, string terms, DateTime? shipDate)
        {
            RefreshLookups();
            CustomerChoice? match = null;
            for (int i = 0; i < _customer.Items.Count; i++)
            {
                if (_customer.Items[i] is not CustomerChoice choice)
                    continue;
                bool byCode = code.Length > 0 && choice.Code.Equals(code, StringComparison.OrdinalIgnoreCase);
                bool byName = name.Length > 0 && choice.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
                if (!byCode && !byName)
                    continue;
                match = choice;
                _loadingCustomer = true;
                _customer.SelectedIndex = i;
                _loadingCustomer = false;
                break;
            }

            if (code.Length > 0)
                _customerCode.Text = code;
            else if (match != null)
                _customerCode.Text = match.Code;

            if (match == null && name.Length > 0)
                _customer.Text = name;

            if (terms.Length > 0)
                _terms.Text = terms;
            else if (match != null && match.Terms.Length > 0)
                _terms.Text = match.Terms;

            _soldTo.Text = DataFiles.CompanyAddressBlock();
            string shipTo = (AppState.Address ?? "").Trim();
            _shipTo.Text = shipTo.Length > 0 ? shipTo : _soldTo.Text;

            if (shipDate != null)
                _shipDate.Value = shipDate.Value;

            _salesRep.Text = VendorContact(code, name, match?.Contact);
            RefreshPoSuggestions();
        }

        private bool PrefillMatchesVendor(InvoicePurchasePrefill prefill)
        {
            string code = CurrentPartyCode();
            string name = CurrentPartyName();
            if (prefill.VendorCode.Length > 0 && code.Length > 0)
                return prefill.VendorCode.Equals(code, StringComparison.OrdinalIgnoreCase);
            if (prefill.VendorName.Length > 0 && name.Length > 0)
                return prefill.VendorName.Equals(name, StringComparison.OrdinalIgnoreCase);
            return true;
        }

        private void ApplyCustomerFromPurchase(Dictionary<string, string> record)
        {
            string code = DataFiles.GetRecordAny(record, "Customer Code", "Cust ID");
            string name = DataFiles.GetRecordAny(record, "Customer", "Customer Name");
            string terms = DataFiles.GetRecordAny(record, "Customer Terms");
            string so = DataFiles.GetRecordAny(record, "SO #", "SO NO", "SO Number");
            string contact = "";

            if (code.Length == 0 && name.Length == 0)
                return;

            CustomerChoice? match = null;
            for (int i = 0; i < _customer.Items.Count; i++)
            {
                if (_customer.Items[i] is not CustomerChoice choice)
                    continue;

                bool byCode = code.Length > 0 && choice.Code.Equals(code, StringComparison.OrdinalIgnoreCase);
                bool byName = name.Length > 0 && choice.Name.Equals(name, StringComparison.OrdinalIgnoreCase);
                if (!byCode && !byName)
                    continue;

                match = choice;
                _loadingCustomer = true;
                _customer.SelectedIndex = i;
                _loadingCustomer = false;
                break;
            }

            if (match != null)
            {
                FillCustomer(
                    match.Code,
                    match.Name,
                    terms.Length > 0 ? terms : match.Terms,
                    match.Contact,
                    match.Address);
            }
            else
            {
                FillCustomer(code, name, terms, contact, "");
                if (name.Length > 0)
                    _customer.Text = name;
            }

            if (so.Length > 0)
                _soNo.Text = so;

            string ship = DataFiles.GetRecordAny(record, "Ship Date");
            if (DateTime.TryParse(ship, out var shipDate))
                _shipDate.Value = shipDate;
        }

        private void FillCustomer(string code, string name, string terms, string contact, string address)
        {
            if (code.Length > 0)
                _customerCode.Text = code;
            if (terms.Length > 0)
                _terms.Text = terms;

            var sold = new List<string>();
            if (name.Length > 0)
                sold.Add(name);
            if (contact.Length > 0)
                sold.Add(contact);
            if (sold.Count > 0)
                _soldTo.Text = string.Join(Environment.NewLine, sold);

            _shipTo.Text = address.Trim();

            RefreshPoSuggestions();
        }

        private InvoiceDraft CollectDraft()
        {
            string partyCode = _customerCode.Text.Trim();
            string partyName = _customer.SelectedItem is CustomerChoice c ? c.Name : _customer.Text.Trim();
            var issuer = CurrentIssuer();
            return new InvoiceDraft
            {
                InvoiceNumber = _invoiceNo.Text.Trim(),
                InvoiceDate = _invoiceDate.Value.Date,
                SoNumber = _received ? "" : _soNo.Text.Trim(),
                PoNumber = _received ? _soNo.Text.Trim() : FirstLinePo(),
                Received = _received,
                CustomerCode = _received ? "" : partyCode,
                CustomerName = _received ? "" : partyName,
                VendorCode = _received ? partyCode : "",
                VendorName = _received ? partyName : "",
                IssuerName = issuer.Name,
                IssuerAddress = issuer.Address,
                IssuerPhone = issuer.Phone,
                Terms = _terms.Text.Trim(),
                ShipVia = _shipVia.Text.Trim(),
                SalesRep = _salesRep.Text.Trim(),
                ShipDate = _shipDate.Value.Date,
                SoldTo = _soldTo.Text.Trim(),
                ShipTo = _shipTo.Text.Trim(),
                Discount = InvoiceLineRow.ParseNumber(_discount.Text),
                Freight = InvoiceLineRow.ParseNumber(_freight.Text),
                TaxRate = InvoiceLineRow.ParseNumber(_tax.Text),
                TaxIsPercent = _taxPercent,
                Lines = _lines.Select(line => line.GetLine()).Where(line =>
                    line.PoNumber.Length > 0 ||
                    line.ProductId.Length > 0 ||
                    line.Description.Length > 0).ToList()
            };
        }

        private void UpdateTotals()
        {
            var draft = CollectDraft();
            _totalWeight.Text = draft.TotalWeight.ToString("0.###", CultureInfo.InvariantCulture);
            _subTotal.Text = draft.SubTotal.ToString("0.00", CultureInfo.InvariantCulture);
            _invoiceTotal.Text = draft.InvoiceTotal.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Draw the invoice PDF into Stored Invoices, write an invoices row if needed
        /// (PDF Created = true), and open the viewer.
        /// </summary>
        private void CreateInvoice()
        {
            if (!AppLock.HasFolder())
            {
                MessageBox.Show(
                    "Select a data folder in Settings first.",
                    "No Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var draft = CollectDraft();
            if (string.IsNullOrWhiteSpace(draft.InvoiceNumber))
            {
                MessageBox.Show("Enter an invoice number.", "Invoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!draft.Received &&
                string.IsNullOrWhiteSpace(draft.CustomerName) &&
                string.IsNullOrWhiteSpace(draft.CustomerCode))
            {
                MessageBox.Show("Choose a customer.", "Invoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (draft.Received &&
                string.IsNullOrWhiteSpace(draft.VendorName) &&
                string.IsNullOrWhiteSpace(draft.VendorCode))
            {
                MessageBox.Show("Choose a vendor.", "Invoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (draft.Lines.Count == 0)
            {
                MessageBox.Show(
                    draft.Received
                        ? "Add at least one line. Enter the vendor PO to fill it."
                        : "Add at least one line. Enter a customer PO to fill it.",
                    "Invoice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            try
            {
                SaveInvoicePdf(draft);
                ToastAlert.Success(this, $"Invoice {draft.InvoiceNumber} was saved.");
                ResetDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invoice Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void SaveInvoicePdf(InvoiceDraft draft)
        {
            DateTime due = DueDate(draft.ShipDate, draft.Terms);
            string pdfPath = InvoiceDocument.Save(draft);
            DataFiles.UpsertInvoiceFromDraft(draft, due);
            DataFiles.OpenPdf(pdfPath);
        }

        private static DateTime DueDate(DateTime ship, string terms)
        {
            var match = System.Text.RegularExpressions.Regex.Match(terms ?? "", @"\d+");
            if (match.Success && int.TryParse(match.Value, out int days))
                return ship.AddDays(days);
            return ship.AddDays(15);
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

        private TextBox AddTaxField(Control parent, int x, int y, int width)
        {
            const int buttonWidth = 28;
            var label = new Label { Text = "TAX" };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);

            var box = new TextBox();
            Theme.StyleField(box);
            box.Location = new Point(x, y + 16);
            box.Size = new Size(Math.Max(40, width - buttonWidth - 4), 26);

            _taxMode = new Button
            {
                Text = "#",
                Location = new Point(x + width - buttonWidth, y + 16),
                Size = new Size(buttonWidth, 26),
                TabStop = false
            };
            Theme.StyleOutlineButton(_taxMode);
            _taxMode.Click += (_, _) =>
            {
                _taxPercent = !_taxPercent;
                _taxMode.Text = _taxPercent ? "%" : "#";
                UpdateTotals();
            };

            parent.Controls.Add(label);
            parent.Controls.Add(box);
            parent.Controls.Add(_taxMode);
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

        private ComboBox AddCustomer(Control parent, string caption, int x, int y, int width)
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
            box.SelectedIndexChanged += (_, _) => ApplyCustomer();
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
                Text = "0.00",
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(x, y + 16)
            };
            parent.Controls.Add(label);
            parent.Controls.Add(value);
            return value;
        }

        private static Label CaptionAt(Control parent, string caption) =>
            parent.Controls.OfType<Label>().First(label => label.Text == caption);

        private string FirstLinePo()
        {
            foreach (var line in _lines)
            {
                string po = line.GetLine().PoNumber.Trim();
                if (po.Length > 0)
                    return po;
            }

            return "";
        }

        private (string Name, string Address, string Phone) CurrentIssuer()
        {
            if (!_received)
            {
                return (
                    FirstNonEmpty(AppState.BusinessName, "Cast Right Catch Co."),
                    AppState.Address ?? "",
                    AppState.Phone ?? "");
            }

            string code = CurrentPartyCode();
            string name = CurrentPartyName();
            foreach (var record in DataFiles.VisibleRecords(DataFiles.Vendors))
            {
                if (!DataFiles.MatchesVendor(record, code, name))
                    continue;
                return (
                    FirstNonEmpty(
                        DataFiles.GetRecord(record, "Company"),
                        DataFiles.GetRecord(record, "Name"),
                        name),
                    DataFiles.GetRecordAny(record, "Address", "Description"),
                    DataFiles.GetRecord(record, "Phone"));
            }

            return (name, "", "");
        }

        private static string OurContact() =>
            FirstNonEmpty(AppState.UserEmail, AppState.CompanyEmail, AppState.Phone);

        private static string VendorContact(string code, string name, string? known)
        {
            if (!string.IsNullOrWhiteSpace(known) &&
                !known.Equals(name, StringComparison.OrdinalIgnoreCase))
                return known.Trim();

            foreach (var record in DataFiles.VisibleRecords(DataFiles.Vendors))
            {
                if (!DataFiles.MatchesVendor(record, code, name))
                    continue;
                return FirstNonEmpty(
                    DataFiles.GetRecord(record, "Contact Name"),
                    DataFiles.GetRecord(record, "Phone"));
            }

            return (known ?? "").Trim();
        }

        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

        private sealed class CustomerChoice
        {
            public string Code { get; }
            public string Name { get; }
            public string Terms { get; }
            public string Contact { get; }
            public string Address { get; }

            public CustomerChoice(string code, string name, string terms, string contact, string address)
            {
                Code = code;
                Name = name;
                Terms = terms;
                Contact = contact;
                Address = address;
            }

            public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Code : $"{Name} ({Code})";
        }
    }

    /// <summary>Sales rows carried from the Sales page into Create Invoice.</summary>
    internal sealed class InvoiceSalePrefill
    {
        public string Po { get; set; } = "";
        public string So { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
    }

    /// <summary>Purchase rows carried from Purchases into a received invoice.</summary>
    internal sealed class InvoicePurchasePrefill
    {
        public string Po { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string VendorCode { get; set; } = "";
        public string VendorName { get; set; } = "";
        public string VendorTerms { get; set; } = "";
        public DateTime? ShipDate { get; set; }
    }
}
