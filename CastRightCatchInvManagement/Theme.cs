using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    /// <summary>Cast Right Catch colors, fonts, and shared control styling.</summary>
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
        public static readonly Color WaitAddFill = Color.FromArgb(255, 246, 220);
        public static readonly Color WaitEditFill = Color.FromArgb(255, 236, 196);
        public static readonly Color Success = Color.FromArgb(28, 110, 62);
        public static readonly Color SuccessFill = Color.FromArgb(230, 245, 234);
        public static readonly Color GridAlt = Color.FromArgb(250, 247, 241);
        public static readonly Color GridLine = Color.FromArgb(232, 224, 210);
        public static readonly Color FieldBorder = Color.FromArgb(173, 173, 173);
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

        /// <summary>Create a font, falling back to Segoe UI when the family is missing.</summary>
        public static Font CreateFont(string family, float size, FontStyle style)
        {
            try
            {
                return new Font(family, size, style, GraphicsUnit.Point);
            }
            // family (often Georgia) may be missing on locked-down PCs; Segoe UI is always present.
            catch
            {
                return new Font("Segoe UI", size, style, GraphicsUnit.Point);
            }
        }

        /// <summary>Turn on the protected DoubleBuffered flag to cut nested-page flicker.</summary>
        public static void EnableDoubleBuffer(Control control)
        {
            typeof(Control)
                .GetProperty("DoubleBuffered",
                    System.Reflection.BindingFlags.Instance |
                    System.Reflection.BindingFlags.NonPublic)
                ?.SetValue(control, true, null);
        }

        /// <summary>Georgia page title on navy ink, left-aligned in a fixed-height label.</summary>
        public static void StylePageTitle(Label label)
        {
            label.Font = PageTitle;
            label.ForeColor = Navy;
            label.AutoSize = false;
            label.TextAlign = ContentAlignment.MiddleLeft;
        }

        /// <summary>Primary gold CTA used on save/sign-in. Greys out when Enabled is false.</summary>
        public static void StyleGoldButton(Button button)
        {
            button.FlatStyle = FlatStyle.Flat;
            button.FlatAppearance.BorderSize = 0;
            button.Font = BodyBold;
            button.UseVisualStyleBackColor = false;
            ApplyGoldEnabled(button);
            button.EnabledChanged -= GoldEnabledChanged;
            button.EnabledChanged += GoldEnabledChanged;
        }

        private static void GoldEnabledChanged(object? sender, EventArgs e)
        {
            if (sender is Button button)
                ApplyGoldEnabled(button);
        }

        /// <summary>Gold when enabled; cream/muted when the action is not available.</summary>
        private static void ApplyGoldEnabled(Button button)
        {
            if (button.Enabled)
            {
                button.FlatAppearance.MouseOverBackColor = GoldLight;
                button.FlatAppearance.MouseDownBackColor = Color.FromArgb(180, 136, 28);
                button.BackColor = Gold;
                button.ForeColor = NavyDark;
                button.Cursor = Cursors.Hand;
                return;
            }

            button.FlatAppearance.MouseOverBackColor = CreamDark;
            button.FlatAppearance.MouseDownBackColor = CreamDark;
            button.BackColor = CreamDark;
            button.ForeColor = Muted;
            button.Cursor = Cursors.Default;
        }

        /// <summary>Solid navy action button used on toolbars.</summary>
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

        /// <summary>White navy-outline button for secondary actions.</summary>
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

        /// <summary>Single-border paper text box.</summary>
        public static void StyleField(TextBox box)
        {
            box.BorderStyle = BorderStyle.FixedSingle;
            box.Font = Body;
            box.BackColor = Paper;
            box.ForeColor = Ink;
        }

        private static readonly HashSet<TextBox> AutoScrollFields = new();

        /// <summary>Show a vertical scrollbar only while the text is taller than the box.</summary>
        public static void BindAutoVerticalScroll(TextBox box)
        {
            if (!AutoScrollFields.Add(box))
                return;
            box.Multiline = true;
            box.Disposed += (_, _) => AutoScrollFields.Remove(box);
            box.TextChanged += (_, _) => SyncAutoVerticalScroll(box);
            box.SizeChanged += (_, _) => SyncAutoVerticalScroll(box);
            box.FontChanged += (_, _) => SyncAutoVerticalScroll(box);
            box.HandleCreated += (_, _) => SyncAutoVerticalScroll(box);
            SyncAutoVerticalScroll(box);
        }

        /// <summary>Measure as if there is no scrollbar so showing one does not wrap more and flicker.</summary>
        private static void SyncAutoVerticalScroll(TextBox box)
        {
            if (!box.IsHandleCreated)
                return;

            int fullWidth = box.ClientSize.Width;
            if (box.ScrollBars == ScrollBars.Vertical)
                fullWidth += SystemInformation.VerticalScrollBarWidth;
            fullWidth = Math.Max(1, fullWidth);

            string text = box.Text;
            if (text.Length == 0)
            {
                if (box.ScrollBars != ScrollBars.None)
                    box.ScrollBars = ScrollBars.None;
                return;
            }

            if (!text.EndsWith('\n'))
                text += "\n";
            int needed = TextRenderer.MeasureText(
                text,
                box.Font,
                new Size(fullWidth, int.MaxValue),
                TextFormatFlags.WordBreak | TextFormatFlags.TextBoxControl).Height;
            var want = needed > box.ClientSize.Height ? ScrollBars.Vertical : ScrollBars.None;
            if (box.ScrollBars != want)
                box.ScrollBars = want;
        }

        /// <summary>Read-only money box filled from lots, bank lines, or open invoices, not typed.</summary>
        public static void StyleComputedField(TextBox box)
        {
            StyleField(box);
            box.ReadOnly = true;
            box.TabStop = false;
            box.BackColor = Cream;
            box.Cursor = Cursors.Default;
        }

        /// <summary>Paper combo with a text-field rectangle; click opens the list.</summary>
        public static void StyleCombo(ComboBox box)
        {
            box.FlatStyle = FlatStyle.Flat;
            box.Font = Body;
            box.BackColor = Paper;
            box.ForeColor = Ink;
            box.IntegralHeight = false;
            ComboChrome.Attach(box);
        }

        /// <summary>Small muted caption above a field.</summary>
        public static void StyleFieldLabel(Label label)
        {
            label.Font = Caption;
            label.ForeColor = Muted;
            label.AutoSize = true;
        }

        /// <summary>Navy headers, cream rows, full-row select, and custom cell borders.</summary>
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
            MoneyFormat.WireGrid(grid);
            // Default grid copy dumps the whole row; we copy the clicked cell instead.
            if (grid.ClipboardCopyMode != DataGridViewClipboardCopyMode.Disable)
            {
                grid.ClipboardCopyMode = DataGridViewClipboardCopyMode.Disable;
                UiStyle.BindCellCopy(grid);
            }
        }

        /// <summary>Muted colors while a grid is still loading; restore StyleGrid when fade is false.</summary>
        public static void FadeGrid(DataGridView grid, bool fade)
        {
            grid.EnableHeadersVisualStyles = false;
            // fade is true while rows are still loading; mute colors so the spinner reads as "in progress".
            if (fade)
            {
                grid.BackgroundColor = Cream;
                grid.GridColor = Color.FromArgb(210, 214, 210);
                grid.ColumnHeadersDefaultCellStyle.BackColor = Color.FromArgb(90, 108, 122);
                grid.ColumnHeadersDefaultCellStyle.ForeColor = Color.FromArgb(200, 208, 214);
                grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = Color.FromArgb(90, 108, 122);
                grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = Color.FromArgb(200, 208, 214);
                grid.DefaultCellStyle.BackColor = Cream;
                grid.DefaultCellStyle.ForeColor = Muted;
                grid.DefaultCellStyle.SelectionBackColor = Cream;
                grid.DefaultCellStyle.SelectionForeColor = Muted;
                grid.AlternatingRowsDefaultCellStyle.BackColor = Cream;
                grid.AlternatingRowsDefaultCellStyle.ForeColor = Muted;
                return;
            }

            grid.BackgroundColor = Paper;
            grid.GridColor = GridLine;
            grid.ColumnHeadersDefaultCellStyle.BackColor = HeaderBack;
            grid.ColumnHeadersDefaultCellStyle.ForeColor = HeaderText;
            grid.ColumnHeadersDefaultCellStyle.SelectionBackColor = HeaderBack;
            grid.ColumnHeadersDefaultCellStyle.SelectionForeColor = HeaderText;
            grid.DefaultCellStyle.BackColor = Paper;
            grid.DefaultCellStyle.ForeColor = Ink;
            grid.DefaultCellStyle.SelectionBackColor = GridSelection;
            grid.DefaultCellStyle.SelectionForeColor = NavyDark;
            grid.AlternatingRowsDefaultCellStyle.BackColor = GridAlt;
            grid.AlternatingRowsDefaultCellStyle.ForeColor = Ink;
            grid.AlternatingRowsDefaultCellStyle.SelectionBackColor = GridSelection;
            grid.AlternatingRowsDefaultCellStyle.SelectionForeColor = NavyDark;
        }

        /// <summary>Paint default cell contents plus a single hairline under each data cell.</summary>
        private static void PaintGridDataCell(object? sender, DataGridViewCellPaintingEventArgs e)
        {
            // Headers use their own painter.
            if (e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            if (sender is DataGridView grid &&
                e.ColumnIndex < grid.Columns.Count &&
                IsActionColumn(grid.Columns[e.ColumnIndex]))
            {
                PaintActionCell(grid, e);
                return;
            }

            e.Paint(e.CellBounds, DataGridViewPaintParts.All & ~DataGridViewPaintParts.Border);
            PaintCellHairline(e);
            e.Handled = true;
        }

        /// <summary>Gold Receive Payment chip, or grey when the invoice is already paid.</summary>
        private static void PaintActionCell(DataGridView grid, DataGridViewCellPaintingEventArgs e)
        {
            e.Paint(e.CellBounds, DataGridViewPaintParts.Background | DataGridViewPaintParts.SelectionBackground);
            var g = e.Graphics;
            if (g == null)
            {
                e.Handled = true;
                return;
            }

            string text = e.Value?.ToString() ?? "";
            // Parent aging rows leave this cell blank so only invoices show a button.
            if (text.Length > 0)
            {
                bool enabled = grid.Rows[e.RowIndex].Cells[e.ColumnIndex].Tag is true;
                var inner = Rectangle.Inflate(e.CellBounds, -6, -6);
                if (inner.Width >= 8 && inner.Height >= 8)
                {
                    g.SmoothingMode = SmoothingMode.AntiAlias;
                    using var path = RoundedRect(inner, 4);
                    using var fill = new SolidBrush(enabled ? Gold : CreamDark);
                    g.FillPath(fill, path);
                    TextRenderer.DrawText(
                        g,
                        text,
                        BodyBold,
                        inner,
                        enabled ? NavyDark : Muted,
                        TextFormatFlags.HorizontalCenter |
                        TextFormatFlags.VerticalCenter |
                        TextFormatFlags.EndEllipsis |
                        TextFormatFlags.NoPrefix);
                }
            }

            PaintCellHairline(e);
            e.Handled = true;
        }

        /// <summary>Hairline under a data cell so rows still separate when owner-drawn.</summary>
        private static void PaintCellHairline(DataGridViewCellPaintingEventArgs e)
        {
            if (e.Graphics == null)
                return;
            using var pen = new Pen(GridLine, 1);
            int y = e.CellBounds.Bottom - 1;
            e.Graphics.DrawLine(pen, e.CellBounds.Left, y, e.CellBounds.Right, y);
        }

        /// <summary>Rounded rectangle path for in-grid action chips.</summary>
        private static GraphicsPath RoundedRect(Rectangle bounds, int radius)
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

        /// <summary>Manual column widths with both scrollbars so FitAllColumns can stretch leftover space.</summary>
        public static void ApplyTableScrollMode(DataGridView grid)
        {
            EnsureManualColumnWidths(grid);
            grid.ScrollBars = ScrollBars.Both;
            grid.AllowUserToResizeColumns = true;
        }

        /// <summary>Size each visible data column to its longest cell, then fill leftover width.</summary>
        public static void FitAllColumns(DataGridView grid)
        {
            // Newly created grids have no columns until data binds.
            if (grid.Columns.Count == 0)
                return;

            EnsureManualColumnWidths(grid);
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                // Hidden, "+", and action-button columns keep a fixed width.
                if (!grid.Columns[i].Visible || IsControlColumn(grid.Columns[i]))
                    continue;
                FitLongest(grid, i);
            }

            StretchVisibleColumns(grid);
        }

        public const string AddColumnTag = "__add_column__";
        public const string ActionColumnTag = "__action_column__";
        public const int AddColumnWidth = 42;
        public const int ActionColumnWidth = 156;

        /// <summary>True for the trailing "+" column that opens the add-column menu.</summary>
        public static bool IsAddColumn(DataGridViewColumn? column)
        {
            return column != null &&
                   string.Equals(column.Tag as string, AddColumnTag, StringComparison.Ordinal);
        }

        /// <summary>True for the Receive Payment (or similar) in-grid action column.</summary>
        public static bool IsActionColumn(DataGridViewColumn? column)
        {
            return column != null &&
                   string.Equals(column.Tag as string, ActionColumnTag, StringComparison.Ordinal);
        }

        /// <summary>True for "+" and in-grid action buttons, which are not data fields.</summary>
        public static bool IsControlColumn(DataGridViewColumn? column) =>
            IsAddColumn(column) || IsActionColumn(column);

        /// <summary>Index of the action-button column, or -1 when the grid has none.</summary>
        public static int ActionColumnIndex(DataGridView grid)
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (IsActionColumn(col))
                    return col.Index;
            }

            return -1;
        }

        /// <summary>Append the "+" column once, skipping empty or status-only grids.</summary>
        public static void EnsureAddColumn(DataGridView grid)
        {
            // Already has the trailing "+" control column.
            if (grid.Columns.Cast<DataGridViewColumn>().Any(IsAddColumn))
                return;
            // Empty grids have no data columns to hide/show, so "+" would be the only cell.
            if (grid.Columns.Count == 0)
                return;
            // Status-only pending grids have no user-hidable fields.
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

        /// <summary>Fixed-width Receive Payment column, inserted in front of the "+" so it stays on the row.</summary>
        public static void EnsureActionColumn(
            DataGridView grid,
            string header = "Receive Payment",
            int width = ActionColumnWidth)
        {
            if (grid.Columns.Cast<DataGridViewColumn>().Any(IsActionColumn))
                return;
            if (grid.Columns.Count == 0)
                return;

            var action = new DataGridViewTextBoxColumn
            {
                Name = "ReceivePayment",
                HeaderText = header,
                Tag = ActionColumnTag,
                Width = width,
                MinimumWidth = width,
                Resizable = DataGridViewTriState.False,
                SortMode = DataGridViewColumnSortMode.NotSortable,
                ReadOnly = true,
                AutoSizeMode = DataGridViewAutoSizeColumnMode.None
            };
            action.DefaultCellStyle.Alignment = DataGridViewContentAlignment.MiddleCenter;
            int addIndex = -1;
            for (int i = 0; i < grid.Columns.Count; i++)
            {
                if (IsAddColumn(grid.Columns[i]))
                {
                    addIndex = i;
                    break;
                }
            }

            if (addIndex >= 0)
                grid.Columns.Insert(addIndex, action);
            else
                grid.Columns.Add(action);
        }

        /// <summary>Give leftover client width to visible data columns so the row fills the card.</summary>
        public static void StretchVisibleColumns(DataGridView grid)
        {
            var visible = grid.Columns.Cast<DataGridViewColumn>()
                .Where(c => c.Visible && !IsControlColumn(c))
                .ToList();
            // Nothing to stretch when every data column is hidden.
            if (visible.Count == 0)
                return;

            int used = 0;
            foreach (var col in visible)
                used += col.Width;
            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (!col.Visible || !IsControlColumn(col))
                    continue;
                if (IsAddColumn(col))
                    col.Width = AddColumnWidth;
                if (IsActionColumn(col))
                    col.Width = Math.Max(ActionColumnWidth, col.MinimumWidth);
                used += col.Width;
            }

            int available = Math.Max(0, grid.ClientSize.Width - 2);
            // A visible vertical scrollbar steals width from the last column.
            if (grid.Controls.OfType<VScrollBar>().Any(bar => bar.Visible))
                available -= SystemInformation.VerticalScrollBarWidth;

            int extra = available - used;
            // Columns already overflow; leave horizontal scroll instead of shrinking.
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

        /// <summary>Solid navy fill for a custom column-header cell.</summary>
        public static void PaintHeaderBackground(Graphics g, Rectangle bounds)
        {
            using var fill = new SolidBrush(HeaderBack);
            g.FillRectangle(fill, bounds);
        }

        /// <summary>Sort chevron; active sort gets a gold chip behind the arrow.</summary>
        public static void PaintHeaderArrow(Graphics g, Rectangle bounds, bool active, bool up)
        {
            int cx = bounds.X + bounds.Width / 2;
            int cy = bounds.Y + bounds.Height / 2;

            // Idle headers keep a faint chevron without the gold chip.
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

        /// <summary>Switch AutoSize off while keeping current pixel widths so FitAllColumns can run.</summary>
        public static void EnsureManualColumnWidths(DataGridView grid)
        {
            // Already manual; changing mode would reset widths.
            if (grid.AutoSizeColumnsMode == DataGridViewAutoSizeColumnsMode.None)
                return;

            var widths = new int[grid.Columns.Count];
            for (int i = 0; i < grid.Columns.Count; i++)
                widths[i] = grid.Columns[i].Width;

            grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.None;
            for (int i = 0; i < widths.Length; i++)
                grid.Columns[i].Width = widths[i];
        }

        /// <summary>Pixel width of a cell or header, plus padding for two extra characters.</summary>
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

        /// <summary>Set one column to the widest of its header and cell values.</summary>
        public static void FitLongest(DataGridView grid, int columnIndex)
        {
            // Caller may pass a removed column after a layout change.
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
                return;

            var column = grid.Columns[columnIndex];
            int width = MeasureColumnText(grid, column.HeaderText, BodyBold) + 22;
            var cellFont = grid.DefaultCellStyle.Font ?? Body;
            foreach (DataGridViewRow row in grid.Rows)
            {
                // The asterisk new-row has no values to measure.
                if (row.IsNewRow)
                    continue;
                width = Math.Max(width, MeasureColumnText(grid, row.Cells[columnIndex].Value?.ToString(), cellFont));
            }

            SetColumnWidth(grid, columnIndex, width);
        }

        /// <summary>Clamp a column width so it stays usable but cannot blow out the grid.</summary>
        private static void SetColumnWidth(DataGridView grid, int columnIndex, int width)
        {
            EnsureManualColumnWidths(grid);
            grid.Columns[columnIndex].Width = Math.Clamp(width, 48, 2400);
        }

        /// <summary>Closed rounded-rect path used by chips and cards.</summary>
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

    /// <summary>Hero image with optional wordmark overlay.</summary>
    internal sealed class CoverBanner : Panel
    {
        private Image? _image;
        private Image? _overlay;

        /// <summary>0 = keep the top of the photo, 1 = keep the bottom.</summary>
        public float AlignY { get; set; } = 0.32f;

        /// <summary>Hero photo cover-cropped into the banner.</summary>
        public Image? Image
        {
            get => _image;
            set
            {
                _image = value;
                Invalidate();
            }
        }

        /// <summary>Optional wordmark drawn over the top of the hero.</summary>
        public Image? Overlay
        {
            get => _overlay;
            set
            {
                _overlay = value;
                Invalidate();
            }
        }

        /// <summary>Navy cover that redraws on resize.</summary>
        public CoverBanner()
        {
            Theme.EnableDoubleBuffer(this);
            BackColor = Theme.NavyDark;
            SetStyle(ControlStyles.ResizeRedraw, true);
        }

        /// <summary>Cover-crop the hero, fade the top, and optionally overlay the wordmark.</summary>
        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(BackColor);
            // Layout can paint at 0x0 before the first resize.
            if (Width <= 0 || Height <= 0)
                return;

            e.Graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
            e.Graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
            e.Graphics.SmoothingMode = SmoothingMode.HighQuality;

            // Cover still paints navy when the hero file is missing.
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

            // Wordmark is optional; home hero still works without it.
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

    /// <summary>White content card with a gold leading edge.</summary>
    internal sealed class CardPanel : Panel
    {
        /// <summary>Paper card with a gold leading bar.</summary>
        public CardPanel()
        {
            BackColor = Theme.Paper;
            Padding = new Padding(20, 18, 20, 18);
            Theme.EnableDoubleBuffer(this);
        }

        /// <summary>Cream border plus a 4px gold leading edge.</summary>
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

    /// <summary>Dashboard metric: title, large value, and caption.</summary>
    internal sealed class StatCard : Panel
    {
        private readonly Label _value;
        private readonly Label _hint;

        /// <summary>Large metric number shown on the card.</summary>
        public string Value
        {
            get => _value.Text;
            set => _value.Text = value;
        }

        /// <summary>Small caption under the metric (units, period, …).</summary>
        public string Hint
        {
            get => _hint.Text;
            set => _hint.Text = value;
        }

        /// <summary>Caption, large value, and hint on a gold-edged paper card.</summary>
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

            _hint = new Label
            {
                Text = hint,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Location = new Point(22, 84)
            };

            Controls.Add(lblCaption);
            Controls.Add(_value);
            Controls.Add(_hint);
        }

        /// <summary>Cream border plus a 4px gold leading edge.</summary>
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
