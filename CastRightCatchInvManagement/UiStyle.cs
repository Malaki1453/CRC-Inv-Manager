namespace CastRightCatchInvManagement
{
    /// <summary>Page chrome: titles, data grids, and shared toolbar wiring.</summary>
    internal static class UiStyle
    {
        /// <summary>Cream background and body font for a nested workspace page.</summary>
        public static void ApplyChildPage(Form form)
        {
            form.BackColor = Theme.Cream;
            form.Font = Theme.Body;
            form.ForeColor = Theme.Ink;
            Theme.EnableDoubleBuffer(form);
        }

        /// <summary>Standard table page: jump picker, default-columns, grid, and double-click details.</summary>
        public static void ApplyDataPage(
            Form form,
            string titleText,
            Label title,
            Button upload,
            DataGridView grid,
            string? actionText = null,
            EventHandler? actionClick = null)
        {
            ApplyChildPage(form);
            form.Padding = new Padding(28, 20, 28, 24);

            upload.Visible = false;
            upload.Enabled = false;
            upload.Size = Size.Empty;

            var jump = new ColumnJumpPicker
            {
                Width = 268,
                Dock = DockStyle.Left
            };

            var toolbar = new Panel
            {
                Name = "DataToolbar",
                Dock = DockStyle.Top,
                Height = 50,
                BackColor = Theme.Paper,
                Padding = new Padding(12, 8, 12, 8)
            };
            Theme.EnableDoubleBuffer(toolbar);
            toolbar.Paint += (_, e) =>
            {
                using var gold = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(gold, 0, toolbar.Height - 2, toolbar.Width, 2);
            };
            toolbar.Controls.Add(jump);

            var setDefault = new Button { Text = "Set default", Dock = DockStyle.Fill, TabStop = false };
            Theme.StyleOutlineButton(setDefault);
            setDefault.Click += (_, _) =>
            {
                // Accidental click should not overwrite another user's saved layout.
                if (MessageBox.Show(
                        "Save the current columns as your default for this table?",
                        "Set default",
                        MessageBoxButtons.YesNo,
                        MessageBoxIcon.Question) != DialogResult.Yes)
                    return;

                // Persist succeeded: this user's layout for this table is now the default.
                if (GridLayout.SaveDefault(grid))
                    ToastAlert.Success(form, "Default columns saved for this table.");
                else
                    // Fail because they are not signed in, or the write to settings failed.
                    ToastAlert.Error(form, AppState.SignedIn
                        ? "Could not save default columns for this table."
                        : "Sign in to save default columns.");
            };
            var setHost = new Panel
            {
                Dock = DockStyle.Left,
                Width = 118,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Theme.Paper
            };
            setHost.Controls.Add(setDefault);

            var reset = new Button { Text = "Default columns", Dock = DockStyle.Fill, TabStop = false };
            Theme.StyleOutlineButton(reset);
            reset.Click += (_, _) => DataFiles.ResetGridColumns(grid);
            var resetHost = new Panel
            {
                Dock = DockStyle.Left,
                Width = 148,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Theme.Paper
            };
            resetHost.Controls.Add(reset);
            toolbar.Controls.Add(setHost);
            toolbar.Controls.Add(resetHost);

            grid.ColumnHeaderMouseClick += (_, e) =>
            {
                // Left-click still sorts; hide is a right-click on a real column.
                if (e.Button != MouseButtons.Right || e.ColumnIndex < 0)
                    return;
                ShowRemoveColumnMenu(grid, e.ColumnIndex);
            };

            // Optional primary action (New invoice, etc.) sits on the right of the toolbar.
            if (!string.IsNullOrWhiteSpace(actionText) && actionClick != null)
            {
                var action = new Button { Text = actionText, Dock = DockStyle.Fill };
                Theme.StyleNavyButton(action);
                action.Click += actionClick;
                var actionHost = new Panel
                {
                    Dock = DockStyle.Right,
                    Width = 128,
                    Padding = new Padding(0, 0, 0, 0),
                    BackColor = Theme.Paper
                };
                actionHost.Controls.Add(action);
                toolbar.Controls.Add(actionHost);
            }

            title.Visible = false;
            title.Text = titleText;

            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.Fill;
            grid.Margin = new Padding(0);
            grid.BackgroundColor = Theme.Paper;

            var columnSearch = new ColumnSearch(grid, jump);
            grid.Tag = columnSearch;
            GridLayout.Attach(grid);

            grid.CellDoubleClick += (_, e) =>
            {
                // Header double-click is not a record.
                if (e.RowIndex < 0)
                    return;
                RecordDetailsForm.ShowRecord(
                    form,
                    titleText,
                    DataFiles.GridRowToRecord(grid, e.RowIndex));
            };

            // Three exclusive rows so search, buttons, and table cannot overlap
            // (Dock Fill siblings on one card were stacking the grid at the page top).
            // Row 0 search, row 1 buttons, row 2 table. PlaceAboveTableAndButtons
            // collapses rows 1–2 until the user types.
            var stage = new TableSearchStage(titleText, toolbar, columnSearch);
            stage.Dock = DockStyle.Fill;
            stage.Margin = Padding.Empty;

            toolbar.Dock = DockStyle.Fill;
            // 8px between the bottom of the search bar and the buttons.
            toolbar.Margin = new Padding(0, 8, 0, 0);
            toolbar.Visible = false;

            var body = new Panel
            {
                Name = "DataBody",
                Dock = DockStyle.Fill,
                BackColor = Theme.Paper,
                Visible = false,
                // 8px between the bottom of the buttons and the top of the table (headers).
                Margin = new Padding(0, 8, 0, 0)
            };
            grid.Dock = DockStyle.Fill;
            body.Controls.Add(grid);

            var stack = new TableLayoutPanel
            {
                Name = "DataStack",
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 3,
                BackColor = Theme.Paper,
                Margin = Padding.Empty,
                Padding = Padding.Empty,
                GrowStyle = TableLayoutPanelGrowStyle.FixedSize
            };
            stack.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 100f));
            // Idle: search owns the page. Searching: PlaceAboveTableAndButtons sets
            // row 0 Absolute 56, row 1 Absolute 58 (50px buttons + 8px gap), row 2 Percent.
            stack.RowStyles.Add(new RowStyle(SizeType.Percent, 100f));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
            stack.RowStyles.Add(new RowStyle(SizeType.Absolute, 0f));
            stack.Controls.Add(stage, 0, 0);
            stack.Controls.Add(toolbar, 0, 1);
            stack.Controls.Add(body, 0, 2);

            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                // Inset from the gold leading edge; do not use 1px or the table hugs the form top.
                Padding = new Padding(12, 10, 12, 12)
            };
            card.Controls.Add(stack);
            card.Controls.Add(upload);

            form.Controls.Add(card);
        }

        /// <summary>Add a toolbar button on a data page, to the right of the other actions.</summary>
        public static void AddDataPageAction(Form form, string text, EventHandler click, bool gold = false)
        {
            Panel? toolbar = FindDataToolbar(form);
            // Not an ApplyDataPage grid (e.g. Review); there is no Default-columns bar to add to.
            if (toolbar == null)
                return;

            var button = new Button { Text = text, Dock = DockStyle.Fill };
            // gold true: primary action (e.g. Sync live feed). Otherwise navy secondary.
            if (gold)
                Theme.StyleGoldButton(button);
            else
                Theme.StyleNavyButton(button);
            button.Click += click;
            int width = Math.Max(110, TextRenderer.MeasureText(text, Theme.BodyBold).Width + 28);
            var host = new Panel
            {
                Dock = DockStyle.Right,
                Width = width,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Theme.Paper
            };
            host.Controls.Add(button);
            toolbar.Controls.Add(host);
        }

        /// <summary>True when this page still shows the idle search overlay and the table should not load yet.</summary>
        public static bool DataPageHasIdleSearch(Form form)
        {
            var stage = FindDataSearch(form);
            // No DataSearch (Review/Reports): always load. Idle overlay: skip FillGrid until the user types.
            return stage != null && !stage.IsSearching;
        }

        private static TableSearchStage? FindDataSearch(Form form)
        {
            foreach (Control control in DataPageControls(form))
            {
                // DataSearch lives in DataStack row 0 (or was a direct child of the card).
                if (control is TableSearchStage stage)
                    return stage;
            }

            return null;
        }

        /// <summary>Find the shared DataToolbar inside a page's card, if ApplyDataPage ran.</summary>
        private static Panel? FindDataToolbar(Form form)
        {
            foreach (Control control in DataPageControls(form))
            {
                // Current layout: DataToolbar is DataStack row 1. Older pages put it on the card or in DataBody.
                if (control is Panel panel && panel.Name == "DataToolbar")
                    return panel;
            }

            return null;
        }

        /// <summary>
        /// Walk the data card: CardPanel children, then DataStack rows.
        /// Search, toolbar, and table are separate rows of DataStack — not overlapping Dock Fill siblings.
        /// </summary>
        private static IEnumerable<Control> DataPageControls(Form form)
        {
            foreach (Control outer in form.Controls)
            {
                // Designer leftovers and chrome sit outside the content card.
                if (outer is not CardPanel card)
                    continue;
                foreach (Control inner in card.Controls)
                {
                    yield return inner;
                    // DataStack holds search (row 0), toolbar (row 1), DataBody/table (row 2).
                    if (inner is TableLayoutPanel stack && stack.Name == "DataStack")
                    {
                        foreach (Control nested in stack.Controls)
                        {
                            yield return nested;
                            if (nested.Name != "DataBody")
                                continue;
                            foreach (Control bodyChild in nested.Controls)
                                yield return bodyChild;
                        }
                    }
                }
            }
        }

        /// <summary>Set when BindRowEdit already showed a menu so BindCellCopy does not open a second one.</summary>
        internal static bool RowContextMenuShown;

        /// <summary>Right-click or Ctrl+C copies the cell under the pointer, not the whole row.</summary>
        public static void BindCellCopy(DataGridView grid)
        {
            grid.KeyDown += (_, e) =>
            {
                // Ctrl+C copies the current cell so paste works in other apps.
                if (e.Control && e.KeyCode == Keys.C)
                {
                    CopyCell(grid.CurrentCell);
                    e.Handled = true;
                    e.SuppressKeyPress = true;
                }
            };
            grid.CellMouseClick += (_, e) =>
            {
                // Header clicks have their own column menu.
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.ColumnIndex < 0)
                    return;
                int row = e.RowIndex;
                int col = e.ColumnIndex;
                // BindRowEdit runs on the same click and shows the full row menu; skip a second popup.
                grid.BeginInvoke(new Action(() =>
                {
                    // BindRowEdit already opened the full row menu on this click; do not stack a Copy-only menu.
                    if (RowContextMenuShown)
                    {
                        RowContextMenuShown = false;
                        return;
                    }

                    ShowCopyCellMenu(grid, row, col);
                }));
            };
        }

        /// <summary>Copy the clicked cell, then the rest of the row actions.</summary>
        public static void BindRowEdit(
            DataGridView grid,
            Action<Dictionary<string, string>>? onEdit,
            string detailsTitle = "Details",
            string editText = "Edit Product",
            params (string Text, Action<Dictionary<string, string>> Click)[] extras)
        {
            grid.CellMouseClick += (_, e) =>
            {
                // Context menu is right-click on a data row, not the header.
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;

                grid.ClearSelection();
                grid.Rows[e.RowIndex].Selected = true;
                try
                {
                    int col = e.ColumnIndex;
                    // Hidden or filler columns cannot be the current cell; fall back to the first visible column.
                    if (col < 0 || col >= grid.Columns.Count || !grid.Columns[col].Visible)
                    {
                        var first = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
                        col = first?.Index ?? 0;
                    }
                    // CurrentCell is required for some grid edit paths (copy / delete).
                    if (col < grid.Columns.Count)
                        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[col];
                }
                catch
                {
                    // row may not take a current cell
                }

                var record = DataFiles.GridRowToRecord(grid, e.RowIndex);
                string table = grid.Tag is ColumnSearch search ? search.FileBaseName ?? "" : "";
                bool canMutate = DataAccess.CanMutate(table);
                var menu = new ContextMenuStrip();
                int copyRow = e.RowIndex;
                int copyCol = e.ColumnIndex;
                menu.Items.Add("Copy", null, (_, _) => CopyCell(grid, copyRow, copyCol));
                foreach (var extra in extras)
                {
                    var item = extra;
                    menu.Items.Add(item.Text, null, (_, _) => item.Click(record));
                }

                // Review-only and denied tables cannot edit live rows; skip Edit when onEdit is null or mutate is denied.
                if (onEdit != null && canMutate)
                    menu.Items.Add(editText, null, (_, _) => onEdit(record));
                // Delete needs a real table name so MutateDelete knows which SQLite table to change.
                if (canMutate && table.Length > 0)
                {
                    menu.Items.Add("Delete", null, (_, _) =>
                    {
                        // Deletes queue or apply immediately; confirm either way.
                        if (MessageBox.Show(
                                "Delete this row?",
                                "Delete",
                                MessageBoxButtons.YesNo,
                                MessageBoxIcon.Warning) != DialogResult.Yes)
                            return;
                        var result = DataFiles.MutateDelete(table, record);
                        var host = grid.FindForm();
                        // Grid can be disposed mid-click if the page was closed; nowhere to toast.
                        if (host == null)
                            return;
                        // Server/SQLite rejected the delete (locked row, permission, etc.).
                        if (!result.Ok)
                            ToastAlert.Error(host, result.Message);
                        else if (result.Queued)
                            // Non-admins queue deletes for Review instead of applying them.
                            ToastAlert.Success(host, result.Message);
                    });
                }

                // Copy is always present, so the menu is never empty.
                RowContextMenuShown = true;
                menu.Show(grid, grid.PointToClient(Control.MousePosition));
            };
        }

        private static void ShowCopyCellMenu(DataGridView grid, int row, int col)
        {
            // Header click or stale index after a reload: there is no cell to copy.
            if (row < 0 || col < 0 || row >= grid.Rows.Count || col >= grid.Columns.Count)
                return;
            var menu = new ContextMenuStrip();
            menu.Items.Add("Copy", null, (_, _) => CopyCell(grid, row, col));
            menu.Show(grid, grid.PointToClient(Control.MousePosition));
        }

        private static void CopyCell(DataGridView grid, int row, int col)
        {
            // Same bounds check as the menu: ignore header / out-of-range after a reload.
            if (row < 0 || col < 0 || row >= grid.Rows.Count || col >= grid.Columns.Count)
                return;
            CopyCell(grid.Rows[row].Cells[col]);
        }

        private static void CopyCell(DataGridViewCell? cell)
        {
            // CurrentCell can be null when nothing is selected.
            if (cell == null)
                return;
            string text = Convert.ToString(cell.FormattedValue) ?? "";
            // Clipboard.SetText throws on empty; Clear still lets paste replace with nothing.
            if (text.Length == 0)
                Clipboard.Clear();
            else
                Clipboard.SetText(text);
        }

        /// <summary>Context menu listing hidden columns the user is allowed to show again.</summary>
        internal static void ShowAddColumnMenu(DataGridView grid)
        {
            string table = grid.Tag is ColumnSearch search ? search.FileBaseName ?? "" : "";
            var hidden = grid.Columns.Cast<DataGridViewColumn>()
                .Where(c => !c.Visible && !Theme.IsAddColumn(c))
                .Where(c =>
                {
                    string key = c.Tag as string ?? c.Name;
                    return !DataAccess.IsColumnHidden(table, key) &&
                           !DataAccess.IsColumnHidden(table, c.HeaderText);
                })
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            var menu = new ContextMenuStrip();
            // Nothing user-hidden: show a disabled hint instead of an empty menu.
            if (hidden.Count == 0)
            {
                menu.Items.Add("All columns are showing").Enabled = false;
            }
            else
            {
                // Each item unhides one user-hidden column (not IT-denied columns).
                foreach (var col in hidden)
                {
                    var column = col;
                    menu.Items.Add(column.HeaderText, null, (_, _) =>
                    {
                        column.Visible = true;
                        Theme.FitAllColumns(grid);
                        // Jump picker and search boxes must match the new visible set.
                        if (grid.Tag is ColumnSearch search)
                            search.NotifyColumnsChanged();
                        GridLayout.Save(grid);
                    });
                }
            }

            menu.Show(grid, grid.PointToClient(Control.MousePosition));
        }

        /// <summary>Right-click a header to hide that column, keeping at least one data column.</summary>
        private static void ShowRemoveColumnMenu(DataGridView grid, int columnIndex)
        {
            // Click landed on the header gutter, not a column.
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
                return;

            var column = grid.Columns[columnIndex];
            // The trailing "+" column is not a data field.
            if (!column.Visible || Theme.IsAddColumn(column))
                return;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Hide column", null, (_, _) =>
            {
                int visible = grid.Columns.Cast<DataGridViewColumn>()
                    .Count(c => c.Visible && !Theme.IsAddColumn(c));
                // A table with zero data columns cannot be used.
                if (visible <= 1)
                    return;

                column.Visible = false;
                Theme.FitAllColumns(grid);
                if (grid.Tag is ColumnSearch search)
                    search.NotifyColumnsChanged();
                GridLayout.Save(grid);
            });
            menu.Show(grid, grid.PointToClient(Control.MousePosition));
        }

        /// <summary>Header title: catalog/custom label, or a built-in name for chrome pages.</summary>
        public static string PageTitle(AppPage page)
        {
            string custom = MenuLayout.LabelFor(page);
            // Catalog pages (and Admin-renamed ones) win over the built-in chrome titles.
            if (custom.Length > 0)
                return custom;

            // Chrome pages are not in the menu catalog.
            return page switch
            {
                AppPage.Dashboard => "Home",
                AppPage.Settings => "Settings",
                AppPage.Admin => "Admin",
                AppPage.Help => "Controls",
                AppPage.ItUsers => "Users",
                AppPage.ItAccess => "IT and admins",
                _ => page.ToString()
            };
        }

        /// <summary>Subtitle: term/view plus folder or file name, or a lock message with no folder.</summary>
        public static string PageSubtitle(AppPage page)
        {
            // No folder or server yet: explain why tables are locked.
            if (!AppLock.HasFolder())
                return "Select a data folder in Settings to unlock the workspace";

            string term = AppState.ViewingOldInventory
                ? "All inventory"
                : AppState.TermStartDate is DateTime start
                    ? $"Term started {start:MMM d, yyyy}"
                    : "Term not set";

            string? file = DataFiles.GetDisplayedFileName(page);
            // Chrome pages (Home/Help) have no table file; show the folder name instead.
            return string.IsNullOrWhiteSpace(file)
                ? $"{term}  ·  {Path.GetFileName(AppState.InventoryFolder)}"
                : $"{term}  ·  {file}";
        }
    }
}
