namespace CastRightCatchInvManagement
{
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
            UiStyle.BindRowEdit(dataGridView1, AddPurchase.OpenEdit);
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        public void HighlightCurrentPage() => LoadTable();

        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.PurchaseSales);

        private void btnUpload_Click(object sender, EventArgs e)
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Select a CSV file to import",
                Filter = "CSV files (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            if (DataFiles.TryImportCsv(dialog.FileName, out string message))
            {
                MessageBox.Show(message, "Import Complete", MessageBoxButtons.OK, MessageBoxIcon.Information);
                LoadTable();
            }
            else
            {
                MessageBox.Show(message, "Import Failed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }
    }
}
