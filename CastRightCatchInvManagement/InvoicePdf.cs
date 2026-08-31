using System.Globalization;

namespace CastRightCatchInvManagement
{
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
        private bool _taxPercent;
        private Label _invoiceTotal = null!;
        private bool _loadingCustomer;
        private bool _busyAdding;

        public InvoicePdf()
        {
            InitializeComponent();
            BuildUi();
            ResetDraft();
        }

        public void HighlightCurrentPage()
        {
            RefreshLookups();
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);
            RefreshLines();
        }

        internal void TryAddSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);

            if (InvoiceHasCustomer() && !PrefillMatchesCustomer(prefill))
            {
                done("Customer does not match. Please remove old data or pick a different sale.");
                return;
            }

            string key = prefill.Po.Length > 0 ? prefill.Po : prefill.So;
            string code = InvoiceHasCustomer() ? CurrentCustomerCode() : prefill.CustomerCode;
            string name = InvoiceHasCustomer() ? CurrentCustomerName() : prefill.CustomerName;
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
        }

        private CardPanel BuildHeader()
        {
            var card = new CardPanel { Height = 214, Padding = new Padding(16, 12, 16, 12) };

            var heading = new Label
            {
                Text = "Invoice",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 8)
            };
            card.Controls.Add(heading);

            _invoiceNo = AddField(card, "INVOICE NO.", 20, 36, 110);
            _invoiceDate = AddDate(card, "INVOICE DATE", 144, 36, 120);
            _soNo = AddField(card, "SO #", 278, 36, 110);

            _customer = AddCustomer(card, "CUSTOMER", 20, 86, 250);
            _customerCode = AddField(card, "CUST ID", 284, 86, 90);
            _terms = AddField(card, "TERMS", 388, 86, 160);

            _shipDate = AddDate(card, "SHIP DATE", 20, 136, 120);
            _shipVia = AddField(card, "SHIP VIA", 154, 136, 180);
            _salesRep = AddField(card, "SALES REP", 348, 136, 200);

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
            var hint = new Label
            {
                Text = "Enter the customer PO from Sales to fill the line. After that, only that customer’s POs are suggested.",
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
            foreach (var record in DataFiles.ReadRecords(DataFiles.Customers))
            {
                _customer.Items.Add(new CustomerChoice(
                    DataFiles.GetRecord(record, "Code"),
                    DataFiles.GetRecord(record, "Name"),
                    DataFiles.GetRecord(record, "Terms"),
                    DataFiles.GetRecord(record, "Contact Name"),
                    DataFiles.GetRecord(record, "Address")));
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
            if (string.IsNullOrWhiteSpace(_salesRep.Text))
                _salesRep.Text = AppState.UserEmail;

            RefreshPoSuggestions();
        }

        private void RefreshPoSuggestions()
        {
            _poSource = DataFiles.InvoicePoSuggestions(
                CurrentCustomerCode(),
                CurrentCustomerName());
            foreach (var row in _lines)
                row.SetPoSuggestions(_poSource);
        }

        private string CurrentCustomerCode()
        {
            if (_customer.SelectedItem is CustomerChoice choice && choice.Code.Length > 0)
                return choice.Code;
            return _customerCode.Text.Trim();
        }

        private string CurrentCustomerName()
        {
            if (_customer.SelectedItem is CustomerChoice choice)
                return choice.Name;
            return _customer.Text.Trim();
        }

        private bool InvoiceHasCustomer()
        {
            return CurrentCustomerCode().Length > 0 ||
                   _customer.SelectedItem is CustomerChoice ||
                   _lines.Any(line => line.HasContent());
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

        private static string PoItemKey(string? po, string? item)
        {
            string poKey = DataFiles.NormalizePo(po);
            string itemKey = (item ?? "").Trim().ToUpperInvariant();
            if (poKey.Length == 0)
                return "";
            return poKey + "|" + itemKey;
        }

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
            var used = UsedPoItemKeys();

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

                    var remaining = sources
                        .Where(record =>
                        {
                            string po = DataFiles.SalePo(record);
                            string item = DataFiles.GetRecord(record, "Item Code");
                            string itemKey = PoItemKey(po, item);
                            return itemKey.Length == 0 || !used.Contains(itemKey);
                        })
                        .ToList();

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
                        ApplyCustomerFromPurchase(sources[i]);
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

        private void ResetDraft()
        {
            foreach (var row in _lines.ToList())
            {
                _lineHost.Controls.Remove(row);
                row.Dispose();
            }

            _lines.Clear();
            _customer.SelectedIndex = -1;
            _customerCode.Text = "";
            RefreshLookups();
            _invoiceNo.Text = DataFiles.NextNumber(DataFiles.Invoices, "Invoice #", 1001);
            _soNo.Text = DataFiles.NextNumber(DataFiles.Invoices, "SO #", 10001);
            _invoiceDate.Value = DateTime.Today;
            _shipDate.Value = DateTime.Today;
            _terms.Text = AppState.PaymentTerms;
            _shipVia.Text = "";
            _salesRep.Text = AppState.UserEmail;
            _soldTo.Text = "";
            _shipTo.Text = "";
            _discount.Text = "";
            _freight.Text = "";
            _tax.Text = "";
            _taxPercent = false;
            if (_taxMode != null)
                _taxMode.Text = "#";
            AddLine(lockPrevious: false);
            UpdateTotals();
        }

        private void ApplyCustomer()
        {
            if (_loadingCustomer || _customer.SelectedItem is not CustomerChoice choice)
                return;

            FillCustomer(choice.Code, choice.Name, choice.Terms, choice.Contact, choice.Address);
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
            return new InvoiceDraft
            {
                InvoiceNumber = _invoiceNo.Text.Trim(),
                InvoiceDate = _invoiceDate.Value.Date,
                SoNumber = _soNo.Text.Trim(),
                CustomerCode = _customerCode.Text.Trim(),
                CustomerName = _customer.SelectedItem is CustomerChoice c ? c.Name : _customer.Text.Trim(),
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

            if (string.IsNullOrWhiteSpace(draft.CustomerName) && string.IsNullOrWhiteSpace(draft.CustomerCode))
            {
                MessageBox.Show("Choose a customer.", "Invoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (draft.Lines.Count == 0)
            {
                MessageBox.Show("Add at least one line. Enter a customer PO to fill it.", "Invoice", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            DateTime due = DueDate(draft.ShipDate, draft.Terms);
            try
            {
                string pdfPath = InvoiceDocument.Save(draft);
                DataFiles.AppendRow(DataFiles.Invoices, new[]
                {
                    draft.InvoiceNumber,
                    draft.SoNumber,
                    draft.CustomerCode,
                    draft.CustomerName,
                    CsvIO.Date(draft.ShipDate),
                    CsvIO.Date(due),
                    CsvIO.Money((double)draft.InvoiceTotal),
                    "",
                    CsvIO.Money((double)draft.InvoiceTotal),
                    "Open",
                    "",
                    ""
                });

                MessageBox.Show(
                    $"Invoice {draft.InvoiceNumber} was saved.\n\n{pdfPath}",
                    "Invoice Created",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                ResetDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Invoice Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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

    internal sealed class InvoiceSalePrefill
    {
        public string Po { get; set; } = "";
        public string So { get; set; } = "";
        public string ItemCode { get; set; } = "";
        public string CustomerCode { get; set; } = "";
        public string CustomerName { get; set; } = "";
    }
}
