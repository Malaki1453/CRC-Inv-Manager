namespace CastRightCatchInvManagement
{
    internal sealed class CustomerHistoryForm : Form
    {
        public static void ShowFor(IWin32Window? owner, Dictionary<string, string> customer)
        {
            using var form = new CustomerHistoryForm(customer);
            if (owner != null)
                form.ShowDialog(owner);
            else
                form.ShowDialog();
        }

        private CustomerHistoryForm(Dictionary<string, string> customer)
        {
            string name = First(customer, "Name", "Company");
            string code = DataFiles.GetRecord(customer, "Code");
            Text = name.Length > 0 ? name + "  ·  History" : "Customer history";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(760, 620);
            BackColor = Theme.Cream;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

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
                Height = 56,
                BackColor = Theme.Cream
            };
            footer.Controls.Add(close);
            footer.Resize += (_, _) =>
                close.Location = new Point(Math.Max(20, footer.Width - 130), 10);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 12, 12)
            };

            var info = new CardPanel { Height = 150, Dock = DockStyle.Top };
            string company = First(customer, "Company", "Name");
            string phone = DataFiles.GetRecord(customer, "Phone");
            string balance = DataFiles.GetRecord(customer, "Current Balance");
            string notes = DataFiles.GetRecord(customer, "Notes");
            info.Controls.Add(InfoLabel("NAME", name, 20, 16));
            info.Controls.Add(InfoValue(name, 20, 32));
            info.Controls.Add(InfoLabel("COMPANY", company, 260, 16));
            info.Controls.Add(InfoValue(company, 260, 32));
            info.Controls.Add(InfoLabel("PHONE", phone, 500, 16));
            info.Controls.Add(InfoValue(phone, 500, 32));
            info.Controls.Add(InfoLabel("CURRENT BALANCE", balance, 20, 70));
            info.Controls.Add(InfoValue(string.IsNullOrWhiteSpace(balance) ? "—" : balance, 20, 86));
            info.Controls.Add(InfoLabel("NOTES", notes, 260, 70));
            var notesValue = InfoValue(string.IsNullOrWhiteSpace(notes) ? "—" : notes, 260, 86);
            notesValue.Size = new Size(420, 40);
            info.Controls.Add(notesValue);

            var salesCard = new CardPanel { Height = 220, Dock = DockStyle.Top };
            var salesHeading = new Label
            {
                Text = "Sales history",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 12)
            };
            var salesGrid = new DataGridView
            {
                Location = new Point(16, 44),
                Size = new Size(700, 160),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false
            };
            Theme.StyleGrid(salesGrid);
            FillHistory(salesGrid, code, name);
            salesCard.Controls.Add(salesHeading);
            salesCard.Controls.Add(salesGrid);
            salesCard.Resize += (_, _) =>
                salesGrid.Width = Math.Max(200, salesCard.Width - 36);

            var payCard = new CardPanel { Height = 120, Dock = DockStyle.Top };
            var payHeading = new Label
            {
                Text = "Payment history",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 12)
            };
            var payHint = new Label
            {
                Text = "Payment history will show here when the bank is connected.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Location = new Point(20, 48),
                Size = new Size(680, 48)
            };
            payCard.Controls.Add(payHeading);
            payCard.Controls.Add(payHint);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Cream };
            var spacer2 = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Cream };

            scroll.Controls.Add(payCard);
            scroll.Controls.Add(spacer2);
            scroll.Controls.Add(salesCard);
            scroll.Controls.Add(spacer);
            scroll.Controls.Add(info);

            Controls.Add(scroll);
            Controls.Add(footer);
        }

        private static void FillHistory(DataGridView grid, string code, string name)
        {
            grid.Columns.Clear();
            grid.Columns.Add("Ship Date", "Ship Date");
            grid.Columns.Add("SO #", "SO #");
            grid.Columns.Add("PO #", "PO #");
            grid.Columns.Add("Item Code", "Item Code");
            grid.Columns.Add("Amount", "Amount");

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

            if (grid.Rows.Count == 0)
                grid.Rows.Add("No sales yet", "", "", "", "");
        }

        private static Label InfoLabel(string caption, string _, int x, int y)
        {
            var label = new Label { Text = caption, Location = new Point(x, y), AutoSize = true };
            Theme.StyleFieldLabel(label);
            return label;
        }

        private static Label InfoValue(string text, int x, int y)
        {
            return new Label
            {
                Text = string.IsNullOrWhiteSpace(text) ? "—" : text,
                Location = new Point(x, y),
                AutoSize = true,
                Font = Theme.Body,
                ForeColor = Theme.Ink
            };
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
    }
}
