using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Create Invoice form. Stores the PDF in the database and writes the invoice row.</summary>
    public partial class InvoicePdf : Form, INavigationPage
    {
        private readonly List<InvoiceLineRow> _lines = new();
        private AutoCompleteStringCollection _poSource = new();
        private List<LookupSuggest.Hit> _orderHits = new();
        private LookupSuggest? _soSuggest;
        private string _filledKey = "";
        private bool _filledPurchase;

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
        private ComboBox _freightCo = null!;
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
        private bool _editing;
        private string _editInvoice = "";
        private Button _save = null!;
        private byte[]? _importPdf;
        private string _importName = "";

        internal static PurchaseInvoiceDraft? PendingImport { get; set; }

        /// <summary>Build Create Invoice and start on a blank outgoing draft.</summary>
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
            // OpenImport queued a parsed PDF for review on this page.
            if (PendingImport != null)
            {
                var draft = PendingImport;
                PendingImport = null;
                ApplyImport(draft);
                return;
            }

            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
            RefreshLines();
        }

        /// <summary>Pick an invoice PDF, detect incoming vs outgoing, and fill this form.</summary>
        public static void OpenImport()
        {
            // View-only accounts cannot write invoice rows.
            if (!DataAccess.CanMutate(DataFiles.Invoices))
            {
                MessageBox.Show(
                    "This account can only view invoices.",
                    "Import Invoice PDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Cancel leaves this page unopened.
            if (!PurchaseInvoiceImport.TryPick(Form.ActiveForm, out string fileName, out byte[] bytes))
                return;

            PurchaseInvoiceDraft draft;
            try
            {
                draft = PurchaseInvoiceImport.Read(bytes, fileName);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Import Invoice PDF", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Scans and unreadable PDFs still ask before attaching a blank draft.
            if (!ConfirmImport(draft))
                return;

            PendingImport = draft;
            Navigator.GoTo(AppPage.InvoicePdf);
        }

        /// <summary>
        /// Add matching sale lines for this PO/SO onto the invoice.
        /// If another sale is already on the invoice, the draft is cleared and replaced.
        /// <paramref name="done"/> gets an error string, or null on success.
        /// </summary>
        internal void TryAddSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            string key = prefill.Po.Length > 0 ? prefill.Po : prefill.So;
            // A different customer or PO replaces the current draft instead of mixing invoices.
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
            // Prefill looks up purchase lines by PO.
            if (key.Length == 0)
            {
                done("This purchase has no PO number.");
                return;
            }

            // A different vendor, or an issued draft, is replaced by this received invoice.
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
            // A second rebuild would race the first background lookup.
            if (_busyAdding)
            {
                done("Still adding lines to the invoice.");
                return;
            }

            bool received = DataFiles.IsReceivedInvoice(invoice);
            // Prefer the snapshot stored on the invoices row over rebuilding from sales.
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
                    // Retry without customer filters when the invoice row has stale party fields.
                    if (sources.Count == 0)
                        sources = DataFiles.FindInvoiceSourcesForInvoice(
                            invoiceNo, so, null, null);
                    BeginInvoke(new Action(() =>
                    {
                        // The form may have closed while the lookup ran.
                        if (IsDisposed)
                        {
                            _busyAdding = false;
                            done("Could not create that invoice PDF.");
                            return;
                        }

                        var remaining = UnusedSources(sources);
                        // Snapshot/header already has the lines; just write the PDF.
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
                    // Store lookup errors must surface on the UI thread.
                    BeginInvoke(new Action(() =>
                    {
                        _busyAdding = false;
                        done(ex.Message);
                    }));
                }
            });
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
        }

        /// <summary>Invoice header: number, party, terms, ship-to, and import PDF.</summary>
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

            var import = new Button
            {
                Text = "Import Invoice PDF",
                Size = new Size(160, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleNavyButton(import);
            import.Click += (_, _) => ImportPdf();
            card.Controls.Add(import);
            import.Location = new Point(420, 6);

            _invoiceNo = AddField(card, "INVOICE NO.", 20, 36, 110);
            _invoiceDate = AddDate(card, "INVOICE DATE", 144, 36, 120);
            _soNo = AddField(card, "SO / PO #", 278, 36, 150);
            _soSuggest = new LookupSuggest(
                _soNo,
                () => _orderHits,
                codeFirst: true,
                ApplyOrderHit,
                minListWidth: 360);
            _soNo.Leave += (_, _) => TryFillTypedOrder();

            _customer = AddCustomer(card, "CUSTOMER", 20, 86, 250);
            _customerCode = AddField(card, "CUST ID", 284, 86, 90);
            _terms = AddField(card, "TERMS", 388, 86, 160);

            _shipDate = AddDate(card, "SHIP DATE", 20, 136, 120);
            _shipVia = AddField(card, "SHIP VIA", 154, 136, 180);
            _salesRep = AddField(card, "SALES REP", 348, 136, 200);

            _soCaption = CaptionAt(card, "SO / PO #");
            _partyCaption = CaptionAt(card, "CUSTOMER");
            _partyCodeCaption = CaptionAt(card, "CUST ID");
            _salesRepCaption = CaptionAt(card, "SALES REP");

            _soldTo = AddMultiline(card, "SOLD TO", 564, 36, 220, 58);
            _shipTo = AddMultiline(card, "SHIP TO", 564, 110, 220, 58);
            _shipTo.PlaceholderText = "Not found, please input manually";

            card.Resize += (_, _) =>
            {
                import.Location = new Point(Math.Max(220, card.Width - 180), 6);
                int right = Math.Max(180, card.Width - 260);
                _soldTo.Left = right;
                _shipTo.Left = right;
                foreach (Control c in card.Controls)
                {
                    // Keep Sold To / Ship To captions aligned with their boxes on resize.
                    if (c is Label lbl && (lbl.Text == "SOLD TO" || lbl.Text == "SHIP TO"))
                        lbl.Left = right;
                }
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

        /// <summary>Weight, subtotal, discount, freight, tax, and Create Invoice.</summary>
        private CardPanel BuildFooter()
        {
            var card = new CardPanel { Height = 128, Padding = new Padding(16, 10, 16, 10) };

            _save = new Button
            {
                Text = "Create Invoice",
                Size = new Size(150, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleGoldButton(_save);
            _save.Click += (_, _) => CreateInvoice();

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
            _freightCo = AddCombo(card, "FREIGHT CO", 300, 64, 200);
            _invoiceTotal.Font = Theme.SectionTitle;
            _invoiceTotal.ForeColor = Theme.Navy;

            _discount.TextChanged += (_, _) => UpdateTotals();
            _freight.TextChanged += (_, _) => UpdateTotals();
            _tax.TextChanged += (_, _) => UpdateTotals();

            card.Controls.Add(_save);
            card.Controls.Add(clear);
            _save.Location = new Point(620, 64);
            clear.Location = new Point(522, 64);
            card.Resize += (_, _) =>
            {
                _save.Location = new Point(Math.Max(360, card.Width - 174), 64);
                clear.Location = new Point(Math.Max(260, card.Width - 272), 64);
            };

            return card;
        }

        /// <summary>Append a blank invoice line, optionally locking the previous filled row.</summary>
        private void AddLine(bool lockPrevious)
        {
            // "+" should not stack extra empty rows; reuse the last unlocked blank line.
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
                // Received invoices fill from purchase POs; issued invoices fill from sales.
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

        /// <summary>Drop a product line, keeping at least one empty row so a PO can still be typed.</summary>
        private void RemoveLine(InvoiceLineRow row)
        {
            _lines.Remove(row);
            _lineHost.Controls.Remove(row);
            row.Dispose();
            // An invoice with zero lines cannot be saved; leave a blank row instead.
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
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
                row.SetBounds(_lineHost.Padding.Left, y, width, InvoiceLineRow.RowHeight);
                y += InvoiceLineRow.RowHeight + 6;
            }

            _lineHost.AutoScrollMinSize = new Size(0, y + 8);
        }

        /// <summary>Relayout and repaint locked rows after a batch fill.</summary>
        private void RefreshLines()
        {
            LayoutLines();
            foreach (var row in _lines)
                row.Refresh();
            _lineHost.Refresh();
        }

        /// <summary>Invalidate the navy header so column captions redraw at the new width.</summary>
        private static void LayoutLineLabels(Panel labels)
        {
            labels.Invalidate();
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

        /// <summary>Reload customer/vendor combo, freight, and SO/PO suggestions.</summary>
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

            // Restore the previously selected party after the combo is rebuilt.
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
            // Issued invoices default the sales-rep box to the signed-in user.
            if (!_received && string.IsNullOrWhiteSpace(_salesRep.Text))
                _salesRep.Text = OurContact();

            if (_freightCo != null)
                VendorChoice.Fill(_freightCo);
            _orderHits = DataFiles.SalesOrderSuggestHits();
            _orderHits.AddRange(DataFiles.PurchaseOrderSuggestHits());
            RefreshPoSuggestions();
        }

        /// <summary>Limit line PO autocomplete to unused POs for the current party.</summary>
        private void RefreshPoSuggestions()
        {
            _poSource = _received
                ? DataFiles.PurchasePoSuggestions(CurrentPartyCode(), CurrentPartyName())
                : DataFiles.InvoicePoSuggestions(CurrentPartyCode(), CurrentPartyName());
            foreach (var row in _lines)
                row.SetPoSuggestions(_poSource);
        }

        /// <summary>Selected party code, or the typed Cust/Vend ID when the combo has no pick.</summary>
        private string CurrentPartyCode()
        {
            if (_customer.SelectedItem is CustomerChoice choice && choice.Code.Length > 0)
                return choice.Code;
            return _customerCode.Text.Trim();
        }

        /// <summary>Selected party name, or the typed combo text when nothing is selected.</summary>
        private string CurrentPartyName()
        {
            if (_customer.SelectedItem is CustomerChoice choice)
                return choice.Name;
            return _customer.Text.Trim();
        }

        /// <summary>Current party code; alias used by sale-prefill matching.</summary>
        private string CurrentCustomerCode() => CurrentPartyCode();

        /// <summary>Current party name; alias used by sale-prefill matching.</summary>
        private string CurrentCustomerName() => CurrentPartyName();

        /// <summary>True when the draft already has a party or product lines.</summary>
        private bool InvoiceHasCustomer()
        {
            return CurrentPartyCode().Length > 0 ||
                   _customer.SelectedItem is CustomerChoice ||
                   _lines.Any(line => line.HasContent());
        }

        /// <summary>True when the draft already has lines or a chosen party and should not mix another invoice.</summary>
        private bool InvoiceHasSale()
        {
            return _lines.Any(line => line.Locked || line.HasContent()) ||
                   CurrentCustomerCode().Length > 0 ||
                   _customer.SelectedItem is CustomerChoice;
        }

        /// <summary>True when a line on this invoice already uses this PO.</summary>
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

        /// <summary>True when the incoming sale is the same customer as this draft, or the draft has none.</summary>
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

        /// <summary>PO|item keys already on the invoice so unused sale/purchase lines can be appended.</summary>
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

        /// <summary>Drop source rows whose PO+item is already on the invoice.</summary>
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

        /// <summary>Fill header fields from an invoices-grid row, choosing vendor vs customer mode.</summary>
        private void ApplyInvoiceHeader(Dictionary<string, string> invoice)
        {
            RefreshLookups();
            string number = DataFiles.GetRecord(invoice, "Invoice #").Trim();
            if (number.Length > 0)
                _invoiceNo.Text = number;

            // Received invoices are vendor-issued; others fill the customer.
            if (_received || DataFiles.IsReceivedInvoice(invoice))
                ApplyVendorFromPurchase(invoice);
            else
                ApplyCustomerFromPurchase(invoice);
            ApplyStoredHeader(invoice);
        }

        /// <summary>Replace the form with a stored invoice snapshot, locking filled lines.</summary>
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
            // Combo Text is needed when the party is not in the lookup list.
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
            VendorChoice.Select(_freightCo, draft.FreightCompany);
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
            // Always keep one empty line under the locked snapshot rows.
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
            UpdateTotals();
        }

        /// <summary>Copy optional header cells from an invoices-table row onto the form.</summary>
        private void ApplyStoredHeader(Dictionary<string, string> invoice)
        {
            string invoiceDate = DataFiles.GetRecord(invoice, DataFiles.InvoiceDateColumn);
            if (DateTime.TryParse(invoiceDate, out var dated))
                _invoiceDate.Value = dated;
            string terms = DataFiles.GetRecord(invoice, "Terms");
            // Only overwrite when the stored row has a value.
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
            string freightCo = DataFiles.GetRecordAny(invoice, DataFiles.FreightCompanyColumn, "Forwarder", "Logistics");
            if (freightCo.Length > 0)
                VendorChoice.Select(_freightCo, freightCo);
            string tax = DataFiles.GetRecord(invoice, "Tax");
            if (tax.Length > 0)
                _tax.Text = tax;
            string mode = DataFiles.GetRecord(invoice, DataFiles.InvoiceTaxModeColumn);
            _taxPercent = mode.Trim() == "%";
            if (_taxMode != null)
                _taxMode.Text = _taxPercent ? "%" : "#";
        }

        /// <summary>Format a money field, leaving zero as blank so the box stays empty.</summary>
        private static string MoneyField(decimal value) =>
            value == 0 ? "" : value.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Validate the rebuilt draft, store the PDF, then report success or the missing field.</summary>
        private void FinishCreatedPdf(Action<string?> done)
        {
            var draft = CollectDraft();
            if (draft.Lines.Count == 0)
            {
                done("No lines were found for this invoice. Add them here, then Create Invoice.");
                return;
            }

            // Issued invoices need a customer before the PDF can be stored.
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

        /// <summary>PO|item identity used to skip sale/purchase rows already on the invoice.</summary>
        private static string PoItemKey(string? po, string? item)
        {
            string poKey = DataFiles.NormalizePo(po);
            string itemKey = (item ?? "").Trim().ToUpperInvariant();
            // A line with no PO cannot be matched later.
            if (poKey.Length == 0)
                return "";
            return poKey + "|" + itemKey;
        }

        /// <summary>
        /// Look up sale rows for this PO/SO and append each unused line onto the invoice,
        /// filling sold-to / ship-to from the first match.
        /// </summary>
        private void ApplyOrderHit(LookupSuggest.Hit hit)
        {
            _soNo.Text = hit.Code;
            // Extra is "PO" for purchase suggestions and "SO" for sales orders.
            if (hit.Extra.Equals("PO", StringComparison.OrdinalIgnoreCase))
                FillFromPurchaseOrder(hit.Code);
            else
                FillFromSalesOrder(hit.Code);
        }

        /// <summary>When SO/PO leaves focus, fill from a unique known sales order or purchase PO.</summary>
        private void TryFillTypedOrder()
        {
            // A fill already in flight would race this leave-handler.
            if (_busyAdding)
                return;

            string key = _soNo.Text.Trim();
            if (key.Length == 0)
                return;
            // Leave should not reload the same number we just filled.
            if (AlreadyFilled(key, purchase: null))
                return;

            bool so = KnownOrder(key, purchase: false);
            bool po = KnownOrder(key, purchase: true);
            // Ambiguous numbers wait for an explicit suggestion pick.
            if (so && po)
                return;
            if (po)
                FillFromPurchaseOrder(key);
            else if (so)
                FillFromSalesOrder(key);
        }

        /// <summary>True when this number is in the SO or PO suggestion list.</summary>
        private bool KnownOrder(string key, bool purchase)
        {
            string needle = DataFiles.NormalizePo(key);
            string kind = purchase ? "PO" : "SO";
            foreach (var hit in _orderHits)
            {
                if (!hit.Extra.Equals(kind, StringComparison.OrdinalIgnoreCase))
                    continue;
                if (DataFiles.NormalizePo(hit.Code).Equals(needle, StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }

        /// <summary>True when this SO/PO was the last number filled, optionally matching purchase vs sale.</summary>
        private bool AlreadyFilled(string key, bool? purchase)
        {
            if (!key.Equals(_filledKey, StringComparison.OrdinalIgnoreCase))
                return false;
            return purchase == null || purchase == _filledPurchase;
        }

        /// <summary>Replace the draft with every sale line on that sales order, plus customer and ship-to.</summary>
        private void FillFromSalesOrder(string so)
        {
            if (string.IsNullOrWhiteSpace(so))
                return;
            if (AlreadyFilled(so, purchase: false))
                return;

            var existing = DataFiles.FindInvoiceByOrder(so, received: false);
            // Reopen the stored invoice instead of creating a second one for the same SO.
            if (existing != null)
            {
                ResetDraft();
                BeginEdit(existing);
            }
            else if (InvoiceHasSale() || _received)
            {
                // A different draft (or a received invoice) cannot share this SO.
                ResetDraft();
            }

            _soNo.Text = so;
            _filledKey = so;
            _filledPurchase = false;
            SetReceivedMode(false);
            StartAddItems(
                so,
                "",
                "",
                error =>
                {
                    if (error != null)
                        ToastAlert.Error(this, error);
                    else
                        ToastAlert.Success(this, _editing
                            ? "Editing invoice " + _invoiceNo.Text.Trim() + "."
                            : "The sales order was added.");
                },
                salesOrderOnly: true);
        }

        /// <summary>Switch to a received invoice and fill every purchase line on that PO.</summary>
        private void FillFromPurchaseOrder(string po)
        {
            if (string.IsNullOrWhiteSpace(po))
                return;
            if (AlreadyFilled(po, purchase: true))
                return;

            var existing = DataFiles.FindInvoiceByOrder(po, received: true);
            // Reopen the stored received invoice instead of creating a second one for the same PO.
            if (existing != null)
            {
                ResetDraft(received: true);
                BeginEdit(existing);
            }
            else if (InvoiceHasSale() || !_received)
            {
                // An issued draft cannot share this vendor PO.
                ResetDraft(received: true);
            }

            _soNo.Text = po;
            _filledKey = po;
            _filledPurchase = true;
            SetReceivedMode(true);
            StartAddPurchases(
                po,
                "",
                "",
                error =>
                {
                    if (error != null)
                        ToastAlert.Error(this, error);
                    else
                        ToastAlert.Success(this, _editing
                            ? "Editing invoice " + _invoiceNo.Text.Trim() + "."
                            : "The purchase was added.");
                },
                allowShort: true);
        }

        /// <summary>Look up sale rows for this PO/SO and append each unused line onto the invoice.</summary>
        private void StartAddItems(
            string? key,
            string customerCode,
            string customerName,
            Action<string?> done,
            bool salesOrderOnly = false)
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
                    var sources = DataFiles.FindInvoiceSourcesForKey(
                        key, customerCode, customerName, salesOrderOnly);
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

                    var remaining = UnusedSources(sources);

                    BeginInvoke(new Action(() =>
                    {
                        // The form may have closed while the lookup ran.
                        if (IsDisposed)
                        {
                            _busyAdding = false;
                            done("Could not add that sale.");
                            return;
                        }

                        if (sources.Count == 0)
                        {
                            _busyAdding = false;
                            done(salesOrderOnly
                                ? "No sales were found for that sales order."
                                : "No sales were found for that PO.");
                            return;
                        }

                        if (remaining.Count == 0)
                        {
                            _busyAdding = false;
                            done(salesOrderOnly
                                ? "This sales order is already on the invoice."
                                : "This PO is already on the invoice.");
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
        private void StartAddPurchases(
            string? key,
            string vendorCode,
            string vendorName,
            Action<string?> done,
            bool allowShort = false)
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
                    var sources = DataFiles.FindPurchaseSourcesForKey(
                        key, vendorCode, vendorName, allowShort);
                    // No vendor was chosen yet: lock onto the first matching purchase's vendor.
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
                        // The form may have closed while the lookup ran.
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
                    // Header (party, ship-to, freight) comes from the first remaining row.
                    if (i == 0)
                    {
                        if (_received)
                            ApplyVendorFromPurchase(sources[i]);
                        else
                            ApplyCustomerFromPurchase(sources[i]);
                        SuggestFreightCompany(sources[i]);
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

            // Yield so the UI can paint before the next batch.
            if (index < sources.Count)
            {
                BeginInvoke(new Action(() => AddRecordsInBatches(sources, index, done)));
                return;
            }

            // Leave a blank row after the last locked line.
            if (_lines.All(line => line.Locked))
                AddLine(lockPrevious: false);

            RefreshLines();
            RefreshPoSuggestions();
            _busyAdding = false;
            done(null);
        }

        /// <summary>Reuse the last unlocked empty product cell, or add a new line, before filling.</summary>
        private InvoiceLineRow GetOpenLine()
        {
            var open = _lines.LastOrDefault(line => !line.Locked);
            // A row with only a typed PO still counts as open for the next product.
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
            _importPdf = null;
            _importName = "";
            _filledKey = "";
            _filledPurchase = false;
            SetEditMode(false);
            SetReceivedMode(received);
            _customer.SelectedIndex = -1;
            _customerCode.Text = "";
            RefreshLookups();
            _invoiceNo.Text = DataFiles.NextNumber(DataFiles.Invoices, "Invoice #", 1001);
            _soNo.Text = "";
            _invoiceDate.Value = DateTime.Today;
            _shipDate.Value = DateTime.Today;
            _terms.Text = received ? "" : AppState.PaymentTerms;
            _shipVia.Text = "";
            _salesRep.Text = received ? "" : OurContact();
            // Incoming invoices are sold/shipped to us; issued invoices start with a blank ship-to.
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
            VendorChoice.Select(_freightCo, "");
            _tax.Text = "";
            _taxPercent = false;
            if (_taxMode != null)
                _taxMode.Text = "#";
            AddLine(lockPrevious: false);
            UpdateTotals();
        }

        /// <summary>Switch to Save Invoice and load header fields from an existing invoices row.</summary>
        private void BeginEdit(Dictionary<string, string> invoice)
        {
            SetEditMode(true);
            _editInvoice = DataFiles.GetRecord(invoice, "Invoice #").Trim();
            ApplyInvoiceHeader(invoice);
        }

        /// <summary>Toggle Create vs Save caption and remember the invoice # being edited.</summary>
        private void SetEditMode(bool editing)
        {
            _editing = editing;
            if (!editing)
                _editInvoice = "";
            if (_save != null)
                _save.Text = editing ? "Save Invoice" : "Create Invoice";
            RefreshHeading();
        }

        /// <summary>Update the page title for issued vs received and new vs edit.</summary>
        private void RefreshHeading()
        {
            if (_heading == null)
                return;
            if (_editing)
                _heading.Text = _received ? "Edit received invoice" : "Edit Invoice";
            else
                _heading.Text = _received ? "Received invoice" : "Invoice";
        }

        /// <summary>Switch captions and hints for a vendor-issued invoice versus one we issue.</summary>
        private void SetReceivedMode(bool received)
        {
            _received = received;
            RefreshHeading();
            if (_soCaption != null)
                _soCaption.Text = received ? "PO #" : "SO / PO #";
            if (_partyCaption != null)
                _partyCaption.Text = received ? "VENDOR" : "CUSTOMER";
            if (_partyCodeCaption != null)
                _partyCodeCaption.Text = received ? "VEND ID" : "CUST ID";
            if (_salesRepCaption != null)
                _salesRepCaption.Text = received ? "CONTACT" : "SALES REP";
            if (_linesHint != null)
            {
                _linesHint.Text = received
                    ? "Lines come from the vendor purchase PO. We are the receiving company; the vendor is the issuer. Type a different SO # to switch back to a customer invoice."
                    : "Type a sales order or purchase PO. Suggestions show the number, party, and item count. Picking a purchase PO switches this to a received invoice.";
            }
        }

        /// <summary>Fill party fields when the customer/vendor combo changes, ignoring lookup rebuilds.</summary>
        private void ApplyCustomer()
        {
            // Combo rebuilds fire SelectedIndexChanged; ignore those.
            if (_loadingCustomer || _customer.SelectedItem is not CustomerChoice choice)
                return;

            if (_received)
                FillVendor(choice.Code, choice.Name, choice.Terms, null);
            else
                FillCustomer(choice.Code, choice.Name, choice.Terms, choice.Contact, choice.Address);
        }

        /// <summary>Fill vendor, terms, and ship date from a purchase row.</summary>
        private void ApplyVendorFromPurchase(Dictionary<string, string> record)
        {
            string code = DataFiles.GetRecordAny(record, "Vendor Code", "Code");
            string name = DataFiles.GetRecordAny(record, "Vendor", "Name", "Company");
            string terms = DataFiles.GetRecordAny(record, "Vendor Terms", "Terms");
            string ship = DataFiles.GetRecordAny(record, "Arrival Date", "Ship Date");
            DateTime? shipDate = DateTime.TryParse(ship, out var parsed) ? parsed : null;
            FillVendor(code, name, terms, shipDate);
        }

        /// <summary>Select the matching vendor, set Sold To to our company, and fill contact/terms.</summary>
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

            // Unknown vendors still show the name from the purchase/PDF.
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

        /// <summary>True when the incoming purchase is the same vendor as this draft, or the draft has none.</summary>
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

        /// <summary>Fill customer, SO, and ship date from a sale row.</summary>
        private void ApplyCustomerFromPurchase(Dictionary<string, string> record)
        {
            string code = DataFiles.GetRecordAny(record, "Customer Code", "Cust ID");
            string name = DataFiles.GetRecordAny(record, "Customer", "Customer Name");
            string terms = DataFiles.GetRecordAny(record, "Customer Terms");
            string so = DataFiles.GetRecordAny(record, "SO #", "SO NO", "SO Number");
            string contact = "";

            // A sale without a customer cannot fill the party header.
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

            // Known customers also fill contact and address; unknown sales still get code/name.
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

        /// <summary>Write customer identity, sold-to, and ship-to from a sale or lookup pick.</summary>
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

        /// <summary>Snapshot header fields and product lines for save and PDF output.</summary>
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
                FreightCompany = VendorChoice.TextOf(_freightCo),
                TaxRate = InvoiceLineRow.ParseNumber(_tax.Text),
                TaxIsPercent = _taxPercent,
                Lines = _lines.Select(line => line.GetLine()).Where(line =>
                    line.PoNumber.Length > 0 ||
                    line.ProductId.Length > 0 ||
                    line.Description.Length > 0).ToList()
            };
        }

        /// <summary>Refresh footer weight, subtotal, and invoice total from the current draft.</summary>
        private void UpdateTotals()
        {
            var draft = CollectDraft();
            _totalWeight.Text = draft.TotalWeight.ToString("0.###", CultureInfo.InvariantCulture);
            _subTotal.Text = draft.SubTotal.ToString("0.00", CultureInfo.InvariantCulture);
            _invoiceTotal.Text = draft.InvoiceTotal.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>
        /// Draw the invoice PDF into the database, write an invoices row if needed, and open the viewer.
        /// </summary>
        private void CreateInvoice()
        {
            // Invoices are stored in the chosen data folder.
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

            // Issued invoices are billed to a customer.
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

            // Blank rows are only placeholders for typing.
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
                ToastAlert.Success(this, _editing
                    ? $"Invoice {draft.InvoiceNumber} was updated."
                    : $"Invoice {draft.InvoiceNumber} was saved.");
                ResetDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invoice Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        /// <summary>Write the PDF, invoices row, and optional source PDF, then open the viewer.</summary>
        private void SaveInvoicePdf(InvoiceDraft draft)
        {
            DateTime due = DueDate(draft.ShipDate, draft.Terms);
            // Incoming vendor invoices also create/update a CRC purchase PO PDF.
            if (draft.Received &&
                draft.Lines.Any(line => line.ProductId.Length > 0 || line.Description.Length > 0))
                SaveIncomingPurchase(draft);

            // Renaming an invoice must drop the PDF stored under the old number.
            if (_editing &&
                _editInvoice.Length > 0 &&
                !_editInvoice.Equals(draft.InvoiceNumber, StringComparison.OrdinalIgnoreCase))
                DataFiles.DeleteStoredPdf(DataFiles.PdfKindInvoice, _editInvoice);
            DataFiles.DeleteStoredPdf(DataFiles.PdfKindInvoice, draft.InvoiceNumber);
            string pdfPath = InvoiceDocument.Save(draft);
            DataFiles.UpsertInvoiceFromDraft(draft, due);
            // Keep the original imported PDF beside the generated invoice PDF.
            if (_importPdf is { Length: > 0 })
            {
                DataFiles.SaveStoredPdf(
                    DataFiles.PdfKindInvoiceSource,
                    draft.InvoiceNumber,
                    string.IsNullOrWhiteSpace(_importName) ? "invoice.pdf" : _importName,
                    _importPdf);
            }

            DataFiles.OpenPdf(pdfPath, DataFiles.PdfKindInvoice, draft.InvoiceNumber);
        }

        /// <summary>
        /// Incoming vendor invoices also write a CRC purchase order PDF (and the source PDF)
        /// keyed by PO #, the same way a sales order PDF is stored.
        /// </summary>
        private void SaveIncomingPurchase(InvoiceDraft draft)
        {
            // View-only purchase access still keeps the invoice; skip writing PO rows.
            if (!DataAccess.CanMutate(DataFiles.PurchaseSales))
                return;

            string po = (draft.PoNumber ?? "").Trim();
            if (po.Length == 0)
                po = draft.Lines.Select(line => line.PoNumber.Trim()).FirstOrDefault(v => v.Length > 0) ?? "";
            if (po.Length == 0)
                po = DataFiles.NextPurchasePo();

            // Do not duplicate a purchase that already exists for this PO.
            if (DataFiles.FindPurchasesByPo(po).Count == 0)
            {
                foreach (var line in draft.Lines)
                {
                    string item = line.ProductId.Trim();
                    // Blank product rows are placeholders, not purchase lines.
                    if (item.Length == 0 && line.Description.Trim().Length == 0)
                        continue;
                    decimal lbs = InvoiceLineRow.ParseNumber(line.Weight);
                    decimal price = InvoiceLineRow.ParseNumber(line.Price);
                    decimal total = line.Amount != 0 ? line.Amount : lbs * price;
                    var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                    {
                        ["PO #"] = po,
                        ["Vendor Code"] = draft.VendorCode,
                        ["Vendor"] = draft.VendorName,
                        ["Item Code"] = item,
                        ["Description"] = line.Description,
                        ["CS"] = line.Ordered,
                        ["Volume"] = line.Weight,
                        ["Price Paid / LB"] = line.Price,
                        ["Total Cost"] = total == 0 ? "" : total.ToString("0.00", CultureInfo.InvariantCulture),
                        ["Agreement Date"] = CsvIO.Date(draft.InvoiceDate),
                        ["Vendor Terms"] = draft.Terms,
                        ["Ship Date"] = CsvIO.Date(draft.ShipDate),
                        ["Status"] = "Pending"
                    };
                    var inserted = DataFiles.MutateInsert(DataFiles.PurchaseSales, values);
                    // Stop the rest of the PO if one line cannot be stored.
                    if (!inserted.Ok)
                        throw new InvalidOperationException(inserted.Message);
                }
            }

            try
            {
                PurchaseDocument.SaveFromPo(po);
            }
            catch
            {
                // keep the invoice even if the purchase PDF cannot be written
            }

            if (_importPdf is { Length: > 0 })
            {
                try
                {
                    PurchaseInvoiceImport.AttachToPo(po, _importName, _importPdf);
                }
                catch
                {
                    // source PDF is already stored on the invoice
                }
            }

            if (string.IsNullOrWhiteSpace(draft.PoNumber))
                draft.PoNumber = po;
        }

        /// <summary>Pick an invoice PDF on this page and fill the draft after confirmation.</summary>
        private void ImportPdf()
        {
            if (!PurchaseInvoiceImport.TryPick(this, out string fileName, out byte[] bytes))
                return;

            PurchaseInvoiceDraft draft;
            try
            {
                draft = PurchaseInvoiceImport.Read(bytes, fileName);
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            if (!ConfirmImport(draft))
                return;

            ApplyImport(draft);
        }

        /// <summary>Fill Create Invoice from a parsed PDF, asking incoming vs outgoing when detection is unsure.</summary>
        private void ApplyImport(PurchaseInvoiceDraft draft)
        {
            bool incoming;
            if (draft.Incoming is bool known)
            {
                incoming = known;
            }
            else
            {
                var ask = MessageBox.Show(
                    "Could not tell if this is a vendor invoice we received, or an invoice we issued.\n\nYes = incoming (we received it).\nNo = outgoing (we issued it).",
                    "Import Invoice PDF",
                    MessageBoxButtons.YesNoCancel,
                    MessageBoxIcon.Question);
                if (ask == DialogResult.Cancel)
                    return;
                incoming = ask == DialogResult.Yes;
            }

            ResetDraft(received: incoming);
            _importPdf = draft.Pdf;
            _importName = draft.FileName;

            if (draft.InvoiceNumber.Length > 0)
                _invoiceNo.Text = draft.InvoiceNumber;
            if (draft.InvoiceDate != null)
                _invoiceDate.Value = draft.InvoiceDate.Value.Date;
            if (draft.ShipDate != null)
                _shipDate.Value = draft.ShipDate.Value.Date;
            else if (draft.InvoiceDate != null)
                _shipDate.Value = draft.InvoiceDate.Value.Date;
            if (draft.Terms.Length > 0)
                _terms.Text = draft.Terms;

            if (incoming)
            {
                FillVendor(draft.VendorCode, draft.VendorName, draft.Terms, draft.ShipDate);
                if (draft.Po.Length > 0)
                    _soNo.Text = draft.Po;
                if (draft.VendorName.Length > 0 && string.IsNullOrWhiteSpace(_customer.Text))
                    _customer.Text = draft.VendorName;
            }
            else
            {
                FillCustomer(draft.CustomerCode, draft.CustomerName, draft.Terms, "", draft.ShipTo);
                if (draft.CustomerName.Length > 0)
                    _customer.Text = draft.CustomerName;
                if (draft.SoNumber.Length > 0)
                    _soNo.Text = draft.SoNumber;
                if (draft.SoldTo.Length > 0)
                    _soldTo.Text = draft.SoldTo;
                if (draft.ShipTo.Length > 0)
                    _shipTo.Text = draft.ShipTo;
            }

            foreach (var row in _lines.ToList())
            {
                _lineHost.Controls.Remove(row);
                row.Dispose();
            }

            _lines.Clear();
            string po = incoming ? draft.Po : (draft.Po.Length > 0 ? draft.Po : "");
            foreach (var line in draft.Lines)
            {
                decimal lbs = InvoiceLineRow.ParseNumber(line.Volume);
                decimal price = InvoiceLineRow.ParseNumber(line.Price);
                AddLine(lockPrevious: false);
                _lines[^1].FillFromLine(new InvoiceLine
                {
                    PoNumber = po,
                    ProductId = line.ItemCode,
                    Description = line.Description,
                    Ordered = line.Cases,
                    Shipped = line.Cases,
                    Weight = line.Volume,
                    Price = line.Price,
                    Amount = lbs * price
                });
            }

            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
            UpdateTotals();

            // Scans still attach the PDF; the user fills the form by hand.
            if (!draft.HasText)
            {
                ToastAlert.Success(this, "No text could be read. Fill the invoice, then Create Invoice to keep the PDF.");
                return;
            }

            ToastAlert.Success(
                this,
                incoming
                    ? "Incoming vendor invoice loaded. Review, then Create Invoice. A purchase PDF will be stored with it."
                    : "Outgoing invoice loaded. Review, then Create Invoice.");
        }

        /// <summary>Ask before attaching an unreadable or scan-only PDF as a blank draft.</summary>
        private static bool ConfirmImport(PurchaseInvoiceDraft draft)
        {
            if (draft.Error is { Length: > 0 })
            {
                return MessageBox.Show(
                    "Could not read that PDF (" + draft.Error + ").\n\nAttach it and fill Create Invoice by hand?",
                    "Import Invoice PDF",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) == DialogResult.Yes;
            }

            // Readable text can fill the form without asking.
            if (draft.HasText)
                return true;

            return MessageBox.Show(
                "This PDF has no readable text (it may be a scan).\n\nAttach it and fill Create Invoice by hand?",
                "Import Invoice PDF",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning) == DialogResult.Yes;
        }

        /// <summary>Due date is ship date plus NET days from terms, defaulting to 15.</summary>
        private static DateTime DueDate(DateTime ship, string terms)
        {
            var match = System.Text.RegularExpressions.Regex.Match(terms ?? "", @"\d+");
            if (match.Success && int.TryParse(match.Value, out int days))
                return ship.AddDays(days);
            return ship.AddDays(15);
        }

        /// <summary>Fill Freight Co from the source row when the combo is still empty.</summary>
        private void SuggestFreightCompany(Dictionary<string, string> record)
        {
            if (VendorChoice.TextOf(_freightCo).Length > 0)
                return;
            string company = DataFiles.GetRecordAny(
                record,
                DataFiles.FreightCompanyColumn,
                "Forwarder",
                "Logistics");
            if (company.Length > 0)
                VendorChoice.Select(_freightCo, company);
        }

        /// <summary>Labeled combo placed at a fixed header or footer coordinate.</summary>
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

        /// <summary>Labeled text box placed at a fixed header or footer coordinate.</summary>
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

        /// <summary>Tax amount box plus a #/% toggle that switches dollar vs percent mode.</summary>
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

        /// <summary>Party combo that fills customer or vendor fields on selection.</summary>
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

        /// <summary>Labeled multiline box used for Sold To and Ship To.</summary>
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

        /// <summary>Find the existing caption label so received-mode text can be swapped.</summary>
        private static Label CaptionAt(Control parent, string caption) =>
            parent.Controls.OfType<Label>().First(label => label.Text == caption);

        /// <summary>PO from the first filled line, used as the issued-invoice PO #.</summary>
        private string FirstLinePo()
        {
            foreach (var line in _lines)
            {
                string po = line.GetLine().PoNumber.Trim();
                // Skip blank placeholder rows.
                if (po.Length > 0)
                    return po;
            }

            return "";
        }

        /// <summary>Issuer printed on the PDF: our company for issued invoices, the vendor when received.</summary>
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

        /// <summary>Default sales-rep text from the signed-in user, then company email/phone.</summary>
        private static string OurContact() =>
            FirstNonEmpty(AppState.UserEmail, AppState.CompanyEmail, AppState.Phone);

        /// <summary>Vendor contact name or phone, skipping a contact that is just the company name.</summary>
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

        /// <summary>First non-blank value, used for company and contact fallbacks.</summary>
        private static string FirstNonEmpty(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";

        /// <summary>Party lookup row used to fill code, name, terms, contact, and address.</summary>
        private sealed class CustomerChoice
        {
            public string Code { get; }
            public string Name { get; }
            public string Terms { get; }
            public string Contact { get; }
            public string Address { get; }

            /// <summary>Store one visible customer or vendor record for the party combo.</summary>
            public CustomerChoice(string code, string name, string terms, string contact, string address)
            {
                Code = code;
                Name = name;
                Terms = terms;
                Contact = contact;
                Address = address;
            }

            /// <summary>Display name with code, or code alone when the name is blank.</summary>
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
