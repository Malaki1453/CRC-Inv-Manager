namespace CastRightCatchInvManagement
{
    public partial class Vendors : Form, INavigationPage
    {
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
                (_, _) => PartyEditForm.OpenVendorNew());
            UiStyle.BindRowEdit(dataGridView1, PartyEditForm.OpenVendorEdit, "Vendor", "Edit Vendor");
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        public void HighlightCurrentPage() => LoadTable();

        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Vendors);

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
