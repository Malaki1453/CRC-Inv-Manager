namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Sales grid page. Each row is a sold product line from the sales table.
    /// Clicks on a data row:
    /// double-click → View Details,
    /// Shift+click or right-click Add to Invoice → fill Create Invoice (replaces a different sale already on it),
    /// middle-click → open the sales-order PDF if one exists, otherwise fill Create Sales Order.
    /// Right-click Edit Sale is wired by <see cref="UiStyle.BindRowEdit"/>.
    /// </summary>
    public partial class Sales : Form, INavigationPage
    {
        /// <summary>Wire the sales grid, New Sale, row edit, PDF, and invoice/sales-order shortcuts.</summary>
        public Sales()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(
                this,
                "Sales",
                lblTitle,
                btnUpload,
                dataGridView1,
                "New Sale",
                (_, _) => SalesOrder.OpenNew());
            UiStyle.BindRowEdit(
                dataGridView1,
                SalesOrder.OpenEdit,
                "Sale",
                "Edit Sale",
                ("Show PDF", ShowSalePdf),
                ("Add to Invoice", record => AddSaleRecordToInvoice(record, stayOnPage: Navigator.IsOpen(AppPage.InvoicePdf))));
            DataFiles.DataChanged += LoadTable;
            dataGridView1.CellMouseClick += dataGridView1_CellMouseClick;
            dataGridView1.CellMouseDown += dataGridView1_CellMouseDown;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from sales (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Sales);

        /// <summary>Open the stored sale PDF, or build one from the customer PO if it is missing.</summary>
        private void ShowSalePdf(Dictionary<string, string> record)
        {
            string po = DataFiles.SalePo(record);
            DataFiles.ShowPdf(
                DataFiles.PdfKindSale,
                po,
                "sale " + po,
                () =>
                {
                    string? path = SaleDocument.SaveFromPo(po);
                    // No matching sale lines, so there is nothing to draw.
                    if (path == null)
                    {
                        ToastAlert.Error(this, "Could not create a PDF for this sale.");
                        return;
                    }

                    DataFiles.OpenPdf(path, DataFiles.PdfKindSale, po);
                });
        }

        /// <summary>
        /// Shift+left-click a sale: add that PO’s lines to Create Invoice but keep this page visible.
        /// Plain left-click does nothing extra (selection / edit still work as usual).
        /// </summary>
        private void dataGridView1_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            // Ignore header clicks and non-left buttons.
            if (e.Button != MouseButtons.Left || e.RowIndex < 0)
                return;
            // Plain left-click is selection only; Shift+click adds to the invoice.
            if ((ModifierKeys & Keys.Shift) != Keys.Shift)
                return;

            AddSaleToDocument(e.RowIndex, invoice: true, stayOnPage: true);
        }

        /// <summary>
        /// Middle-click a sale: work with a sales order.
        /// If the row already has an SO # and a stored PDF, open that PDF.
        /// Otherwise fill Create Sales Order from this PO and go there.
        /// </summary>
        private void dataGridView1_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            // Middle-click is the sales-order shortcut; other buttons are handled elsewhere.
            if (e.Button != MouseButtons.Middle || e.RowIndex < 0)
                return;

            AddSaleToDocument(e.RowIndex, invoice: false);
        }

        /// <summary>
        /// Push this grid row onto Create Invoice or Create Sales Order.
        /// Uses customer PO (and SO # if present) so every matching sale line is added, not only this row.
        /// <paramref name="invoice"/> true = invoice form; false = sales-order form.
        /// <paramref name="stayOnPage"/> true = do not navigate away after a successful add.
        /// </summary>
        private void AddSaleRecordToInvoice(Dictionary<string, string> record, bool stayOnPage)
        {
            string po = DataFiles.SalePo(record);
            string so = DataFiles.GetRecord(record, "SO #");
            // Prefill looks up sale lines by customer PO or SO #.
            if (po.Length == 0 && so.Length == 0)
                return;

            var prefill = new InvoiceSalePrefill
            {
                Po = po,
                So = so,
                ItemCode = DataFiles.GetRecord(record, "Item Code"),
                CustomerCode = DataFiles.GetRecord(record, "Customer Code"),
                CustomerName = DataFiles.GetRecord(record, "Customer")
            };

            var form = Navigator.Ensure<InvoicePdf>(AppPage.InvoicePdf);
            form.TryAddSale(prefill, error =>
            {
                // Errors stay on this page; stayOnPage is Shift+click so the user can add more sales.
                if (error != null || stayOnPage)
                {
                    ShowAddResultSafe(error);
                    return;
                }

                Navigator.GoTo(AppPage.InvoicePdf);
            });
        }

        /// <summary>Add this grid row to Create Invoice or Create Sales Order, or open an existing SO PDF.</summary>
        private void AddSaleToDocument(int rowIndex, bool invoice, bool stayOnPage = false)
        {
            var record = DataFiles.GridRowToRecord(dataGridView1, rowIndex);
            string po = DataFiles.SalePo(record);
            string so = DataFiles.GetRecord(record, "SO #");
            // Cannot look up matching lines without a PO or SO.
            if (po.Length == 0 && so.Length == 0)
                return;

            // Middle-click shortcut: if this sale already has a sales-order PDF, just open it.
            if (!invoice && so.Length > 0)
            {
                string? existing = DataFiles.FindStoredSalesOrder(so);
                // Reopen the stored pick ticket instead of filling a new draft.
                if (existing != null)
                {
                    DataFiles.OpenPdf(existing, DataFiles.PdfKindSalesOrder, so);
                    return;
                }
            }

            var prefill = new InvoiceSalePrefill
            {
                Po = po,
                So = so,
                ItemCode = DataFiles.GetRecord(record, "Item Code"),
                CustomerCode = DataFiles.GetRecord(record, "Customer Code"),
                CustomerName = DataFiles.GetRecord(record, "Customer")
            };

            // Right-click / Shift+click path: Create Invoice.
            if (invoice)
            {
                var form = Navigator.Ensure<InvoicePdf>(AppPage.InvoicePdf);
                form.TryAddSale(prefill, error =>
                {
                    // Stay on Sales after Shift+click, or when the add failed.
                    if (error != null || stayOnPage)
                    {
                        ShowAddResultSafe(error);
                        return;
                    }

                    Navigator.GoTo(AppPage.InvoicePdf);
                });
                return;
            }

            var order = Navigator.Ensure<SalesOrder>(AppPage.SalesOrder);
            order.TryAddSale(prefill, error =>
            {
                // A customer mismatch should not jump away from this grid.
                if (error != null)
                {
                    ShowAddResultSafe(error);
                    return;
                }

                Navigator.GoTo(AppPage.SalesOrder);
            });
        }

        /// <summary>
        /// Show the add result on the UI thread. TryAddSale may finish from a callback
        /// after this form has been torn down or from a different thread.
        /// </summary>
        private void ShowAddResultSafe(string? error)
        {
            // TryAddSale can finish after this page was closed.
            if (IsDisposed)
                return;
            // Callbacks may arrive off the UI thread.
            if (InvokeRequired)
            {
                BeginInvoke(() => ShowAddResult(error));
                return;
            }

            ShowAddResult(error);
        }

        /// <summary>Toast on this page: error text, or a short success if the lines were added.</summary>
        private void ShowAddResult(string? error)
        {
            // Null error means the sale lines were added.
            if (error != null)
                ToastAlert.Error(this, error);
            else
                ToastAlert.Success(this, "The information was added.");
        }

        /// <summary>Read one visible cell by column heading. Unused by the click handlers; kept for callers that need a single field.</summary>
        private string CellText(int rowIndex, string header)
        {
            DataGridViewColumn? col = dataGridView1.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(c => c.HeaderText.Equals(header, StringComparison.OrdinalIgnoreCase));
            // Hidden or renamed columns should not throw from a helper.
            if (col == null)
                return "";

            return dataGridView1.Rows[rowIndex].Cells[col.Index].Value?.ToString()?.Trim() ?? "";
        }
    }
}
