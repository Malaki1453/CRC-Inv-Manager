using System.Globalization;

namespace CastRightCatchInvManagement
{
    public partial class SalesOrder : Form, INavigationPage
    {
        private readonly List<SalesOrderLineRow> _lines = new();
        private AutoCompleteStringCollection _poSource = new();

        private ComboBox _customer = null!;
        private TextBox _customerCode = null!;
        private TextBox _address = null!;
        private TextBox _customerPhone = null!;
        private TextBox _contact = null!;
        private TextBox _email = null!;
        private TextBox _contactPhone = null!;
        private TextBox _warehouse = null!;
        private DateTimePicker _releaseDate = null!;
        private TextBox _customerPo = null!;
        private TextBox _soNo = null!;
        private DateTimePicker _orderDate = null!;
        private TextBox _freightCo = null!;
        private TextBox _freightTerms = null!;
        private Panel _lineHost = null!;
        private Label _totalCases = null!;
        private Label _totalVolume = null!;
        private bool _loadingCustomer;
        private bool _busyAdding;
        private bool _fillingPo;

        public SalesOrder()
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

        internal void BeginFromSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            if (OrderHasCustomer() && !PrefillMatchesCustomer(prefill))
                ResetDraft();
            TryAddSale(prefill, done);
        }

        internal string? CreateOrOpenPdf()
        {
            CreateSalesOrder();
            return _soNo.Text.Trim();
        }

        internal void TryAddSale(InvoiceSalePrefill prefill, Action<string?> done)
        {
            if (_lines.Count == 0)
                AddLine(lockPrevious: false);

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
        }

        private CardPanel BuildHeader()
        {
            var card = new CardPanel { Height = 310, Padding = new Padding(16, 12, 16, 12) };

            var heading = new Label
            {
                Text = "Sales Order / Pick Ticket",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 8)
            };
            card.Controls.Add(heading);

            _customerCode = AddField(card, "CUSTOMER CODE", 20, 36, 110);
            _customer = AddCustomer(card, "CUSTOMER", 144, 36, 250);
            _contact = AddField(card, "CONTACT", 408, 36, 180);

            _address = AddMultiline(card, "SHIP TO", 20, 86, 250, 48);
            _address.PlaceholderText = "Not found, please input manually";
            _email = AddField(card, "EMAIL", 284, 86, 200);
            _contactPhone = AddField(card, "PHONE", 498, 86, 140);
            _customerPhone = AddField(card, "CUSTOMER PHONE", 284, 136, 140);

            _warehouse = AddField(card, "WAREHOUSE", 20, 188, 160);
            _releaseDate = AddDate(card, "RELEASE DATE", 194, 188, 120);
            _customerPo = AddField(card, "CUSTOMER PO", 328, 188, 160);
            _soNo = AddField(card, "SALES ORDER NO.", 502, 188, 120);

            _orderDate = AddDate(card, "ORDER DATE", 20, 238, 120);
            _freightCo = AddField(card, "FREIGHT CO", 154, 238, 180);
            _freightTerms = AddField(card, "FREIGHT TERMS", 348, 238, 160);

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
            add.Click += (_, _) => AddLine(lockPrevious: true);
            var hint = new Label
            {
                Text = "Middle-click a Sales row, or enter that customer’s PO, to fill the pick ticket.",
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
                DrawHeader(e.Graphics, "ITEM CODE", slots.Item);
                DrawHeader(e.Graphics, "LOT NO", slots.Lot);
                DrawHeader(e.Graphics, "DESCRIPTION", slots.Description);
                DrawHeader(e.Graphics, "UNIT SIZE", slots.UnitSize);
                DrawHeader(e.Graphics, "CASES", slots.Cases);
                DrawHeader(e.Graphics, "VOLUME (LBS)", slots.Volume);
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
            var card = new CardPanel { Height = 92, Padding = new Padding(16, 10, 16, 10) };

            var create = new Button
            {
                Text = "Create Sales Order",
                Size = new Size(168, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleGoldButton(create);
            create.Click += (_, _) => CreateSalesOrder();

            var clear = new Button
            {
                Text = "Clear",
                Size = new Size(90, 34),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleOutlineButton(clear);
            clear.Click += (_, _) => ResetDraft();

            _totalCases = TotalLabel(card, "TOTAL CASES", 20, 16);
            _totalVolume = TotalLabel(card, "TOTAL VOLUME (LBS)", 180, 16);

            card.Controls.Add(create);
            card.Controls.Add(clear);
            card.Resize += (_, _) =>
            {
                create.Location = new Point(Math.Max(360, card.Width - 192), 28);
                clear.Location = new Point(Math.Max(250, card.Width - 290), 28);
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
                    open.FocusItem();
                    return;
                }

                open?.Lock();
            }

            var row = new SalesOrderLineRow();
            row.Changed += (_, _) => UpdateTotals();
            row.RemoveRequested += (_, _) => RemoveLine(row);
            _lines.Add(row);
            _lineHost.Controls.Add(row);
            LayoutLines();
            row.FocusItem();
            UpdateTotals();
        }

        private void RemoveLine(SalesOrderLineRow row)
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
                row.SetBounds(_lineHost.Padding.Left, y, width, SalesOrderLineRow.RowHeight);
                y += SalesOrderLineRow.RowHeight + 6;
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
                    DataFiles.GetRecord(record, "Address"),
                    DataFiles.GetRecord(record, "Email"),
                    DataFiles.GetRecord(record, "Phone")));
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

