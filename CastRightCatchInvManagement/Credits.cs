namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Customer credit claims. Completed (approved) rows move to Old Inventory on roll-over.
    /// </summary>
    public partial class Credits : Form, INavigationPage
    {
        public Credits()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(this, "Credits", lblTitle, btnUpload, dataGridView1);
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from credits (live only, or archive + live when Old is on).</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Credits);
    }
}
