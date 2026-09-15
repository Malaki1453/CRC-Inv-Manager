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
        /// <summary>Lot keys whose sales list is currently expanded under the purchase row.</summary>
        private readonly HashSet<string> _expandedLots = new(StringComparer.OrdinalIgnoreCase);
        private int _lotsPaintedAt;
        private bool _lotsDone;
        private LotFilter _lotFilter = LotFilter.All;
        private Button? _filterAll;
        private Button? _filterInStock;
        private Button? _filterSoldOut;
        /// <summary>Open the matching details view: customer/vendor history, item lots, or a field popup.</summary>
        public static void ShowRecord(
            IWin32Window? owner,
            string title,
            Dictionary<string, string> record,
            string? table = null)
        {
            // Customers have identity plus invoice/sale history, not a field list.
            if (IsCustomer(title, table))
            {
                CustomerHistoryForm.ShowFor(owner, record);
                return;
            }

            // Vendors use the same history view, filtered to purchases.
            if (IsVendor(title, table))
            {
                CustomerHistoryForm.ShowVendor(owner, record);
                return;
            }

            var form = new RecordDetailsForm(title, record, table);
            // Lots scan in the background, so inventory stays modeless.
            if (IsItem(title, table))
            {
                form.FormClosed += (_, _) => form.Dispose();
                form.Show();
                form.Activate();
                return;
            }

            using (form)
            {
                if (owner != null)
                    form.ShowDialog(owner);
                else
                    form.ShowDialog();
            }
        }

        /// <summary>Build header, body, and footer for an order, inventory item, or generic field list.</summary>
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
            // The clicked row is enough to show details even if the PO lookup returned nothing.
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
            ShowInTaskbar = item;
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

        /// <summary>Cancel the lots scan so closing the window does not keep the background work running.</summary>
        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            _lotsLoad?.Cancel();
            base.OnFormClosing(e);
        }

        /// <summary>Dispose the lots-scan cancellation source with this form.</summary>
        protected override void Dispose(bool disposing)
        {
            if (disposing)
                _lotsLoad?.Dispose();
            base.Dispose(disposing);
        }

        /// <summary>Party card, order header card, and the items grid for a purchase or sale.</summary>
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
            _lotsGrid.CellMouseClick += OnLotRowClick;
            void CenterSpinner()
            {
                // Resize can fire after the spinner was disposed with the form.
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
                // Skip empty item fields so the header card only shows what is filled.
                if (value.Length == 0)
                    continue;
                fields.Add(new KeyValuePair<string, string>(key, value));
            }

            wrap.Controls.Add(host);
            wrap.Controls.Add(BuildLotFilterBar());
            // Skip the item card when every header field is blank.
            if (fields.Count > 0)
            {
                int rows = Math.Max(1, (fields.Count + 2) / 3);
                var itemCard = LabeledFieldsCard("ITEM", fields, "Item", 3, 28 + 16 + rows * 40);
                itemCard.Margin = new Padding(0);
                wrap.Controls.Add(itemCard);
            }

            return wrap;
        }

        /// <summary>All / in stock / sold out. Does not re-query; it only hides rows already loaded.</summary>
        private Control BuildLotFilterBar()
        {
            var bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 68,
                BackColor = Theme.Paper
            };
            var buttons = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Paper,
                Padding = new Padding(12, 6, 12, 6)
            };
            _filterAll = FilterButton("All");
            _filterInStock = FilterButton("In stock");
            _filterSoldOut = FilterButton("Sold out");
            _filterAll.Click += (_, _) => SetLotFilter(LotFilter.All);
            _filterInStock.Click += (_, _) => SetLotFilter(LotFilter.InStock);
            _filterSoldOut.Click += (_, _) => SetLotFilter(LotFilter.SoldOut);
            buttons.Controls.Add(_filterSoldOut);
            buttons.Controls.Add(_filterInStock);
            buttons.Controls.Add(_filterAll);
            void LayoutFilters()
            {
                int x = buttons.Padding.Left;
                int y = buttons.Padding.Top;
                int h = Math.Max(24, buttons.ClientSize.Height - buttons.Padding.Vertical);
                foreach (var button in new[] { _filterAll, _filterInStock, _filterSoldOut })
                {
                    if (button == null)
                        continue;
                    button.Height = h;
                    button.Location = new Point(x, y);
                    x += button.Width + 8;
                }
            }

            buttons.Resize += (_, _) => LayoutFilters();
            LayoutFilters();
            bar.Controls.Add(buttons);
            bar.Controls.Add(SectionBar("LOTS"));
            StyleFilterButtons();
            return bar;
        }

        private Button FilterButton(string text)
        {
            var button = new Button
            {
                Text = text,
                AutoSize = true,
                MinimumSize = new Size(88, 28),
                TabStop = false
            };
            Theme.StyleOutlineButton(button);
            return button;
        }

        /// <summary>Apply All, In stock (remaining &gt; 0), or Sold out (remaining &lt;= 0) without scanning again.</summary>
        private void SetLotFilter(LotFilter filter)
        {
            _lotFilter = filter;
            StyleFilterButtons();
            LotSnap[] copy;
            lock (_lots)
                copy = _lots.Values.ToArray();
            foreach (var snap in copy)
                PaintLot(snap);
        }

        private void StyleFilterButtons()
        {
            StyleFilterButton(_filterAll, _lotFilter == LotFilter.All);
            StyleFilterButton(_filterInStock, _lotFilter == LotFilter.InStock);
            StyleFilterButton(_filterSoldOut, _lotFilter == LotFilter.SoldOut);
        }

        private static void StyleFilterButton(Button? button, bool selected)
        {
            if (button == null)
                return;
            if (selected)
                Theme.StyleGoldButton(button);
            else
                Theme.StyleOutlineButton(button);
        }

        /// <summary>True when the lot belongs on the grid for the current filter.</summary>
        private bool LotMatchesFilter(LotSnap snap)
        {
            decimal remain = snap.Purchased - snap.Sold;
            // In stock: still have pounds. Sold out: remaining is zero or negative (over sold).
            if (_lotFilter == LotFilter.InStock)
                return remain > 0;
            if (_lotFilter == LotFilter.SoldOut)
                return remain <= 0;
            return true;
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
        /// Skip waiting-to-add rows only. Lots come from purchase PO #. A sale counts against a lot only when
        /// its Lot # or PO # matches that purchase PO. Remaining is purchased minus sold.
        /// </summary>
        private void LoadLots(string code, string description, CancellationToken token)
        {
            try
            {
                // Prefer Item Code. Description is only used when the inventory row has no code.
                string column = code.Length > 0 ? "Item Code" : "Description";
                string needle = code.Length > 0 ? code : description;
                if (needle.Length == 0)
                    return;

                // Purchases create lots. Skip this table if the user cannot see Purchases.
                if (TableAccess.Can(TableAccess.Purchases))
                {
                    SqliteInventory.ForEachWhere(
                        DataFiles.PurchaseSales,
                        column,
                        needle,
                        purchase =>
                        {
                            // Unconfirmed adds are not stock yet.
                            if (DataFiles.IsWaitingAdd(purchase))
                                return;
                            string lot = DataFiles.GetRecord(purchase, "PO #").Trim();
                            decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(purchase, "Volume"));
                            string vendor = DataFiles.GetRecordAny(purchase, "Vendor", "Name");
                            AddPurchase(lot, vendor, volume);
                        },
                        token);
                }

                // Sales only reduce lots that already exist from purchases.
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
                            decimal volume = DataFiles.ParseMoney(DataFiles.GetRecord(sale, "Volume"));
                            string so = DataFiles.GetRecord(sale, "SO #").Trim();
                            // PO # is the purchase lot. Lot # is only a fallback for old rows.
                            if (!AddSale(DataFiles.GetRecord(sale, "PO #"), volume, so))
                                AddSale(DataFiles.GetRecord(sale, "Lot #"), volume, so);
                        },
                        token);
                }
            }
            catch (OperationCanceledException)
            {
                // Form closed while the scan was running.
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
            // Blank PO numbers share one bucket so they still show on the grid.
            string key = lot.Length > 0 ? DataFiles.NormalizePo(lot) : "(no lot)";
            bool isNew;
            lock (_lots)
            {
                isNew = !_lots.ContainsKey(key);
                // First purchase line on this PO creates the lot row.
                if (!_lots.TryGetValue(key, out var snap))
                {
                    snap = new LotSnap
                    {
                        Key = key,
                        Lot = lot.Length > 0 ? lot : "(no lot)",
                        SalesBySo = new Dictionary<string, decimal>(StringComparer.OrdinalIgnoreCase)
                    };
                    _lots[key] = snap;
                }

                snap.Purchased += volume;
                // Keep the vendor from the first purchase on this PO.
                if (snap.Vendor.Length == 0)
                    snap.Vendor = vendor;
            }

            QueueLotsPaint(immediate: isNew, done: false);
        }

        /// <summary>
        /// Add sale Volume onto an existing purchase lot and remember SO # : lbs for the expand list.
        /// Returns false if this number is not a purchase PO for the item.
        /// </summary>
        private bool AddSale(string lot, decimal volume, string soNumber)
        {
            lot = (lot ?? "").Trim();
            // No lot/PO on the sale, so it cannot match a purchase PO.
            if (lot.Length == 0)
                return false;

            string key = DataFiles.NormalizePo(lot);
            if (key.Length == 0)
                return false;

            bool firstSale;
            lock (_lots)
            {
                // Only count the sale if this number is already a purchase PO for the item.
                if (!_lots.TryGetValue(key, out var snap))
                    return false;

                firstSale = snap.Sold == 0;
                snap.Sold += volume;
                string so = soNumber.Trim();
                if (so.Length == 0)
                    so = "—";
                if (!snap.SalesBySo.ContainsKey(so))
                    snap.SalesBySo[so] = 0;
                snap.SalesBySo[so] += volume;
            }

            QueueLotsPaint(immediate: firstSale, done: false);
            return true;
        }

        /// <summary>
        /// Push the current lots onto the grid. New lots paint immediately; later updates wait 50ms so the UI is not flooded.
        /// </summary>
        private void QueueLotsPaint(bool immediate, bool done)
        {
            if (done)
                _lotsDone = true;
            int now = Environment.TickCount;
            // Skip extra paints unless this is a new lot, the scan finished, or 50ms have passed.
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
            // Background thread must marshal to the UI thread.
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
            // Remaining > 0 still on hand; negative means sold more than purchased; zero is sold out.
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
            // Update the existing grid row when this lot was already drawn.
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

            if (_lotRows.TryGetValue(key, out int rowIndex) && rowIndex < _lotsGrid.Rows.Count)
            {
                bool visible = LotMatchesFilter(snap);
                _lotsGrid.Rows[rowIndex].Visible = visible;
                _lotsGrid.Rows[rowIndex].Tag = "lot:" + key;
                // Hidden lots should not keep an open sales list.
                if (!visible)
                    CollapseLotSales(key);
                else if (_expandedLots.Contains(key))
                    RefreshLotSales(key);
            }
        }

        /// <summary>Left-click a purchase lot to expand or collapse its sales. Ignore double-click and sale lines.</summary>
        private void OnLotRowClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.Clicks != 1 || e.RowIndex < 0 || _lotsGrid == null)
                return;
            string? tag = _lotsGrid.Rows[e.RowIndex].Tag as string;
            // Sale lines are not themselves expandable.
            if (tag != null && tag.StartsWith("sale:", StringComparison.Ordinal))
                return;
            string key = tag != null && tag.StartsWith("lot:", StringComparison.Ordinal)
                ? tag.Substring(4)
                : "";
            if (key.Length == 0)
                return;
            if (_expandedLots.Contains(key))
                CollapseLotSales(key);
            else
                ExpandLotSales(key);
        }

        /// <summary>Insert SO # : lbs rows under the purchase lot.</summary>
        private void ExpandLotSales(string key)
        {
            if (_lotsGrid == null || !_lotRows.TryGetValue(key, out int parent) || parent < 0)
                return;
            CollapseLotSales(key);
            List<KeyValuePair<string, decimal>> sales;
            lock (_lots)
            {
                if (!_lots.TryGetValue(key, out var snap))
                    return;
                sales = snap.SalesBySo
                    .OrderBy(pair => pair.Key, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }

            // No sales yet, so there is nothing to list under the PO.
            if (sales.Count == 0)
                return;

            int insertAt = parent + 1;
            foreach (var pair in sales)
            {
                int index = insertAt;
                _lotsGrid.Rows.Insert(index, pair.Key + " : " + Lbs(pair.Value), "", "", "", "", "");
                var row = _lotsGrid.Rows[index];
                row.Tag = "sale:" + key;
                row.DefaultCellStyle.ForeColor = Theme.Muted;
                row.DefaultCellStyle.SelectionForeColor = Theme.Muted;
                row.DefaultCellStyle.Padding = new Padding(28, 0, 8, 0);
                insertAt++;
            }

            ShiftLotRows(parent, sales.Count);
            _expandedLots.Add(key);
        }

        /// <summary>Remove the SO # : lbs rows under this purchase lot.</summary>
        private void CollapseLotSales(string key)
        {
            if (_lotsGrid == null || !_lotRows.TryGetValue(key, out int parent))
            {
                _expandedLots.Remove(key);
                return;
            }

            int removed = 0;
            int i = parent + 1;
            while (i < _lotsGrid.Rows.Count)
            {
                string? tag = _lotsGrid.Rows[i].Tag as string;
                // Stop at the next purchase lot or a sale list that belongs to another PO.
                if (tag == null || !tag.Equals("sale:" + key, StringComparison.OrdinalIgnoreCase))
                    break;
                _lotsGrid.Rows.RemoveAt(i);
                removed++;
            }

            if (removed > 0)
                ShiftLotRows(parent, -removed);
            _expandedLots.Remove(key);
        }

        /// <summary>Rebuild an open sales list so lbs stay current while the scan is still running.</summary>
        private void RefreshLotSales(string key)
        {
            if (!_expandedLots.Contains(key))
                return;
            ExpandLotSales(key);
        }

        /// <summary>Keep purchase-row indexes correct after inserting or removing sale lines.</summary>
        private void ShiftLotRows(int afterIndex, int delta)
        {
            foreach (var lotKey in _lotRows.Keys.ToList())
            {
                if (_lotRows[lotKey] > afterIndex)
                    _lotRows[lotKey] += delta;
            }
        }

        /// <summary>Hide the spinner and restore full-contrast grid colors when the scan finishes.</summary>
        private void FinishLotsLoad()
        {
            if (_lotsSpinner != null)
                _lotsSpinner.Visible = false;
            if (_lotsGrid != null)
                FadeLotsGrid(false);
            RefreshFilterCounts();
        }

        /// <summary>Show how many lots sit in each filter bucket after the scan finishes.</summary>
        private void RefreshFilterCounts()
        {
            int all = 0;
            int inStock = 0;
            int soldOut = 0;
            lock (_lots)
            {
                foreach (var snap in _lots.Values)
                {
                    all++;
                    if (snap.Purchased - snap.Sold > 0)
                        inStock++;
                    else
                        soldOut++;
                }
            }

            if (_filterAll != null)
                _filterAll.Text = all == 0 ? "All" : "All (" + all + ")";
            if (_filterInStock != null)
                _filterInStock.Text = inStock == 0 ? "In stock" : "In stock (" + inStock + ")";
            if (_filterSoldOut != null)
                _filterSoldOut.Text = soldOut == 0 ? "Sold out" : "Sold out (" + soldOut + ")";
        }

        /// <summary>Muted colors while rows are still arriving; normal theme when done.</summary>
        private void FadeLotsGrid(bool fade)
        {
            if (_lotsGrid == null)
                return;
            _lotsGrid.EnableHeadersVisualStyles = false;
            // Faded while loading so arriving rows are visible but the grid is not the finished look.
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

        /// <summary>Format pounds for the lots grid, keeping trailing zeros off.</summary>
        private static string Lbs(decimal value) =>
            value.ToString("0.###", CultureInfo.InvariantCulture);

        private enum LotFilter
        {
            All,
            InStock,
            SoldOut
        }

        /// <summary>One PO lot while the scan is running. Key is the normalized PO #.</summary>
        private sealed class LotSnap
        {
            public string Key { get; set; } = "";
            public string Lot { get; set; } = "";
            public string Vendor { get; set; } = "";
            public decimal Purchased { get; set; }
            public decimal Sold { get; set; }
            public Dictionary<string, decimal> SalesBySo { get; set; } =
                new(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>Navy section bar plus a compact labeled field table, docked to the top.</summary>
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

        /// <summary>Navy caption bar used above order, party, and item cards.</summary>
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

        /// <summary>Scrollable two-column field list for invoices and other non-order rows.</summary>
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

        /// <summary>Read-only items grid for a PO, highlighting the row that was clicked.</summary>
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

            // Purchases show cost columns; sales show lot, sell price, and amount.
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
                // Highlight the clicked product so the user can see which line they opened.
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

        /// <summary>Add a fill-weighted, non-sortable text column to a details grid.</summary>
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

        /// <summary>Order-header fields for a purchase or sale, including record status.</summary>
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
                // Skip duplicate headings if the preferred list repeats a column.
                if (!used.Add(key))
                    continue;
                string value = key.Equals(DataFiles.RecordStatus, StringComparison.OrdinalIgnoreCase)
                    ? DataFiles.StatusOf(record)
                    : DataFiles.GetRecord(record, key);
                list.Add(new KeyValuePair<string, string>(key, value));
            }

            return list;
        }

        /// <summary>Vendor or customer identity fields, filled from the party table when found.</summary>
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
                // Party table may be missing; fall back to the order row for code, name, and terms.
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

        /// <summary>Find a customer or vendor by code first, then by name.</summary>
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
                // Codes are unique; return immediately on a code hit.
                if (code.Length > 0 &&
                    recCode.Equals(code, StringComparison.OrdinalIgnoreCase))
                    return record;
                // Keep the first name match in case no code matches.
                if (byName == null &&
                    name.Length > 0 &&
                    recName.Equals(name, StringComparison.OrdinalIgnoreCase))
                    byName = record;
            }

            return byName;
        }

        /// <summary>Lay out labeled cells in columns, giving wide fields such as Address a full row.</summary>
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
                // Wide fields start on their own row so Address/Notes are not squeezed.
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
                    // Wrap to the next row after the last column.
                    if (col >= columns)
                    {
                        col = 0;
                        row++;
                    }
                }
            }

            return grid;
        }

        /// <summary>Top title bar with gold accent used by every details popup.</summary>
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

        /// <summary>Close button docked at the bottom of the popup.</summary>
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

        /// <summary>Purchase Description is labeled Species so it matches the inventory wording.</summary>
        private static string DisplayCaption(string title, string key)
        {
            if (title.Equals("Purchase", StringComparison.OrdinalIgnoreCase) &&
                key.Equals("Description", StringComparison.OrdinalIgnoreCase))
                return "Species";
            return key;
        }

        /// <summary>One caption-plus-value cell; blank values show an em dash.</summary>
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

        /// <summary>True for long text fields that should span every column.</summary>
        private static bool IsWide(string name)
        {
            return name.Equals("Notes", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Address", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Description", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Sold To", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals("Ship To", StringComparison.OrdinalIgnoreCase) ||
                   name.Equals(DataFiles.InvoiceLinesColumn, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Status first, then preferred customer/vendor keys, then remaining record fields.</summary>
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
            // Show status whenever the row has it, or when this is a customer/vendor card.
            if (TryGet(record, DataFiles.RecordStatus, out _) || prefer.Length > 0)
            {
                used.Add(DataFiles.RecordStatus);
                list.Add(new KeyValuePair<string, string>(
                    DataFiles.RecordStatus,
                    DataFiles.StatusOf(record)));
            }

            foreach (var key in prefer)
            {
                // Preferred keys that are missing or already listed are skipped.
                if (used.Contains(key) || !TryGet(record, key, out var value))
                    continue;
                used.Add(key);
                list.Add(new KeyValuePair<string, string>(key, DisplayField(key, value)));
            }

            foreach (var pair in record)
            {
                string key = pair.Key.Trim();
                // Skip unnamed or already-listed keys so the card does not repeat columns.
                if (key.Length == 0 || !used.Add(key))
                    continue;
                list.Add(new KeyValuePair<string, string>(key, DisplayField(key, pair.Value ?? "")));
            }

            return list;
        }

        /// <summary>Mask bank account numbers; other fields pass through.</summary>
        private static string DisplayField(string key, string value)
        {
            if (key.Equals(DataFiles.AccountNumber, StringComparison.OrdinalIgnoreCase))
                return DataFiles.MaskAccountNumber(value);
            return value ?? "";
        }

        /// <summary>Case-insensitive lookup of one record field.</summary>
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

        /// <summary>First non-blank field among the given keys, used for the window title.</summary>
        private static string First(Dictionary<string, string> record, params string[] keys)
        {
            foreach (var key in keys)
            {
                string value = DataFiles.GetRecord(record, key).Trim();
                // Title uses the first identity field that is actually filled.
                if (value.Length > 0)
                    return value;
            }

            return "";
        }

        /// <summary>True when this details popup is a purchases-table row.</summary>
        private static bool IsPurchase(string title, string? table) =>
            (table != null && table.Equals(DataFiles.PurchaseSales, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Purchases", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Purchase", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this details popup is a sales-table row.</summary>
        private static bool IsSale(string title, string? table) =>
            (table != null && table.Equals(DataFiles.Sales, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Sales", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Sale", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this details popup should open customer history instead of a field list.</summary>
        private static bool IsCustomer(string title, string? table) =>
            (table != null && table.Equals(DataFiles.Customers, StringComparison.OrdinalIgnoreCase)) ||
            title.Equals("Customers", StringComparison.OrdinalIgnoreCase) ||
            title.Equals("Customer", StringComparison.OrdinalIgnoreCase);

        /// <summary>True when this details popup should open vendor history instead of a field list.</summary>
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
