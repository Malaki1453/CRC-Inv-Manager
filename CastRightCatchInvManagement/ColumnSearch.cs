using System.Collections;
using System.ComponentModel;
using System.Drawing.Drawing2D;
using System.Globalization;
using System.Runtime.InteropServices;

namespace CastRightCatchInvManagement
{
    internal sealed class ColumnSearch
    {
        private const int HeaderHeight = 40;
        private const int OpenHeaderHeight = 70;
        private const int ArrowWidth = 22;
        private const int BoxHeight = 22;

        private readonly DataGridView _grid;
        private readonly ColumnJumpPicker? _jump;
        private readonly Dictionary<int, TextBox> _boxes = new();
        public string? FileBaseName { get; set; }
        private int _openColumn = -1;
        private int _sortColumn = -1;
        private ListSortDirection _sortDirection = ListSortDirection.Ascending;
        private bool _wired;

        public ColumnSearch(DataGridView grid, ColumnJumpPicker? jump = null)
        {
            _grid = grid;
            _jump = jump;
            Wire();
        }

        public void Rebuild()
        {
            foreach (var box in _boxes.Values)
            {
                box.Parent?.Controls.Remove(box);
                box.Dispose();
            }

            _boxes.Clear();
            _openColumn = -1;
            _sortColumn = -1;
            _sortDirection = ListSortDirection.Ascending;
            _grid.ColumnHeadersHeight = HeaderHeight;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                if (Theme.IsAddColumn(col))
                    continue;
                col.SortMode = DataGridViewColumnSortMode.Programmatic;
                var box = CreateBox(col.Index);
                _boxes[col.Index] = box;
                _grid.Controls.Add(box);
            }

            LayoutBoxes();
            Apply();
            Theme.FitAllColumns(_grid);
            RefreshJump();
            _grid.Refresh();
        }

        public void Apply()
        {
            if (_grid.IsDisposed || _grid.Rows.Count == 0)
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
                if (row.IsNewRow)
                    continue;

                bool match = true;
                foreach (var pair in queries)
                {
                    if (pair.Value.Length == 0 || pair.Key >= row.Cells.Count)
                        continue;

                    string text = row.Cells[pair.Key].Value?.ToString() ?? "";
                    if (text.IndexOf(pair.Value, StringComparison.OrdinalIgnoreCase) < 0)
                    {
                        match = false;
                        break;
                    }
                }

                row.Visible = match;
            }

            _grid.Invalidate();
        }

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
                    if (string.IsNullOrWhiteSpace(box.Text) && _openColumn == columnIndex)
                        _openColumn = -1;
                    SyncHeaderHeight();
                }));
            };
            return box;
        }

        public void NotifyColumnsChanged()
        {
            LayoutBoxes();
            RefreshJump();
            _grid.Refresh();
        }

        private void OnHeaderClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.ColumnIndex < 0)
                return;

            if (Theme.IsAddColumn(_grid.Columns[e.ColumnIndex]))
            {
                UiStyle.ShowAddColumnMenu(_grid);
                return;
            }

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

        private void OpenSearch(int columnIndex)
        {
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
        }

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

            bool filtered = _boxes.TryGetValue(e.ColumnIndex, out var box) &&
                            box != null &&
                            !string.IsNullOrWhiteSpace(box.Text);
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

        private bool AnySearchVisible()
        {
            if (_openColumn >= 0)
                return true;
            return _boxes.Values.Any(b => !string.IsNullOrWhiteSpace(b.Text));
        }

        private void SyncHeaderHeight()
        {
            int needed = AnySearchVisible() ? OpenHeaderHeight : HeaderHeight;
            if (_grid.ColumnHeadersHeight != needed)
                _grid.ColumnHeadersHeight = needed;

            LayoutBoxes();
            _grid.Invalidate();
        }

        private void LayoutBoxes()
        {
            if (_boxes.Count == 0)
                return;

            bool expanded = AnySearchVisible();
            int y = HeaderHeight + (OpenHeaderHeight - HeaderHeight - BoxHeight) / 2;

            foreach (DataGridViewColumn col in _grid.Columns)
            {
                if (!_boxes.TryGetValue(col.Index, out var box))
                    continue;

                bool keep = expanded &&
                            (col.Index == _openColumn || !string.IsNullOrWhiteSpace(box.Text));
                if (!keep || !col.Visible)
                {
                    box.Visible = false;
                    continue;
                }

                int x = ColumnLeft(col) + 4;
                int width = Math.Max(16, col.Width - 8);
                box.Bounds = new Rectangle(x, y, width, BoxHeight);
                box.Visible = x + width > 0 && x < _grid.ClientSize.Width;
                box.BringToFront();
            }
        }

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
