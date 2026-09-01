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
            string company = First(customer, "Company", "Name");
            string phone = DataFiles.GetRecord(customer, "Phone");
            string balance = DataFiles.GetRecord(customer, "Current Balance");
            string notes = DataFiles.GetRecordAny(customer, "Description", "Notes");
            string email = DataFiles.GetRecord(customer, "Email");
            string contact = DataFiles.GetRecord(customer, "Contact Name");
            string terms = DataFiles.GetRecord(customer, "Terms");

            Text = name.Length > 0 ? name : "Customer history";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(820, 680);
            MinimumSize = new Size(640, 520);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
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
                Text = name.Length > 0 ? name : "Customer",
                Dock = DockStyle.Top,
                Height = 38,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            var subtitleParts = new List<string>();
            if (company.Length > 0 && !company.Equals(name, StringComparison.OrdinalIgnoreCase))
                subtitleParts.Add(company);
            if (code.Length > 0)
                subtitleParts.Add(code);
            if (contact.Length > 0)
                subtitleParts.Add(contact);
            var subtitle = new Label
            {
                Text = subtitleParts.Count > 0 ? string.Join("  ·  ", subtitleParts) : "Customer history",
                Dock = DockStyle.Top,
                Height = 24,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft
            };
            header.Controls.Add(subtitle);
            header.Controls.Add(title);
            header.Controls.Add(gold);

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
                BackColor = Theme.Paper
            };
            footer.Paint += (_, e) =>
            {
                using var line = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(line, 0, 0, footer.Width, 2);
            };
            footer.Controls.Add(close);
            footer.Resize += (_, _) =>
                close.Location = new Point(Math.Max(20, footer.Width - 130), 13);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 8, 16)
            };

            var stats = new CardPanel { Dock = DockStyle.Top, Height = 108 };
            AddStat(stats, "PHONE", Display(phone), 20);
            AddStat(stats, "EMAIL", Display(email), 220);
            AddStat(stats, "TERMS", Display(terms), 420);
            AddStat(stats, "BALANCE", Display(balance), 600);
            stats.Resize += (_, _) =>
            {
                int gap = Math.Max(160, (stats.Width - 48) / 4);
                int i = 0;
                foreach (Control child in stats.Controls)
                {
                    if (child is Label lbl && lbl.Tag is int col)
                        lbl.Left = 20 + col * gap;
                    i++;
                }
            };

            var notesCard = new CardPanel { Dock = DockStyle.Top, Height = 96 };
            var notesHeading = new Label
            {
                Text = "Description",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 12)
            };
            var notesValue = new Label
            {
                Text = Display(notes),
                Font = Theme.Body,
                ForeColor = Theme.Ink,
                Location = new Point(20, 42),
                Size = new Size(720, 40)
            };
            notesCard.Controls.Add(notesHeading);
            notesCard.Controls.Add(notesValue);
            notesCard.Resize += (_, _) =>
                notesValue.Width = Math.Max(200, notesCard.Width - 44);

            var salesCard = new CardPanel { Dock = DockStyle.Top, Height = 260 };
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
                Size = new Size(740, 196),
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom
            };
            Theme.StyleGrid(salesGrid);
            FillHistory(salesGrid, code, name);
            salesCard.Controls.Add(salesHeading);
            salesCard.Controls.Add(salesGrid);
            salesCard.Resize += (_, _) =>
            {
                salesGrid.Width = Math.Max(200, salesCard.Width - 36);
                salesGrid.Height = Math.Max(80, salesCard.Height - 60);
            };

            var payCard = new CardPanel { Dock = DockStyle.Top, Height = 110 };
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
                Size = new Size(720, 40)
            };
            payCard.Controls.Add(payHeading);
            payCard.Controls.Add(payHint);
            payCard.Resize += (_, _) =>
                payHint.Width = Math.Max(200, payCard.Width - 44);

            body.Controls.Add(payCard);
            body.Controls.Add(Spacer());
            body.Controls.Add(salesCard);
            body.Controls.Add(Spacer());
            body.Controls.Add(notesCard);
            body.Controls.Add(Spacer());
            body.Controls.Add(stats);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private static void AddStat(Control parent, string caption, string value, int x)
        {
            var label = new Label
            {
                Text = caption,
                Location = new Point(x, 18),
                AutoSize = true,
                Tag = caption == "PHONE" ? 0 : caption == "EMAIL" ? 1 : caption == "TERMS" ? 2 : 3
            };
            Theme.StyleFieldLabel(label);
            var box = new Label
            {
                Text = value,
                Location = new Point(x, 38),
                AutoSize = true,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                Tag = label.Tag
            };
            parent.Controls.Add(label);
            parent.Controls.Add(box);
        }

        private static Panel Spacer()
        {
            return new Panel
            {
                Dock = DockStyle.Top,
                Height = 12,
                BackColor = Theme.Cream
            };
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

        private static string Display(string value) =>
            string.IsNullOrWhiteSpace(value) ? "—" : value.Trim();

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
