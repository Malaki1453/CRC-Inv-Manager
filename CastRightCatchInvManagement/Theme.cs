using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    internal static class Theme
    {
        public static readonly Color NavyDark = Color.FromArgb(8, 22, 38);
        public static readonly Color Navy = Color.FromArgb(15, 42, 68);
        public static readonly Color NavyMid = Color.FromArgb(24, 58, 92);
        public static readonly Color NavyHover = Color.FromArgb(28, 68, 108);
        public static readonly Color Gold = Color.FromArgb(201, 154, 39);
        public static readonly Color GoldLight = Color.FromArgb(232, 201, 106);
        public static readonly Color Cream = Color.FromArgb(246, 242, 234);
        public static readonly Color CreamDark = Color.FromArgb(226, 217, 198);
        public static readonly Color Paper = Color.White;
        public static readonly Color Ink = Color.FromArgb(22, 32, 45);
        public static readonly Color Muted = Color.FromArgb(96, 110, 126);
        public static readonly Color Danger = Color.FromArgb(153, 48, 48);
        public static readonly Color DangerFill = Color.FromArgb(255, 236, 230);
        public static readonly Color Success = Color.FromArgb(28, 110, 62);
        public static readonly Color SuccessFill = Color.FromArgb(230, 245, 234);
        public static readonly Color GridAlt = Color.FromArgb(250, 247, 241);
        public static readonly Color GridLine = Color.FromArgb(232, 224, 210);
        public static readonly Color HeaderBack = Navy;
        public static readonly Color HeaderText = Cream;
        public static readonly Color HeaderArrowIdle = Color.FromArgb(170, 196, 176, 130);
        public static readonly Color HeaderArrowActive = NavyDark;
        public static readonly Color HeaderArrowActiveFill = GoldLight;
        public static readonly Color GridSelection = Color.FromArgb(232, 214, 160);

        public static readonly Font BrandTitle = CreateFont("Georgia", 13.5f, FontStyle.Bold);
        public static readonly Font BrandItalic = CreateFont("Georgia", 11f, FontStyle.Italic);
        public static readonly Font PageTitle = CreateFont("Georgia", 20f, FontStyle.Bold);
        public static readonly Font SectionTitle = CreateFont("Georgia", 13f, FontStyle.Bold);
        public static readonly Font NavFont = CreateFont("Segoe UI Semibold", 10f, FontStyle.Regular);
        public static readonly Font Body = CreateFont("Segoe UI", 10f, FontStyle.Regular);
        public static readonly Font BodyBold = CreateFont("Segoe UI Semibold", 10f, FontStyle.Regular);
        public static readonly Font Small = CreateFont("Segoe UI", 8.5f, FontStyle.Regular);
        public static readonly Font Caption = CreateFont("Segoe UI", 8.25f, FontStyle.Bold);
        public static readonly Font StatValue = CreateFont("Georgia", 22f, FontStyle.Bold);
        public static readonly Font HeroTitle = CreateFont("Georgia", 28f, FontStyle.Bold);
        public static readonly Font HeroSub = CreateFont("Georgia", 12f, FontStyle.Italic);

        public static Font CreateFont(string family, float size, FontStyle style)
        {
            try
            {
                return new Font(family, size, style, GraphicsUnit.Point);
            }
            catch
            {
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
        }

        public static void EnableDoubleBuffer(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }

        public static void StylePageTitle(Label label)
        {
            label.Font = PageTitle;
            label.ForeColor = Navy;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        public static void StyleGoldButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = GoldLight;
            button.FlatAppearance.MouseDownBackColor = Color.FromArgb(180, 136, 28);
            button.BackColor = Gold;
            button.ForeColor = NavyDark;
            button.Font = BodyBold;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleNavyButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.FlatAppearance.MouseOverBackColor = NavyMid;
            button.FlatAppearance.MouseDownBackColor = NavyDark;
            button.BackColor = Navy;
            button.ForeColor = Color.White;
            button.Font = BodyBold;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleOutlineButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 1;
            button.FlatAppearance.BorderColor = Navy;
            button.BackColor = Paper;
            button.ForeColor = Navy;
            button.Font = BodyBold;
            button.Cursor = Cursors.Hand;
            button.UseVisualStyleBackColor = false;
        }

        public static void StyleField(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = Body;
            box.BackColor = Paper;
            box.ForeColor = Ink;
        }

        public static void StyleCombo(ComboBox box)
        {
            box.FlatStyle = FlatStyle.Flat;
            box.Font = Body;
            box.BackColor = Paper;
            box.ForeColor = Ink;
        }

        public static void StyleFieldLabel(Label label)
        {
            label.Font = Caption;
            label.ForeColor = Muted;
            label.AutoSize = true;
        }

        public static void StyleGrid(DataGridView grid)
        {
            grid.BackgroundColor = Paper;
            grid.AllowUserToOrderColumns = true;
            grid.BorderStyle = BorderStyle.None;
            grid.CellBorderStyle = DataGridViewCellBorderStyle.None;
            grid.GridColor = GridLine;
            grid.EnableHeadersVisualStyles = false;
            grid.ColumnHeadersBorderStyle = DataGridViewHeaderBorderStyle.None;
            grid.ColumnHeadersHeight = 40;
            grid.ColumnHeadersHeightSizeMode = DataGridViewColumnHeadersHeightSizeMode.DisableResizing;
            grid.ColumnHeadersDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = HeaderBack,
                ForeColor = HeaderText,
                Font = BodyBold,
                SelectionBackColor = HeaderBack,
                SelectionForeColor = HeaderText,
                Alignment = DataGridViewContentAlignment.MiddleLeft,
                Padding = new Padding(8, 0, 8, 0)
            };
            grid.DefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = Paper,
                ForeColor = Ink,
                Font = Body,
                SelectionBackColor = GridSelection,
                SelectionForeColor = NavyDark,
                Padding = new Padding(8, 0, 8, 0)
            };
            grid.AlternatingRowsDefaultCellStyle = new DataGridViewCellStyle
            {
                BackColor = GridAlt,
                ForeColor = Ink,
                SelectionBackColor = GridSelection,
                SelectionForeColor = NavyDark
            };
            grid.RowHeadersVisible = false;
            grid.RowTemplate.Height = 34;
            grid.RowTemplate.DividerHeight = 0;
            grid.AllowUserToResizeRows = false;
            grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            grid.MultiSelect = false;
            ApplyTableScrollMode(grid);
            grid.CellPainting -= PaintGridDataCell;
            grid.CellPainting += PaintGridDataCell;
        }

        private static void PaintGridDataCell(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;

            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Border);
            if (e.Graphics != null)
            {
                using var pen = new Pen(GridLine, 1);
                int y = e.CellBounds.Bottom - 1;
                e.Graphics.DrawLine(pen, e.CellBounds.Left, y, e.CellBounds.Right, y);
            }

            e.Handled = true;
        }

        public static void ApplyTableScrollMode(DataGridView grid)
        {
            EnsureManualColumnWidths(grid);
            grid.ScrollBars = ScrollBars.Both;
            grid.AllowUserToResizeColumns = true;
        }

        public static void FitAllColumns(DataGridView grid)
        {
            if (grid.Columns.Count == 0)
                return;

            EnsureManualColumnWidths(grid);
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                if (!grid.Columns[i].Visible || IsAddColumn(grid.Columns[i]))
                    continue;
                FitLongest(grid, i);
            }

            StretchVisibleColumns(grid);
        }

        public const string AddColumnTag = "__add_column__";
        public const int AddColumnWidth = 42;

        public static bool IsAddColumn(DataGridViewColumn? column)
        {
            return column != null &&
                   string.Equals(column.Tag as string, AddColumnTag, StringComparison.Ordinal);
        }

        public static void EnsureAddColumn(DataGridView grid)
        {
            if (grid.Columns.Cast<DataGridViewColumn>().Any(IsAddColumn))
                return;
            if (grid.Columns.Count == 0)
                return;
            if (grid.Columns.Count == 1 &&
                string.Equals(grid.Columns[0].HeaderText, "Status", StringComparison.OrdinalIgnoreCase))
                return;

            var add = new DataGridViewTextBoxColumn
            {
                Name = "AddColumn",
                HeaderText = "+",
                Tag = AddColumnTag,
                Width = AddColumnWidth,
                MinimumWidth = AddColumnWidth,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            add.DefaultCellStyle.BackColor = Paper;
            add.DefaultCellStyle.SelectionBackColor = Paper;
            add.DefaultCellStyle.SelectionForeColor = Ink;
            grid.Columns.Add(add);
        }

        public static void StretchVisibleColumns(DataGridView grid)
        {
            var visible = grid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible && !IsAddColumn(c))
                .ToList();
            if (visible.Count == 0)
                return;

            int used = 0;
            foreach (var col in visible)
                used += col.Width;
            var add = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(IsAddColumn);
            if (add != null && add.Visible)
            {
                add.Width = AddColumnWidth;
                used += add.Width;
            }

            int available = Math.Max(0, grid.ClientSize.Width - 2);
            if (grid.Controls.OfType<VScrollBar>().Any(bar => bar.Visible))
                available -= SystemInformation.VerticalScrollBarWidth;

            int extra = available - used;
            if (extra <= 0)
                return;

            int share = extra / visible.Count;
            int remainder = extra % visible.Count;
            for (int i = 0; i < visible.Count; i++)
            {
                int grow = share + (i == visible.Count - 1 ? remainder : 0);
                visible[i].Width = Math.Max(48, visible[i].Width + grow);
            }
        }

        public static void PaintHeaderBackground(Graphics g, Rectangle bounds)
        {
            using var fill = new SolidBrush(HeaderBack);
            g.FillRectangle(fill, bounds);
        }

        public static void PaintHeaderArrow(Graphics g, Rectangle bounds, bool active, bool up)
        {
            int cx = bounds.X + bounds.Width / 2;
            int cy = bounds.Y + bounds.Height / 2;

            if (active)
            {
                var chip = new Rectangle(cx - 9, cy - 9, 18, 18);
                using var path = RoundRect(chip, 4);
                using var chipFill = new SolidBrush(HeaderArrowActiveFill);
                g.FillPath(chipFill, path);
            }

            Point[] pts = up
                ? new[] { new Point(cx, cy - 5), new Point(cx + 6, cy + 4), new Point(cx - 6, cy + 4) }
                : new[] { new Point(cx - 6, cy - 4), new Point(cx + 6, cy - 4), new Point(cx, cy + 5) };

            using var brush = new SolidBrush(active ? HeaderArrowActive : HeaderArrowIdle);
            g.FillPolygon(brush, pts);
        }

        public static void EnsureManualColumnWidths(DataGridView grid)
        {
            if (grid.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.None)
                return;

            var widths = new int[grid.Columns.Count];
            for (int i = 0; i < grid.Columns.Count; i++)
                widths[i] = grid.Columns[i].Width;

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            for (int i = 0; i < widths.Length; i++)
                grid.Columns[i].Width = widths[i];
        }

        public static int MeasureColumnText(DataGridView grid, string? text, Font? font)
        {
            font ??= grid.DefaultCellStyle.Font ?? Body;
            var flags = TextFormatFlags.NoPadding | TextFormatFlags.NoPrefix;
            var size = TextRenderer.MeasureText(
                string.IsNullOrEmpty(text) ? " " : text,
                font,
                new Size(int.MaxValue, int.MaxValue),
                flags);
            int twoChars = TextRenderer.MeasureText("MM", font, new Size(int.MaxValue, int.MaxValue), flags).Width;
            return size.Width + 24 + twoChars;
        }

        public static void FitLongest(DataGridView grid, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
                return;

            var column = grid.Columns[columnIndex];
            int width = MeasureColumnText(grid, column.HeaderText, BodyBold) + 22;
            var cellFont = grid.DefaultCellStyle.Font ?? Body;
            foreach (DataGridViewRow row in grid.Rows)
            {
                if (row.IsNewRow)
                    continue;
                width = Math.Max(width, MeasureColumnText(grid, row.Cells[columnIndex].Value?.ToString(), cellFont));
            }

            SetColumnWidth(grid, columnIndex, width);
        }

        private static void SetColumnWidth(DataGridView grid, int columnIndex, int width)
        {
            EnsureManualColumnWidths(grid);
            grid.Columns[columnIndex].Width = Math.Clamp(width, 48, 2400);
        }

        public static GraphicsPath RoundRect(Rectangle bounds, int radius)
        {
            int d = radius * 2;
            var path = new GraphicsPath();
            path.AddArc(bounds.X, bounds.Y, d, d, 180, 90);
            path.AddArc(bounds.Right - d, bounds.Y, d, d, 270, 90);
            path.AddArc(bounds.Right - d, bounds.Bottom - d, d, d, 0, 90);
            path.AddArc(bounds.X, bounds.Bottom - d, d, d, 90, 90);
            path.CloseFigure();
            return path;
        }
    }

    internal sealed class CoverBanner : Panel
    {
        private Image? _image;
        private Image? _overlay;

        /// <summary>0 = keep the top of the photo, 1 = keep the bottom.</summary>
        public float AlignY { get; set; } = 0.32f;

        public Image? Image
        {
            get => _image;
            set
            {
                _image = value;
                Invalidate();
            }
        }

        public Image? Overlay
        {
            get => _overlay;
            set
            {
                _overlay = value;
                Invalidate();
            }
        }

        public CoverBanner()
        {
            Theme.EnableDoubleBuffer(this);
            BackColor = Theme.NavyDark;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            if (Width <= 0 || Height <= 0)
                return;

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            if (_image != null)
            {
                float scale = Math.Max((float)Width / _image.Width, (float)Height / _image.Height);
                int w = (int)Math.Ceiling(_image.Width * scale);
                int h = (int)Math.Ceiling(_image.Height * scale);
                int x = (Width - w) / 2;
                float t = Math.Clamp(AlignY, 0f, 1f);
                int y = (int)Math.Round((Height - h) * t);
                e.Graphics.DrawImage(_image, x, y, w, h);
            }

            int topBand = Math.Max(1, Height / 3);
            using (var fade = new LinearGradientBrush(
                new Rectangle(0, 0, Width, topBand),
                Color.FromArgb(80, Theme.NavyDark),
                Color.FromArgb(0, Theme.NavyDark),
                LinearGradientMode.Vertical))
            {
                e.Graphics.FillRectangle(fade, 0, 0, Width, topBand);
            }

            if (_overlay != null)
            {
                int maxW = Math.Max(160, (int)(Width * 0.32));
                int maxH = Math.Max(40, (int)(Height * 0.20));
                float scale = Math.Min(maxW / (float)_overlay.Width, maxH / (float)_overlay.Height);
                int ow = Math.Max(1, (int)(_overlay.Width * scale));
                int oh = Math.Max(1, (int)(_overlay.Height * scale));
                int ox = (Width - ow) / 2;
                int oy = 14;
                e.Graphics.DrawImage(_overlay, ox, oy, ow, oh);
            }
        }
    }

    internal sealed class CardPanel : Panel
    {
        public CardPanel()
        {
            BackColor = Theme.Paper;
            Padding = new Padding(20, 18, 20, 18);
            Theme.EnableDoubleBuffer(this);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;
            var rect = new Rectangle(0, 0, Width - 1, Height - 1);
            using var border = new Pen(Theme.CreamDark);
            e.Graphics.DrawRectangle(border, rect);
            using var gold = new SolidBrush(Theme.Gold);
            e.Graphics.FillRectangle(gold, 0, 0, 4, Height);
        }
    }

    internal sealed class StatCard : Panel
    {
        private readonly Label _value;

        public string Value
        {
            get => _value.Text;
            set => _value.Text = value;
        }

        public StatCard(string caption, string value, string hint)
        {
            BackColor = Theme.Paper;
            Margin = new Padding(0, 0, 16, 0);
            MinimumSize = new Size(180, 118);
            Theme.EnableDoubleBuffer(this);

            var lblCaption = new Label
            {
                Text = caption.ToUpperInvariant(),
                Font = Theme.Caption,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(22, 16)
            };

            _value = new Label
            {
                Text = value,
                Font = Theme.StatValue,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 40)
            };

            var lblHint = new Label
            {
                Text = hint,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(22, 84)
            };

            Controls.Add(lblCaption);
            Controls.Add(_value);
            Controls.Add(lblHint);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            using var border = new Pen(Theme.CreamDark);
            e.Graphics.DrawRectangle(border, 0, 0, Width - 1, Height - 1);
            using var gold = new SolidBrush(Theme.Gold);
            e.Graphics.FillRectangle(gold, 0, 0, 4, Height);
        }
    }
}
