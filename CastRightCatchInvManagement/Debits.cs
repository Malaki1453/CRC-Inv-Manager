namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Vendor debit claims. Completed (vendor-approved) rows move to Old Inventory on roll-over.
    /// </summary>
    public partial class Debits : Form, INavigationPage
    {
        public Debits()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(this, "Debits", lblTitle, btnUpload, dataGridView1);
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from debits (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Debits);
    }
}
