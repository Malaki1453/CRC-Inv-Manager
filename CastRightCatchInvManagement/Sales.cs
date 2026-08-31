namespace CastRightCatchInvManagement
{
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
            UiStyle.BindRowEdit(dataGridView1, AddSale.OpenEdit);
            DataFiles.DataChanged += LoadTable;
            dataGridView1.CellDoubleClick += dataGridView1_CellDoubleClick;
            dataGridView1.CellMouseDown += dataGridView1_CellMouseDown;
            LoadTable();
        }

        public void HighlightCurrentPage() => LoadTable();

        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Sales);

        private void dataGridView1_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            AddSaleToDocument(e.RowIndex, invoice: true);
        }

        private void dataGridView1_CellMouseDown(object? sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.Button != MouseButtons.Middle || e.RowIndex < 0)
                return;

            AddSaleToDocument(e.RowIndex, invoice: false);
        }

        private void AddSaleToDocument(int rowIndex, bool invoice)
        {
            var record = DataFiles.GridRowToRecord(dataGridView1, rowIndex);
            string po = DataFiles.SalePo(record);
            string so = DataFiles.GetRecord(record, "SO #");
            if (po.Length == 0 && so.Length == 0)
                return;

            if (!invoice && so.Length > 0)
            {
                DataFiles.OpenStoredSalesOrder(so);
                return;
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
                form.TryAddSale(prefill, ShowAddResultSafe);
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

        private void ShowAddResult(string? error)
        {
            if (error != null)
                ToastAlert.Error(this, error);
            else
                ToastAlert.Success(this, "The information was added.");
        }

        private string CellText(int rowIndex, string header)
        {
            DataGridViewColumn? col = dataGridView1.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(c => c.HeaderText.Equals(header, StringComparison.OrdinalIgnoreCase));
            if (col == null)
                return "";

            return dataGridView1.Rows[rowIndex].Cells[col.Index].Value?.ToString()?.Trim() ?? "";
        }

        private void btnUpload_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select a CSV file to import",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            if (DataFiles.TryImportCsv(dialog.FileName, out string message))
            {
                MessageBox.Show(message, "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadTable();
            }
            else
            {
                MessageBox.Show(message, "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
