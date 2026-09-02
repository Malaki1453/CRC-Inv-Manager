namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Customer lookup (name, company, phone, balance). Always reads the live database.
    /// Toolbar Add Customer opens a blank record. Right-click for View Details or Edit Customer.
    /// Double-click a row for that customer’s sales and bank history.
    /// </summary>
    public partial class Customers : Form, INavigationPage
    {
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
            UiStyle.BindRowEdit(dataGridView1, PartyEditForm.OpenCustomerEdit, "Customer", "Edit Customer");
            dataGridView1.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex < 0)
                    return;
                CustomerHistoryForm.ShowFor(
                    this,
                    DataFiles.GridRowToRecord(dataGridView1, e.RowIndex));
            };
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown. Reloads the customer grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from the customers table in the live database.</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Customers);
    }
}
