namespace CastRightCatchInvManagement
{
    /// <summary>Collect email addresses and send the open PDF to each one through Admin SMTP.</summary>
    internal sealed class PdfSendForm : Form
    {
        private readonly string _pdfPath;
        private readonly string _title;
        private readonly TextBox _emails;
        private readonly TextBox _subject;
        private readonly Label _status;
        private readonly Button _send;
        private bool _busy;

        /// <summary>Show the send dialog parented to the PDF viewer.</summary>
        public static void ShowFor(IWin32Window owner, string pdfPath, string title)
        {
            using var form = new PdfSendForm(pdfPath, title);
            form.ShowDialog(owner);
        }

        /// <summary>Build the address list, subject, and send action.</summary>
        private PdfSendForm(string pdfPath, string title)
        {
            _pdfPath = pdfPath;
            _title = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(pdfPath)
                : title.Trim();

            Text = "Send PDF";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(520, 420);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 64,
                BackColor = Theme.Paper
            };
            var gold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };
            var heading = new Label
            {
                Text = "Send PDF",
                Dock = DockStyle.Top,
                Height = 36,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                Padding = new Padding(20, 10, 20, 0)
            };
            header.Controls.Add(heading);
            header.Controls.Add(gold);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Theme.Cream
            };
            _send = new Button
            {
                Text = "Send",
                Size = new Size(110, 34),
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            Theme.StyleGoldButton(_send);
            _send.Click += async (_, _) => await SendAsync();
            var cancel = new Button
            {
                Text = "Cancel",
                Size = new Size(96, 34),
                DialogResult = DialogResult.Cancel,
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            Theme.StyleOutlineButton(cancel);
            footer.Controls.Add(_send);
            footer.Controls.Add(cancel);
            footer.Resize += (_, _) =>
            {
                _send.Location = new Point(Math.Max(140, footer.Width - 128), 10);
                cancel.Location = new Point(Math.Max(20, footer.Width - 240), 10);
            };

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 16, 20, 8)
            };
            var fileLabel = new Label
            {
                Text = "FILE",
                Location = new Point(0, 0),
                AutoSize = true
            };
            Theme.StyleFieldLabel(fileLabel);
            var fileValue = new Label
            {
                Text = Path.GetFileName(_pdfPath),
                Location = new Point(0, 16),
                AutoSize = true,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy
            };

            var subjectLabel = new Label
            {
                Text = "SUBJECT",
                Location = new Point(0, 48),
                AutoSize = true
            };
            Theme.StyleFieldLabel(subjectLabel);
            _subject = new TextBox
            {
                Location = new Point(0, 66),
                Width = 460,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
                Text = _title
            };
            Theme.StyleField(_subject);

            var emailLabel = new Label
            {
                Text = "EMAILS  ·  one per line, or separated by commas",
                Location = new Point(0, 108),
                AutoSize = true
            };
            Theme.StyleFieldLabel(emailLabel);
            _emails = new TextBox
            {
                Location = new Point(0, 126),
                Size = new Size(460, 160),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                AcceptsReturn = true,
                Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Bottom,
                PlaceholderText = "name@company.com"
            };
            Theme.StyleField(_emails);
            string mine = (AppState.UserEmail ?? "").Trim();
            if (mine.Length > 0 && mine.Contains('@'))
                _emails.Text = mine;

            _status = new Label
            {
                Text = "",
                Dock = DockStyle.Bottom,
                Height = 28,
                ForeColor = Theme.Muted
            };

            body.Controls.Add(_emails);
            body.Controls.Add(emailLabel);
            body.Controls.Add(_subject);
            body.Controls.Add(subjectLabel);
            body.Controls.Add(fileValue);
            body.Controls.Add(fileLabel);
            body.Controls.Add(_status);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }

        /// <summary>Parse the list and send the PDF to each address through Admin SMTP.</summary>
        private async Task SendAsync()
        {
            if (_busy)
                return;

            var addresses = ParseEmails(_emails.Text);
            string mine = (AppState.UserEmail ?? "").Trim();
            if (mine.Length > 0 && mine.Contains('@') &&
                !addresses.Any(a => a.Equals(mine, StringComparison.OrdinalIgnoreCase)))
                addresses.Insert(0, mine);
            if (addresses.Count == 0)
            {
                _status.ForeColor = Theme.Danger;
                _status.Text = "Enter at least one email address.";
                _emails.Focus();
                return;
            }

            if (!File.Exists(_pdfPath))
            {
                _status.ForeColor = Theme.Danger;
                _status.Text = "The PDF file could not be found.";
                return;
            }

            string subject = _subject.Text.Trim();
            if (subject.Length == 0)
                subject = _title;
            string senderName = (AppState.CurrentDisplayName ?? "").Trim();
            if (senderName.Length == 0)
                senderName = (AppState.CurrentUsername ?? "").Trim();
            string senderLine = senderName.Length > 0 ? senderName : "a Cast Right Catch user";
            if (mine.Length > 0)
                senderLine += " (" + mine + ")";
            string body =
                "Please find the attached PDF.\n\n" +
                _title + "\n\n" +
                "Sent by " + senderLine + ".\n";

            _busy = true;
            _send.Enabled = false;
            _emails.Enabled = false;
            _subject.Enabled = false;
            int sent = 0;
            var failures = new List<string>();
            try
            {
                for (int i = 0; i < addresses.Count; i++)
                {
                    string to = addresses[i];
                    _status.ForeColor = Theme.Muted;
                    _status.Text = "Sending " + (i + 1) + " of " + addresses.Count + "…";
                    var result = await Task.Run(() =>
                    {
                        bool ok = Mailer.TrySend(to, subject, body, _pdfPath, out string error);
                        return (ok, error);
                    });
                    if (result.ok)
                        sent++;
                    else
                        failures.Add(to + ": " + result.error);
                }
            }
            finally
            {
                _busy = false;
                _send.Enabled = true;
                _emails.Enabled = true;
                _subject.Enabled = true;
            }

            if (failures.Count == 0)
            {
                ToastAlert.Success(this, sent == 1
                    ? "The PDF was sent."
                    : "The PDF was sent to " + sent + " addresses.");
                DialogResult = DialogResult.OK;
                return;
            }

            _status.ForeColor = Theme.Danger;
            _status.Text = sent == 0
                ? "Could not send: " + failures[0]
                : "Sent " + sent + ", failed " + failures.Count + ". " + failures[0];
        }

        /// <summary>Unique email addresses from lines, commas, and semicolons.</summary>
        public static List<string> ParseEmails(string? text)
        {
            var list = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string part in (text ?? "").Split(
                new[] { ',', ';', '\n', '\r', ' ', '\t' },
                StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                if (!part.Contains('@') || !seen.Add(part))
                    continue;
                list.Add(part);
            }

            return list;
        }
    }
}
