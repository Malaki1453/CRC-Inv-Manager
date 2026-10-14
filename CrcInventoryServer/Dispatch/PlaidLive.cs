using CrcInventory.Protocol;

namespace CrcInventory.Server;

/// <summary>Host-side Plaid connect and sync. Secrets and access tokens stay in the store.</summary>
internal static class PlaidLive
{
    private static int _busy;

    /// <summary>Mint a Link token using stored keys.</summary>
    public static BankLinkTokenDto CreateLinkToken(InventoryStore store, string username)
    {
        var creds = RequireKeys(store);
        var result = PlaidHttp.CreateLinkTokenAsync(
                creds.ClientId,
                creds.Secret,
                creds.Env,
                creds.ClientName,
                username)
            .GetAwaiter()
            .GetResult();
        if (!result.Ok)
            throw new InvalidOperationException(result.Error);
        return new BankLinkTokenDto { LinkToken = result.Value };
    }

    /// <summary>Exchange a public token, store the live link, and import the first batch.</summary>
    public static BankConnectResult Connect(InventoryStore store, BankConnectRequest request)
    {
        var creds = RequireKeys(store);
        string publicToken = (request.PublicToken ?? "").Trim();
        if (publicToken.Length == 0)
            return new BankConnectResult { Error = "Plaid did not return a public token." };

        var exchange = PlaidHttp.ExchangePublicTokenAsync(
                creds.ClientId, creds.Secret, creds.Env, publicToken)
            .GetAwaiter()
            .GetResult();
        if (!exchange.Ok)
            return new BankConnectResult { Error = exchange.Error };

        var accounts = PlaidHttp.GetAccountsAsync(
                creds.ClientId, creds.Secret, creds.Env, exchange.AccessToken)
            .GetAwaiter()
            .GetResult();
        if (!accounts.Ok)
            return new BankConnectResult { Error = accounts.Error };

        var chosen = !string.IsNullOrWhiteSpace(request.AccountId)
            ? accounts.Accounts.FirstOrDefault(a => a.Id == request.AccountId)
            : accounts.Accounts.FirstOrDefault();
        if (chosen == null && accounts.Accounts.Count > 0)
            chosen = accounts.Accounts[0];
        if (chosen == null)
            return new BankConnectResult { Error = "No bank accounts were returned." };

        string name = chosen.Name.Length > 0 ? chosen.Name : (request.AccountName ?? "").Trim();
        if (name.Length == 0)
            name = string.IsNullOrWhiteSpace(request.Institution) ? "Plaid account" : request.Institution!.Trim();
        string mask = chosen.Mask.Length > 0 ? chosen.Mask : (request.AccountMask ?? "").Trim();
        long id = store.InsertBankAccount(name, request.Institution ?? "", mask, "Plaid live feed");
        store.SetBankLiveLink(id, exchange.AccessToken, exchange.ItemId, chosen.Id, "");

        var sync = PlaidHttp.SyncTransactionsAsync(
                creds.ClientId, creds.Secret, creds.Env, exchange.AccessToken, chosen.Id, "")
            .GetAwaiter()
            .GetResult();
        if (!sync.Ok)
        {
            return new BankConnectResult
            {
                Id = id,
                Name = name,
                Error = "The account was connected, but the first sync failed. " + sync.Error
            };
        }

        int added = store.ImportBankTxns(sync.Added, name);
        store.SetBankLiveCursor(id, sync.Cursor);
        NoteLastSync(store);
        return new BankConnectResult { Id = id, Name = name, Added = added };
    }

    /// <summary>Pull new transactions for every connected live account.</summary>
    public static BankSyncResult SyncAll(InventoryStore store)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0)
            return new BankSyncResult();

        try
        {
            var creds = store.PlaidCredentials();
            if (creds.ClientId.Length == 0 || creds.Secret.Length == 0)
                return new BankSyncResult { Error = "An administrator must add Plaid API keys under Admin > Admin management, then Connect bank." };

            var connected = store.ListLinkedBankAccounts();
            if (connected.Count == 0)
                return new BankSyncResult { Error = "An administrator must connect the bank on the Admin page first." };

            int added = 0;
            foreach (var account in connected)
            {
                var result = PlaidHttp.SyncTransactionsAsync(
                        creds.ClientId,
                        creds.Secret,
                        creds.Env,
                        account.Link.AccessToken,
                        account.Link.AccountId,
                        account.Link.Cursor)
                    .GetAwaiter()
                    .GetResult();
                if (!result.Ok)
                    return new BankSyncResult { Added = added, Error = result.Error };

                added += store.ImportBankTxns(result.Added, account.Name);
                store.SetBankLiveCursor(account.Id, result.Cursor);
            }

            NoteLastSync(store);
            return new BankSyncResult { Added = added };
        }
        finally
        {
            Interlocked.Exchange(ref _busy, 0);
        }
    }

    /// <summary>Quiet timer tick: skip when auto-sync is off or the interval has not elapsed.</summary>
    public static BankSyncResult SyncIfDue(InventoryStore store)
    {
        var settings = store.ReadSettings(revealSecrets: false);
        if (!int.TryParse(Setting(settings, "plaid_sync_hours"), out int hours) || hours <= 0)
            return new BankSyncResult();
        if (DateTime.TryParse(Setting(settings, "plaid_last_sync"), out var last) &&
            DateTime.Now - last < TimeSpan.FromHours(hours))
            return new BankSyncResult();

        var creds = store.PlaidCredentials();
        if (creds.ClientId.Length == 0 || creds.Secret.Length == 0)
            return new BankSyncResult();
        if (store.ListLinkedBankAccounts().Count == 0)
            return new BankSyncResult();

        return SyncAll(store);
    }

    private static (string ClientId, string Secret, string Env, string ClientName) RequireKeys(InventoryStore store)
    {
        var creds = store.PlaidCredentials();
        if (creds.ClientId.Length == 0 || creds.Secret.Length == 0)
            throw new InvalidOperationException(
                "An administrator must add Plaid API keys under Admin > Admin management, then Connect bank.");
        return creds;
    }

    private static void NoteLastSync(InventoryStore store)
    {
        store.WriteSettings(new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["plaid_last_sync"] = DateTime.Now.ToString("o")
        });
    }

    private static string Setting(Dictionary<string, string> map, string key) =>
        map.TryGetValue(key, out var value) ? (value ?? "").Trim() : "";
}
