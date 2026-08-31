namespace CastRightCatchInvManagement
{
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

            Theme.StyleGoldButton(upload);
            upload.Text = "Upload CSV";
            upload.Dock = DockStyle.Fill;

            var uploadHost = new Panel
            {
                Dock = DockStyle.Right,
                Width = 132,
                Padding = new Padding(10, 0, 0, 0),
                BackColor = Theme.Paper
            };
            uploadHost.Controls.Add(upload);

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

            toolbar.Controls.Add(uploadHost);

            title.Visible = false;
            title.Text = titleText;

            Theme.StyleGrid(grid);
            grid.Dock = DockStyle.Fill;
            grid.Margin = new Padding(0);
            grid.BackgroundColor = Theme.Paper;

            var columnSearch = new ColumnSearch(grid, jump);
            grid.Tag = columnSearch;

            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(1)
            };
            card.Controls.Add(grid);
            card.Controls.Add(toolbar);

            form.Controls.Add(card);
        }

        public static void BindRowEdit(DataGridView grid, Action<Dictionary<string, string>> onEdit)
        {
            grid.CellMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;

                grid.ClearSelection();
                grid.Rows[e.RowIndex].Selected = true;
                try
                {
                    int col = Math.Max(0, e.ColumnIndex);
                    if (col < grid.Columns.Count)
                        grid.CurrentCell = grid.Rows[e.RowIndex].Cells[col];
                }
                catch
                {
                    // row may not take a current cell
                }

                var menu = new ContextMenuStrip();
                menu.Items.Add("Edit Product", null, (_, _) =>
                    onEdit(DataFiles.GridRowToRecord(grid, e.RowIndex)));
                menu.Show(grid, grid.PointToClient(Control.MousePosition));
            };
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
            _ => page.ToString()
        };

        public static string PageSubtitle(AppPage page)
        {
            if (!AppLock.HasFolder())
                return "Select a data folder in Settings to unlock the workspace";

            string term = AppState.TermStartDate is DateTime start
                ? $"Term started {start:MMM d, yyyy}"
                : "Term not set";

            string? file = DataFiles.GetDisplayedFileName(page);
            return string.IsNullOrWhiteSpace(file)
                ? $"{term}  ·  {Path.GetFileName(AppState.InventoryFolder)}"
                : $"{term}  ·  {file}";
        }
    }
}