        private bool OrderHasCustomer()
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

        private Dictionary<string, int> UsedLineCounts()
        {
            var used = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in _lines)
            {
                string key = LineIdentity(line.GetLine());
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

        private SalesOrderLineRow GetOpenLine()
        {
            var open = _lines.LastOrDefault(line => !line.Locked);
            if (open != null && string.IsNullOrWhiteSpace(open.GetLine().ItemCode))
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
            _address.Text = "";
            _customerPhone.Text = "";
            _contact.Text = "";
            _email.Text = "";
            _contactPhone.Text = "";
            _warehouse.Text = "";
            _customerPo.Text = "";
            _freightCo.Text = "";
            _freightTerms.Text = "";
            RefreshLookups();
            _soNo.Text = DataFiles.NextSalesOrderNumber();
            _orderDate.Value = DateTime.Today;
            _releaseDate.Value = DateTime.Today;
            AddLine(lockPrevious: false);
            UpdateTotals();
        }

        private void ApplyCustomer()
        {
            if (_loadingCustomer || _customer.SelectedItem is not CustomerChoice choice)
                return;

            FillCustomer(choice, choice.Terms, overwrite: true);
        }

        private void ApplySaleHeader(Dictionary<string, string> record)
        {
            string code = DataFiles.GetRecordAny(record, "Customer Code", "Cust ID");
            string name = DataFiles.GetRecordAny(record, "Customer", "Customer Name");
            string terms = DataFiles.GetRecordAny(record, "Customer Terms");
            string so = DataFiles.GetRecordAny(record, "SO #", "SO NO", "SO Number");
            string po = DataFiles.SalePo(record);

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

            if (_freightCo.Text.Trim().Length == 0)
            {
                string freight = DataFiles.GetRecordAny(record, "Forwarder", "Logistics");
                _freightCo.Text = freight;
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
            Put(_customerPhone, phone);
            if (name.Length > 0 && _customer.SelectedItem is not CustomerChoice)
                _customer.Text = name;
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
                CustomerName = _customer.SelectedItem is CustomerChoice c ? c.Name : _customer.Text.Trim(),
                Address = _address.Text.Trim(),
                CustomerPhone = _customerPhone.Text.Trim(),
                Contact = _contact.Text.Trim(),
                Email = _email.Text.Trim(),
                ContactPhone = _contactPhone.Text.Trim(),
                Warehouse = _warehouse.Text.Trim(),
                CustomerPo = _customerPo.Text.Trim(),
                FreightCompany = _freightCo.Text.Trim(),
                FreightTerms = _freightTerms.Text.Trim(),
                Lines = _lines.Select(line => line.GetLine()).Where(line =>
                    line.ItemCode.Length > 0 ||
                    line.LotNumber.Length > 0 ||
                    line.Description.Length > 0).ToList()
            };
        }

        private void UpdateTotals()
        {
            var draft = CollectDraft();
            _totalCases.Text = draft.TotalCases.ToString("0.###", CultureInfo.InvariantCulture);
            _totalVolume.Text = draft.TotalVolume.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private void CreateSalesOrder()
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
            if (string.IsNullOrWhiteSpace(draft.SoNumber))
            {
                MessageBox.Show("Enter a sales order number.", "Sales Order", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (string.IsNullOrWhiteSpace(draft.CustomerName) && string.IsNullOrWhiteSpace(draft.CustomerCode))
            {
                MessageBox.Show("Choose a customer.", "Sales Order", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (draft.Lines.Count == 0)
            {
                MessageBox.Show("Add at least one line. Middle-click a sale or enter a customer PO.", "Sales Order", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            try
            {
                var pos = draft.Lines
                    .Select(line => line.PoNumber)
                    .Where(po => po.Length > 0)
                    .ToList();
                if (draft.CustomerPo.Length > 0)
                    pos.Add(draft.CustomerPo);

                string soNumber = draft.SoNumber;
                string? existingSo = DataFiles.FindExistingSalesOrderNumber(
                    pos,
                    draft.CustomerCode,
                    draft.CustomerName);
                if (!string.IsNullOrWhiteSpace(existingSo))
                    soNumber = existingSo;

                string? pdfPath = DataFiles.FindStoredSalesOrder(soNumber);
                bool created = false;
                if (pdfPath == null)
                {
                    draft.SoNumber = soNumber;
                    pdfPath = SalesOrderDocument.Save(draft);
                    created = true;
                }

                DataFiles.AssignSalesOrderNumber(
                    pos,
                    draft.CustomerCode,
                    draft.CustomerName,
                    soNumber);
                DataFiles.OpenPdf(pdfPath);
                ToastAlert.Success(
                    this,
                    created
                        ? $"Sales order {soNumber} was saved."
                        : $"Sales order {soNumber} already has a PDF.");
                ResetDraft();
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Sales Order Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
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
