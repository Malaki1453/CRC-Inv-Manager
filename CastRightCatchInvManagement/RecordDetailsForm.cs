namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Read-only popup of a grid row. Purchases and sales show the whole order plus an item table.
    /// Customers and vendors open the identity and history view.
    /// </summary>
    internal sealed class RecordDetailsForm : Form
    {
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
                    : First(record, "Name", "Customer", "Vendor", "Invoice #", "PO #", "SO #");
            Text = name.Length > 0 ? heading + "  ·  " + name : heading + " details";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = purchase || sale ? new Size(980, 800) : new Size(640, 640);
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
                : heading + " details");

            var footer = Footer();
            Control body = purchase || sale
                ? BuildOrderBody(purchase, first, lines, record)
                : BuildFieldBody(heading, record);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
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
                Width = width,
                FillWeight = fill > 0 ? fill : width,
                MinimumWidth = Math.Min(width, 48),
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
    }
}
