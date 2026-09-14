namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Vendor lookup (name, company, phone, balance). Always reads the live database.
    /// Toolbar Add Vendor opens a blank record. Double-click a row for View Details
    /// (identity and history). Right-click for Edit Vendor.
    /// </summary>
    public partial class Vendors : Form, INavigationPage
    {
        public Vendors()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(
                this,
                "Vendors",
                lblTitle,
                btnUpload,
                dataGridView1,
                "Add Vendor",
                (_, _) => PartyEditForm.OpenVendorNew());
            UiStyle.BindRowEdit(
                dataGridView1,
                PartyEditForm.OpenVendorEdit,
                "Vendor",
                "Edit Vendor");
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown. Reloads the vendor grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from the vendors table in the live database.</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Vendors);
    }
}
