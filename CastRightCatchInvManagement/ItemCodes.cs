namespace CastRightCatchInvManagement
{
    /// <summary>Item-code lookup used on purchase and sales forms. Always reads the live database.</summary>
    public partial class ItemCodes : Form, INavigationPage
    {
        public ItemCodes()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(this, "Item Codes", lblTitle, btnUpload, dataGridView1);
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown. Reloads the item-code grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from the item_codes table in the live database.</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.ItemCodes);
    }
}
