namespace CastRightCatchInvManagement
{
    public partial class Customers : Form, INavigationPage
    {
        public Customers()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(this, "Customers", lblTitle, btnUpload, dataGridView1);
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        public void HighlightCurrentPage() => LoadTable();

        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Customers);

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
