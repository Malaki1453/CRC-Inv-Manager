using System.Collections;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CastRightCatchInvManagement
{
    /// <summary>Per-column filter boxes under grid headers, plus sort arrows and hide/show.</summary>
    internal sealed class ColumnSearch
    {
        // HeaderHeight is the title row; OpenHeaderHeight adds room for the filter boxes.
        private const int HeaderHeight = 40;
        private const int OpenHeaderHeight = 70;
        private const int ArrowWidth = 22;
        private const int BoxHeight = 22;

        private readonly DataGridView _grid;
        private readonly ColumnJumpPicker? _jump;
        private readonly Dictionary<int, TextBox> _boxes = new();
        private readonly Dictionary<int, DateRangeHost> _dates = new();
        private Control? _empty;
        public string? FileBaseName { get; set; }
        public bool HasDateColumns { get; private set; }
        public event Action? ColumnsReady;
        private string _globalQuery = "";
        private DateTime? _fromDate;
        private DateTime? _toDate;
        private int _openColumn = -1;
        private int _sortColumn = -1;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        private bool _wired;

        /// <summary>Attach filters to the grid; the table stays hidden until a search is typed.</summary>
        public ColumnSearch(DataGridView grid, ColumnJumpPicker? jump = null)
        {
            _grid = grid;
            _jump = jump;
            Wire();
            _grid.Visible = false;
        }

        /// <summary>Bind the idle placeholder that shows when no search or date filter is active.</summary>
        public void SetEmptyState(Control empty)
        {
            _empty = empty;
            SyncTableVisible();
        }

        /// <summary>Recreate per-column text and date filters after the grid columns change.</summary>
        public void Rebuild()
        {
            foreach (var box in _boxes.Values)
            {
                box.Parent?.Controls.Remove(box);
                box.Dispose();
            }

            foreach (var range in _dates.Values)
            {
                range.Parent?.Controls.Remove(range);
                range.Dispose();
            }

            _boxes.Clear();
            _dates.Clear();
            _openColumn = -1;
            _sortColumn = -1;
            _sortDirection = ListSortDirection.Ascending;
            _grid.ColumnHeadersHeight = HeaderHeight;
            HasDateColumns = false;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                // The trailing "+" column is not searchable or sortable.
                if (Theme.IsAddColumn(col))
                    continue;
                col.SortMode = DataGridViewColumnSortMode.Programmatic;
                if (IsDateColumn(col.HeaderText))
                {
                    HasDateColumns = true;
                    var range = new DateRangeHost();
                    range.Changed += Apply;
                    _dates[col.Index] = range;
                    _grid.Controls.Add(range);
                }
                else
                {
                    var box = CreateBox(col.Index);
                    _boxes[col.Index] = box;
                    _grid.Controls.Add(box);
                }
            }

            LayoutBoxes();
            Apply();
            Theme.FitAllColumns(_grid);
            RefreshJump();
            _grid.Refresh();
            ColumnsReady?.Invoke();
        }

        /// <summary>Apply the page-wide search box as an extra row filter.</summary>
        public void SetGlobalQuery(string query)
        {
            _globalQuery = (query ?? "").Trim();
            Apply();
        }

        /// <summary>Apply the page-wide from/to dates across every date column on the row.</summary>
        public void SetDateRange(DateTime? from, DateTime? to)
        {
            _fromDate = from;
            _toDate = to;
            Apply();
        }

        /// <summary>True for Established, * At, and any header containing Date.</summary>
        public static bool IsDateColumn(string? header)
        {
            header = (header ?? "").Trim();
            if (header.Length == 0)
                return false;
            if (header.Equals("Established", StringComparison.OrdinalIgnoreCase))
                return true;
            if (header.EndsWith(" At", StringComparison.OrdinalIgnoreCase))
                return true;
            return header.Contains("Date", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Show or hide each data row from column filters, date ranges, and the global query.</summary>
        public void Apply()
        {
            if (_grid.IsDisposed)
                return;

            SyncTableVisible();
            // Nothing to filter while the idle placeholder is showing.
            if (!_grid.Visible || _grid.Rows.Count == 0)
                return;

            var queries = _boxes.ToDictionary(p => p.Key, p => p.Value.Text.Trim());

            try
            {
                _grid.CurrentCell = null;
            }
            catch
            {
                // no current cell yet
            }

            foreach (DataGridViewRow row in _grid.Rows)
            {
                // The unused new-row template is not inventory data.
                if (row.IsNewRow)
                    continue;

                bool match = true;
                foreach (var pair in queries)
                {
                    if (pair.Value.Length == 0 || pair.Key >= row.Cells.Count)
                        continue;

                    string text = row.Cells[pair.Key].Value?.ToString() ?? "";
                    // Column box is a case-insensitive substring filter.
                    if (text.IndexOf(pair.Value, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        match = false;
                        break;
                    }
                }

                if (match)
                {
                    foreach (var pair in _dates)
                    {
                        if (pair.Key >= row.Cells.Count)
                            continue;
                        if (!pair.Value.HasRange)
                            continue;
                        string text = row.Cells[pair.Key].Value?.ToString() ?? "";
                        // Unparseable or out-of-range dates fail this column's from/to.
                        if (!NumericDateBox.TryParseCell(text, out var date) ||
                            !NumericDateBox.InRange(date, pair.Value.From, pair.Value.To))
                        {
                            match = false;
                            break;
                        }
                    }
                }

                // Page-wide dates keep the row if any date column falls in range.
                if (match && (_fromDate != null || _toDate != null) && HasDateColumns)
                {
                    bool any = false;
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        if (cell.OwningColumn == null || Theme.IsAddColumn(cell.OwningColumn))
                            continue;
                        if (!IsDateColumn(cell.OwningColumn.HeaderText))
                            continue;
                        string text = cell.Value?.ToString() ?? "";
                        if (NumericDateBox.TryParseCell(text, out var date) &&
                            NumericDateBox.InRange(date, _fromDate, _toDate))
                        {
                            any = true;
                            break;
                        }
                    }

                    match = any;
                }

                if (match && _globalQuery.Length > 0)
                {
                    var fields = new List<string>();
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        if (cell.OwningColumn != null && Theme.IsAddColumn(cell.OwningColumn))
                            continue;
                        fields.Add(cell.Value?.ToString() ?? "");
                    }

                    match = TextMatch.MatchesAny(fields, _globalQuery);
                }

                row.Visible = match;
            }

            _grid.Invalidate();
        }

        /// <summary>Show the grid only after a search or date filter is set; otherwise show the idle placeholder.</summary>
        private void SyncTableVisible()
        {
            bool show = _globalQuery.Length > 0 ||
                        _fromDate != null ||
                        _toDate != null ||
                        _boxes.Values.Any(box => !string.IsNullOrWhiteSpace(box.Text)) ||
                        _dates.Values.Any(range => range.HasRange);
            if (_grid.Visible == show)
            {
                if (_empty != null)
                    _empty.Visible = !show;
                return;
            }

            _grid.Visible = show;
            if (_empty != null)
                _empty.Visible = !show;
            if (!show)
                _grid.Parent?.Invalidate(true);
        }

        /// <summary>Subscribe once to paint, click, scroll, and jump events.</summary>
        private void Wire()
        {
            if (_wired)
                return;
            _wired = true;

            _grid.CellPainting += OnCellPainting;
            _grid.ColumnHeaderMouseClick += OnHeaderClick;
            _grid.ColumnWidthChanged += (_, _) => LayoutBoxes();
            _grid.Scroll += (_, _) => LayoutBoxes();
            _grid.SizeChanged += (_, _) =>
            {
                Theme.StretchVisibleColumns(_grid);
                LayoutBoxes();
            };
            if (_jump != null)
                _jump.ColumnChosen += OnJumpChosen;
        }

        /// <summary>Build a header filter box; Escape clears it and returns focus to the grid.</summary>
        private TextBox CreateBox(int columnIndex)
        {
            var box = new TextBox
            {
                BorderStyle = BorderStyle.FixedSingle,
                Font = Theme.Small,
                Visible = false,
                TabStop = true
            };
            Theme.StyleField(box);
            box.TextChanged += (_, _) =>
            {
                Apply();
                SyncHeaderHeight();
            };
            box.KeyDown += (_, e) =>
            {
                if (e.KeyCode != Keys.Escape)
                    return;
                box.Clear();
                e.Handled = true;
                if (_openColumn == columnIndex)
                    _openColumn = -1;
                _grid.Focus();
                SyncHeaderHeight();
            };
            box.LostFocus += (_, _) =>
            {
                _grid.BeginInvoke(new Action(() =>
                {
                    if (box.IsDisposed)
                        return;
                    // Collapse an empty filter when the user leaves the box.
                    if (string.IsNullOrWhiteSpace(box.Text) && _openColumn == columnIndex)
                        _openColumn = -1;
                    SyncHeaderHeight();
                }));
            };
            return box;
        }

        /// <summary>Reposition filter boxes and refresh the jump list after show/hide or reorder.</summary>
        public void NotifyColumnsChanged()
        {
            LayoutBoxes();
            RefreshJump();
            _grid.Refresh();
        }

        /// <summary>Left-click: add-column menu, sort arrow, or open the header filter box.</summary>
        private void OnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
                return;

            if (Theme.IsAddColumn(_grid.Columns[e.ColumnIndex]))
            {
                UiStyle.ShowAddColumnMenu(_grid);
                return;
            }

            // Clicks in the filter-box band should not toggle sort or reopen search.
            if (e.Y > HeaderHeight)
                return;

            int width = _grid.Columns[e.ColumnIndex].Width;
            if (e.X >= width - ArrowWidth)
            {
                ToggleSort(e.ColumnIndex);
                return;
            }

            OpenSearch(e.ColumnIndex);
        }

        /// <summary>Fill the jump picker with visible data-column headers in display order.</summary>
        private void RefreshJump()
        {
            if (_jump == null || _jump.IsDisposed)
                return;

            var headers = _grid.Columns
                .Cast<DataGridViewColumn>()
                .OrderBy(c => c.DisplayIndex)
                .Where(c => c.Visible && !Theme.IsAddColumn(c))
                .Select(c => c.HeaderText)
                .ToList();
            _jump.SetColumns(headers);
        }

        /// <summary>Scroll the grid to the visible column at the jump picker's index.</summary>
        private void OnJumpChosen(object? sender, int index)
        {
            if (index < 0)
                return;

            int n = 0;
            foreach (var col in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
            {
                if (!col.Visible)
                    continue;
                if (n == index)
                {
                    ScrollToColumn(col);
                    return;
                }

                n++;
            }
        }

        /// <summary>Scroll horizontally to <paramref name="target"/> and keep the current row selected when possible.</summary>
        private void ScrollToColumn(DataGridViewColumn target)
        {
            if (target == null || !target.Visible || _grid.Columns.Count == 0)
                return;

            int rowIndex = -1;
            try
            {
                if (_grid.CurrentCell != null)
                    rowIndex = _grid.CurrentCell.RowIndex;
                _grid.CurrentCell = null;
            }
            catch
            {
                // no current cell
            }

            int offset = 0;
            foreach (var col in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
            {
                if (!col.Visible)
                    continue;
                if (ReferenceEquals(col, target))
                    break;
                offset += col.Width;
            }

            try
            {
                _grid.HorizontalScrollingOffset = Math.Max(0, offset);
            }
            catch
            {
                // offset may exceed scroll range
            }

            try
            {
                _grid.FirstDisplayedScrollingColumnIndex = target.Index;
            }
            catch
            {
                // column may already be as far left as it can go
            }

            // Restore selection on the same data row after the scroll, if it is still visible.
            if (rowIndex >= 0 &&
                rowIndex < _grid.Rows.Count &&
                _grid.Rows[rowIndex].Visible)
            {
                try
                {
                    var cell = _grid.Rows[rowIndex].Cells[target.Index];
                    if (cell.Visible)
                    {
                        _grid.CurrentCell = cell;
                        _grid.Rows[rowIndex].Selected = true;
                    }
                }
                catch
                {
                    // cell may not be selectable
                }
            }

            LayoutBoxes();
            _grid.Refresh();
        }

        /// <summary>Expand the header and focus this column's text or date filter.</summary>
        private void OpenSearch(int columnIndex)
        {
            // Close a previously opened empty box so only one idle filter stays expanded.
            if (_openColumn >= 0 &&
                _openColumn != columnIndex &&
                _boxes.TryGetValue(_openColumn, out var previous) &&
                string.IsNullOrWhiteSpace(previous.Text))
            {
                _openColumn = -1;
            }

            _openColumn = columnIndex;
            SyncHeaderHeight();
            if (_boxes.TryGetValue(columnIndex, out var box))
            {
                box.Visible = true;
                box.BringToFront();
                box.Focus();
                box.SelectAll();
            }
            else if (_dates.TryGetValue(columnIndex, out var range))
            {
                range.Visible = true;
                range.BringToFront();
                range.FocusFrom();
            }
        }

        /// <summary>Toggle asc/desc on the same column; a new column starts ascending.</summary>
        private void ToggleSort(int columnIndex)
        {
            if (_sortColumn == columnIndex)
            {
                _sortDirection = _sortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }
            else
            {
                _sortColumn = columnIndex;
                _sortDirection = ListSortDirection.Ascending;
            }

            try
            {
                _grid.Sort(new FileSystemRowComparer(_grid, columnIndex, _sortDirection));
            }
            catch
            {
                // ignore if the grid cannot sort yet
            }

            Apply();
            _grid.Invalidate();
        }

        /// <summary>Owner-draw header title, sort/filter arrow, and the add-column plus.</summary>
        private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex != -1 || e.ColumnIndex < 0)
                return;

            e.Handled = true;
            var g = e.Graphics;
            if (g == null)
                return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Theme.PaintHeaderBackground(g, e.CellBounds);

            if (Theme.IsAddColumn(_grid.Columns[e.ColumnIndex]))
            {
                TextRenderer.DrawText(
                    g,
                    "+",
                    Theme.BodyBold,
                    e.CellBounds,
                    Theme.GoldLight,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);
                return;
            }

            var titleBounds = new Rectangle(
                e.CellBounds.X,
                e.CellBounds.Y,
                e.CellBounds.Width,
                Math.Min(HeaderHeight, e.CellBounds.Height));

            var arrowBounds = new Rectangle(
                titleBounds.Right - ArrowWidth,
                titleBounds.Y,
                ArrowWidth,
                titleBounds.Height);

            bool filtered =
                (_boxes.TryGetValue(e.ColumnIndex, out var box) &&
                 box != null &&
                 !string.IsNullOrWhiteSpace(box.Text)) ||
                (_dates.TryGetValue(e.ColumnIndex, out var range) && range.HasRange);
            bool sorted = _sortColumn == e.ColumnIndex;
            bool ascending = _sortDirection == ListSortDirection.Ascending;

            string title = e.FormattedValue?.ToString() ?? _grid.Columns[e.ColumnIndex].HeaderText;
            var textBounds = new Rectangle(
                titleBounds.X + 8,
                titleBounds.Y,
                Math.Max(4, titleBounds.Width - ArrowWidth - 10),
                titleBounds.Height);

            TextRenderer.DrawText(
                g,
                title,
                Theme.BodyBold,
                textBounds,
                Theme.HeaderText,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);

            Theme.PaintHeaderArrow(g, arrowBounds, sorted || filtered, sorted && ascending);
        }

        /// <summary>True when a header filter is open or any column still has filter text/dates.</summary>
        private bool AnySearchVisible()
        {
            if (_openColumn >= 0)
                return true;
            return _boxes.Values.Any(b => !string.IsNullOrWhiteSpace(b.Text)) ||
                   _dates.Values.Any(range => range.HasRange);
        }

        /// <summary>Grow the header to fit filter boxes while any search is active.</summary>
        private void SyncHeaderHeight()
        {
            int needed = AnySearchVisible() ? OpenHeaderHeight : HeaderHeight;
            if (_grid.ColumnHeadersHeight != needed)
                _grid.ColumnHeadersHeight = needed;

            LayoutBoxes();
            _grid.Invalidate();
        }

        /// <summary>Place visible filter boxes under their columns, hiding those scrolled off-screen.</summary>
        private void LayoutBoxes()
        {
            if (_boxes.Count == 0)
                return;

            bool expanded = AnySearchVisible();
            int y = HeaderHeight + (OpenHeaderHeight - HeaderHeight - BoxHeight) / 2;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                int x = ColumnLeft(col) + 4;
                int width = Math.Max(16, col.Width - 8);
                bool onScreen = x + width > 0 && x < _grid.ClientSize.Width;

                if (_boxes.TryGetValue(col.Index, out var box))
                {
                    bool keep = expanded &&
                                (col.Index == _openColumn || !string.IsNullOrWhiteSpace(box.Text));
                    // Hidden columns and unused empty boxes should not sit over the grid.
                    if (!keep || !col.Visible)
                    {
                        box.Visible = false;
                        continue;
                    }

                    box.Bounds = new Rectangle(x, y, width, BoxHeight);
                    box.Visible = onScreen;
                    box.BringToFront();
                    continue;
                }

                if (!_dates.TryGetValue(col.Index, out var range))
                    continue;
                bool keepDate = expanded && (col.Index == _openColumn || range.HasRange);
                if (!keepDate || !col.Visible)
                {
                    range.Visible = false;
                    continue;
                }

                range.Bounds = new Rectangle(x, y, width, BoxHeight);
                range.Visible = onScreen;
                range.BringToFront();
            }
        }

        /// <summary>From/to date pair hosted under a date-column header.</summary>
        private sealed class DateRangeHost : Panel
        {
            private readonly NumericDateBox _from = new();
            private readonly NumericDateBox _to = new();

            public event Action? Changed;

            public DateRangeHost()
            {
                Height = BoxHeight;
                BackColor = Theme.Paper;
                _from.PlaceholderText = "FROM";
                _to.PlaceholderText = "TO";
                _from.BorderStyle = BorderStyle.FixedSingle;
                _to.BorderStyle = BorderStyle.FixedSingle;
                _from.DateChanged += (_, _) => Changed?.Invoke();
                _to.DateChanged += (_, _) => Changed?.Invoke();
                Controls.Add(_from);
                Controls.Add(_to);
                Resize += (_, _) => LayoutBoxes();
            }

            public DateTime? From => _from.Value;
            public DateTime? To => _to.Value;
            public bool HasRange => From != null || To != null;

            public void FocusFrom() => _from.Focus();

            private void LayoutBoxes()
            {
                int gap = 4;
                int w = Math.Max(20, (Width - gap) / 2);
                _from.Bounds = new Rectangle(0, 0, w, Height);
                _to.Bounds = new Rectangle(w + gap, 0, Width - w - gap, Height);
            }
        }

        /// <summary>Client X of a column, accounting for row headers and horizontal scroll.</summary>
        private int ColumnLeft(DataGridViewColumn target)
        {
            int x = _grid.RowHeadersVisible ? _grid.RowHeadersWidth : 0;
            x -= _grid.HorizontalScrollingOffset;
            foreach (var col in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
            {
                if (!col.Visible)
                    continue;
                if (col == target)
                    return x;
                x += col.Width;
            }

            return x;
        }

        /// <summary>Sort dates, then money, then Windows Explorer-style text (A2 before A10).</summary>
        private sealed class FileSystemRowComparer : IComparer
        {
            private readonly DataGridView _grid;
            private readonly int _column;
            private readonly int _direction;

            public FileSystemRowComparer(DataGridView grid, int column, ListSortDirection direction)
            {
                _grid = grid;
                _column = column;
                _direction = direction == ListSortDirection.Ascending ? 1 : -1;
            }

            public int Compare(object? x, object? y)
            {
                var row1 = x as DataGridViewRow ?? (x is int i1 ? _grid.Rows[i1] : null);
                var row2 = y as DataGridViewRow ?? (y is int i2 ? _grid.Rows[i2] : null);
                if (row1 == null || row2 == null)
                    return 0;

                string a = row1.Cells[_column].Value?.ToString() ?? "";
                string b = row2.Cells[_column].Value?.ToString() ?? "";
                return CompareValues(a, b) * _direction;
            }

            /// <summary>Prefer calendar order, then numeric money/qty, then natural string order.</summary>
            private static int CompareValues(string a, string b)
            {
                if (DateTime.TryParse(a, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d1) &&
                    DateTime.TryParse(b, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d2))
                    return d1.CompareTo(d2);

                if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.CurrentCulture, out var n1) &&
                    decimal.TryParse(b, NumberStyles.Any, CultureInfo.CurrentCulture, out var n2))
                    return n1.CompareTo(n2);

                if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.InvariantCulture, out n1) &&
                    decimal.TryParse(b, NumberStyles.Any, CultureInfo.InvariantCulture, out n2))
                    return n1.CompareTo(n2);

                return StrCmpLogicalW(a, b);
            }

            [DllImport("shlwapi.dll", CharSet = CharSet.Unicode)]
            private static extern int StrCmpLogicalW(string psz1, string psz2);
        }
    }
}
