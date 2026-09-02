namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Bank transactions (date, amount, method, invoice/SO reference).
    /// Completed rows move to Old Inventory on roll-over.
    /// </summary>
    public partial class Banking : Form, INavigationPage
    {
        public Banking()
        {
            InitializeComponent();
            UiStyle.ApplyDataPage(this, "Banking", lblTitle, btnUpload, dataGridView1);
            UiStyle.AddDataPageAction(this, "Accounts", (_, _) => BankAccountsForm.ShowList(this));
            UiStyle.AddDataPageAction(this, "Read file", (_, _) => ReadBankFile());
            if (AppState.IsAdmin)
                UiStyle.AddDataPageAction(this, "Sync live feed", async (_, _) => await BankLive.SyncAllAsync(this), gold: true);
            DataFiles.DataChanged += LoadTable;
            BankFeed.EnsureSchema();
            LoadTable();
        }

        /// <summary>Called when this page is shown or the Current/Old view changes. Reloads the grid.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Fill the grid from bank_transactions (live only, or archive + live when Old is on).</summary>
        private void LoadTable()
        {
            BankFeed.EnsureSchema();
            DataFiles.FillGrid(dataGridView1, DataFiles.BankTransactions);
        }

        /// <summary>
        /// Import an OFX, QFX, or bank CSV into a chosen account.
        /// Duplicates are skipped. Invoice # / SO # / customer are filled when the memo matches.
        /// </summary>
        private void ReadBankFile()
        {
            var account = BankAccountsForm.PickAccount(this);
            if (account == null)
                return;

            using var dialog = new OpenFileDialog
            {
                Title = "Select a bank statement file",
                Filter =
                    "Bank files (*.ofx;*.qfx;*.csv)|*.ofx;*.qfx;*.csv|OFX/QFX (*.ofx;*.qfx)|*.ofx;*.qfx|CSV (*.csv)|*.csv|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            if (!BankFeed.TryParseFile(dialog.FileName, out var rows, out string error))
            {
                MessageBox.Show(error, "Read file", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                $"Found {rows.Count} transaction(s) for {account.Value.Name}.\n\nImport new lines? Duplicates already in Banking are skipped.",
                "Read file",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            int added = BankFeed.Import(rows, account.Value.Name, out int skipped);
            MessageBox.Show(
                $"{added} new transaction(s) imported. {skipped} already on file.",
                "Read file",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
            LoadTable();
        }
    }
}
