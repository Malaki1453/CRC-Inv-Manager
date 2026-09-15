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

                if (GridLayout.SaveDefault(grid))
                    ToastAlert.Success(form, "Default columns saved for this table.");
                else
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

            var stage = new TableSearchStage(titleText, toolbar, columnSearch);
            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            card.Controls.Add(grid);
            card.Controls.Add(toolbar);
            card.Controls.Add(stage);
            card.Controls.Add(upload);

            form.Controls.Add(card);
        }

        /// <summary>Add a toolbar button on a data page, to the right of the other actions.</summary>
        public static void AddDataPageAction(Form form, string text, EventHandler click, bool gold = false)
        {
            Panel? toolbar = FindDataToolbar(form);
            // Page did not use ApplyDataPage, so there is no shared toolbar.
            if (toolbar == null)
                return;

            var button = new Button { Text = text, Dock = DockStyle.Fill };
            // Gold is the primary CTA; navy is a secondary action.
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

        /// <summary>Find the shared DataToolbar inside a page's card, if ApplyDataPage ran.</summary>
        private static Panel? FindDataToolbar(Form form)
        {
            foreach (Control outer in form.Controls)
            {
                // Designer leftovers and chrome sit outside the content card.
                if (outer is not CardPanel card)
                    continue;
                foreach (Control inner in card.Controls)
                {
                    // The card also hosts the grid and search stage.
                    if (inner is Panel panel && panel.Name == "DataToolbar")
                        return panel;
                }
            }

            return null;
        }

        /// <summary>Right-click row menu: extras, edit, and delete when the user can mutate the table.</summary>
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
                    // Hidden or filler columns cannot be the current cell.
                    if (col < 0 || col >= grid.Columns.Count || !grid.Columns[col].Visible)
                    {
                        var first = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
                        col = first?.Index ?? 0;
                    }
                    // CurrentCell is required for some grid edit paths.
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
                foreach (var extra in extras)
                {
                    var item = extra;
                    menu.Items.Add(item.Text, null, (_, _) => item.Click(record));
                }

                // Review-only and denied tables cannot edit live rows.
                if (onEdit != null && canMutate)
                    menu.Items.Add(editText, null, (_, _) => onEdit(record));
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
                        // Grid can be disposed mid-click if the page was stolen.
                        if (host == null)
                            return;
                        if (!result.Ok)
                            ToastAlert.Error(host, result.Message);
                        else if (result.Queued)
                            // Non-admins queue deletes for Review instead of applying them.
                            ToastAlert.Success(host, result.Message);
                    });
                }

                // Review-only users get no items; skip an empty menu.
                if (menu.Items.Count == 0)
                    return;

                menu.Show(grid, grid.PointToClient(Control.MousePosition));
            };
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
            return string.IsNullOrWhiteSpace(file)
                ? $"{term}  ·  {Path.GetFileName(AppState.InventoryFolder)}"
                : $"{term}  ·  {file}";
        }
    }
}
