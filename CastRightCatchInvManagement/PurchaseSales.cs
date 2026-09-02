namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Purchases grid. Each row is a purchase line (PO, vendor, item, costs, dates).
    /// Toolbar Add Product opens a blank purchase form. Right-click a row for View Details or Edit Product.
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
                "Add Product",
                (_, _) => AddPurchase.OpenNew());
            UiStyle.BindRowEdit(dataGridView1, AddPurchase.OpenEdit, "Purchase");
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from purchases (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.PurchaseSales);
    }
}
