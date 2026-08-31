namespace CastRightCatchInvManagement
{
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
