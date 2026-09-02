namespace CastRightCatchInvManagement
{
    /// <summary>Page chrome: titles, data grids, and shared toolbar wiring.</summary>
    internal static class UiStyle
    {
        public static void ApplyChildPage(Form form)
        {
            form.BackColor = Theme.Cream;
            form.Font = Theme.Body;
            form.ForeColor = Theme.Ink;
            Theme.EnableDoubleBuffer(form);
        }

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
            toolbar.Controls.Add(resetHost);

            grid.ColumnHeaderMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.ColumnIndex < 0)
                    return;
                ShowRemoveColumnMenu(grid, e.ColumnIndex);
            };

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

            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            card.Controls.Add(grid);
            card.Controls.Add(toolbar);
            card.Controls.Add(upload);

            form.Controls.Add(card);
        }

        /// <summary>Add a toolbar button on a data page, to the right of the other actions.</summary>
        public static void AddDataPageAction(Form form, string text, EventHandler click, bool gold = false)
        {
            Panel? toolbar = FindDataToolbar(form);
            if (toolbar == null)
                return;

            var button = new Button { Text = text, Dock = DockStyle.Fill };
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

        private static Panel? FindDataToolbar(Form form)
        {
            foreach (Control outer in form.Controls)
            {
                if (outer is not CardPanel card)
                    continue;
                foreach (Control inner in card.Controls)
                {
                    if (inner is Panel panel && panel.Dock == DockStyle.Top && panel.Height >= 48)
                        return panel;
                }
            }

            return null;
        }

        public static void BindRowEdit(
            DataGridView grid,
            Action<Dictionary<string, string>> onEdit,
            string detailsTitle = "Details",
            string editText = "Edit Product")
        {
            grid.CellMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;

                grid.ClearSelection();
                grid.Rows[e.RowIndex].Selected = true;
                try
                {
                    int col = e.ColumnIndex;
                    if (col < 0 || col >= grid.Columns.Count || !grid.Columns[col].Visible)
                    {
                        var first = grid.Columns.GetFirstColumn(DataGridViewElementStates.Visible);
                        col = first?.Index ?? 0;
                    }
                    if (col < grid.Columns.Count)
                        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[col];
                }
                catch
                {
                    // row may not take a current cell
                }

                var record = DataFiles.GridRowToRecord(grid, e.RowIndex);
                var menu = new ContextMenuStrip();
                menu.Items.Add("View Details", null, (_, _) =>
                    RecordDetailsForm.ShowRecord(grid.FindForm(), detailsTitle, record));
                menu.Items.Add(editText, null, (_, _) => onEdit(record));
                menu.Show(grid, grid.PointToClient(Control.MousePosition));
            };
        }

        internal static void ShowAddColumnMenu(DataGridView grid)
        {
            var hidden = grid.Columns.Cast<DataGridViewColumn>()
                .Where(c => !c.Visible && !Theme.IsAddColumn(c))
                .OrderBy(c => c.DisplayIndex)
                .ToList();

            var menu = new ContextMenuStrip();
            if (hidden.Count == 0)
            {
                menu.Items.Add("All columns are showing").Enabled = false;
            }
            else
            {
                foreach (var col in hidden)
                {
                    var column = col;
                    menu.Items.Add(column.HeaderText, null, (_, _) =>
                    {
                        column.Visible = true;
                        Theme.FitAllColumns(grid);
                        if (grid.Tag is ColumnSearch search)
                            search.NotifyColumnsChanged();
                        GridLayout.Save(grid);
                    });
                }
            }

            menu.Show(grid, grid.PointToClient(Control.MousePosition));
        }

        private static void ShowRemoveColumnMenu(DataGridView grid, int columnIndex)
        {
            if (columnIndex < 0 || columnIndex >= grid.Columns.Count)
                return;

            var column = grid.Columns[columnIndex];
            if (!column.Visible || Theme.IsAddColumn(column))
                return;

            var menu = new ContextMenuStrip();
            menu.Items.Add("Hide column", null, (_, _) =>
            {
                int visible = grid.Columns.Cast<DataGridViewColumn>()
                    .Count(c => c.Visible && !Theme.IsAddColumn(c));
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

        public static string PageTitle(AppPage page) => page switch
        {
            AppPage.Dashboard => "Command Center",
            AppPage.PurchaseSales => "Purchases",
            AppPage.AddPurchase => "Purchase Form",
            AppPage.Sales => "Sales",
            AppPage.AddSale => "Sales Form",
            AppPage.SalesOrder => "Create Sales Order",
            AppPage.Customers => "Customers",
            AppPage.Vendors => "Vendors",
            AppPage.ItemCodes => "Item Codes",
            AppPage.Invoicing => "Invoices",
            AppPage.InvoicePdf => "Create Invoice",
            AppPage.Debits => "Debits",
            AppPage.Credits => "Credits",
            AppPage.Banking => "Banking",
            AppPage.Reports => "Reports",
            AppPage.Settings => "Settings",
            AppPage.Admin => "Admin",
            AppPage.Help => "Controls",
            AppPage.ItUsers => "Users",
            AppPage.ItAccess => "IT and admins",
            _ => page.ToString()
        };

        public static string PageSubtitle(AppPage page)
        {
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
