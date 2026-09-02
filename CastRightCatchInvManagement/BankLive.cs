namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Plaid live feed. Only an administrator may connect or sync.
    /// Anyone with Banking access can view imported lines.
    /// </summary>
    internal static class BankLive
    {
        private static int _busy;
        /// <summary>Open Plaid Link, save the access token, and import the first batch of transactions.</summary>
        public static async Task ConnectAsync(IWin32Window owner)
        {
            if (!AppState.IsAdmin)
            {
                MessageBox.Show(
                    owner,
                    "Only an administrator can log into the bank.",
                    "Connect bank",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (!PlaidClient.IsConfigured)
            {
                MessageBox.Show(
                    owner,
                    "Enter Plaid Client ID and Secret on this page, then save, before connecting.",
                    "Connect bank",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            Cursor.Current = Cursors.WaitCursor;
            var link = await PlaidClient.CreateLinkTokenAsync();
            Cursor.Current = Cursors.Default;
            if (!link.Ok)
            {
                MessageBox.Show(owner, link.Error, "Connect bank", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            using var host = new PlaidLinkForm(link.Value);
            if (host.ShowDialog(owner) != DialogResult.OK || string.IsNullOrWhiteSpace(host.PublicToken))
                return;

            Cursor.Current = Cursors.WaitCursor;
            var exchange = await PlaidClient.ExchangePublicTokenAsync(host.PublicToken);
            if (!exchange.Ok)
            {
                Cursor.Current = Cursors.Default;
                MessageBox.Show(owner, exchange.Error, "Connect bank", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var accounts = await PlaidClient.GetAccountsAsync(exchange.AccessToken);
            Cursor.Current = Cursors.Default;
            if (!accounts.Ok)
            {
                MessageBox.Show(owner, accounts.Error, "Connect bank", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            var chosen = host.AccountId.Length > 0
                ? accounts.Accounts.FirstOrDefault(a => a.Id == host.AccountId)
                : accounts.Accounts.FirstOrDefault();
            if (chosen == null && accounts.Accounts.Count > 0)
                chosen = accounts.Accounts[0];
            if (chosen == null)
            {
                MessageBox.Show(
                    owner,
                    "No bank accounts were returned.",
                    "Connect bank",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            string name = chosen.Name.Length > 0 ? chosen.Name : host.AccountName;
            if (name.Length == 0)
                name = host.InstitutionName.Length > 0 ? host.InstitutionName : "Plaid account";
            long id = SqliteInventory.InsertBankAccount(
                name,
                host.InstitutionName,
                chosen.Mask.Length > 0 ? chosen.Mask : host.AccountMask,
                "Plaid live feed");
            SqliteInventory.SetBankLiveLink(id, exchange.AccessToken, exchange.ItemId, chosen.Id, "");
            await SyncOneAsync(owner, id, name, firstConnect: true);
        }

        /// <summary>Pull new transactions for every connected live account. Does not open bank login.</summary>
        public static async Task SyncAllAsync(IWin32Window owner)
        {
            if (!AppState.IsAdmin)
            {
                MessageBox.Show(
                    owner,
                    "Only an administrator can sync the live bank feed.",
                    "Sync live feed",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            Cursor.Current = Cursors.WaitCursor;
            var result = await SyncAllQuietAsync();
            Cursor.Current = Cursors.Default;
            if (result.Error.Length > 0)
            {
                MessageBox.Show(owner, result.Error, "Sync live feed", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            MessageBox.Show(
                owner,
                $"{result.Added} new transaction(s) from the live feed.",
                "Sync live feed",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }

        /// <summary>Sync without dialogs. Used by the hourly/3-hour timer.</summary>
        public static async Task<(int Added, string Error)> SyncAllQuietAsync()
        {
            if (System.Threading.Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
                return (0, "");

            try
            {
                if (!PlaidClient.IsConfigured)
                    return (0, "An administrator must connect the bank on the Admin page first.");

                var connected = SqliteInventory.ListBankAccounts()
                    .Select(a => (a.Id, a.Name, Link: SqliteInventory.GetBankLiveLink(a.Id)))
                    .Where(a => a.Link.AccessToken.Length > 0)
                    .ToList();
                if (connected.Count == 0)
                    return (0, "An administrator must connect the bank on the Admin page first.");

                int added = 0;
                foreach (var account in connected)
                {
                    var result = await PlaidClient.SyncTransactionsAsync(
                        account.Link.AccessToken,
                        account.Link.AccountId,
                        account.Link.Cursor);
                    if (!result.Ok)
                        return (added, result.Error);

                    added += BankFeed.Import(result.Added, account.Name, out _);
                    SqliteInventory.SetBankLiveCursor(account.Id, result.Cursor);
                }

                AppState.PlaidLastSync = DateTime.Now;
                AppLock.SaveSettings();
                return (added, "");
            }
            finally
            {
                System.Threading.Interlocked.Exchange(ref _busy, 0);
            }
        }

        private static async Task SyncOneAsync(IWin32Window owner, long id, string name, bool firstConnect)
        {
            var link = SqliteInventory.GetBankLiveLink(id);
            Cursor.Current = Cursors.WaitCursor;
            var result = await PlaidClient.SyncTransactionsAsync(
                link.AccessToken, link.AccountId, link.Cursor);
            Cursor.Current = Cursors.Default;
            if (!result.Ok)
            {
                MessageBox.Show(
                    owner,
                    firstConnect
                        ? "The account was connected, but the first sync failed.\n\n" + result.Error
                        : result.Error,
                    firstConnect ? "Connect bank" : "Sync bank",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            int added = BankFeed.Import(result.Added, name, out _);
            SqliteInventory.SetBankLiveCursor(id, result.Cursor);
            AppState.PlaidLastSync = DateTime.Now;
            AppLock.SaveSettings();
            MessageBox.Show(
                owner,
                firstConnect
                    ? $"Connected {name}. {added} transaction(s) imported."
                    : $"{added} new transaction(s) from {name}.",
                firstConnect ? "Connect bank" : "Sync bank",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
        }
    }
}
