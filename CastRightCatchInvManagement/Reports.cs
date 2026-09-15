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
        private TabControl _tabs = null!;
        private TextBox _filter = null!;
        private Button _export = null!;
        private ReportKind _kind;
        private bool _showingDetail;
        private ReportResult? _current;
        private readonly HashSet<string> _expanded = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Build the report home cards and subscribe to data changes.</summary>
        public Reports()
        {
            InitializeComponent();
            BuildUi();
            DataFiles.DataChanged += OnDataChanged;
        }

        /// <summary>Rebuild the open report when inventory data changes.</summary>
        private void OnDataChanged()
        {
            // Ignore changes while the home cards are showing or the page is closing.
            if (IsDisposed || !_showingDetail)
                return;
            // DataChanged can arrive off the UI thread.
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
            // Only the open report needs a rebuild when Current/Old flips.
            if (_showingDetail)
                ShowReport(_kind);
        }

        /// <summary>Home of six report cards plus the hidden detail view.</summary>
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
                (ReportKind.Aging, "Aging Report", "Customers and vendors — tabs and a filter"),
                (ReportKind.Commission, "Commission Tracker", "Deals by PO / SO — no commission rate is stored yet"),
                (ReportKind.ProfitLoss, "Monthly P&L", "Revenue vs lot cost by ship month"),
                (ReportKind.Suppliers, "Supplier Performance", "Vendor volume, cost, and average cost / lb"),
                (ReportKind.CustomerRisk, "Customer Risk Report", "Credit, terms, and late balances"),
                (ReportKind.Species, "Profit Per Species", "By species — click to expand item codes")
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

        /// <summary>Toolbar, stats chips, tabs, and grid for an open report.</summary>
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

            _filter = new TextBox
            {
                PlaceholderText = "Filter this table",
                Size = new Size(220, 28),
                Anchor = AnchorStyles.Top | AnchorStyles.Right
            };
            Theme.StyleField(_filter);
            _filter.TextChanged += (_, _) => ApplyFilter();
            toolbar.Resize += (_, _) =>
            {
                _export.Location = new Point(Math.Max(360, toolbar.Width - _export.Width), 4);
                _filter.Location = new Point(Math.Max(140, _export.Left - _filter.Width - 12), 6);
            };
            toolbar.Controls.Add(_export);
            toolbar.Controls.Add(_filter);
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
            _grid.CellClick += OnSpeciesClick;

            _tabs = new TabControl
            {
                Dock = DockStyle.Top,
                Height = 32,
                Font = Theme.BodyBold,
                Visible = false
            };

            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            card.Controls.Add(_grid);
            card.Controls.Add(_tabs);

            host.Controls.Add(card);
            host.Controls.Add(_stats);
            host.Controls.Add(_hint);
            host.Controls.Add(_title);
            host.Controls.Add(toolbar);
        }

        /// <summary>Clickable card that opens one report.</summary>
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

        /// <summary>Hide the detail grid and show the six cards again.</summary>
        private void ShowHome()
        {
            _showingDetail = false;
            _detail.Visible = false;
            _home.Visible = true;
        }

        /// <summary>Build and show one report table from the current database view.</summary>
        private void ShowReport(ReportKind kind)
        {
            // Switching reports clears expanded species groups.
            if (_kind != kind)
                _expanded.Clear();
            _kind = kind;
            _showingDetail = true;
            var report = ReportData.Build(kind);
            _current = report;
            _title.Text = report.Title;
            _hint.Text = report.Hint + "  ·  " + ReportData.ScopeHint();
            _filter.Clear();
            BuildTabs(report);
            ShowActiveTab();
            _home.Visible = false;
            _detail.Visible = true;
        }

        /// <summary>Aging (and similar) tabs, or hide the tab strip for single tables.</summary>
        private void BuildTabs(ReportResult report)
        {
            _tabs.SelectedIndexChanged -= TabChanged;
            _tabs.TabPages.Clear();
            // Aging has Customers and Vendors; other reports are one table.
            if (report.Tabs is { Count: > 0 })
            {
                foreach (var tab in report.Tabs)
                    _tabs.TabPages.Add(tab.Name);
                _tabs.Visible = true;
                _tabs.SelectedIndex = 0;
            }
            // Opposite branch of the condition above.
            else
            {
                _tabs.Visible = false;
            }

            _tabs.SelectedIndexChanged += TabChanged;
        }

        /// <summary>Reload chips and grid when the Aging tab changes.</summary>
        private void TabChanged(object? sender, EventArgs e) => ShowActiveTab();

        /// <summary>Fill stats and grid for the selected tab, or the whole report.</summary>
        private void ShowActiveTab()
        {
            // Export is only for an open report.
            if (_current == null)
                return;
            // Fill from the selected Aging tab rather than the unused main table.
            if (_current.Tabs is { Count: > 0 } tabs &&
                _tabs.SelectedIndex >= 0 &&
                _tabs.SelectedIndex < tabs.Count)
            {
                var tab = tabs[_tabs.SelectedIndex];
                FillStats(tab.Stats);
                FillTable(tab.Columns, tab.Rows, tab.Empty, _current.Groups);
            }
            // Opposite branch of the condition above.
            else
            {
                FillStats(_current.Stats);
                FillGrid(_current);
            }

            ApplyFilter();
        }

        /// <summary>Up to four summary chips above the table.</summary>
        private void FillStats(List<(string Label, string Value)> stats)
        {
            _stats.Controls.Clear();
            for (int i = 0; i < 4; i++)
            {
                string label = i < stats.Count ? stats[i].Label : "";
                string value = i < stats.Count ? stats[i].Value : "";
                _stats.Controls.Add(StatChip(label, value), i, 0);
            }
        }

        /// <summary>One caption-plus-value chip.</summary>
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

        /// <summary>Fill the grid from the report’s main columns and rows.</summary>
        private void FillGrid(ReportResult report) =>
            FillTable(report.Columns, report.Rows, report.Empty, report.Groups);

        /// <summary>Render grouped (species) or flat rows, or an empty-state line.</summary>
        private void FillTable(
            string[] columns,
            List<string[]> rows,
            string empty,
            List<ReportGroup>? groups)
        {
            _grid.Columns.Clear();
            _grid.Rows.Clear();
            foreach (var column in columns)
                _grid.Columns.Add(column, column);

            // Profit per species uses expandable parent rows.
            if (groups is { Count: > 0 })
            {
                foreach (var group in groups)
                {
                    bool open = _expanded.Contains(group.Key);
                    var parent = (string[])group.Parent.Clone();
                    // Show a triangle on the species name cell.
                    if (parent.Length > 0)
                        parent[0] = (open ? "▼  " : "▶  ") + group.Key;
                    int index = _grid.Rows.Add(PadRow(parent, columns.Length));
                    _grid.Rows[index].Tag = group.Key;
                    _grid.Rows[index].DefaultCellStyle.Font = Theme.BodyBold;
                    // Collapsed groups hide item-code children.
                    if (!open)
                        continue;
                    foreach (var child in group.Children)
                    {
                        int childIndex = _grid.Rows.Add(PadRow(child, columns.Length));
                        _grid.Rows[childIndex].Tag = null;
                        _grid.Rows[childIndex].DefaultCellStyle.ForeColor = Theme.Muted;
                    }
                }

                Theme.FitAllColumns(_grid);
                return;
            }

            // Keep a placeholder so the grid is not a blank hole.
            if (rows.Count == 0)
            {
                // Need at least one column to add the empty-state row.
                if (_grid.Columns.Count > 0)
                    _grid.Rows.Add(Pad(empty, columns.Length));
                return;
            }

            foreach (var row in rows)
                _grid.Rows.Add(PadRow(row, columns.Length));

            Theme.FitAllColumns(_grid);
        }

        /// <summary>Hide rows that do not contain the filter text.</summary>
        private void ApplyFilter()
        {
            string query = _filter.Text.Trim();
            foreach (DataGridViewRow row in _grid.Rows)
            {
                // The unbound extra row is not report data.
                if (row.IsNewRow)
                    continue;
                // Clearing the filter shows every row again.
                if (query.Length == 0)
                {
                    row.Visible = true;
                    continue;
                }

                bool match = false;
                foreach (DataGridViewCell cell in row.Cells)
                {
                    string text = cell.Value?.ToString() ?? "";
                    // Any cell containing the text keeps the row visible.
                    if (text.Contains(query, StringComparison.OrdinalIgnoreCase))
                    {
                        match = true;
                        break;
                    }
                }

                row.Visible = match;
            }
        }

        /// <summary>Expand or collapse a species group on click.</summary>
        private void OnSpeciesClick(object? sender, DataGridViewCellEventArgs e)
        {
            // Header clicks and flat reports have nothing to expand.
            if (_current?.Groups == null || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            // Child item-code rows are not toggles.
            if (_grid.Rows[e.RowIndex].Tag is not string key || key.Length == 0)
                return;
            // Second click collapses the species.
            if (!_expanded.Add(key))
                _expanded.Remove(key);
            FillGrid(_current);
            ApplyFilter();
        }

        /// <summary>Empty-state row with the message in the first cell.</summary>
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
            // Export is only for an open report.
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
            // Cancel leaves the report on screen.
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            // Write can fail if Excel has the CSV open.
            try
            {
                CsvIO.WriteExcel(dialog.FileName, ExportRows(_current));
                ToastAlert.Success(this, "Report exported.");
            }
            // Keep the report on screen and show why export failed.
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
            // CSV header includes the on-screen hint when present.
            if (!string.IsNullOrWhiteSpace(report.Hint))
                lines.Add(new[] { "Notes", report.Hint });
            foreach (var stat in report.Stats)
                lines.Add(new[] { stat.Label, stat.Value });
            lines.Add(Array.Empty<string>());
            // Aging has Customers and Vendors; other reports are one table.
            if (report.Tabs is { Count: > 0 })
            {
                foreach (var tab in report.Tabs)
                {
                    lines.Add(Array.Empty<string>());
                    lines.Add(new[] { tab.Name });
                    lines.Add(tab.Columns);
                    // Still export the empty-state line so the sheet is not blank.
                    if (tab.Rows.Count == 0)
                        lines.Add(new[] { tab.Empty });
                    // Opposite branch of the condition above.
                    else
                        lines.AddRange(tab.Rows);
                }

                return lines;
            }

            lines.Add(report.Columns);
            // Species CSV includes children only for expanded groups.
            if (report.Groups is { Count: > 0 })
            {
                foreach (var group in report.Groups)
                {
                    lines.Add(group.Parent);
                    lines.AddRange(group.Children);
                }
            }
            // Alternative when the previous branch did not apply.
            else if (report.Rows.Count == 0)
            {
                lines.Add(new[] { report.Empty });
            }
            // Opposite branch of the condition above.
            else
            {
                lines.AddRange(report.Rows);
            }
            return lines;
        }

        /// <summary>Replace characters Windows will not allow in a file name.</summary>
        private static string SanitizeFileName(string title)
        {
            var chars = title.Select(c => char.IsLetterOrDigit(c) ? c : '_').ToArray();
            string name = new string(chars).Trim('_');
            while (name.Contains("__"))
                name = name.Replace("__", "_");
            return name.Length > 0 ? name : "Report";
        }

        /// <summary>Pad or trim a data row to the column count.</summary>
        private static object[] PadRow(string[] row, int columns)
        {
            var cells = new object[columns];
            for (int i = 0; i < columns; i++)
                cells[i] = i < row.Length ? row[i] : "";
            return cells;
        }
    }
}
