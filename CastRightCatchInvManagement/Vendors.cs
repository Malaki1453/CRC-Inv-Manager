namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Vendor lookup (name, company, phone, balance). Always reads the live database.
    /// Toolbar Add Vendor opens a blank record. Double-click a row for View Details
    /// (identity and history). Right-click for Edit Vendor.
    /// </summary>
    public partial class Vendors : Form, INavigationPage
    {
        /// <summary>Build the Vendors grid with add/edit actions and load live rows.</summary>
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
                (_, _) => AddVendor.OpenNew());
            UiStyle.AddDataPageAction(this, "Import", (_, _) => ImportVendors());
            UiStyle.AddIdleSearchAction(this, "Import list", (_, _) => ImportVendors());
            UiStyle.BindRowEdit(
                dataGridView1,
                AddVendor.OpenEdit,
                "Vendor",
                "Edit Vendor");
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Load a vendor CSV into the live table. Duplicate codes are skipped.</summary>
        private void ImportVendors()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Import vendors",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };
            string testData = Path.GetFullPath(Path.Combine(
                AppContext.BaseDirectory, "..", "..", "..", "..", "test data"));
            if (Directory.Exists(testData))
                dialog.InitialDirectory = testData;

            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            if (DataFiles.TryImportCsv(dialog.FileName, out string message))
                ToastAlert.Success(this, message);
            else
                ToastAlert.Error(this, message);
        }

        /// <summary>Called when this page is shown. Reloads the vendor grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from the vendors table in the live database.</summary>
        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Vendors);
    }
}
