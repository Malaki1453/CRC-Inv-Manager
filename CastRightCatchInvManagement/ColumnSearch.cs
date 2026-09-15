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
        /// <summary>Table file base name used as the GridLayout prefs key.</summary>
        public string? FileBaseName { get; set; }
        /// <summary>True when at least one visible column is a date (Established, * At, *Date*).</summary>
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
                // Date headers get a FROM/TO pair instead of a substring box.
                if (IsDateColumn(col.HeaderText))
                {
                    HasDateColumns = true;
                    var range = new DateRangeHost();
                    range.Changed += Apply;
                    _dates[col.Index] = range;
                    _grid.Controls.Add(range);
                }
                // Text/number columns get a case-insensitive substring filter box under the header.
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
            // Blank headers are not date columns and would match Contains("Date") incorrectly if left untrimmed only.
            if (header.Length == 0)
                return false;
            // Established is a party-table date even though the word Date is not in the header.
            if (header.Equals("Established", StringComparison.OrdinalIgnoreCase))
                return true;
            // Created At / Updated At timestamps are dates for filtering.
            if (header.EndsWith(" At", StringComparison.OrdinalIgnoreCase))
                return true;
            return header.Contains("Date", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Show or hide each data row from column filters, date ranges, and the global query.</summary>
        public void Apply()
        {
            // The page may have closed while a filter TextChanged was still queued.
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
            // Clearing CurrentCell throws when the grid has no current cell yet (empty or not focused).
            catch
            {
            }

            foreach (DataGridViewRow row in _grid.Rows)
            {
                // The unused new-row template is not inventory data.
                if (row.IsNewRow)
                    continue;

                bool match = true;
                foreach (var pair in queries)
                {
                    // Empty box text does not filter; pair.Key can be stale after columns were removed.
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

                // Still matching text filters: next apply each date column's FROM/TO if that column has a range.
                if (match)
                {
                    foreach (var pair in _dates)
                    {
                        // Column index can be past this row if the grid was rebuilt mid-filter.
                        if (pair.Key >= row.Cells.Count)
                            continue;
                        // No FROM or TO typed on this header: this date column does not constrain the row.
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
                        // Skip cells with no column and the trailing "+" control column.
                        if (cell.OwningColumn == null || Theme.IsAddColumn(cell.OwningColumn))
                            continue;
                        // Page-wide dates only look at date columns (Established, * At, *Date*).
                        if (!IsDateColumn(cell.OwningColumn.HeaderText))
                            continue;
                        string text = cell.Value?.ToString() ?? "";
                        // One date cell in [_fromDate, _toDate] is enough to keep the row.
                        if (NumericDateBox.TryParseCell(text, out var date) &&
                            NumericDateBox.InRange(date, _fromDate, _toDate))
                        {
                            any = true;
                            break;
                        }
                    }

                    match = any;
                }

                // Page-wide search box: require a match on any remaining data cell.
                if (match && _globalQuery.Length > 0)
                {
                    var fields = new List<string>();
                    foreach (DataGridViewCell cell in row.Cells)
                    {
                        // Do not search the dummy "+" column.
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
            // Visibility already matches `show`: only keep the idle placeholder in sync.
            if (_grid.Visible == show)
            {
                // _empty is the "type to search" placeholder; hide it when the grid is showing.
                if (_empty != null)
                    _empty.Visible = !show;
                return;
            }

            _grid.Visible = show;
            // Keep the placeholder opposite the grid so they never both show.
            if (_empty != null)
                _empty.Visible = !show;
            // Hiding the grid can leave stale pixels; invalidate the parent to repaint the placeholder.
            if (!show)
                _grid.Parent?.Invalidate(true);
        }

        /// <summary>Subscribe once to paint, click, scroll, and jump events.</summary>
        private void Wire()
        {
            // Wire is called from the constructor and Rebuild; subscribe to paint/click/scroll only once.
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
            // Optional jump picker: subscribe so choosing a header scrolls this grid.
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
                // Escape is the only key that clears the filter; other keys type into the box.
                if (e.KeyCode != Keys.Escape)
                    return;
                box.Clear();
                e.Handled = true;
                // This box is the expanded filter: collapse the header after clearing.
                if (_openColumn == columnIndex)
                    _openColumn = -1;
                _grid.Focus();
                SyncHeaderHeight();
            };
            box.LostFocus += (_, _) =>
            {
                _grid.BeginInvoke(new Action(() =>
                {
                    // Rebuild can dispose the box before this deferred LostFocus runs.
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
            // Only a left-click on a real header cell sorts or opens a filter; ignore right-click and the gutter.
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
                return;

            // The trailing "+" column opens the add-column menu instead of sorting.
            if (Theme.IsAddColumn(_grid.Columns[e.ColumnIndex]))
            {
                UiStyle.ShowAddColumnMenu(_grid);
                return;
            }

            // Clicks in the filter-box band should not toggle sort or reopen search.
            if (e.Y > HeaderHeight)
                return;

            int width = _grid.Columns[e.ColumnIndex].Width;
            // Clicks in the right-edge chevron toggle sort instead of opening the filter box.
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
            // Jump picker is optional and can be disposed with the toolbar.
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
            // Jump picker can fire with -1 when the list was cleared.
            if (index < 0)
                return;

            int n = 0;
            foreach (var col in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
            {
                // Hidden columns are not in the jump list, so they must not consume `index`.
                if (!col.Visible)
                    continue;
                // n is the visible-column index matching the picker; scroll that column into view.
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
            // Nothing to scroll when the target is missing, hidden, or the grid has no columns.
            if (target == null || !target.Visible || _grid.Columns.Count == 0)
                return;

            int rowIndex = -1;
            try
            {
                // Remember the selected data row so we can restore it after changing CurrentCell.
                if (_grid.CurrentCell != null)
                    rowIndex = _grid.CurrentCell.RowIndex;
                _grid.CurrentCell = null;
            }
            // CurrentCell get/set throws when the grid has no current cell (empty selection).
            catch
            {
            }

            int offset = 0;
            foreach (var col in _grid.Columns.Cast<DataGridViewColumn>().OrderBy(c => c.DisplayIndex))
            {
                // Hidden columns do not occupy horizontal scroll space.
                if (!col.Visible)
                    continue;
                // Stop summing widths once we reach the jumped-to column.
                if (ReferenceEquals(col, target))
                    break;
                offset += col.Width;
            }

            try
            {
                _grid.HorizontalScrollingOffset = Math.Max(0, offset);
            }
            // HorizontalScrollingOffset throws when offset is past the current scroll range.
            catch
            {
            }

            try
            {
                _grid.FirstDisplayedScrollingColumnIndex = target.Index;
            }
            // FirstDisplayedScrollingColumnIndex throws when the column is already as far left as it can go.
            catch
            {
            }

            // Restore selection on the same data row after the scroll, if it is still visible.
            if (rowIndex >= 0 &&
                rowIndex < _grid.Rows.Count &&
                _grid.Rows[rowIndex].Visible)
            {
                try
                {
                    var cell = _grid.Rows[rowIndex].Cells[target.Index];
                    // Frozen or hidden cells cannot become CurrentCell.
                    if (cell.Visible)
                    {
                        _grid.CurrentCell = cell;
                        _grid.Rows[rowIndex].Selected = true;
                    }
                }
                // Setting CurrentCell throws when the cell is not selectable (hidden row, new-row, …).
                catch
                {
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
            // Text columns have a substring box; focus it so the user can type immediately.
            if (_boxes.TryGetValue(columnIndex, out var box))
            {
                box.Visible = true;
                box.BringToFront();
                box.Focus();
                box.SelectAll();
            }
            // Date columns have a FROM/TO host instead of a text box.
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
            // Same column again: flip asc/desc instead of starting a new sort.
            if (_sortColumn == columnIndex)
            {
                _sortDirection = _sortDirection == ListSortDirection.Ascending
                    ? ListSortDirection.Descending
                    : ListSortDirection.Ascending;
            }
            // A different column starts a new sort, always ascending first.
            else
            {
                _sortColumn = columnIndex;
                _sortDirection = ListSortDirection.Ascending;
            }

            try
            {
                _grid.Sort(new FileSystemRowComparer(_grid, columnIndex, _sortDirection));
            }
            // Sort throws when the grid is still binding or has no comparable rows yet.
            catch
            {
            }

            Apply();
            _grid.Invalidate();
        }

        /// <summary>Owner-draw header title, sort/filter arrow, and the add-column plus.</summary>
        private void OnCellPainting(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            // Owner-draw only header cells (RowIndex -1); data cells use Theme.PaintGridDataCell.
            if (e.RowIndex != -1 || e.ColumnIndex < 0)
                return;

            e.Handled = true;
            var g = e.Graphics;
            // Graphics can be null on some accessibility paint paths.
            if (g == null)
                return;
            g.SmoothingMode = SmoothingMode.AntiAlias;

            Theme.PaintHeaderBackground(g, e.CellBounds);

            // "+" column draws a gold plus instead of a sortable title.
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
            // A header filter is currently expanded even if its box is still empty.
            if (_openColumn >= 0)
                return true;
            return _boxes.Values.Any(b => !string.IsNullOrWhiteSpace(b.Text)) ||
                   _dates.Values.Any(range => range.HasRange);
        }

        /// <summary>Grow the header to fit filter boxes while any search is active.</summary>
        private void SyncHeaderHeight()
        {
            int needed = AnySearchVisible() ? OpenHeaderHeight : HeaderHeight;
            // Avoid a layout churn when the header is already the right height.
            if (_grid.ColumnHeadersHeight != needed)
                _grid.ColumnHeadersHeight = needed;

            LayoutBoxes();
            _grid.Invalidate();
        }

        /// <summary>Place visible filter boxes under their columns, hiding those scrolled off-screen.</summary>
        private void LayoutBoxes()
        {
            // Rebuild has not created filter boxes yet, so there is nothing to position.
            if (_boxes.Count == 0)
                return;

            bool expanded = AnySearchVisible();
            int y = HeaderHeight + (OpenHeaderHeight - HeaderHeight - BoxHeight) / 2;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                int x = ColumnLeft(col) + 4;
                int width = Math.Max(16, col.Width - 8);
                bool onScreen = x + width > 0 && x < _grid.ClientSize.Width;

                // Text-filter columns: position the substring box under this header.
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

                // Not a date column (and not a text box): skip leftover columns such as "+".
                if (!_dates.TryGetValue(col.Index, out var range))
                    continue;
                bool keepDate = expanded && (col.Index == _openColumn || range.HasRange);
                // Hide FROM/TO when the header is collapsed, empty, or the column is hidden.
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
                // Hidden columns do not occupy X space in the header band.
                if (!col.Visible)
                    continue;
                // Reached the requested column: x is its left edge in grid client coordinates.
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
                // DataGridView.Sort can pass non-row objects; treat them as equal so sort does not throw.
                if (row1 == null || row2 == null)
                    return 0;

                string a = row1.Cells[_column].Value?.ToString() ?? "";
                string b = row2.Cells[_column].Value?.ToString() ?? "";
                return CompareValues(a, b) * _direction;
            }

            /// <summary>Prefer calendar order, then numeric money/qty, then natural string order.</summary>
            private static int CompareValues(string a, string b)
            {
                // Both cells parse as dates: sort in calendar order, not as text (01/02 vs 12/31).
                if (DateTime.TryParse(a, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d1) &&
                    DateTime.TryParse(b, CultureInfo.CurrentCulture, DateTimeStyles.None, out var d2))
                    return d1.CompareTo(d2);

                // Both cells parse as money/qty in the current culture: numeric order ($2 before $10).
                if (decimal.TryParse(a, NumberStyles.Any, CultureInfo.CurrentCulture, out var n1) &&
                    decimal.TryParse(b, NumberStyles.Any, CultureInfo.CurrentCulture, out var n2))
                    return n1.CompareTo(n2);

                // CSV-style invariant numbers when culture parse failed (1.5 vs 1,5 locales).
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
