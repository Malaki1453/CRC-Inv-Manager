namespace CastRightCatchInvManagement
{
    /// <summary>Item-code lookup used on purchase and sales forms. Always reads the live database.</summary>
    public partial class ItemCodes : Form, INavigationPage
    {
        public ItemCodes()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(this, "Inventory", lblTitle, btnUpload, dataGridView1);
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown. Reloads the item-code grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the inventory (item codes) grid from the live database.</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.ItemCodes);
    }
}
