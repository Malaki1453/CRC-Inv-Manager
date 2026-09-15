namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Invoice list. Double-click a row for View Details. Right-click Show PDF.
    /// </summary>
    public partial class Invoicing : Form, INavigationPage
    {
        /// <summary>Wire the invoices grid, Import Invoice PDF, and Show PDF.</summary>
        public Invoicing()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Invoicing, this);
            UiStyle.ApplyDataPage(this, "Invoices", lblTitle, btnUpload, dataGridView1);
            UiStyle.AddDataPageAction(this, "Import Invoice PDF", (_, _) => InvoicePdf.OpenImport());
            UiStyle.BindRowEdit(
                dataGridView1,
                onEdit: null,
                "Invoice",
                "Edit",
                ("Show PDF", ShowInvoicePdf));
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from invoices (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Invoices);

        /// <summary>Open the stored invoice PDF, or rebuild it from the invoice row if it is missing.</summary>
        private void ShowInvoicePdf(Dictionary<string, string> record)
        {
            string invoiceNumber = DataFiles.GetRecord(record, "Invoice #").Trim();
            DataFiles.ShowPdf(
                DataFiles.PdfKindInvoice,
                invoiceNumber,
                "invoice " + invoiceNumber,
                () =>
                {
                    var form = Navigator.Ensure<InvoicePdf>(AppPage.InvoicePdf);
                    form.CreatePdfFromInvoice(record, error =>
                    {
                        // This page may have closed before the rebuild finished.
                        if (IsDisposed)
                            return;
                        // Missing lines/customer: send the user to Create Invoice to finish it.
                        if (error != null)
                        {
                            Navigator.GoTo(AppPage.InvoicePdf);
                            ToastAlert.Error(this, error);
                            return;
                        }

                        ToastAlert.Success(this, "Invoice " + invoiceNumber + " PDF was created.");
                    });
                });
        }
    }
}
