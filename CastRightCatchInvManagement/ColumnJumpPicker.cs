using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    internal sealed class ColumnJumpPicker : Control
    {
        private readonly List<string> _columns = new();
        private int _selectedIndex = -1;
        private bool _hover;
        private bool _open;
        private ToolStripDropDown? _drop;

        public event EventHandler<int>? ColumnChosen;

        public ColumnJumpPicker()
        {
            SetStyle(
                ControlStyles.AllPaintingInWmPaint |
                ControlStyles.OptimizedDoubleBuffer |
                ControlStyles.ResizeRedraw |
                ControlStyles.UserPaint,
                true);
            Cursor = Cursors.Hand;
            Font = Theme.Body;
            TabStop = true;
            Height = 34;
        }

        public IReadOnlyList<string> Columns => _columns;

        public int SelectedIndex => _selectedIndex;

        public void SetColumns(IEnumerable<string> headers)
        {
            _columns.Clear();
            _columns.AddRange(headers);
            _selectedIndex = -1;
            Enabled = _columns.Count > 0;
            Invalidate();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
                CloseDrop();
            base.Dispose(disposing);
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            _hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            _hover = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnGotFocus(EventArgs e)
        {
            Invalidate();
            base.OnGotFocus(e);
        }

        protected override void OnLostFocus(EventArgs e)
        {
            Invalidate();
            base.OnLostFocus(e);
        }

        protected override void OnClick(EventArgs e)
        {
            Focus();
            ToggleDrop();
            base.OnClick(e);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode is Keys.Down or Keys.Enter or Keys.Space)
            {
                ToggleDrop();
                e.Handled = true;
                return;
            }

            if (e.KeyCode == Keys.Escape && _open)
            {
                CloseDrop();
                e.Handled = true;
            }

            base.OnKeyDown(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            g.Clear(Parent?.BackColor ?? Theme.Paper);

            var bounds = new Rectangle(0, 0, Math.Max(1, Width - 1), Math.Max(1, Height - 1));
            using var path = Theme.RoundRect(bounds, 6);
            using var fill = new SolidBrush(Theme.Paper);
            g.FillPath(fill, path);

            bool active = _open || _hover || Focused;
            using var border = new Pen(active ? Theme.Gold : Theme.CreamDark, active ? 1.6f : 1f);
            g.DrawPath(border, path);

            using var accent = new SolidBrush(active ? Theme.Gold : Theme.GoldLight);
            g.FillRectangle(accent, 1, 8, 3, Height - 16);

            var arrowBounds = new Rectangle(Width - 30, 0, 22, Height);
            Theme.PaintHeaderArrow(g, arrowBounds, active, _open);

            const string caption = "JUMP TO";
            int captionWidth = TextRenderer.MeasureText(
                caption,
                Theme.Caption,
                new Size(int.MaxValue, int.MaxValue),
                TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix).Width + 8;

            TextRenderer.DrawText(
                g,
                caption,
                Theme.Caption,
                new Rectangle(14, 0, captionWidth, Height),
                Enabled ? Theme.Muted : Theme.CreamDark,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.NoPrefix);

            bool hasSelection = _selectedIndex >= 0 && _selectedIndex < _columns.Count;
            string value = !Enabled
                ? "No columns"
                : hasSelection
                    ? _columns[_selectedIndex]
                    : "Select a column";

            int valueX = 14 + captionWidth + 4;
            var valueBounds = new Rectangle(
                valueX,
                0,
                Math.Max(4, arrowBounds.X - valueX - 2),
                Height);

            TextRenderer.DrawText(
                g,
                value,
                Theme.BodyBold,
                valueBounds,
                !Enabled || !hasSelection ? Theme.Muted : Theme.Navy,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private void ToggleDrop()
        {
            if (!Enabled || _columns.Count == 0)
                return;

            if (_open)
                CloseDrop();
            else
                ShowDrop();
        }

        private void ShowDrop()
        {
            CloseDrop();

            const int itemHeight = 32;
            int listWidth = Width;
            foreach (var header in _columns)
            {
                int w = TextRenderer.MeasureText(header, Theme.Body).Width + 28;
                if (w > listWidth)
                    listWidth = w;
            }

            listWidth = Math.Clamp(listWidth, Width, 520);
            int visible = Math.Min(_columns.Count, 9);
            int listHeight = visible * itemHeight;

            var list = new ListBox
            {
                BorderStyle = BorderStyle.None,
                DrawMode = DrawMode.OwnerDrawFixed,
                ItemHeight = itemHeight,
                Font = Theme.Body,
                BackColor = Theme.Paper,
                ForeColor = Theme.Ink,
                IntegralHeight = false,
                Dock = DockStyle.Fill,
                Cursor = Cursors.Hand
            };
            foreach (var header in _columns)
                list.Items.Add(header);
            if (_selectedIndex >= 0 && _selectedIndex < list.Items.Count)
                list.SelectedIndex = _selectedIndex;

            list.DrawItem += (_, e) => DrawItem(list, e);
            list.MouseMove += (_, e) =>
            {
                int index = list.IndexFromPoint(e.Location);
                if (index >= 0 && list.SelectedIndex != index)
                    list.SelectedIndex = index;
            };
            list.MouseDown += (_, e) =>
            {
                int index = list.IndexFromPoint(e.Location);
                if (index < 0)
                    return;
                Pick(index);
            };
            list.KeyDown += (_, e) =>
            {
                if (e.KeyCode == Keys.Enter && list.SelectedIndex >= 0)
                {
                    Pick(list.SelectedIndex);
                    e.Handled = true;
                }
                else if (e.KeyCode == Keys.Escape)
                {
                    CloseDrop();
                    e.Handled = true;
                }
            };

            var hostPanel = new Panel
            {
                Size = new Size(listWidth, listHeight + 2),
                BackColor = Theme.Gold,
                Padding = new Padding(1)
            };
            var inner = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Paper,
                Padding = new Padding(3, 0, 0, 0)
            };
            inner.Controls.Add(list);
            hostPanel.Controls.Add(inner);

            var host = new ToolStripControlHost(hostPanel)
            {
                AutoSize = false,
                Size = hostPanel.Size,
                Margin = Padding.Empty,
                Padding = Padding.Empty
            };

            _drop = new ToolStripDropDown
            {
                Padding = Padding.Empty,
                Margin = Padding.Empty,
                DropShadowEnabled = true,
                AutoClose = true,
                Renderer = new JumpDropRenderer()
            };
            _drop.Items.Add(host);
            _drop.Closed += (_, _) =>
            {
                _open = false;
                _drop = null;
                Invalidate();
            };

            _open = true;
            Invalidate();
            _drop.Show(this, new Point(0, Height + 3));
            list.Focus();
        }

        private void Pick(int index)
        {
            if (index < 0 || index >= _columns.Count)
                return;

            _selectedIndex = index;
            Invalidate();

            int chosen = index;
            BeginInvoke(() =>
            {
                CloseDrop();
                ColumnChosen?.Invoke(this, chosen);
            });
        }

        private void CloseDrop()
        {
            var drop = _drop;
            _drop = null;
            _open = false;
            if (drop == null)
            {
                Invalidate();
                return;
            }

            try
            {
                if (!drop.IsDisposed)
                    drop.Close();
            }
            catch (ObjectDisposedException)
            {
            }

            if (!drop.IsDisposed)
                drop.Dispose();

            Invalidate();
        }

        private static void DrawItem(ListBox list, DrawItemEventArgs e)
        {
            if (e.Index < 0)
                return;

            bool selected = (e.State & DrawItemState.Selected) != 0;
            var g = e.Graphics;
            using var bg = new SolidBrush(selected ? Theme.GridSelection : Theme.Paper);
            g.FillRectangle(bg, e.Bounds);

            if (selected)
            {
                using var gold = new SolidBrush(Theme.Gold);
                g.FillRectangle(gold, e.Bounds.X, e.Bounds.Y + 7, 3, e.Bounds.Height - 14);
            }

            TextRenderer.DrawText(
                g,
                list.Items[e.Index]?.ToString() ?? "",
                Theme.Body,
                new Rectangle(e.Bounds.X + 12, e.Bounds.Y, e.Bounds.Width - 18, e.Bounds.Height),
                Theme.Navy,
                TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
        }

        private sealed class JumpDropRenderer : ToolStripProfessionalRenderer
        {
            public JumpDropRenderer() : base(new ProfessionalColorTable()) { }

            protected override void OnRenderToolStripBorder(ToolStripRenderEventArgs e) { }

            protected override void OnRenderToolStripBackground(ToolStripRenderEventArgs e)
            {
                e.Graphics.Clear(Theme.Paper);
            }
        }
    }
}
