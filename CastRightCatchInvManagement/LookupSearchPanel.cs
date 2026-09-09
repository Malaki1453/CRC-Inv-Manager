namespace CastRightCatchInvManagement
{
    /// <summary>A lookup row the user can pick to fill a form.</summary>
    internal sealed class LookupPick
    {
        public required string Kind { get; init; }
        public required string Code { get; init; }
        public required string Name { get; init; }
        public required string Extra { get; init; }
        public required Dictionary<string, string> Record { get; init; }
    }

    /// <summary>One table the search bar can query (vendors, items, customers, lots, …).</summary>
    internal sealed class LookupSource
    {
        public required string Kind { get; init; }
        public required IReadOnlyList<Dictionary<string, string>> Rows { get; init; }
        public required string CodeColumn { get; init; }
        public required string[] NameColumns { get; init; }
        public string? ExtraColumn { get; init; }
    }

    /// <summary>
    /// Search bar that stays empty until the user types. Then it scores every field on
    /// every source row for a close match and shows those hits in a table.
    /// </summary>
    internal sealed class LookupSearchPanel : Panel
    {
        private const int CollapsedHeight = 108;
        private const int GridHeight = 188;
        private const int MaxHits = 40;

        private readonly TextBox _search;
        private readonly Label _hint;
        private readonly DataGridView _grid;
        private readonly System.Windows.Forms.Timer _debounce;
        private IReadOnlyList<LookupSource> _sources = Array.Empty<LookupSource>();
        private readonly List<LookupPick> _hits = new();

        public event Action<LookupPick>? Picked;

        public LookupSearchPanel(string placeholder)
        {
            BackColor = Theme.Paper;
            Dock = DockStyle.Top;
            Height = CollapsedHeight;
            Padding = new Padding(12, 10, 12, 10);
            Theme.EnableDoubleBuffer(this);

            var heading = new Label
            {
                Text = "Search",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Dock = DockStyle.Top,
                Height = 28,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 0, 0)
            };

            _search = new TextBox
            {
                Dock = DockStyle.Fill,
                PlaceholderText = placeholder
            };
            Theme.StyleField(_search);
            var searchRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 32,
                Padding = new Padding(8, 0, 8, 0)
            };
            searchRow.Controls.Add(_search);

            _debounce = new System.Windows.Forms.Timer { Interval = 120 };
            _debounce.Tick += (_, _) =>
            {
                _debounce.Stop();
                RunSearch();
            };
            _search.TextChanged += (_, _) => _debounce.Start();
            _search.KeyDown += OnSearchKeyDown;

            _hint = new Label
            {
                Text = "Type to search. Matching rows appear here — nothing is listed until then.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 22,
                TextAlign = ContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                Visible = false,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                AllowUserToResizeRows = false,
                RowHeadersVisible = false,
                MultiSelect = false,
                AutoGenerateColumns = false
            };
            Theme.StyleGrid(_grid);
            _grid.AllowUserToOrderColumns = false;
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Type",
                HeaderText = "Type",
                FillWeight = 16
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Code",
                HeaderText = "Code",
                FillWeight = 22
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Name",
                HeaderText = "Name",
                FillWeight = 40
            });
            _grid.Columns.Add(new DataGridViewTextBoxColumn
            {
                Name = "Detail",
                HeaderText = "Detail",
                FillWeight = 22
            });
            _grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex >= 0)
                    Accept(e.RowIndex);
            };
            _grid.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Enter)
                    return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                if (_grid.CurrentRow != null)
                    Accept(_grid.CurrentRow.Index);
            };

            Controls.Add(_grid);
            Controls.Add(_hint);
            Controls.Add(searchRow);
            Controls.Add(heading);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var border = new Pen(Theme.CreamDark);
            e.Graphics.DrawRectangle(border, rect);
            using var gold = new SolidBrush(Theme.Gold);
            e.Graphics.FillRectangle(gold, 0, 0, 4, Height);
        }

        public void SetSources(params LookupSource[] sources)
        {
            _sources = sources ?? Array.Empty<LookupSource>();
            if (_search.Text.Trim().Length > 0)
                RunSearch();
        }

        public void ClearSearch()
        {
            _search.Clear();
            Collapse("Type to search. Matching rows appear here — nothing is listed until then.");
        }

        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _debounce.Stop();
                RunSearch();
                if (_hits.Count == 1)
                    Accept(0);
            }
            else if (e.KeyCode == Keys.Down && _grid.Visible && _hits.Count > 0)
            {
                e.SuppressKeyPress = true;
                _grid.Focus();
                if (_grid.Rows.Count > 0)
                    _grid.CurrentCell = _grid.Rows[0].Cells[0];
            }
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                ClearSearch();
            }
        }

        private void RunSearch()
        {
            string query = _search.Text.Trim();
            _hits.Clear();
            _grid.Rows.Clear();

            if (query.Length == 0)
            {
                Collapse("Type to search. Matching rows appear here — nothing is listed until then.");
                return;
            }

            var ranked = new List<(int Score, LookupPick Pick)>();
            foreach (var source in _sources)
            {
                foreach (var record in source.Rows)
                {
                    int score = ScoreRecord(record, query);
                    if (score <= 0)
                        continue;
                    ranked.Add((score, ToPick(source, record)));
                }
            }

            foreach (var hit in ranked
                .OrderByDescending(row => row.Score)
                .ThenBy(row => row.Pick.Name, StringComparer.OrdinalIgnoreCase)
                .Take(MaxHits))
            {
                _hits.Add(hit.Pick);
                _grid.Rows.Add(hit.Pick.Kind, hit.Pick.Code, hit.Pick.Name, hit.Pick.Extra);
            }

            if (_hits.Count == 0)
            {
                Expand("No close matches. Try another code, name, or word.");
                return;
            }

            string noun = _hits.Count == 1 ? "match" : "matches";
            Expand($"{_hits.Count} close {noun}. Double-click a row (or press Enter) to fill the form.");
        }

        private void Expand(string hint)
        {
            _hint.Text = hint;
            _grid.Visible = true;
            Height = CollapsedHeight + GridHeight;
        }

        private void Collapse(string hint)
        {
            _hint.Text = hint;
            _grid.Visible = false;
            Height = CollapsedHeight;
        }

        private void Accept(int rowIndex)
        {
            if (rowIndex < 0 || rowIndex >= _hits.Count)
                return;
            Picked?.Invoke(_hits[rowIndex]);
        }

        private static LookupPick ToPick(LookupSource source, Dictionary<string, string> record)
        {
            string name = "";
            foreach (var column in source.NameColumns)
            {
                name = DataFiles.GetRecord(record, column).Trim();
                if (name.Length > 0)
                    break;
            }

            return new LookupPick
            {
                Kind = source.Kind,
                Code = DataFiles.GetRecord(record, source.CodeColumn).Trim(),
                Name = name,
                Extra = source.ExtraColumn == null
                    ? ""
                    : DataFiles.GetRecord(record, source.ExtraColumn).Trim(),
                Record = record
            };
        }

        /// <summary>
        /// Score a row by every stored field: exact, starts-with, contains, token, then close spelling.
        /// </summary>
        private static int ScoreRecord(Dictionary<string, string> record, string query)
        {
            int best = 0;
            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            string hay = "";

            foreach (var value in record.Values)
            {
                string text = (value ?? "").Trim();
                if (text.Length == 0)
                    continue;
                hay += " " + text;
                best = Math.Max(best, TextMatch.Score(text, query));
            }

            if (tokens.Length > 1 && tokens.All(token => TextMatch.Contains(hay, token)))
                best = Math.Max(best, 55);

            return best;
        }
    }
}
