namespace CastRightCatchInvManagement
{
    /// <summary>Read-only popup of every field on a grid row. Not a workspace tab.</summary>
    internal sealed class RecordDetailsForm : Form
    {
        public static void ShowRecord(IWin32Window? owner, string title, Dictionary<string, string> record)
        {
            using var form = new RecordDetailsForm(title, record);
            if (owner != null)
                form.ShowDialog(owner);
            else
                form.ShowDialog();
        }

        private RecordDetailsForm(string title, Dictionary<string, string> record)
        {
            string heading = string.IsNullOrWhiteSpace(title) ? "Details" : title;
            string name = First(record, "Name", "Customer", "Vendor", "Invoice #", "PO #", "SO #");
            Text = name.Length > 0 ? heading + "  ·  " + name : heading + " details";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(640, 640);
            MinimumSize = new Size(480, 400);
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
            var titleLabel = new Label
            {
                Text = name.Length > 0 ? name : heading,
                Dock = DockStyle.Top,
                Height = 36,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            var subtitle = new Label
            {
                Text = heading + " details",
                Dock = DockStyle.Top,
                Height = 22,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft
            };
            header.Controls.Add(subtitle);
            header.Controls.Add(titleLabel);
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
            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                RowCount = 1,
                Dock = DockStyle.Top,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int col = 0;
            int row = 0;
            foreach (var pair in fields)
            {
                bool wide = IsWide(pair.Key);
                if (wide && col == 1)
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

                var cell = FieldCell(pair.Key, pair.Value);
                grid.Controls.Add(cell, wide ? 0 : col, row);
                if (wide)
                {
                    grid.SetColumnSpan(cell, 2);
                    col = 0;
                    row++;
                }
                else
                {
                    col++;
                    if (col > 1)
                    {
                        col = 0;
                        row++;
                    }
                }
            }

            card.Controls.Add(grid);
            card.Height = Math.Max(200, 48 + ((fields.Count + 1) / 2) * 58);
            scroller.Resize += (_, _) =>
                card.Width = Math.Max(280, scroller.ClientSize.Width - 28);

            scroller.Controls.Add(card);
            Controls.Add(scroller);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private static Panel FieldCell(string caption, string value)
        {
            var cell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(12, 8, 12, 8),
                Height = 48
            };
            var label = new Label
            {
                Text = caption.ToUpperInvariant(),
                Dock = DockStyle.Top,
                Height = 18
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
                   name.Equals("Description", StringComparison.OrdinalIgnoreCase);
        }

        private static List<KeyValuePair<string, string>> OrderedFields(
            string title,
            Dictionary<string, string> record)
        {
            string[] prefer = title.Equals("Customer", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    "Code", "Name", "Company", "Phone", "Email", "Contact Name",
                    "Terms", "Credit Limit", "Current Balance", "Established",
                    "Address", "Description", "Notes"
                }
                : title.Equals("Vendor", StringComparison.OrdinalIgnoreCase)
                ? new[]
                {
                    "Code", "Name", "Company", "Phone", "Type", "Terms",
                    "Amount", "Current Balance", "Finalized", "Description", "Notes"
                }
                : Array.Empty<string>();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<KeyValuePair<string, string>>();
            foreach (var key in prefer)
            {
                if (!TryGet(record, key, out var value))
                    continue;
                used.Add(key);
                list.Add(new KeyValuePair<string, string>(key, value));
            }

            foreach (var pair in record)
            {
                string key = pair.Key.Trim();
                if (key.Length == 0 || !used.Add(key))
                    continue;
                list.Add(new KeyValuePair<string, string>(key, pair.Value ?? ""));
            }

            return list;
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
    }
}
