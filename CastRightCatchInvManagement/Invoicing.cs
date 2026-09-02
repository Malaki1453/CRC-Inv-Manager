namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Invoice list (SO #, customer, ship date, due date, status, paid).
    /// Double-click a row to open that invoice PDF. If none is stored, offer to build one from the sales on that invoice.
    /// </summary>
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

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from invoices (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Invoices);

        /// <summary>
        /// Double-click: open the stored PDF for this invoice number.
        /// If there is no PDF, ask whether to create one from matching sales and then open Create Invoice.
        /// </summary>
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
    }
}
