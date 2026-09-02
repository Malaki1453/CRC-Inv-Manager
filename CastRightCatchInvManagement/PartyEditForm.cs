namespace CastRightCatchInvManagement
{
    /// <summary>Add or edit a customer or vendor. History tabs cover description, sales or purchases, and bank transactions.</summary>
    internal sealed class PartyEditForm : Form
    {
        private readonly bool _vendor;
        private readonly string _originalCode;
        private readonly TextBox _code;
        private readonly TextBox _name;
        private readonly TextBox _company;
        private readonly TextBox _phone;
        private readonly TextBox _balance;
        private readonly TextBox _notes;
        private readonly TextBox _contact;
        private readonly TextBox _address;
        private readonly TextBox _email;
        private readonly TextBox _terms;
        private readonly TextBox _extra;
        private readonly TextBox _established;
        private readonly Label _subtitle;

        /// <summary>Modal: add a customer.</summary>
        public static void OpenCustomerNew() => ShowEdit(false, null);

        /// <summary>Modal: edit this customer row.</summary>
        public static void OpenCustomerEdit(Dictionary<string, string> record) => ShowEdit(false, record);

        /// <summary>Modal: add a vendor.</summary>
        public static void OpenVendorNew() => ShowEdit(true, null);

        /// <summary>Modal: edit this vendor row.</summary>
        public static void OpenVendorEdit(Dictionary<string, string> record) => ShowEdit(true, record);

        /// <summary>Show the dialog; save writes to the live customers or vendors table.</summary>
        private static void ShowEdit(bool vendor, Dictionary<string, string>? record)
        {
            using var form = new PartyEditForm(vendor, record);
            form.ShowDialog();
        }

        private PartyEditForm(bool vendor, Dictionary<string, string>? record)
        {
            _vendor = vendor;
            _originalCode = record == null ? "" : DataFiles.GetRecord(record, "Code").Trim();
            bool editing = _originalCode.Length > 0;
            string kind = vendor ? "Vendor" : "Customer";

            Text = editing ? "Edit " + kind : "Add " + kind;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(780, 860);
            MinimumSize = new Size(640, 680);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

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
            var title = new Label
            {
                Text = Text,
                Dock = DockStyle.Top,
                Height = 36,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            _subtitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft
            };
            header.Controls.Add(_subtitle);
            header.Controls.Add(title);
            header.Controls.Add(gold);

            var save = new Button
            {
                Text = editing ? "Save" : "Add " + kind,
                Size = new Size(editing ? 110 : 140, 34)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                if (SaveRecord())
                    DialogResult = DialogResult.OK;
            };
            var cancel = new Button
            {
                Text = "Cancel",
                Size = new Size(96, 34),
                DialogResult = DialogResult.Cancel
            };
            Theme.StyleOutlineButton(cancel);
            CancelButton = cancel;

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
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            footer.Resize += (_, _) =>
            {
                save.Location = new Point(Math.Max(140, footer.Width - save.Width - 128), 13);
                cancel.Location = new Point(Math.Max(20, footer.Width - 116), 13);
            };

            _contact = new TextBox { Visible = false };
            _email = new TextBox { Visible = false };
            _address = new TextBox { Visible = false };
            _established = new TextBox { Visible = false };

            CardPanel identity;
            if (vendor)
            {
                identity = Section("Identity", 210, 4, out var grid);
                _code = PutField(grid, 0, 0, "CODE");
                _name = PutField(grid, 1, 0, "NAME");
                _company = PutField(grid, 2, 0, "COMPANY");
                _phone = PutField(grid, 3, 0, "PHONE");
                _terms = PutField(grid, 0, 1, "TERMS");
                _extra = PutField(grid, 1, 1, "TYPE");
                _contact = PutField(grid, 2, 1, "AMOUNT");
                _balance = PutField(grid, 3, 1, "CURRENT BALANCE");
                SetRowHeights(grid, 56, 56);
                _contact.PlaceholderText = "0.00";
                _extra.PlaceholderText = "Processor";
            }
            else
            {
                identity = Section("Identity", 340, 4, out var grid);
                _code = PutField(grid, 0, 0, "CODE");
                _name = PutField(grid, 1, 0, "NAME");
                _company = PutField(grid, 2, 0, "COMPANY");
                _phone = PutField(grid, 3, 0, "PHONE");
                _contact = PutField(grid, 0, 1, "CONTACT NAME");
                _email = PutField(grid, 1, 1, "EMAIL");
                _terms = PutField(grid, 2, 1, "TERMS");
                _extra = PutField(grid, 3, 1, "CREDIT LIMIT");
                _balance = PutField(grid, 0, 2, "CURRENT BALANCE");
                _established = PutField(grid, 1, 2, "ESTABLISHED");
                _address = PutField(grid, 0, 3, "ADDRESS", colSpan: 4, multiline: true);
                SetRowHeights(grid, 56, 56, 56, 80);
                _contact.PlaceholderText = "Who we talk to";
                _email.PlaceholderText = "name@company.com";
                _extra.PlaceholderText = "0.00";
            }

            _code.PlaceholderText = vendor ? "V-1001" : "C-1001";
            _name.PlaceholderText = vendor ? "Vendor name" : "Customer name";
            _company.PlaceholderText = "Company";
            _phone.PlaceholderText = "(253) 000-0000";
            _terms.PlaceholderText = "NET 15";
            _balance.PlaceholderText = "0.00";
            identity.Dock = DockStyle.Fill;

            _notes = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                AcceptsTab = false,
                Dock = DockStyle.Fill,
                PlaceholderText = vendor
                    ? "Notes about this vendor"
                    : "Notes about this customer"
            };
            Theme.StyleField(_notes);
            var history = BuildHistoryCard(vendor, record, _notes);

            if (record != null)
            {
                _code.Text = DataFiles.GetRecord(record, "Code");
                _name.Text = DataFiles.GetRecord(record, "Name");
                _company.Text = First(record, "Company", "Name");
                _phone.Text = DataFiles.GetRecord(record, "Phone");
                _balance.Text = DataFiles.GetRecord(record, "Current Balance");
                _terms.Text = DataFiles.GetRecord(record, "Terms");
                _notes.Text = First(record, "Description", "Notes");
                if (vendor)
                {
                    _extra.Text = DataFiles.GetRecord(record, "Type");
                    _contact.Text = DataFiles.GetRecord(record, "Amount");
                }
                else
                {
                    _extra.Text = DataFiles.GetRecord(record, "Credit Limit");
                    _contact.Text = DataFiles.GetRecord(record, "Contact Name");
                    _email.Text = DataFiles.GetRecord(record, "Email");
                    _address.Text = DataFiles.GetRecord(record, "Address");
                    _established.Text = DataFiles.GetRecord(record, "Established");
                }
            }

            UpdateSubtitle();
            _name.TextChanged += (_, _) => UpdateSubtitle();
            _code.TextChanged += (_, _) => UpdateSubtitle();
            _company.TextChanged += (_, _) => UpdateSubtitle();

            var topHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = identity.Height + 28,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 20, 12)
            };
            topHost.Controls.Add(identity);

            var bottomHost = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 0, 20, 8)
            };
            history.Dock = DockStyle.Fill;
            bottomHost.Controls.Add(history);

            Controls.Add(bottomHost);
            Controls.Add(topHost);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private void UpdateSubtitle()
        {
            string name = _name.Text.Trim();
            string company = _company.Text.Trim();
            string code = _code.Text.Trim();
            var parts = new List<string>();
            if (company.Length > 0 && !company.Equals(name, StringComparison.OrdinalIgnoreCase))
                parts.Add(company);
            else if (name.Length > 0)
                parts.Add(name);
            if (code.Length > 0)
                parts.Add(code);
            _subtitle.Text = parts.Count > 0
                ? string.Join("  ·  ", parts)
                : (_vendor ? "Vendor record" : "Customer record");
        }

        /// <summary>Insert or replace the customer/vendor by Code. Returns false if validation fails.</summary>
        private bool SaveRecord()
        {
            if (!AppLock.HasFolder())
            {
                MessageBox.Show("Select a data folder in Settings first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string code = _code.Text.Trim();
            string name = _name.Text.Trim();
            if (code.Length == 0 || name.Length == 0)
            {
                MessageBox.Show("Enter a code and a name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string baseName = _vendor ? DataFiles.Vendors : DataFiles.Customers;
            bool exists = DataFiles.ReadRecords(baseName).Any(record =>
                DataFiles.GetRecord(record, "Code").Equals(code, StringComparison.OrdinalIgnoreCase));
            if (exists && !code.Equals(_originalCode, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("That code is already in use.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = code,
                ["Name"] = name,
                ["Company"] = _company.Text.Trim(),
                ["Phone"] = _phone.Text.Trim(),
                ["Current Balance"] = _balance.Text.Trim(),
                ["Notes"] = _notes.Text.Trim(),
                ["Description"] = _notes.Text.Trim(),
                ["Terms"] = _terms.Text.Trim()
            };

            if (_vendor)
            {
                fields["Type"] = _extra.Text.Trim();
                fields["Amount"] = _contact.Text.Trim();
            }
            else
            {
                fields["Credit Limit"] = _extra.Text.Trim();
                fields["Contact Name"] = _contact.Text.Trim();
                fields["Email"] = _email.Text.Trim();
                fields["Address"] = _address.Text.Trim();
                fields["Established"] = _established.Text.Trim();
            }

            try
            {
                if (_originalCode.Length > 0)
                {
                    bool updated = DataFiles.ReplaceMatchingRow(
                        baseName,
                        record => DataFiles.GetRecord(record, "Code")
                            .Equals(_originalCode, StringComparison.OrdinalIgnoreCase),
                        DataFiles.NamedRow(baseName, fields));
                    if (!updated)
                    {
                        MessageBox.Show("Could not find that record to update.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
                else
                {
                    DataFiles.AppendNamedRow(baseName, fields);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
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

        private static void SetRowHeights(TableLayoutPanel grid, params int[] heights)
        {
            grid.RowStyles.Clear();
            grid.RowCount = heights.Length;
            for (int i = 0; i < heights.Length; i++)
            {
                grid.RowStyles.Add(heights[i] <= 0
                    ? new RowStyle(SizeType.Percent, 100)
                    : new RowStyle(SizeType.Absolute, heights[i]));
            }
        }

        /// <summary>
        /// One History card with tabs: Description (text), Sales or Purchases, and Bank transactions.
        /// </summary>
        internal static CardPanel BuildHistoryCard(
            bool vendor,
            Dictionary<string, string>? record,
            TextBox notes)
        {
            string code = record == null ? "" : DataFiles.GetRecord(record, "Code");
            string name = record == null ? "" : DataFiles.GetRecord(record, "Name");
            string company = record == null ? "" : DataFiles.GetRecord(record, "Company");

            var card = new CardPanel
            {
                Height = 360,
                Padding = new Padding(8, 8, 8, 8)
            };

            var tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = Theme.BodyBold,
                Padding = new Point(14, 6),
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(168, 30),
                DrawMode = TabDrawMode.OwnerDrawFixed
            };
            tabs.DrawItem += PaintHistoryTab;

            var descPage = new TabPage("Description")
            {
                BackColor = Theme.Paper,
                Padding = new Padding(8)
            };
            descPage.Controls.Add(notes);

            var lines = MakeHistoryGrid();
            if (vendor)
                FillPurchases(lines, code, name);
            else
                FillSales(lines, code, name);
            var linesPage = new TabPage(vendor ? "Purchases" : "Sales")
            {
                BackColor = Theme.Paper,
                Padding = new Padding(8)
            };
            lines.Dock = DockStyle.Fill;
            linesPage.Controls.Add(lines);

            var bank = MakeHistoryGrid();
            BankFeed.FillPartyGrid(bank, vendor, code, name, company);
            var bankPage = new TabPage("Bank transactions")
            {
                BackColor = Theme.Paper,
                Padding = new Padding(8)
            };
            bank.Dock = DockStyle.Fill;
            bankPage.Controls.Add(bank);

            tabs.TabPages.Add(descPage);
            tabs.TabPages.Add(linesPage);
            tabs.TabPages.Add(bankPage);
            card.Controls.Add(tabs);
            return card;
        }

        private static void PaintHistoryTab(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            var bounds = e.Bounds;
            using var fill = new SolidBrush(selected ? Theme.Paper : Theme.Cream);
            e.Graphics.FillRectangle(fill, bounds);
            TextRenderer.DrawText(
                e.Graphics,
                tabs.TabPages[e.Index].Text,
                Theme.BodyBold,
                bounds,
                selected ? Theme.Navy : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected)
            {
                using var gold = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(gold, bounds.X, bounds.Bottom - 3, bounds.Width, 3);
            }
        }

        private static DataGridView MakeHistoryGrid()
        {
            var grid = new DataGridView
            {
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false
            };
            Theme.StyleGrid(grid);
            return grid;
        }

        private static void FillSales(DataGridView grid, string code, string name)
        {
            grid.Columns.Clear();
            grid.Columns.Add("Ship Date", "Ship Date");
            grid.Columns.Add("SO #", "SO #");
            grid.Columns.Add("PO #", "PO #");
            grid.Columns.Add("Item Code", "Item Code");
            grid.Columns.Add("Amount", "Amount");

            if (code.Length > 0 || name.Length > 0)
            {
                foreach (var sale in DataFiles.ReadRecords(DataFiles.Sales))
                {
                    if (!DataFiles.MatchesCustomer(sale, code, name))
                        continue;
                    grid.Rows.Add(
                        DataFiles.GetRecord(sale, "Ship Date"),
                        DataFiles.GetRecord(sale, "SO #"),
                        DataFiles.SalePo(sale),
                        DataFiles.GetRecord(sale, "Item Code"),
                        DataFiles.GetRecord(sale, "Amount"));
                }
            }

            if (grid.Rows.Count == 0)
                grid.Rows.Add("No sales yet", "", "", "", "");
        }

        private static void FillPurchases(DataGridView grid, string code, string name)
        {
            grid.Columns.Clear();
            grid.Columns.Add("Ship Date", "Ship Date");
            grid.Columns.Add("PO #", "PO #");
            grid.Columns.Add("Item Code", "Item Code");
            grid.Columns.Add("Vendor Invoice #", "Vendor Invoice #");
            grid.Columns.Add("Total Cost", "Total Cost");

            if (code.Length > 0 || name.Length > 0)
            {
                foreach (var purchase in DataFiles.ReadRecords(DataFiles.PurchaseSales))
                {
                    if (!DataFiles.MatchesVendor(purchase, code, name))
                        continue;
                    grid.Rows.Add(
                        DataFiles.GetRecord(purchase, "Ship Date"),
                        DataFiles.GetRecord(purchase, "PO #"),
                        DataFiles.GetRecord(purchase, "Item Code"),
                        DataFiles.GetRecord(purchase, "Vendor Invoice #"),
                        DataFiles.GetRecord(purchase, "Total Cost"));
                }
            }

            if (grid.Rows.Count == 0)
                grid.Rows.Add("No purchases yet", "", "", "", "");
        }

        private static CardPanel Section(string title, int height, int columns, out TableLayoutPanel grid)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = height
            };
            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 12)
            };
            grid = new TableLayoutPanel
            {
                ColumnCount = columns,
                RowCount = 1,
                Location = new Point(12, 42)
            };
            float share = 100f / Math.Max(1, columns);
            for (int i = 0; i < columns; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, share));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(heading);
            card.Controls.Add(grid);
            var table = grid;
            card.Resize += (_, _) =>
            {
                table.Width = Math.Max(180, card.ClientSize.Width - 28);
                table.Height = Math.Max(40, card.ClientSize.Height - 54);
            };
            return card;
        }

        private static TextBox PutField(
            TableLayoutPanel grid,
            int col,
            int row,
            string caption,
            int colSpan = 1,
            bool multiline = false)
        {
            while (grid.RowCount <= row)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                grid.RowCount++;
            }

            var box = new TextBox
            {
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
            };
            Theme.StyleField(box);
            var cell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 4, 8, 8)
            };
            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Top,
                Height = 18
            };
            Theme.StyleFieldLabel(label);
            box.Dock = multiline ? DockStyle.Fill : DockStyle.Top;
            if (!multiline)
                box.Height = 28;
            cell.Controls.Add(box);
            cell.Controls.Add(label);
            grid.Controls.Add(cell, col, row);
            if (colSpan > 1)
                grid.SetColumnSpan(cell, colSpan);
            return box;
        }
    }
}
