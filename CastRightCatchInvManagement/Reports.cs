namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Term reports. Home is six cards; each card opens a table built from the current database view.
    /// </summary>
    public partial class Reports : Form, INavigationPage
    {
        private Panel _home = null!;
        private Panel _detail = null!;
        private Label _title = null!;
        private Label _hint = null!;
        private TableLayoutPanel _stats = null!;
        private DataGridView _grid = null!;
        private Button _export = null!;
        private ReportKind _kind;
        private bool _showingDetail;
        private ReportResult? _current;

        public Reports()
        {
            InitializeComponent();
            BuildUi();
            DataFiles.DataChanged += OnDataChanged;
        }

        private void OnDataChanged()
        {
            if (IsDisposed || !_showingDetail)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(OnDataChanged);
                return;
            }

            ShowReport(_kind);
        }

        /// <summary>Rebuild the open report when this page is shown or Current/Old changes.</summary>
        public void HighlightCurrentPage()
        {
            if (_showingDetail)
                ShowReport(_kind);
        }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);

            _home = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            var intro = new Label
            {
                Text = "Reports use the current database view. Switch to Old to include archived process rows.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 28
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(0, 12, 0, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            var cards = new (ReportKind Kind, string Title, string Hint)[]
            {
                (ReportKind.Aging, "Aging Report", "Outstanding invoices by age"),
                (ReportKind.Commission, "Commission Tracker", "Deals by PO / SO — no commission rate is stored yet"),
                (ReportKind.ProfitLoss, "Monthly P&L", "Revenue vs lot cost by ship month"),
                (ReportKind.Suppliers, "Supplier Performance", "Vendor volume, cost, and average cost / lb"),
                (ReportKind.CustomerRisk, "Customer Risk Report", "Credit, terms, and late balances"),
                (ReportKind.Species, "Profit Per Species", "Margin rolled up by species")
            };

            for (int i = 0; i < cards.Length; i++)
            {
                var card = BuildReportCard(cards[i].Kind, cards[i].Title, cards[i].Hint);
                grid.Controls.Add(card, i % 3, i / 3);
            }

            _home.Controls.Add(grid);
            _home.Controls.Add(intro);

            _detail = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream, Visible = false };
            BuildDetail(_detail);

            Controls.Add(_detail);
            Controls.Add(_home);
        }

        private void BuildDetail(Panel host)
        {
            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Theme.Cream
            };
            var back = new Button
            {
                Text = "All reports",
                Size = new Size(120, 32),
                Location = new Point(0, 4)
            };
            Theme.StyleOutlineButton(back);
            back.Click += (_, _) => ShowHome();

            _export = new Button
            {
                Text = "Export CSV",
                Size = new Size(120, 32),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleGoldButton(_export);
            _export.Click += (_, _) => ExportCsv();
            toolbar.Resize += (_, _) =>
                _export.Location = new Point(Math.Max(140, toolbar.Width - _export.Width), 4);
            toolbar.Controls.Add(_export);
            toolbar.Controls.Add(back);

            _title = new Label
            {
                Dock = DockStyle.Top,
                Height = 32,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.MiddleLeft
            };
            _hint = new Label
            {
                Dock = DockStyle.Top,
                Height = 24,
                Font = Theme.Body,
                ForeColor = Theme.Muted
            };

            _stats = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 92,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0, 8, 0, 8)
            };
            for (int i = 0; i < 4; i++)
                _stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false
            };
            Theme.StyleGrid(_grid);

            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            card.Controls.Add(_grid);

            host.Controls.Add(card);
            host.Controls.Add(_stats);
            host.Controls.Add(_hint);
            host.Controls.Add(_title);
            host.Controls.Add(toolbar);
        }

        private Control BuildReportCard(ReportKind kind, string title, string hint)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 12, 12),
                Cursor = Cursors.Hand
            };

            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(24, 22),
                Cursor = Cursors.Hand
            };
            var body = new Label
            {
                Text = hint,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Location = new Point(24, 56),
                Size = new Size(220, 48),
                Cursor = Cursors.Hand
            };
            var open = new Label
            {
                Text = "Open report →",
                Font = Theme.Caption,
                ForeColor = Theme.Gold,
                AutoSize = true,
                Location = new Point(24, 112),
                Cursor = Cursors.Hand
            };
            card.Controls.Add(heading);
            card.Controls.Add(body);
            card.Controls.Add(open);
            card.Resize += (_, _) =>
            {
                body.Width = Math.Max(120, card.Width - 48);
                open.Top = Math.Max(100, card.Height - 36);
            };

            void Open(object? sender, EventArgs e) => ShowReport(kind);
            card.Click += Open;
            heading.Click += Open;
            body.Click += Open;
            open.Click += Open;
            return card;
        }

        private void ShowHome()
        {
            _showingDetail = false;
            _detail.Visible = false;
            _home.Visible = true;
        }

        private void ShowReport(ReportKind kind)
        {
            _kind = kind;
            _showingDetail = true;
            var report = ReportData.Build(kind);
            _current = report;
            _title.Text = report.Title;
            _hint.Text = report.Hint + "  ·  " + ReportData.ScopeHint();
            FillStats(report);
            FillGrid(report);
            _home.Visible = false;
            _detail.Visible = true;
        }

        private void FillStats(ReportResult report)
        {
            _stats.Controls.Clear();
            for (int i = 0; i < 4; i++)
            {
                string label = i < report.Stats.Count ? report.Stats[i].Label : "";
                string value = i < report.Stats.Count ? report.Stats[i].Value : "";
                _stats.Controls.Add(StatChip(label, value), i, 0);
            }
        }

        private static Control StatChip(string label, string value)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 10, 0)
            };
            var caption = new Label
            {
                Text = label.ToUpperInvariant(),
                Font = Theme.Caption,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(16, 10)
            };
            var amount = new Label
            {
                Text = value,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(16, 32)
            };
            card.Controls.Add(caption);
            card.Controls.Add(amount);
            return card;
        }

        private void FillGrid(ReportResult report)
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();
            foreach (var column in report.Columns)
                _grid.Columns.Add(column, column);

            if (report.Rows.Count == 0)
            {
                if (_grid.Columns.Count > 0)
                    _grid.Rows.Add(Pad(report.Empty, report.Columns.Length));
                return;
            }

            foreach (var row in report.Rows)
                _grid.Rows.Add(PadRow(row, report.Columns.Length));

            Theme.FitAllColumns(_grid);
        }

        private static object[] Pad(string text, int columns)
        {
            var cells = new object[columns];
            cells[0] = text;
            for (int i = 1; i < columns; i++)
                cells[i] = "";
            return cells;
        }

        /// <summary>Save the open report table as CSV (opens in Excel). PDF is a better fit for invoices, not these grids.</summary>
        private void ExportCsv()
        {
            if (_current == null)
                return;

            string stamp = DateTime.Today.ToString("yyyy-MM-dd");
            string name = SanitizeFileName(_current.Title) + "_" + stamp + ".csv";
            using var dialog = new SaveFileDialog
            {
                Title = "Export report",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                FileName = name,
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                CsvIO.WriteExcel(dialog.FileName, ExportRows(_current));
                ToastAlert.Success(this, "Report exported.");
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        /// <summary>Title, scope, summary chips, then the same table shown on screen.</summary>
        private static List<string[]> ExportRows(ReportResult report)
        {
            var lines = new List<string[]>
            {
                new[] { "Report", report.Title },
                new[] { "Scope", ReportData.ScopeHint() },
                new[] { "Exported", DateTime.Now.ToString("yyyy-MM-dd HH:mm") }
            };
            if (!string.IsNullOrWhiteSpace(report.Hint))
                lines.Add(new[] { "Notes", report.Hint });
            foreach (var stat in report.Stats)
                lines.Add(new[] { stat.Label, stat.Value });
            lines.Add(Array.Empty<string>());
            lines.Add(report.Columns);
            if (report.Rows.Count == 0)
                lines.Add(new[] { report.Empty });
            else
                lines.AddRange(report.Rows);
            return lines;
        }

        private static string SanitizeFileName(string title)
        {
            var chars = title.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            string name = new string(chars).Trim('_');
            while (name.Contains("__"))
                name = name.Replace("__", "_");
            return name.Length > 0 ? name : "Report";
        }

        private static object[] PadRow(string[] row, int columns)
        {
            var cells = new object[columns];
            for (int i = 0; i < columns; i++)
                cells[i] = i < row.Length ? row[i] : "";
            return cells;
        }
    }
}
