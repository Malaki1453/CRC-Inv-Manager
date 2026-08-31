namespace CastRightCatchInvManagement
{
    public partial class Invoicing : Form, INavigationPage
    {
        public Invoicing()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Invoicing, this);
            UiStyle.ApplyDataPage(this, "Invoices", lblTitle, btnUpload, dataGridView1);
            DataFiles.DataChanged += LoadTable;
            dataGridView1.CellDoubleClick += dataGridView1_CellDoubleClick;
            LoadTable();
        }

        public void HighlightCurrentPage() => LoadTable();

        private void LoadTable() => DataFiles.FillGrid(dataGridView1, DataFiles.Invoices);

        private void dataGridView1_CellDoubleClick(object? sender, DataGridViewCellEventArgs e)
        {
            if (e.RowIndex < 0)
                return;

            DataGridViewColumn? col = dataGridView1.Columns
                .Cast<DataGridViewColumn>()
                .FirstOrDefault(c => c.HeaderText.Equals("Invoice #", StringComparison.OrdinalIgnoreCase));

            string? invoiceNumber = col == null
                ? dataGridView1.Rows[e.RowIndex].Cells[0].Value?.ToString()
                : dataGridView1.Rows[e.RowIndex].Cells[col.Index].Value?.ToString();

            DataFiles.OpenStoredInvoice(invoiceNumber);
        }

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
