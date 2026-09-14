using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Read-only popup of a grid row. Purchases and sales show the whole order plus an item table.
    /// Customers and vendors open the identity and history view.
    /// Inventory shows item fields and a lots table (purchase PO in, sales out, remaining lb).
    /// </summary>
    internal sealed class RecordDetailsForm : Form
    {
        private DataGridView? _lotsGrid;
        private WaitSpinner? _lotsSpinner;
        private CancellationTokenSource? _lotsLoad;
        private string _itemCode = "";
        private string _itemDescription = "";
        /// <summary>Running totals keyed by normalized PO / lot.</summary>
        private readonly Dictionary<string, LotSnap> _lots = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Grid row index for each lot key so we update in place instead of appending duplicates.</summary>
        private readonly Dictionary<string, int> _lotRows = new(StringComparer.OrdinalIgnoreCase);
        private int _lotsPaintedAt;
        private bool _lotsDone;
        public static void ShowRecord(
            IWin32Window? owner,
            string title,
            Dictionary<string, string> record,
            string? table = null)
        {
            if (IsCustomer(title, table))
            {
                CustomerHistoryForm.ShowFor(owner, record);
                return;
            }

            if (IsVendor(title, table))
            {
                CustomerHistoryForm.ShowVendor(owner, record);
                return;
            }

            using var form = new RecordDetailsForm(title, record, table);
            if (owner != null)
                form.ShowDialog(owner);
            else
                form.ShowDialog();
        }

        private RecordDetailsForm(string title, Dictionary<string, string> record, string? table)
        {
            string heading = string.IsNullOrWhiteSpace(title) ? "Details" : title;
            bool purchase = IsPurchase(title, table);
            bool sale = IsSale(title, table);
            bool item = IsItem(title, table);
            var lines = purchase
                ? DataFiles.FindPurchasesByPo(DataFiles.GetRecord(record, "PO #"))
                : sale
                    ? DataFiles.FindSalesByPo(DataFiles.SalePo(record))
                    : new List<Dictionary<string, string>>();
            if ((purchase || sale) && lines.Count == 0)
                lines.Add(record);

            var first = lines.Count > 0 ? lines[0] : record;
            string name = purchase
                ? First(first, "PO #", "Vendor", "Vendor Code")
                : sale
                    ? First(first, "PO #", "SO #", "Customer", "Customer Code")
                    : item
                        ? First(record, "Code", "Description", "Name")
                        : First(record, "Name", "Customer", "Vendor", "Invoice #", "PO #", "SO #");
            Text = name.Length > 0 ? heading + "  ·  " + name : heading + " details";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = purchase || sale || item ? new Size(980, 720) : new Size(640, 640);
            MinimumSize = new Size(480, 400);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var header = Chrome(
                name.Length > 0 ? name : heading,
                purchase ? "Purchase order  ·  " + lines.Count + " item" + (lines.Count == 1 ? "" : "s")
                : sale ? "Sales order  ·  " + lines.Count + " item" + (lines.Count == 1 ? "" : "s")
                : item ? "Lots"
                : heading + " details");

            var footer = Footer();
            Control body = purchase || sale
                ? BuildOrderBody(purchase, first, lines, record)
                : item
                    ? BuildItemLotsTable(record)
                    : BuildFieldBody(heading, record);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);

            if (item)
            {
                _itemCode = DataFiles.GetRecord(record, "Code").Trim();
                _itemDescription = DataFiles.GetRecordAny(record, "Description", "Species");
                // Start after the empty grid is on screen so the spinner can paint.
                Shown += (_, _) => StartLotsLoad();
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _lotsLoad?.Cancel();
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _lotsLoad?.Dispose();
            base.Dispose(disposing);
        }

        private Control BuildOrderBody(
            bool purchase,
            Dictionary<string, string> header,
            List<Dictionary<string, string>> lines,
            Dictionary<string, string> clicked)
        {
            var split = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 12, 20, 8)
            };

            var itemsCard = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            itemsCard.Controls.Add(MakeItemsGrid(purchase, lines, clicked));
            itemsCard.Controls.Add(SectionBar("ITEMS"));

            const int columns = 3;
            var orderFields = HeaderFields(purchase, header);
            int orderRows = Math.Max(1, (orderFields.Count + columns - 1) / columns);
            var headCard = LabeledFieldsCard(
                "ORDER",
                orderFields,
                purchase ? "Purchase" : "Sale",
                columns,
                28 + 16 + orderRows * 40);

            var partyFields = PartyFields(purchase, header);
            int partyRows = Math.Max(1, (partyFields.Count + columns - 1) / columns);
            var partyCard = LabeledFieldsCard(
                purchase ? "VENDOR" : "CUSTOMER",
                partyFields,
                purchase ? "Vendor" : "Customer",
                columns,
                28 + 16 + partyRows * 40);

            split.Controls.Add(itemsCard);
            split.Controls.Add(headCard);
            split.Controls.Add(partyCard);
            return split;
        }

        /// <summary>
        /// Item header from the clicked inventory row, plus an empty lots grid that fills from a background scan.
        /// </summary>
        private Control BuildItemLotsTable(Dictionary<string, string> item)
        {
            var wrap = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Cream,
                Padding = new Padding(0)
            };
            _lotsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                Margin = new Padding(0),
                BorderStyle = BorderStyle.None
            };
            Theme.StyleGrid(_lotsGrid);
            FadeLotsGrid(true);
            _lotsGrid.ColumnHeadersHeight = 32;
            _lotsGrid.RowTemplate.Height = 32;
            AddCol(_lotsGrid, "Lot (PO #)", 110);
            AddCol(_lotsGrid, "Vendor", 180, 160);
            AddCol(_lotsGrid, "Purchased", 90);
            AddCol(_lotsGrid, "Sold", 90);
            AddCol(_lotsGrid, "Remaining", 90);
            AddCol(_lotsGrid, "Note", 160, 140);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            _lotsSpinner = new WaitSpinner
            {
                Size = new Size(48, 48),
                BackColor = Theme.Cream
            };
            host.Controls.Add(_lotsGrid);
            host.Controls.Add(_lotsSpinner);
            _lotsSpinner.BringToFront();
            void CenterSpinner()
            {
                if (_lotsSpinner == null)
                    return;
                _lotsSpinner.Location = new Point(
                    Math.Max(0, (host.ClientSize.Width - _lotsSpinner.Width) / 2),
                    Math.Max(0, (host.ClientSize.Height - _lotsSpinner.Height) / 2));
            }

            host.Resize += (_, _) => CenterSpinner();
            CenterSpinner();

            var fields = new List<KeyValuePair<string, string>>();
            foreach (var key in new[] { "Code", "Description", "Species", "COO", "Pack Size", "Scientific Name" })
            {
                string value = DataFiles.GetRecord(item, key).Trim();
                if (value.Length == 0)
                    continue;
                fields.Add(new KeyValuePair<string, string>(key, value));
            }

            wrap.Controls.Add(host);
            if (fields.Count > 0)
            {
                int rows = Math.Max(1, (fields.Count + 2) / 3);
                var itemCard = LabeledFieldsCard("ITEM", fields, "Item", 3, 28 + 16 + rows * 40);
                itemCard.Margin = new Padding(0);
                wrap.Controls.Add(itemCard);
            }

            return wrap;
        }

        /// <summary>Start the purchase/sales scan off the UI thread. Closing the form cancels it.</summary>
        private void StartLotsLoad()
        {
            _lotsLoad = new CancellationTokenSource();
            var token = _lotsLoad.Token;
            string code = _itemCode;
            string description = _itemDescription;
            _ = Task.Run(() => LoadLots(code, description, token), token);
        }

        /// <summary>
        /// Fold matching purchase and sale lines into lots. Match on Item Code, or Description if code is empty.
        /// Skip waiting-to-add rows only. Purchased uses purchase Volume grouped by PO #.
        /// Sold uses sale Volume grouped by <see cref="DataFiles.SaleLot"/>, then sale PO # if that is empty.
        /// Remaining is purchased minus sold.
        /// </summary>
        private void LoadLots(string code, string description, CancellationToken token)
        {
            try
            {
                string column = code.Length > 0 ? "Item Code" : "Description";
                string needle = code.Length > 0 ? code : description;
                if (needle.Length == 0)
                    return;

                if (TableAccess.Can(TableAccess.Purchases))
                {
                    SqliteInventory.ForEachWhere(
                        DataFiles.PurchaseSales,
                        column,
                        needle,
                        purchase =>
                        {
                            if (DataFiles.IsWaitingAdd(purchase))
                                return;
                            string lot = DataFiles.GetRecord(purchase, "PO #").Trim();
                            decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume"));
                            string vendor = DataFiles.GetRecordAny(purchase, "Vendor", "Name");
                            AddPurchase(lot, vendor, volume);
                        },
                        token);
                }

                if (TableAccess.Can(TableAccess.Sales))
                {
                    SqliteInventory.ForEachWhere(
                        DataFiles.Sales,
                        column,
                        needle,
                        sale =>
                        {
                            if (DataFiles.IsWaitingAdd(sale))
                                return;
                            // SaleLot prefers Lot #; if that is blank it may return the sale PO # (often the customer PO).
                            string lot = DataFiles.SaleLot(sale);
                            if (lot.Length == 0)
                                lot = DataFiles.GetRecord(sale, "PO #").Trim();
                            decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
                            AddSale(lot, volume);
                        },
                        token);
                }
            }
            catch (OperationCanceledException)
            {
                return;
            }
            finally
            {
                QueueLotsPaint(immediate: true, done: true);
            }
        }

        /// <summary>Add purchase Volume onto the PO lot. Vendor is kept from the first purchase on that lot.</summary>
        private void AddPurchase(string lot, string vendor, decimal volume)
        {
            // Blank PO / lot numbers share one bucket so they still show on the grid.
            string key = lot.Length > 0 ? DataFiles.NormalizePo(lot) : "(no lot)";
            bool isNew;
            lock (_lots)
            {
                isNew = !_lots.ContainsKey(key);
                if (!_lots.TryGetValue(key, out var snap))
                {
                    snap = new LotSnap
                    {
                        Key = key,
                        Lot = lot.Length > 0 ? lot : "(no lot)"
                    };
                    _lots[key] = snap;
                }

                snap.Purchased += volume;
                if (snap.Vendor.Length == 0)
                    snap.Vendor = vendor;
            }

            QueueLotsPaint(immediate: isNew, done: false);
        }

        /// <summary>Add sale Volume onto the lot. Creates a lot row if sales exist with no matching purchase.</summary>
        private void AddSale(string lot, decimal volume)
        {
            string key = lot.Length > 0 ? DataFiles.NormalizePo(lot) : "(no lot)";
            bool isNew;
            lock (_lots)
            {
                isNew = !_lots.ContainsKey(key);
                if (!_lots.TryGetValue(key, out var snap))
                {
                    snap = new LotSnap
                    {
                        Key = key,
                        Lot = lot.Length > 0 ? lot : "(no lot)"
                    };
                    _lots[key] = snap;
                }

                snap.Sold += volume;
            }

            QueueLotsPaint(immediate: isNew, done: false);
        }

        /// <summary>
        /// Push the current lots onto the grid. New lots paint immediately; later updates wait 50ms so the UI is not flooded.
        /// </summary>
        private void QueueLotsPaint(bool immediate, bool done)
        {
            if (done)
                _lotsDone = true;
            int now = Environment.TickCount;
            if (!immediate && !done && now - _lotsPaintedAt < 50)
                return;
            _lotsPaintedAt = now;

            LotSnap[] copy;
            lock (_lots)
                copy = _lots.Values.ToArray();

            void Paint()
            {
                if (IsDisposed || _lotsGrid == null || _lotsGrid.IsDisposed)
                    return;
                foreach (var snap in copy)
                    PaintLot(snap);
                if (_lotsDone)
                    FinishLotsLoad();
            }

            if (IsDisposed)
                return;
            if (InvokeRequired)
                BeginInvoke(Paint);
            else
                Paint();
        }

        /// <summary>Insert or refresh one grid row. Remaining is purchased minus sold.</summary>
        private void PaintLot(LotSnap snap)
        {
            if (_lotsGrid == null)
                return;
            string key = snap.Key.Length > 0 ? snap.Key : "(no lot)";
            decimal remain = snap.Purchased - snap.Sold;
            string note = remain > 0
                ? Lbs(remain) + " lb remaining"
                : remain < 0
                    ? Lbs(-remain) + " lb over sold"
                    : "Sold out";
            object[] cells =
            {
                snap.Lot,
                snap.Vendor.Length > 0 ? snap.Vendor : "—",
                Lbs(snap.Purchased),
                Lbs(snap.Sold),
                Lbs(remain),
                note
            };
            if (_lotRows.TryGetValue(key, out int index) && index < _lotsGrid.Rows.Count)
            {
                var row = _lotsGrid.Rows[index];
                for (int i = 0; i < cells.Length; i++)
                    row.Cells[i].Value = cells[i];
            }
            else
            {
                int added = _lotsGrid.Rows.Add(cells);
                _lotRows[key] = added;
            }
        }

        /// <summary>Hide the spinner and restore full-contrast grid colors when the scan finishes.</summary>
        private void FinishLotsLoad()
        {
            if (_lotsSpinner != null)
                _lotsSpinner.Visible = false;
            if (_lotsGrid != null)
                FadeLotsGrid(false);
        }

        /// <summary>Muted colors while rows are still arriving; normal theme when done.</summary>
        private void FadeLotsGrid(bool fade)
        {
            if (_lotsGrid == null)
                return;
            _lotsGrid.EnableHeadersVisualStyles = false;
            if (fade)
            {
                _lotsGrid.BackgroundColor = Theme.Cream;
                _lotsGrid.GridColor = Color.FromArgb(210, 214, 210);
                _lotsGrid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(90, 108, 122);
                _lotsGrid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 208, 214);
                _lotsGrid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(90, 108, 122);
                _lotsGrid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(200, 208, 214);
                _lotsGrid.DefaultCellStyle.BackColor = Theme.Cream;
                _lotsGrid.DefaultCellStyle.ForeColor = Theme.Muted;
                _lotsGrid.DefaultCellStyle.SelectionBackColor = Theme.Cream;
                _lotsGrid.DefaultCellStyle.SelectionForeColor = Theme.Muted;
                _lotsGrid.AlternatingRowsDefaultCellStyle.BackColor = Theme.Cream;
                _lotsGrid.AlternatingRowsDefaultCellStyle.ForeColor = Theme.Muted;
            }
            else
            {
                Theme.StyleGrid(_lotsGrid);
                _lotsGrid.EnableHeadersVisualStyles = false;
            }
        }

        private static string Lbs(decimal value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>One PO lot while the scan is running. Key is the normalized PO #.</summary>
        private sealed class LotSnap
        {
            public string Key { get; set; } = "";
            public string Lot { get; set; } = "";
            public string Vendor { get; set; } = "";
            public decimal Purchased { get; set; }
            public decimal Sold { get; set; }
        }

        private static CardPanel LabeledFieldsCard(
            string title,
            List<KeyValuePair<string, string>> fields,
            string captionTitle,
            int columns,
            int height)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = height,
                Padding = new Padding(1)
            };
            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(8, 6, 8, 6)
            };
            var table = FieldTable(fields, captionTitle, columns, compact: true);
            table.Dock = DockStyle.Fill;
            body.Controls.Add(table);
            card.Controls.Add(body);
            card.Controls.Add(SectionBar(title));
            return card;
        }

        private static Panel SectionBar(string title)
        {
            var bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 28,
                BackColor = Theme.Navy
            };
            bar.Controls.Add(new Label
            {
                Text = title,
                Dock = DockStyle.Fill,
                Font = Theme.Caption,
                ForeColor = Theme.HeaderText,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            });
            return bar;
        }

        private Control BuildFieldBody(string title, Dictionary<string, string> record)
        {
            var scroller = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 8, 16)
            };
            var card = new CardPanel
            {
                Dock = DockStyle.Top,
                Padding = new Padding(12, 16, 12, 16)
            };
            var fields = OrderedFields(title, record);
            var grid = FieldTable(fields, title, columns: 2, compact: false);
            card.Controls.Add(grid);
            card.Height = Math.Max(200, 48 + ((fields.Count + 1) / 2) * 58);
            scroller.Resize += (_, _) =>
                card.Width = Math.Max(280, scroller.ClientSize.Width - 28);
            scroller.Controls.Add(card);
            return scroller;
        }

        private static DataGridView MakeItemsGrid(
            bool purchase,
            List<Dictionary<string, string>> lines,
            Dictionary<string, string> clicked)
        {
            var grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToOrderColumns = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect,
                AutoGenerateColumns = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill
            };
            Theme.StyleGrid(grid);
            grid.ColumnHeadersHeight = 32;
            grid.RowTemplate.Height = 30;

            if (purchase)
            {
                AddCol(grid, "Item Code", 90);
                AddCol(grid, "Description", 180, fill: 180);
                AddCol(grid, "COO", 50);
                AddCol(grid, "Pack Size", 70);
                AddCol(grid, "CS", 50);
                AddCol(grid, "Volume", 70);
                AddCol(grid, "Price Paid / LB", 90);
                AddCol(grid, "Total Cost / LB", 90);
                AddCol(grid, "Total Cost", 80);
            }
            else
            {
                AddCol(grid, "Item Code", 90);
                AddCol(grid, "Lot #", 90);
                AddCol(grid, "Description", 180, fill: 180);
                AddCol(grid, "COO", 50);
                AddCol(grid, "Pack Size", 70);
                AddCol(grid, "CS", 50);
                AddCol(grid, "Volume", 70);
                AddCol(grid, "Sell Price / LB", 90);
                AddCol(grid, "Amount", 80);
            }

            string clickedItem = DataFiles.GetRecord(clicked, "Item Code");
            string clickedLot = DataFiles.SaleLot(clicked);
            int select = -1;
            foreach (var line in lines)
            {
                var values = new List<object>();
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    string key = col.Name;
                    string value = key.Equals("Lot #", StringComparison.OrdinalIgnoreCase)
                        ? DataFiles.SaleLot(line)
                        : DataFiles.GetRecord(line, key);
                    values.Add(string.IsNullOrWhiteSpace(value) ? "—" : value);
                }

                int index = grid.Rows.Add(values.ToArray());
                bool sameItem = clickedItem.Length > 0 &&
                    DataFiles.GetRecord(line, "Item Code")
                        .Equals(clickedItem, StringComparison.OrdinalIgnoreCase);
                bool sameLot = !purchase &&
                    (clickedLot.Length == 0 ||
                     DataFiles.SaleLot(line).Equals(clickedLot, StringComparison.OrdinalIgnoreCase));
                if (sameItem && (purchase || sameLot) && select < 0)
                    select = index;
            }

            if (select >= 0 && select < grid.Rows.Count)
            {
                grid.ClearSelection();
                grid.Rows[select].Selected = true;
            }

            return grid;
        }

        private static void AddCol(DataGridView grid, string name, int width, int fill = 0)
        {
            grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = name,
                HeaderText = name,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill,
                FillWeight = fill > 0 ? fill : width,
                MinimumWidth = 48,
                SortMode = DataGridViewColumnSortMode.NotSortable
            });
        }

        private static List<KeyValuePair<string, string>> HeaderFields(
            bool purchase,
            Dictionary<string, string> record)
        {
            string[] keys = purchase
                ? new[]
                {
                    "PO #", "Location", "Status",
                    "Agreement Date", "Expected Ship Date", "Ship Date",
                    "Arrival Date", "Vendor Due Date",
                    "Forwarder", "Logistics", DataFiles.FreightCompanyColumn,
                    "Overhead / LB", "Freight / LB", "Forwarder / LB", "Other / LB",
                    DataFiles.RecordStatus
                }
                : new[]
                {
                    "PO #", "SO #", "Status",
                    "Ship Date", "Due Date", "Invoice #",
                    "Paid", DataFiles.FreightCompanyColumn,
                    DataFiles.RecordStatus
                };

            var list = new List<KeyValuePair<string, string>>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in keys)
            {
                if (!used.Add(key))
                    continue;
                string value = key.Equals(DataFiles.RecordStatus, StringComparison.OrdinalIgnoreCase)
                    ? DataFiles.StatusOf(record)
                    : DataFiles.GetRecord(record, key);
                list.Add(new KeyValuePair<string, string>(key, value));
            }

            return list;
        }

        private static List<KeyValuePair<string, string>> PartyFields(
            bool purchase,
            Dictionary<string, string> order)
        {
            string code = purchase
                ? DataFiles.GetRecord(order, "Vendor Code")
                : DataFiles.GetRecord(order, "Customer Code");
            string name = purchase
                ? DataFiles.GetRecord(order, "Vendor")
                : DataFiles.GetRecord(order, "Customer");
            var party = FindParty(
                    purchase ? DataFiles.Vendors : DataFiles.Customers,
                    code,
                    name)
                ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            string[] keys = purchase
                ? new[]
                {
                    "Code", "Name", "Company",
                    "Type", "Terms", "Phone",
                    "Contact Name", "Current Balance"
                }
                : new[]
                {
                    "Code", "Name", "Company",
                    "Terms", "Phone", "Contact Name",
                    "Email", "Current Balance"
                };

            var list = new List<KeyValuePair<string, string>>();
            foreach (var key in keys)
            {
                string value = DataFiles.GetRecord(party, key);
                if (value.Length == 0)
                {
                    if (key.Equals("Code", StringComparison.OrdinalIgnoreCase))
                        value = code;
                    else if (key.Equals("Name", StringComparison.OrdinalIgnoreCase))
                        value = name;
                    else if (key.Equals("Terms", StringComparison.OrdinalIgnoreCase))
                        value = DataFiles.GetRecord(
                            order,
                            purchase ? "Vendor Terms" : "Customer Terms");
                }

                list.Add(new KeyValuePair<string, string>(key, value));
            }

            return list;
        }

        private static Dictionary<string, string>? FindParty(string table, string code, string name)
        {
            code = (code ?? "").Trim();
            name = (name ?? "").Trim();
            if (code.Length == 0 && name.Length == 0)
                return null;

            Dictionary<string, string>? byName = null;
            foreach (var record in DataFiles.VisibleRecords(table))
            {
                string recCode = DataFiles.GetRecord(record, "Code").Trim();
                string recName = DataFiles.GetRecordAny(record, "Name", "Company").Trim();
                if (code.Length > 0 &&
                    recCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                    return record;
                if (byName == null &&
                    name.Length > 0 &&
                    recName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    byName = record;
            }

            return byName;
        }

        private static TableLayoutPanel FieldTable(
            List<KeyValuePair<string, string>> fields,
            string title,
            int columns,
            bool compact)
        {
            columns = Math.Max(1, columns);
            var grid = new TableLayoutPanel
            {
                ColumnCount = columns,
                RowCount = 1,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            for (int i = 0; i < columns; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f / columns));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int col = 0;
            int row = 0;
            foreach (var pair in fields)
            {
                bool wide = !compact && IsWide(pair.Key);
                if (wide && col != 0)
                {
                    col = 0;
                    row++;
                    grid.RowCount = row + 1;
                    grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                }

                while (grid.RowCount <= row)
                {
                    grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));
                    grid.RowCount++;
                }

                var cell = FieldCell(DisplayCaption(title, pair.Key), pair.Value, compact);
                grid.Controls.Add(cell, wide ? 0 : col, row);
                if (wide)
                {
                    grid.SetColumnSpan(cell, columns);
                    col = 0;
                    row++;
                }
                else
                {
                    col++;
                    if (col >= columns)
                    {
                        col = 0;
                        row++;
                    }
                }
            }

            return grid;
        }

        private Panel Chrome(string title, string subtitleText)
        {
            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Theme.Paper,
                Padding = new Padding(24, 8, 24, 0)
            };
            var gold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };
            var titleLabel = new Label
            {
                Text = title,
                Dock = DockStyle.Top,
                Height = 36,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            var subtitle = new Label
            {
                Text = subtitleText,
                Dock = DockStyle.Top,
                Height = 22,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft
            };
            header.Controls.Add(subtitle);
            header.Controls.Add(titleLabel);
            header.Controls.Add(gold);
            return header;
        }

        private Panel Footer()
        {
            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Size = new Size(110, 34)
            };
            Theme.StyleNavyButton(close);
            AcceptButton = close;
            CancelButton = close;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Theme.Paper,
                Padding = new Padding(20, 12, 20, 12)
            };
            footer.Paint += (_, e) =>
            {
                using var line = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(line, 0, 0, footer.Width, 2);
            };
            footer.Controls.Add(close);
            footer.Resize += (_, _) =>
                close.Location = new Point(Math.Max(20, footer.Width - 130), 13);
            return footer;
        }

        private static string DisplayCaption(string title, string key)
        {
            if (title.Equals("Purchase", StringComparison.OrdinalIgnoreCase) &&
                key.Equals("Description", StringComparison.OrdinalIgnoreCase))
                return "Species";
            return key;
        }

        private static Panel FieldCell(string caption, string value, bool compact = false)
        {
            var cell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = compact ? new Padding(8, 2, 8, 2) : new Padding(12, 6, 12, 6),
                Height = compact ? 36 : 44
            };
            var label = new Label
            {
                Text = caption.ToUpperInvariant(),
                Dock = DockStyle.Top,
                Height = compact ? 14 : 16
            };
            Theme.StyleFieldLabel(label);
            var box = new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                Dock = DockStyle.Fill,
                Font = Theme.Body,
                ForeColor = Theme.Ink
            };
            cell.Controls.Add(box);
            cell.Controls.Add(label);
            return cell;
        }

        private static bool IsWide(string name)
        {
            return name.Equals("Notes", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Description", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Sold To", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Ship To", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(DataFiles.InvoiceLinesColumn, StringComparison.OrdinalIgnoreCase);
        }

        private static List<KeyValuePair<string, string>> OrderedFields(
            string title,
            Dictionary<string, string> record)
        {
            string[] prefer = title.Equals("Customer", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    DataFiles.RecordStatus, "Code", "Name", "Company", "Phone", "Email", "Contact Name",
                    "Terms", "Credit Limit", "Current Balance", "Established",
                    "Address", DataFiles.RoutingNumber, DataFiles.AccountNumber, "Description", "Notes"
                }
                : title.Equals("Vendor", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    DataFiles.RecordStatus, "Code", "Name", "Company", "Phone", "Contact Name", "Type", "Terms",
                    "Amount", "Current Balance", "Finalized",
                    DataFiles.RoutingNumber, DataFiles.AccountNumber, "Description", "Notes"
                }
                : Array.Empty<string>();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<KeyValuePair<string, string>>();
            if (TryGet(record, DataFiles.RecordStatus, out _) || prefer.Length > 0)
            {
                used.Add(DataFiles.RecordStatus);
                list.Add(new KeyValuePair<string, string>(
                    DataFiles.RecordStatus,
                    DataFiles.StatusOf(record)));
            }

            foreach (var key in prefer)
            {
                if (used.Contains(key) || !TryGet(record, key, out var value))
                    continue;
                used.Add(key);
                list.Add(new KeyValuePair<string, string>(key, DisplayField(key, value)));
            }

            foreach (var pair in record)
            {
                string key = pair.Key.Trim();
                if (key.Length == 0 || !used.Add(key))
                    continue;
                list.Add(new KeyValuePair<string, string>(key, DisplayField(key, pair.Value ?? "")));
            }

            return list;
        }

        private static string DisplayField(string key, string value)
        {
            if (key.Equals(DataFiles.AccountNumber, StringComparison.OrdinalIgnoreCase))
                return DataFiles.MaskAccountNumber(value);
            return value ?? "";
        }

        private static bool TryGet(Dictionary<string, string> record, string key, out string value)
        {
            foreach (var pair in record)
            {
                if (pair.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                {
                    value = pair.Value ?? "";
                    return true;
                }
            }

            value = "";
            return false;
        }

        private static string First(Dictionary<string, string> record, params string[] keys)
        {
            foreach (var key in keys)
            {
                string value = DataFiles.GetRecord(record, key).Trim();
                if (value.Length > 0)
                    return value;
            }

            return "";
        }

        private static bool IsPurchase(string title, string? table) =>
            (table != null && table.Equals(DataFiles.PurchaseSales, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Purchases", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Purchase", StringComparison.OrdinalIgnoreCase);

        private static bool IsSale(string title, string? table) =>
            (table != null && table.Equals(DataFiles.Sales, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Sales", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Sale", StringComparison.OrdinalIgnoreCase);

        private static bool IsCustomer(string title, string? table) =>
            (table != null && table.Equals(DataFiles.Customers, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Customers", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Customer", StringComparison.OrdinalIgnoreCase);

        private static bool IsVendor(string title, string? table) =>
            (table != null && table.Equals(DataFiles.Vendors, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Vendors", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Vendor", StringComparison.OrdinalIgnoreCase);

        /// <summary>Inventory / item-code grids use the lots table instead of the field list.</summary>
        private static bool IsItem(string title, string? table) =>
            (table != null && table.Equals(DataFiles.ItemCodes, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Inventory", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Item Codes", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Item", StringComparison.OrdinalIgnoreCase);
    }
}
