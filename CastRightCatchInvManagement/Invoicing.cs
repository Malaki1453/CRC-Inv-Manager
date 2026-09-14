namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Invoice list. Double-click a row for View Details. Right-click Show PDF.
    /// </summary>
    public partial class Invoicing : Form, INavigationPage
    {
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

        public void HighlightCurrentPage() => LoadTable();

        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Invoices);

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
                        if (IsDisposed)
                            return;
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
