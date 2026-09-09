namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Invoice list (SO #, customer, ship date, due date, status, paid, PDF Created).
    /// Double-click a row for View Details. Right-click Open PDF.
    /// </summary>
    public partial class Invoicing : Form, INavigationPage
    {
        public Invoicing()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Invoicing, this);
            UiStyle.ApplyDataPage(this, "Invoices", lblTitle, btnUpload, dataGridView1);
            UiStyle.BindRowEdit(
                dataGridView1,
                onEdit: null,
                "Invoice",
                "Edit",
                ("Open PDF", OpenInvoicePdf));
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from invoices (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Invoices);

        /// <summary>
        /// Right-click Open PDF: open the stored file for this invoice number.
        /// If there is no PDF, ask whether to create one from matching sales and then open Create Invoice.
        /// </summary>
        private void OpenInvoicePdf(Dictionary<string, string> record)
        {
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

            bool created = DataFiles.InvoicePdfWasCreated(record);
            var ask = MessageBox.Show(
                created
                    ? $"Invoice {invoiceNumber} was created before, but the PDF file was not found in Stored Invoices.\n\nCreate it again from what was stored on this invoice?"
                    : $"Invoice {invoiceNumber} does not have a PDF yet.\n\nCreate one from what was stored on this invoice?",
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
