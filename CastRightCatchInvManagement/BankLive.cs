using CrcInventory.Protocol;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Plaid live feed. Only an administrator may connect or sync.
    /// Anyone with Banking access can view imported lines.
    /// Remote sessions run Plaid on the inventory server.
    /// </summary>
    internal static class BankLive
    {
        /// <summary>Open Plaid Link, save the access token on the host, and import the first batch.</summary>
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
                    "Enter Plaid Client ID and Secret under Admin > Admin management, save, then Connect bank.",
                    "Connect bank",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            await Task.Yield();
            var result = PlaidLinkForm.Run(owner);
            if (result == null)
                return;

            if (result.Error.Length > 0 && result.Id == 0)
                return;

            if (result.Error.Length > 0)
            {
                MessageBox.Show(
                    owner,
                    result.Error,
                    "Connect bank",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DataFiles.NotifyDataChanged();
                return;
            }

            DataFiles.NotifyDataChanged();
            MessageBox.Show(
                owner,
                $"Connected {result.Name}. {result.Added} transaction(s) imported.",
                "Connect bank",
                MessageBoxButtons.OK,
                MessageBoxIcon.Information);
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
            var result = await PlaidClient.SyncAllAsync();
            Cursor.Current = Cursors.Default;
            DataFiles.NotifyDataChanged();
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

        /// <summary>Sync without dialogs. Used by the local-mode timer; remote auto-sync runs on the host.</summary>
        public static async Task<(int Added, string Error)> SyncAllQuietAsync()
        {
            if (DataLink.IsRemote)
                return (0, "");

            var result = await PlaidClient.SyncAllAsync();
            return (result.Added, result.Error);
        }
    }
}
