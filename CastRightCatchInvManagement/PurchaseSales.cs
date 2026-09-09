namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Purchases grid. Each row is a purchase line (PO, vendor, item, costs, dates).
    /// Toolbar New Purchase opens a blank purchase form. Double-click a row for View Details.
    /// Right-click Edit Product or Create Invoice (vendor invoice we received).
    /// </summary>
    public partial class PurchaseSales : Form, INavigationPage
    {
        public PurchaseSales()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(
                this,
                "Purchases",
                lblTitle,
                btnUpload,
                dataGridView1,
                "New Purchase",
                (_, _) => AddPurchase.OpenNew());
            UiStyle.BindRowEdit(
                dataGridView1,
                AddPurchase.OpenEdit,
                "Purchase",
                "Edit Product",
                ("Create Invoice", CreateInvoiceFromPurchase));
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from purchases (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.PurchaseSales);

        /// <summary>
        /// Right-click Create Invoice: vendor is the issuer, we are the receiving company.
        /// Matching PO lines fill Create Invoice.
        /// </summary>
        private void CreateInvoiceFromPurchase(Dictionary<string, string> record)
        {
            if (!DataAccess.CanMutate(DataFiles.Invoices))
            {
                MessageBox.Show(
                    "This account can only view invoices.",
                    "Create Invoice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string po = DataFiles.GetRecord(record, "PO #").Trim();
            if (po.Length == 0)
            {
                MessageBox.Show(
                    "This purchase has no PO number.",
                    "Create Invoice",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string ship = DataFiles.GetRecordAny(record, "Arrival Date", "Ship Date");
            var prefill = new InvoicePurchasePrefill
            {
                Po = po,
                ItemCode = DataFiles.GetRecord(record, "Item Code"),
                VendorCode = DataFiles.GetRecord(record, "Vendor Code"),
                VendorName = DataFiles.GetRecord(record, "Vendor"),
                VendorTerms = DataFiles.GetRecord(record, "Vendor Terms"),
                ShipDate = DateTime.TryParse(ship, out var dated) ? dated : null
            };

            var form = Navigator.Ensure<InvoicePdf>(AppPage.InvoicePdf);
            form.TryAddPurchase(prefill, error =>
            {
                if (IsDisposed)
                    return;
                if (error != null)
                {
                    ToastAlert.Error(this, error);
                    return;
                }

                Navigator.GoTo(AppPage.InvoicePdf);
            });
        }
    }
}
