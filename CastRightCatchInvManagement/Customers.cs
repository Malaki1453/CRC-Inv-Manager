namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Customer lookup (name, company, phone, balance). Always reads the live database.
    /// Toolbar Add Customer opens a blank record. Double-click a row for View Details
    /// (identity and history). Right-click for Edit Customer.
    /// </summary>
    public partial class Customers : Form, INavigationPage
    {
        /// <summary>Build the Customers grid with add/edit actions and load live rows.</summary>
        public Customers()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(
                this,
                "Customers",
                lblTitle,
                btnUpload,
                dataGridView1,
                "Add Customer",
                (_, _) => PartyEditForm.OpenCustomerNew());
            UiStyle.BindRowEdit(
                dataGridView1,
                PartyEditForm.OpenCustomerEdit,
                "Customer",
                "Edit Customer");
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown. Reloads the customer grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from the customers table in the live database.</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Customers);
    }
}
