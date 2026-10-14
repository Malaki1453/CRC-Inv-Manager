namespace CrcInventory.Server;

/// <summary>Pulls the live bank feed on the host using Admin auto-sync hours.</summary>
internal static class BankSyncWatch
{
    /// <summary>Poll once a minute until <paramref name="cancel"/> is requested.</summary>
    public static async Task RunAsync(InventoryStore store, CancellationToken cancel)
    {
        while (!cancel.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(TimeSpan.FromMinutes(1), cancel).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            try
            {
                PlaidLive.SyncIfDue(store);
            }
            catch
            {
                // A Plaid or database error on the timer must not stop the host.
            }
        }
    }
}
