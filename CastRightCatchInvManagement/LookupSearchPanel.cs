namespace CastRightCatchInvManagement
{
    /// <summary>A lookup row the user can pick to fill a form.</summary>
    internal sealed class LookupPick
    {
        /// <summary>Source kind shown in the Type column (Vendor, Item, Customer, …).</summary>
        public required string Kind { get; init; }
        /// <summary>Code written into the form's code field.</summary>
        public required string Code { get; init; }
        /// <summary>Display name written into the form's name field.</summary>
        public required string Name { get; init; }
        /// <summary>Optional extra column shown as Detail (phone, species, …).</summary>
        public required string Extra { get; init; }
        /// <summary>Full source row so the parent form can fill remaining fields.</summary>
        public required Dictionary<string, string> Record { get; init; }
    }

    /// <summary>One table the search bar can query (vendors, items, customers, lots, …).</summary>
    internal sealed class LookupSource
    {
        /// <summary>Label for this table in the Type column.</summary>
        public required string Kind { get; init; }
        /// <summary>Rows to score against the typed query.</summary>
        public required IReadOnlyList<Dictionary<string, string>> Rows { get; init; }
        /// <summary>Column used as Code on a hit.</summary>
        public required string CodeColumn { get; init; }
        /// <summary>Name columns in preference order (Company, Name, Description, …).</summary>
        public required string[] NameColumns { get; init; }
        /// <summary>Optional extra column shown as Detail.</summary>
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

        /// <summary>Raised when the user accepts a hit (double-click or Enter).</summary>
        public event Action<LookupPick>? Picked;

        /// <summary>Build the collapsed search bar; the results grid stays hidden until a query is typed.</summary>
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
                // Double-clicking a hit fills the parent form from that record.
                if (e.RowIndex >= 0)
                    Accept(e.RowIndex);
            };
            _grid.KeyDown += (_, e) =>
            {
                // Enter on a highlighted row is the keyboard equivalent of a double-click; other keys are left to the grid.
                if (e.KeyCode != Keys.Enter)
                    return;
                e.Handled = true;
                e.SuppressKeyPress = true;
                // No highlighted row (empty grid): Enter should not fill the form.
                if (_grid.CurrentRow != null)
                    Accept(_grid.CurrentRow.Index);
            };

            Controls.Add(_grid);
            Controls.Add(_hint);
            Controls.Add(searchRow);
            Controls.Add(heading);
        }

        /// <summary>Draw the gold accent bar and panel border.</summary>
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

        /// <summary>Replace the tables this bar can search, and rerun if text is already typed.</summary>
        public void SetSources(params LookupSource[] sources)
        {
            _sources = sources ?? Array.Empty<LookupSource>();
            // Keep results in sync when the parent reloads vendors/items while a query is open.
            if (_search.Text.Trim().Length > 0)
                RunSearch();
        }

        /// <summary>Clear the box and collapse the results grid.</summary>
        public void ClearSearch()
        {
            _search.Clear();
            Collapse("Type to search. Matching rows appear here — nothing is listed until then.");
        }

        /// <summary>Enter runs search (and auto-picks a single hit); Down moves into the grid; Escape clears.</summary>
        private void OnSearchKeyDown(object? sender, KeyEventArgs e)
        {
            // Enter searches immediately and fills the form when only one row matches.
            if (e.KeyCode == Keys.Enter)
            {
                e.SuppressKeyPress = true;
                _debounce.Stop();
                RunSearch();
                // Exactly one close match: fill the form immediately instead of making the user click it.
                if (_hits.Count == 1)
                    Accept(0);
            }
            // Down hands focus to the first result so arrow keys can walk the list.
            else if (e.KeyCode == Keys.Down && _grid.Visible && _hits.Count > 0)
            {
                e.SuppressKeyPress = true;
                _grid.Focus();
                // Select the first result row when the grid has rows after RunSearch.
                if (_grid.Rows.Count > 0)
                    _grid.CurrentCell = _grid.Rows[0].Cells[0];
            }
            // Escape restores the empty search state without filling the form.
            else if (e.KeyCode == Keys.Escape)
            {
                e.SuppressKeyPress = true;
                ClearSearch();
            }
        }

        /// <summary>Score every source row, show the top hits, or collapse when the query is blank.</summary>
        private void RunSearch()
        {
            string query = _search.Text.Trim();
            _hits.Clear();
            _grid.Rows.Clear();

            // Nothing listed until the user types; avoid dumping whole vendor/item tables.
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
                    // Skip rows that are not a close match on any field.
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

            // Keep the grid open with a hint so the user knows to try another word.
            if (_hits.Count == 0)
            {
                Expand("No close matches. Try another code, name, or word.");
                return;
            }

            string noun = _hits.Count == 1 ? "match" : "matches";
            Expand($"{_hits.Count} close {noun}. Double-click a row (or press Enter) to fill the form.");
        }

        /// <summary>Show the results grid and grow the panel to fit it.</summary>
        private void Expand(string hint)
        {
            _hint.Text = hint;
            _grid.Visible = true;
            Height = CollapsedHeight + GridHeight;
        }

        /// <summary>Hide the results grid so the bar stays compact until the next query.</summary>
        private void Collapse(string hint)
        {
            _hint.Text = hint;
            _grid.Visible = false;
            Height = CollapsedHeight;
        }

        /// <summary>Raise <see cref="Picked"/> for the chosen result row.</summary>
        private void Accept(int rowIndex)
        {
            // Ignore clicks on the empty area or a stale index after a new search.
            if (rowIndex < 0 || rowIndex >= _hits.Count)
                return;
            Picked?.Invoke(_hits[rowIndex]);
        }

        /// <summary>Map a source row to the grid columns: first non-empty name, plus optional extra.</summary>
        private static LookupPick ToPick(LookupSource source, Dictionary<string, string> record)
        {
            string name = "";
            foreach (var column in source.NameColumns)
            {
                name = DataFiles.GetRecord(record, column).Trim();
                // Prefer Company/Name/Description in the order the source listed.
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
                // Empty cells do not contribute to ranking or the combined haystack.
                if (text.Length == 0)
                    continue;
                hay += " " + text;
                best = Math.Max(best, TextMatch.Score(text, query));
            }

            // Multi-word queries also match when each word appears in some field.
            if (tokens.Length > 1 && tokens.All(token => TextMatch.Contains(hay, token)))
                best = Math.Max(best, 55);

            return best;
        }
    }
}
