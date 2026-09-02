namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Sales grid page. Each row is a sold product line from the sales table.
    /// Clicks on a data row:
    /// double-click → add every line on that customer PO to Create Invoice (and open it unless it is already open),
    /// Shift+click → add those lines without leaving Sales,
    /// middle-click → open the sales-order PDF if one exists, otherwise fill Create Sales Order.
    /// Right-click edit / details is wired by <see cref="UiStyle.BindRowEdit"/>.
    /// </summary>
    public partial class Sales : Form, INavigationPage
    {
        public Sales()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(
                this,
                "Sales",
                lblTitle,
                btnUpload,
                dataGridView1,
                "Add Product",
                (_, _) => AddSale.OpenNew());
            UiStyle.BindRowEdit(dataGridView1, AddSale.OpenEdit, "Sale");
            DataFiles.DataChanged += LoadTable;
            dataGridView1.CellDoubleClick += dataGridView1_CellDoubleClick;
            dataGridView1.CellMouseClick += dataGridView1_CellMouseClick;
            dataGridView1.CellMouseDown += dataGridView1_CellMouseDown;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from the sales table (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Sales);

        /// <summary>
        /// Double-click a sale: send that PO’s lines to Create Invoice.
        /// Shift+double-click is ignored here; Shift+click is handled in CellMouseClick.
        /// If Create Invoice is already open in another window, stay on Sales.
        /// </summary>
        private void dataGridView1_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;
            if ((ModifierKeys & Keys.Shift) == Keys.Shift)
                return;

            AddSaleToDocument(e.RowIndex, invoice: true, stayOnPage: Navigator.IsOpen(AppPage.InvoicePdf));
        }

        /// <summary>
        /// Shift+left-click a sale: add that PO’s lines to Create Invoice but keep this page visible.
        /// Plain left-click does nothing extra (selection / edit still work as usual).
        /// </summary>
        private void dataGridView1_CellMouseClick(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left || e.RowIndex < 0)
                return;
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
        private void AddSaleToDocument(int rowIndex, bool invoice, bool stayOnPage = false)
        {
            var record = DataFiles.GridRowToRecord(dataGridView1, rowIndex);
            string po = DataFiles.SalePo(record);
            string so = DataFiles.GetRecord(record, "SO #");
            if (po.Length == 0 && so.Length == 0)
                return;

            // Middle-click shortcut: if this sale already has a sales-order PDF, just open it.
            if (!invoice && so.Length > 0)
            {
                string? existing = DataFiles.FindStoredSalesOrder(so);
                if (existing != null)
                {
                    DataFiles.OpenPdf(existing);
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

            if (invoice)
            {
                var form = Navigator.Ensure<InvoicePdf>(AppPage.InvoicePdf);
                form.TryAddSale(prefill, error =>
                {
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
            if (IsDisposed)
                return;
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
            if (col == null)
                return "";

            return dataGridView1.Rows[rowIndex].Cells[col.Index].Value?.ToString()?.Trim() ?? "";
        }
    }
}
