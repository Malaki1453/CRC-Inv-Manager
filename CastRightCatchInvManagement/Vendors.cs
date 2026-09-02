namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Vendor lookup (name, company, phone, balance). Always reads the live database.
    /// Toolbar Add Vendor opens a blank record. Right-click for View Details or Edit Vendor.
    /// Double-click a row for that vendor’s purchase and bank history.
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
            UiStyle.BindRowEdit(dataGridView1, PartyEditForm.OpenVendorEdit, "Vendor", "Edit Vendor");
            dataGridView1.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex < 0)
                    return;
                CustomerHistoryForm.ShowVendor(
                    this,
                    DataFiles.GridRowToRecord(dataGridView1, e.RowIndex));
            };
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown. Reloads the vendor grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from the vendors table in the live database.</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Vendors);
    }
}
