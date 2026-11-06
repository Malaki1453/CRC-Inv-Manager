namespace CastRightCatchInvManagement
{
    /// <summary>Review popup for one queued add/edit/delete. Changed fields show old → new.</summary>
    internal sealed class ReviewDetailsForm : Form
    {
        private readonly List<Dictionary<string, string>> _members;

        /// <summary>Open the review details window. OK means Accept or Reject succeeded.</summary>
        public static bool ShowReview(IWin32Window? owner, List<Dictionary<string, string>> members)
        {
            using var form = new ReviewDetailsForm(members);
            DialogResult result = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
            return result == DialogResult.OK;
        }

        private ReviewDetailsForm(List<Dictionary<string, string>> members)
        {
            _members = members;
            string title = DataFiles.PendingReviewTitle(members);
            string subtitle = DataFiles.PendingReviewSubtitle(members);
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(720, 640);
            MinimumSize = new Size(520, 420);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            Controls.Add(BuildBody());
            Controls.Add(BuildFooter());
            Controls.Add(Chrome(title, subtitle));
        }

        /// <summary>Scrollable field cards. One card for a single request, or one per line on a PO/SO group.</summary>
        private Control BuildBody()
        {
            var scroller = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 8, 16)
            };

            var stack = new Panel
            {
                Location = new Point(0, 0),
                Width = 640,
                BackColor = Theme.Cream
            };

            int y = 0;
            bool many = _members.Count > 1;
            foreach (var pending in _members)
            {
                var fields = DataFiles.PendingReviewFields(pending);
                if (fields.Count == 0)
                    continue;
                string heading = many
                    ? DataFiles.PendingReviewLineTitle(pending)
                    : "Changes";
                var pairs = fields
                    .Select(field => new KeyValuePair<string, string>(field.Label, field.Value))
                    .ToList();
                var changed = fields
                    .Where(field => field.Changed)
                    .Select(field => field.Label)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase);
                int rows = Math.Max(1, (pairs.Count + 1) / 2);
                int height = 48 + 16 + rows * 58;
                if (many)
                    height += 8;
                var card = FieldCard(heading, pairs, changed, height);
                card.Location = new Point(0, y);
                stack.Controls.Add(card);
                y += height + 12;
            }

            if (stack.Controls.Count == 0)
            {
                var empty = new Label
                {
                    Text = "No field values were stored on this request.",
                    Font = Theme.Body,
                    ForeColor = Theme.Muted,
                    AutoSize = true,
                    Location = new Point(8, 8)
                };
                stack.Controls.Add(empty);
                y = 40;
            }

            stack.Height = y;
            scroller.Resize += (_, _) =>
            {
                int width = Math.Max(280, scroller.ClientSize.Width - 28);
                stack.Width = width;
                foreach (Control child in stack.Controls)
                    child.Width = width;
            };
            scroller.Controls.Add(stack);
            return scroller;
        }

        /// <summary>Reject, Close, and Accept along the bottom.</summary>
        private Panel BuildFooter()
        {
            var reject = new Button { Text = "Reject", Size = new Size(120, 34) };
            Theme.StyleNavyButton(reject);
            reject.Click += (_, _) => Decide(accept: false);

            var accept = new Button { Text = "Accept", Size = new Size(120, 34) };
            Theme.StyleGoldButton(accept);
            accept.Click += (_, _) => Decide(accept: true);

            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.Cancel,
                Size = new Size(110, 34)
            };
            Theme.StyleOutlineButton(close);
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
            footer.Controls.Add(reject);
            footer.Controls.Add(close);
            footer.Controls.Add(accept);
            footer.Resize += (_, _) =>
            {
                reject.Location = new Point(20, 13);
                accept.Location = new Point(Math.Max(160, footer.Width - 140), 13);
                close.Location = new Point(Math.Max(160, footer.Width - 260), 13);
            };
            return footer;
        }

        /// <summary>Apply Accept or Reject for every line in this group, then close on success.</summary>
        private void Decide(bool accept)
        {
            var record = _members[0];
            string table = DataFiles.GetRecord(record, "Table");
            if (DataFiles.GetRecord(record, "Action").Equals("delete", StringComparison.OrdinalIgnoreCase) &&
                DataAccess.IsTradeTable(table) &&
                !AppState.IsAdmin)
            {
                ToastAlert.Error(this, "An administrator must review purchase and sales deletes.");
                return;
            }

            bool ok;
            try
            {
                ok = DataFiles.ReviewPendingGroup(_members, accept);
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            if (!ok)
            {
                ToastAlert.Error(this, "Could not update that request.");
                return;
            }

            ToastAlert.Success(this, accept ? "The change was accepted." : "The change was rejected.");
            DialogResult = DialogResult.OK;
            Close();
        }

        private static Panel Chrome(string title, string subtitleText)
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

        private static CardPanel FieldCard(
            string heading,
            List<KeyValuePair<string, string>> fields,
            HashSet<string> changed,
            int height)
        {
            var card = new CardPanel
            {
                Height = height,
                Padding = new Padding(12, 16, 12, 16)
            };
            var grid = new TableLayoutPanel
            {
                ColumnCount = 2,
                Dock = DockStyle.Fill,
                AutoSize = true,
                AutoSizeMode = AutoSizeMode.GrowAndShrink
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.AutoSize));

            int col = 0;
            int row = 0;
            foreach (var pair in fields)
            {
                bool wide = IsWide(pair.Key);
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

                var cell = FieldCell(pair.Key, pair.Value, changed.Contains(pair.Key));
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
                    if (col >= 2)
                    {
                        col = 0;
                        row++;
                    }
                }
            }

            var body = new Panel { Dock = DockStyle.Fill, Padding = new Padding(8, 6, 8, 6) };
            body.Controls.Add(grid);
            card.Controls.Add(body);
            card.Controls.Add(SectionBar(heading));
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
                Text = title.ToUpperInvariant(),
                Dock = DockStyle.Fill,
                Font = Theme.Caption,
                ForeColor = Theme.HeaderText,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(12, 0, 0, 0)
            });
            return bar;
        }

        private static Panel FieldCell(string caption, string value, bool changed)
        {
            var cell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(12, 6, 12, 6),
                Height = 44
            };
            var label = new Label
            {
                Text = caption.ToUpperInvariant(),
                Dock = DockStyle.Top,
                Height = 16
            };
            Theme.StyleFieldLabel(label);
            var box = new Label
            {
                Text = string.IsNullOrWhiteSpace(value) ? "—" : value,
                Dock = DockStyle.Fill,
                Font = changed ? Theme.BodyBold : Theme.Body,
                ForeColor = changed ? Theme.Navy : Theme.Ink
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
                   name.Equals("Ship To", StringComparison.OrdinalIgnoreCase);
        }
    }
}
