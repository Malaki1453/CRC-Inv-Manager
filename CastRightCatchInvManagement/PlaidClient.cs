using CrcInventory.Protocol;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Plaid from the desktop. Remote sessions call the inventory server so the secret
    /// and access tokens never sit on this PC. Local SQLite mode still talks to Plaid here.
    /// </summary>
    internal static class PlaidClient
    {
        public static bool IsConfigured =>
            AppState.PlaidConfigured ||
            (!string.IsNullOrWhiteSpace(AppState.PlaidClientId) &&
             !string.IsNullOrWhiteSpace(AppState.PlaidSecret));

        /// <summary>Create a Link token so the WebView can open Plaid Link.</summary>
        public static async Task<(bool Ok, string Value, string Error)> CreateLinkTokenAsync()
        {
            if (DataLink.IsRemote)
            {
                try
                {
                    var dto = await Task.Run(() => DataLink.Call<BankLinkTokenDto>(ServerOps.BankLinkToken))
                        .ConfigureAwait(true);
                    if (string.IsNullOrWhiteSpace(dto?.LinkToken))
                        return (false, "", "Plaid did not return a link token.");
                    return (true, dto.LinkToken, "");
                }
                catch (Exception ex)
                {
                    return (false, "", ex.Message);
                }
            }

            return await PlaidHttp.CreateLinkTokenAsync(
                    AppState.PlaidClientId,
                    AppState.PlaidSecret,
                    AppState.PlaidEnv,
                    string.IsNullOrWhiteSpace(AppState.BusinessName) ? "Cast Right Catch" : AppState.BusinessName,
                    string.IsNullOrWhiteSpace(AppState.CurrentUsername) ? "crc" : AppState.CurrentUsername)
                .ConfigureAwait(true);
        }

        /// <summary>Finish Connect on the host (remote) or in-process (local SQLite).</summary>
        public static async Task<BankConnectResult> CompleteConnectAsync(
            string publicToken,
            string institution,
            string accountId,
            string accountName,
            string accountMask)
        {
            var request = new BankConnectRequest
            {
                PublicToken = publicToken,
                Institution = institution,
                AccountId = accountId,
                AccountName = accountName,
                AccountMask = accountMask
            };
            if (DataLink.IsRemote)
            {
                try
                {
                    return await Task.Run(() => DataLink.Call<BankConnectResult>(ServerOps.BankConnect, request))
                        .ConfigureAwait(true)
                           ?? new BankConnectResult { Error = "The inventory server did not return a connect result." };
                }
                catch (Exception ex)
                {
                    return new BankConnectResult { Error = ex.Message };
                }
            }

            return await CompleteConnectLocalAsync(request).ConfigureAwait(true);
        }

        /// <summary>Sync every linked account. Remote sessions never see access tokens.</summary>
        public static async Task<BankSyncResult> SyncAllAsync()
        {
            if (DataLink.IsRemote)
            {
                try
                {
                    return await Task.Run(() => DataLink.Call<BankSyncResult>(ServerOps.BankSync))
                        .ConfigureAwait(true)
                           ?? new BankSyncResult { Error = "The inventory server did not return a sync result." };
                }
                catch (Exception ex)
                {
                    return new BankSyncResult { Error = ex.Message };
                }
            }

            return await SyncAllLocalAsync().ConfigureAwait(true);
        }

        /// <summary>Local SQLite: exchange, insert the account, import the first batch.</summary>
        private static async Task<BankConnectResult> CompleteConnectLocalAsync(BankConnectRequest request)
        {
            var exchange = await PlaidHttp.ExchangePublicTokenAsync(
                    AppState.PlaidClientId, AppState.PlaidSecret, AppState.PlaidEnv, request.PublicToken)
                .ConfigureAwait(true);
            if (!exchange.Ok)
                return new BankConnectResult { Error = exchange.Error };

            var accounts = await PlaidHttp.GetAccountsAsync(
                    AppState.PlaidClientId, AppState.PlaidSecret, AppState.PlaidEnv, exchange.AccessToken)
                .ConfigureAwait(true);
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
            long id = SqliteInventory.InsertBankAccount(name, request.Institution ?? "", mask, "Plaid live feed");
            SqliteInventory.SetBankLiveLink(id, exchange.AccessToken, exchange.ItemId, chosen.Id, "");

            var sync = await PlaidHttp.SyncTransactionsAsync(
                    AppState.PlaidClientId,
                    AppState.PlaidSecret,
                    AppState.PlaidEnv,
                    exchange.AccessToken,
                    chosen.Id,
                    "")
                .ConfigureAwait(true);
            if (!sync.Ok)
            {
                return new BankConnectResult
                {
                    Id = id,
                    Name = name,
                    Error = "The account was connected, but the first sync failed. " + sync.Error
                };
            }

            int added = BankFeed.Import(ToParsed(sync.Added), name, out _);
            SqliteInventory.SetBankLiveCursor(id, sync.Cursor);
            AppState.PlaidLastSync = DateTime.Now;
            AppLock.SaveSettings();
            return new BankConnectResult { Id = id, Name = name, Added = added };
        }

        /// <summary>Local SQLite: sync every stored live link.</summary>
        private static async Task<BankSyncResult> SyncAllLocalAsync()
        {
            if (!IsConfigured)
                return new BankSyncResult { Error = "An administrator must add Plaid API keys under Admin > Admin management, then Connect bank." };

            var connected = SqliteInventory.ListBankAccounts()
                .Select(a => (a.Id, a.Name, Link: SqliteInventory.GetBankLiveLink(a.Id)))
                .Where(a => a.Link.AccessToken.Length > 0)
                .ToList();
            if (connected.Count == 0)
                return new BankSyncResult { Error = "An administrator must connect the bank on the Admin page first." };

            int added = 0;
            foreach (var account in connected)
            {
                var result = await PlaidHttp.SyncTransactionsAsync(
                        AppState.PlaidClientId,
                        AppState.PlaidSecret,
                        AppState.PlaidEnv,
                        account.Link.AccessToken,
                        account.Link.AccountId,
                        account.Link.Cursor)
                    .ConfigureAwait(true);
                if (!result.Ok)
                    return new BankSyncResult { Added = added, Error = result.Error };

                added += BankFeed.Import(ToParsed(result.Added), account.Name, out _);
                SqliteInventory.SetBankLiveCursor(account.Id, result.Cursor);
            }

            AppState.PlaidLastSync = DateTime.Now;
            AppLock.SaveSettings();
            return new BankSyncResult { Added = added };
        }

        private static List<BankFeed.Parsed> ToParsed(IEnumerable<BankTxnDto> rows)
        {
            return rows.Select(row => new BankFeed.Parsed
            {
                Date = row.Date,
                Amount = row.Amount,
                Description = row.Description,
                Reference = row.Reference,
                Method = row.Method,
                ExternalId = row.ExternalId,
                Type = row.Type
            }).ToList();
        }
    }
}
