namespace CastRightCatchInvManagement
{
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
            Text = string.IsNullOrWhiteSpace(title) ? "Details" : title + " details";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(560, 640);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            MinimumSize = new Size(420, 360);
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Size = new Size(110, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            Theme.StyleNavyButton(close);
            AcceptButton = close;
            CancelButton = close;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 10, 20, 12)
            };
            footer.Controls.Add(close);
            footer.Resize += (_, _) =>
                close.Location = new Point(Math.Max(20, footer.Width - 130), 10);

            var scroller = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 12, 12)
            };

            var card = new CardPanel
            {
                Location = new Point(20, 16),
                Padding = new Padding(20, 16, 20, 16)
            };

            int y = 12;
            foreach (var pair in record)
            {
                string name = pair.Key.Trim();
                if (name.Length == 0)
                    continue;

                var label = new Label
                {
                    Text = name.ToUpperInvariant(),
                    AutoSize = true,
                    Location = new Point(20, y)
                };
                Theme.StyleFieldLabel(label);

                var value = new Label
                {
                    Text = string.IsNullOrWhiteSpace(pair.Value) ? "—" : pair.Value,
                    AutoSize = false,
                    Location = new Point(20, y + 16),
                    Height = 22,
                    ForeColor = Theme.Ink,
                    Font = Theme.Body
                };

                card.Controls.Add(label);
                card.Controls.Add(value);
                y += 46;
            }

            card.Height = y + 8;
            scroller.Controls.Add(card);
            scroller.Resize += (_, _) =>
            {
                int width = Math.Max(280, scroller.ClientSize.Width - 32);
                card.Width = width;
                foreach (Control child in card.Controls)
                {
                    if (child is Label lbl && !lbl.AutoSize)
                        lbl.Width = Math.Max(120, width - 48);
                }
            };

            Controls.Add(scroller);
            Controls.Add(footer);
        }
    }
}
