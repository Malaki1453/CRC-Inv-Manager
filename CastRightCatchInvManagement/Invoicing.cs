namespace CastRightCatchInvManagement
{
    public partial class Invoicing : Form, INavigationPage
    {
        public Invoicing()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Invoicing, this);
            UiStyle.ApplyDataPage(this, "Invoices", lblTitle, btnUpload, dataGridView1);
            DataFiles.DataChanged += LoadTable;
            dataGridView1.CellDoubleClick += dataGridView1_CellDoubleClick;
            LoadTable();
        }

        public void HighlightCurrentPage() => LoadTable();

        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Invoices);

        private void dataGridView1_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            var record = DataFiles.GridRowToRecord(dataGridView1, e.RowIndex);
            string invoiceNumber = DataFiles.GetRecord(record, "Invoice #").Trim();
            if (invoiceNumber.Length == 0)
            {
                MessageBox.Show(
                    "This row has no invoice number.",
                    "Invoice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string? path = DataFiles.FindStoredPdf(DataFiles.PdfKindInvoice, invoiceNumber);
            if (path != null)
            {
                DataFiles.OpenPdf(path);
                return;
            }

            var ask = MessageBox.Show(
                $"Invoice {invoiceNumber} does not have a PDF yet.\n\nCreate one from the sales on this invoice?",
                "Create Invoice PDF",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (ask != DialogResult.Yes)
                return;

            var form = Navigator.Ensure<InvoicePdf>(AppPage.InvoicePdf);
            form.CreatePdfFromInvoice(record, error =>
            {
                if (IsDisposed)
                    return;
                if (error != null)
                {
                    Navigator.GoTo(AppPage.InvoicePdf);
                    ToastAlert.Error(this, error);
                    return;
                }

                ToastAlert.Success(this, $"Invoice {invoiceNumber} PDF was created.");
            });
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
