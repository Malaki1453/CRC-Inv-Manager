namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Match banking statement lines to an invoice. Remaining is invoice amount minus matched lines.
    /// </summary>
    internal sealed class ReceivePaymentForm : Form
    {
        private readonly Dictionary<string, string> _invoice;
        private readonly bool _received;
        private readonly decimal _amount;
        private readonly string _number;
        private readonly List<Dictionary<string, string>> _available = new();
        private readonly List<Dictionary<string, string>> _applied = new();
        private readonly HashSet<string> _original = new(StringComparer.OrdinalIgnoreCase);

        private readonly DataGridView _availableGrid = new();
        private readonly DataGridView _appliedGrid = new();
        private readonly TextBox _search = new();
        private readonly Label _amountValue = new();
        private readonly Label _paidValue = new();
        private readonly Label _remainingValue = new();
        private readonly Button _match = new();
        private readonly Button _unmatch = new();
        private readonly Button _save = new();
        private readonly Label _hint = new();
        private readonly WaitSpinner _spinner = new();

        /// <summary>Open the matcher. Returns true when a payment was saved.</summary>
        public static bool ShowFor(IWin32Window? owner, Dictionary<string, string> invoice)
        {
            using var form = new ReceivePaymentForm(invoice);
            var result = owner != null ? form.ShowDialog(owner) : form.ShowDialog();
            return result == DialogResult.OK;
        }

        /// <summary>Build the two-list matcher for this invoice.</summary>
        private ReceivePaymentForm(Dictionary<string, string> invoice)
        {
            _invoice = new Dictionary<string, string>(invoice, StringComparer.OrdinalIgnoreCase);
            _received = DataFiles.IsReceivedInvoice(_invoice);
            _amount = DataFiles.ParseMoney(DataFiles.GetRecord(_invoice, "Amount"));
            _number = DataFiles.GetRecord(_invoice, "Invoice #").Trim();

            string party = _received
                ? DataFiles.GetRecord(_invoice, "Vendor")
                : DataFiles.GetRecord(_invoice, "Customer");
            Text = _number.Length > 0 ? "Receive Payment  ·  " + _number : "Receive Payment";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(1100, 640);
            MinimumSize = new Size(840, 520);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var header = BuildHeader(party);
            var totals = BuildTotals();
            var footer = BuildFooter();
            var body = BuildBody();

            Controls.Add(body);
            Controls.Add(totals);
            Controls.Add(footer);
            Controls.Add(header);
            CancelButton = footer.Controls.OfType<Button>().FirstOrDefault(b => b.DialogResult == DialogResult.Cancel);

            Shown += (_, _) => LoadLines();
        }

        /// <summary>Title, invoice number, and customer or vendor.</summary>
        private Panel BuildHeader(string party)
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
            string who = party.Trim().Length > 0 ? party.Trim() : (_received ? "Vendor invoice" : "Customer invoice");
            var title = new Label
            {
                Text = _number.Length > 0 ? "Invoice " + _number : "Receive Payment",
                Dock = DockStyle.Top,
                Height = 36,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            var subtitle = new Label
            {
                Text = who + "  ·  Match banking statements to this invoice.",
                Dock = DockStyle.Top,
                Height = 22,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft
            };
            header.Controls.Add(subtitle);
            header.Controls.Add(title);
            header.Controls.Add(gold);
            return header;
        }

        /// <summary>Amount, paid from matched lines, and remaining due.</summary>
        private Panel BuildTotals()
        {
            var bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Theme.Cream,
                Padding = new Padding(24, 10, 24, 8)
            };
            AddTotal(bar, "INVOICE AMOUNT", _amountValue, 0);
            AddTotal(bar, "MATCHED", _paidValue, 220);
            AddTotal(bar, "REMAINING", _remainingValue, 440);
            _amountValue.Text = MoneyFormat.Display(_amount);
            return bar;
        }

        /// <summary>One caption-plus-value pair on the totals bar.</summary>
        private static void AddTotal(Panel bar, string caption, Label value, int x)
        {
            var label = new Label
            {
                Text = caption,
                Location = new Point(x, 0),
                AutoSize = true,
                Font = Theme.Caption,
                ForeColor = Theme.Muted
            };
            value.Location = new Point(x, 18);
            value.AutoSize = true;
            value.Font = Theme.SectionTitle;
            value.ForeColor = Theme.Navy;
            bar.Controls.Add(label);
            bar.Controls.Add(value);
        }

        /// <summary>Available statements on the left, matched lines on the right, match buttons in the middle.</summary>
        private Control BuildBody()
        {
            var body = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 1,
                Padding = new Padding(20, 4, 20, 8),
                BackColor = Theme.Cream
            };
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Absolute, 120));
            body.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 46));
            body.RowStyles.Add(new RowStyle(SizeType.Percent, 100));

            var left = Column("Banking statements", available: true);
            var right = Column("Matched to this invoice", available: false);
            var mid = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            _match.Text = "Match  >";
            _match.Size = new Size(100, 34);
            Theme.StyleNavyButton(_match);
            _match.Click += (_, _) => MoveSelected(_availableGrid, _available, _applied);
            _unmatch.Text = "<  Unmatch";
            _unmatch.Size = new Size(100, 34);
            Theme.StyleOutlineButton(_unmatch);
            _unmatch.Click += (_, _) => MoveSelected(_appliedGrid, _applied, _available);
            mid.Resize += (_, _) =>
            {
                int x = Math.Max(8, (mid.Width - 100) / 2);
                _match.Location = new Point(x, Math.Max(40, mid.Height / 2 - 44));
                _unmatch.Location = new Point(x, _match.Bottom + 12);
            };
            mid.Controls.Add(_match);
            mid.Controls.Add(_unmatch);

            body.Controls.Add(left, 0, 0);
            body.Controls.Add(mid, 1, 0);
            body.Controls.Add(right, 2, 0);

            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            _spinner.Size = new Size(48, 48);
            _spinner.Visible = false;
            _spinner.BackColor = Theme.Cream;
            body.Dock = DockStyle.Fill;
            host.Controls.Add(body);
            host.Controls.Add(_spinner);
            _spinner.BringToFront();
            host.Resize += (_, _) => CenterSpinner(host);
            return host;
        }

        /// <summary>One matcher column: heading, optional search, and a statement grid.</summary>
        private Panel Column(string heading, bool available)
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            var grid = available ? _availableGrid : _appliedGrid;
            StyleStatementGrid(grid);
            grid.Dock = DockStyle.Fill;
            grid.CellDoubleClick += (_, e) =>
            {
                // Header clicks are not a statement line.
                if (e.RowIndex < 0)
                    return;
                if (available)
                    MoveSelected(_availableGrid, _available, _applied);
                else
                    MoveSelected(_appliedGrid, _applied, _available);
            };

            if (available)
            {
                _search.PlaceholderText = "Search date, amount, or memo";
                Theme.StyleField(_search);
                _search.Dock = DockStyle.Top;
                _search.Height = 32;
                _search.TextChanged += (_, _) => PaintGrid(_availableGrid, _available, _search.Text);
            }

            var title = new Label
            {
                Text = heading,
                Dock = DockStyle.Top,
                Height = 28,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            host.Controls.Add(grid);
            if (available)
                host.Controls.Add(_search);
            host.Controls.Add(title);
            return host;
        }

        /// <summary>Compact read-only statement grid; multi-select so two lines can be matched together.</summary>
        private static void StyleStatementGrid(DataGridView grid)
        {
            Theme.StyleGrid(grid);
            grid.MultiSelect = true;
            grid.ReadOnly = true;
            grid.AllowUserToAddRows = false;
            grid.AllowUserToDeleteRows = false;
            grid.AllowUserToOrderColumns = false;
            grid.RowHeadersVisible = false;
            grid.Columns.Add("Date", "Date");
            grid.Columns.Add("Amount", "Amount");
            grid.Columns.Add("Account", "Account");
            grid.Columns.Add("Memo", "Memo");
            grid.Columns[0].Width = 90;
            grid.Columns[1].Width = 100;
            grid.Columns[2].Width = 120;
            grid.Columns[3].AutoSizeMode = DataGridViewAutoSizeColumnMode.Fill;
        }

        /// <summary>Save applies the matched set; Cancel leaves the invoice unchanged.</summary>
        private Panel BuildFooter()
        {
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
            _hint.AutoSize = false;
            _hint.Height = 34;
            _hint.Font = Theme.Small;
            _hint.ForeColor = Theme.Muted;
            _hint.TextAlign = ContentAlignment.MiddleLeft;
            _save.Text = "Apply payment";
            _save.Size = new Size(140, 34);
            Theme.StyleGoldButton(_save);
            _save.Click += (_, _) => Save();
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(100, 34)
            };
            Theme.StyleOutlineButton(cancel);
            footer.Controls.Add(_hint);
            footer.Controls.Add(_save);
            footer.Controls.Add(cancel);
            footer.Resize += (_, _) =>
            {
                cancel.Location = new Point(Math.Max(20, footer.Width - 120), 13);
                _save.Location = new Point(cancel.Left - 152, 13);
                _hint.Location = new Point(20, 13);
                _hint.Width = Math.Max(80, _save.Left - 32);
            };
            return footer;
        }

        /// <summary>Read statement lines on a worker so the dialog stays responsive.</summary>
        private void LoadLines()
        {
            _spinner.Visible = true;
            _spinner.BringToFront();
            CenterSpinner(_spinner.Parent);
            Theme.FadeGrid(_availableGrid, true);
            Theme.FadeGrid(_appliedGrid, true);
            var snapshot = new Dictionary<string, string>(_invoice, StringComparer.OrdinalIgnoreCase);
            Task.Run(() =>
            {
                List<Dictionary<string, string>> rows;
                try
                {
                    rows = DataFiles.BankLinesForPayment(snapshot);
                }
                catch
                {
                    rows = new List<Dictionary<string, string>>();
                }

                void Apply()
                {
                    if (IsDisposed)
                        return;
                    SplitRows(rows);
                    PaintGrid(_availableGrid, _available, _search.Text);
                    PaintGrid(_appliedGrid, _applied, "");
                    UpdateTotals();
                    Theme.FadeGrid(_availableGrid, false);
                    Theme.FadeGrid(_appliedGrid, false);
                    _spinner.Visible = false;
                }

                if (IsHandleCreated)
                    BeginInvoke(Apply);
                else
                    HandleCreated += (_, _) => Apply();
            });
        }

        /// <summary>Already tagged lines go to matched; the rest stay available.</summary>
        private void SplitRows(List<Dictionary<string, string>> rows)
        {
            _available.Clear();
            _applied.Clear();
            _original.Clear();
            foreach (var row in rows)
            {
                string tagged = DataFiles.GetRecord(row, "Invoice #").Trim();
                bool applied = _number.Length > 0 &&
                               tagged.Equals(_number, StringComparison.OrdinalIgnoreCase);
                if (applied)
                {
                    _applied.Add(row);
                    _original.Add(BankFeed.RowIdentity(row));
                }
                else
                    _available.Add(row);
            }

            SortByDate(_available);
            SortByDate(_applied);
        }

        /// <summary>Newest statement date first so recent deposits sit at the top.</summary>
        private static void SortByDate(List<Dictionary<string, string>> rows)
        {
            rows.Sort((a, b) =>
            {
                DateTime.TryParse(DataFiles.GetRecord(a, "Date"), out var da);
                DateTime.TryParse(DataFiles.GetRecord(b, "Date"), out var db);
                return db.CompareTo(da);
            });
        }

        /// <summary>Move the selected statement lines from one list to the other.</summary>
        private void MoveSelected(
            DataGridView fromGrid,
            List<Dictionary<string, string>> from,
            List<Dictionary<string, string>> to)
        {
            var move = new List<Dictionary<string, string>>();
            foreach (DataGridViewRow view in fromGrid.SelectedRows)
            {
                if (view.Tag is Dictionary<string, string> row)
                    move.Add(row);
            }

            // Nothing selected: matching would be a no-op.
            if (move.Count == 0)
                return;

            foreach (var row in move)
            {
                from.RemoveAll(item =>
                    BankFeed.RowIdentity(item).Equals(BankFeed.RowIdentity(row), StringComparison.OrdinalIgnoreCase));
                string key = BankFeed.RowIdentity(row);
                if (!to.Any(item =>
                        BankFeed.RowIdentity(item).Equals(key, StringComparison.OrdinalIgnoreCase)))
                    to.Add(row);
            }

            SortByDate(from);
            SortByDate(to);
            PaintGrid(_availableGrid, _available, _search.Text);
            PaintGrid(_appliedGrid, _applied, "");
            UpdateTotals();
        }

        /// <summary>Fill a statement grid from the in-memory list, optionally filtered by search text.</summary>
        private static void PaintGrid(
            DataGridView grid,
            List<Dictionary<string, string>> rows,
            string filter)
        {
            string needle = (filter ?? "").Trim();
            grid.Rows.Clear();
            foreach (var row in rows)
            {
                string date = DataFiles.GetRecord(row, "Date");
                string amount = DataFiles.GetRecord(row, "Amount");
                string account = DataFiles.GetRecord(row, "Account");
                string memo = DataFiles.GetRecordAny(row, "Description", "Notes", "Reference");
                // Search is set: skip lines that do not contain the typed text.
                if (needle.Length > 0)
                {
                    string hay = date + " " + amount + " " + account + " " + memo;
                    if (hay.IndexOf(needle, StringComparison.OrdinalIgnoreCase) < 0)
                        continue;
                }

                int i = grid.Rows.Add(date, amount, account, memo);
                grid.Rows[i].Tag = row;
            }
        }

        /// <summary>Paid is the sum of matched statement amounts; remaining is invoice amount minus that.</summary>
        private void UpdateTotals()
        {
            decimal paid = 0;
            foreach (var row in _applied)
                paid += BankFeed.PaymentAmount(row);
            decimal remaining = Math.Max(0m, _amount - paid);
            _paidValue.Text = MoneyFormat.Display(paid);
            _remainingValue.Text = MoneyFormat.Display(remaining);
            _remainingValue.ForeColor = remaining <= 0 ? Theme.Success : Theme.Navy;
            bool dirty = !SameSet(_original, _applied);
            _save.Enabled = dirty;
            _hint.Text = remaining <= 0 && _applied.Count > 0
                ? "This invoice is fully paid."
                : "Select statement lines and press Match. Remaining updates as you match.";
        }

        /// <summary>True when the matched set is the same identities as when the dialog opened.</summary>
        private static bool SameSet(HashSet<string> original, List<Dictionary<string, string>> applied)
        {
            if (original.Count != applied.Count)
                return false;
            foreach (var row in applied)
            {
                if (!original.Contains(BankFeed.RowIdentity(row)))
                    return false;
            }

            return true;
        }

        /// <summary>Write matched Invoice # tags and the invoice Paid / Outstanding cells.</summary>
        private void Save()
        {
            MutateResult result;
            try
            {
                result = DataFiles.ApplyInvoicePayment(_invoice, _applied);
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
                return;
            }

            if (!result.Ok)
            {
                ToastAlert.Error(this, result.Message);
                return;
            }

            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Keep the spinner in the middle of the matcher while statements load.</summary>
        private void CenterSpinner(Control? host)
        {
            if (host == null)
                return;
            _spinner.Location = new Point(
                Math.Max(0, (host.ClientSize.Width - _spinner.Width) / 2),
                Math.Max(0, (host.ClientSize.Height - _spinner.Height) / 2));
        }
    }
}
