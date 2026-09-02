namespace CastRightCatchInvManagement
{
    /// <summary>Customer or vendor history: identity plus tabs for description, sales or purchases, and bank transactions.</summary>
    internal sealed class CustomerHistoryForm : Form
    {
        public static void ShowFor(IWin32Window? owner, Dictionary<string, string> customer) =>
            Show(owner, customer, vendor: false);

        public static void ShowVendor(IWin32Window? owner, Dictionary<string, string> vendor) =>
            Show(owner, vendor, vendor: true);

        private static void Show(IWin32Window? owner, Dictionary<string, string> record, bool vendor)
        {
            using var form = new CustomerHistoryForm(record, vendor);
            if (owner != null)
                form.ShowDialog(owner);
            else
                form.ShowDialog();
        }

        private CustomerHistoryForm(Dictionary<string, string> customer, bool vendor)
        {
            string name = First(customer, "Name", "Company");
            string code = DataFiles.GetRecord(customer, "Code");
            string company = First(customer, "Company", "Name");
            string notes = DataFiles.GetRecordAny(customer, "Description", "Notes");
            string contact = DataFiles.GetRecord(customer, "Contact Name");

            string kind = vendor ? "Vendor" : "Customer";
            Text = name.Length > 0 ? name : kind + " history";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(820, 860);
            MinimumSize = new Size(640, 680);
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
                Text = name.Length > 0 ? name : kind,
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
                Text = subtitleParts.Count > 0 ? string.Join("  ·  ", subtitleParts) : kind + " history",
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

            var notesBox = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                ReadOnly = true,
                Dock = DockStyle.Fill,
                Text = notes
            };
            Theme.StyleField(notesBox);
            notesBox.BackColor = Theme.Paper;

            var history = PartyEditForm.BuildHistoryCard(vendor, customer, notesBox);
            var identity = BuildIdentityCard(vendor, customer);

            var topHost = new Panel
            {
                Dock = DockStyle.Top,
                Height = identity.Height + 28,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 20, 12)
            };
            identity.Dock = DockStyle.Fill;
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

        private static CardPanel BuildIdentityCard(bool vendor, Dictionary<string, string> record)
        {
            var card = new CardPanel
            {
                Height = vendor ? 210 : 340,
                Padding = new Padding(12, 10, 12, 10)
            };
            var heading = new Label
            {
                Text = "Identity",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 10)
            };
            var grid = new TableLayoutPanel
            {
                ColumnCount = 4,
                RowCount = vendor ? 2 : 4,
                Location = new Point(12, 40),
                Dock = DockStyle.None
            };
            for (int i = 0; i < 4; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            PutReadout(grid, 0, 0, "CODE", DataFiles.GetRecord(record, "Code"));
            PutReadout(grid, 1, 0, "NAME", DataFiles.GetRecord(record, "Name"));
            PutReadout(grid, 2, 0, "COMPANY", First(record, "Company", "Name"));
            PutReadout(grid, 3, 0, "PHONE", DataFiles.GetRecord(record, "Phone"));

            if (vendor)
            {
                PutReadout(grid, 0, 1, "TERMS", DataFiles.GetRecord(record, "Terms"));
                PutReadout(grid, 1, 1, "TYPE", DataFiles.GetRecord(record, "Type"));
                PutReadout(grid, 2, 1, "AMOUNT", DataFiles.GetRecord(record, "Amount"));
                PutReadout(grid, 3, 1, "CURRENT BALANCE", DataFiles.GetRecord(record, "Current Balance"));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            }
            else
            {
                PutReadout(grid, 0, 1, "CONTACT NAME", DataFiles.GetRecord(record, "Contact Name"));
                PutReadout(grid, 1, 1, "EMAIL", DataFiles.GetRecord(record, "Email"));
                PutReadout(grid, 2, 1, "TERMS", DataFiles.GetRecord(record, "Terms"));
                PutReadout(grid, 3, 1, "CREDIT LIMIT", DataFiles.GetRecord(record, "Credit Limit"));
                PutReadout(grid, 0, 2, "CURRENT BALANCE", DataFiles.GetRecord(record, "Current Balance"));
                PutReadout(grid, 1, 2, "ESTABLISHED", DataFiles.GetRecord(record, "Established"));
                PutReadout(grid, 0, 3, "ADDRESS", DataFiles.GetRecord(record, "Address"), colSpan: 4);
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
                grid.RowStyles.Add(new RowStyle(SizeType.Absolute, 56));
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }

            card.Controls.Add(heading);
            card.Controls.Add(grid);
            card.Resize += (_, _) =>
            {
                grid.Width = Math.Max(180, card.ClientSize.Width - 28);
                grid.Height = Math.Max(40, card.ClientSize.Height - 52);
            };
            return card;
        }

        private static void PutReadout(
            TableLayoutPanel grid,
            int col,
            int row,
            string caption,
            string value,
            int colSpan = 1)
        {
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
            var box = new Label
            {
                Text = Display(value),
                Dock = DockStyle.Fill,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                AutoEllipsis = true
            };
            cell.Controls.Add(box);
            cell.Controls.Add(label);
            grid.Controls.Add(cell, col, row);
            if (colSpan > 1)
                grid.SetColumnSpan(cell, colSpan);
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
