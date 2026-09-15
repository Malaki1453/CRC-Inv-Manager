using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Calls Plaid for link tokens, token exchange, and transaction sync.
    /// Keys come from Admin live-bank-feed settings.
    /// </summary>
    internal static class PlaidClient
    {
        private static readonly HttpClient Http = new()
        {
            Timeout = TimeSpan.FromSeconds(45)
        };

        public static bool IsConfigured =>
            !string.IsNullOrWhiteSpace(AppState.PlaidClientId) &&
            !string.IsNullOrWhiteSpace(AppState.PlaidSecret);

        /// <summary>Plaid API base URL for sandbox, development, or production.</summary>
        public static string Host
        {
            get
            {
                string env = (AppState.PlaidEnv ?? "sandbox").Trim().ToLowerInvariant();
                return env switch
                {
                    // Live customer banks after Plaid approves the app.
                    "production" => "https://production.plaid.com",
                    // Limited live testing before production approval.
                    "development" => "https://development.plaid.com",
                    // Default: test logins (user_good / pass_good) so keys can be verified safely.
                    _ => "https://sandbox.plaid.com"
                };
            }
        }

        /// <summary>Create a Link token so the WebView can open Plaid Link.</summary>
        public static async Task<(bool Ok, string Value, string Error)> CreateLinkTokenAsync()
        {
            var body = new Dictionary<string, object?>
            {
                ["client_id"] = AppState.PlaidClientId,
                ["secret"] = AppState.PlaidSecret,
                ["client_name"] = string.IsNullOrWhiteSpace(AppState.BusinessName)
                    ? "Cast Right Catch"
                    : AppState.BusinessName,
                ["language"] = "en",
                ["country_codes"] = new[] { "US" },
                ["user"] = new Dictionary<string, string>
                {
                    ["client_user_id"] = string.IsNullOrWhiteSpace(AppState.CurrentUsername)
                        ? "crc"
                        : AppState.CurrentUsername
                },
                ["products"] = new[] { "transactions" }
            };

            var json = await PostAsync("/link/token/create", body).ConfigureAwait(false);
            // Network or API error — do not open Link with an empty token.
            if (!json.Ok)
                return (false, "", json.Error);
            // Success body should include link_token for Plaid.create().
            if (json.Doc.RootElement.TryGetProperty("link_token", out var token))
                return (true, token.GetString() ?? "", "");
            return (false, "", "Plaid did not return a link token.");
        }

        /// <summary>Swap the public_token from Link for a long-lived access_token and item_id.</summary>
        public static async Task<(bool Ok, string AccessToken, string ItemId, string Error)> ExchangePublicTokenAsync(
            string publicToken)
        {
            var body = new Dictionary<string, object?>
            {
                ["client_id"] = AppState.PlaidClientId,
                ["secret"] = AppState.PlaidSecret,
                ["public_token"] = publicToken
            };
            var json = await PostAsync("/item/public_token/exchange", body).ConfigureAwait(false);
            // Invalid public_token or keys — do not store a blank live link.
            if (!json.Ok)
                return (false, "", "", json.Error);
            string access = json.Doc.RootElement.TryGetProperty("access_token", out var a)
                ? a.GetString() ?? "" : "";
            string item = json.Doc.RootElement.TryGetProperty("item_id", out var i)
                ? i.GetString() ?? "" : "";
            // Exchange succeeded but the payload is unusable without an access_token.
            if (access.Length == 0)
                return (false, "", "", "Plaid did not return an access token.");
            return (true, access, item, "");
        }

        /// <summary>List bank accounts on the connected Item so we can pick one to sync.</summary>
        public static async Task<(bool Ok, List<PlaidAccount> Accounts, string Error)> GetAccountsAsync(
            string accessToken)
        {
            var body = new Dictionary<string, object?>
            {
                ["client_id"] = AppState.PlaidClientId,
                ["secret"] = AppState.PlaidSecret,
                ["access_token"] = accessToken
            };
            var json = await PostAsync("/accounts/get", body).ConfigureAwait(false);
            var list = new List<PlaidAccount>();
            // Auth/item errors mean we cannot pick an account.
            if (!json.Ok)
                return (false, list, json.Error);
            // Empty account list is still a successful call — the caller shows a message.
            if (!json.Doc.RootElement.TryGetProperty("accounts", out var accounts))
                return (true, list, "");
            foreach (var item in accounts.EnumerateArray())
            {
                list.Add(new PlaidAccount(
                    item.TryGetProperty("account_id", out var id) ? id.GetString() ?? "" : "",
                    item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                    item.TryGetProperty("mask", out var mask) ? mask.GetString() ?? "" : "",
                    item.TryGetProperty("subtype", out var sub) ? sub.GetString() ?? "" : ""));
            }

            return (true, list, "");
        }

        /// <summary>Pull new transactions since the saved cursor, paging until has_more is false.</summary>
        public static async Task<(bool Ok, List<BankFeed.Parsed> Added, string Cursor, string Error)> SyncTransactionsAsync(
            string accessToken,
            string accountId,
            string cursor)
        {
            var added = new List<BankFeed.Parsed>();
            string next = cursor ?? "";
            bool more = true;
            while (more)
            {
                var body = new Dictionary<string, object?>
                {
                    ["client_id"] = AppState.PlaidClientId,
                    ["secret"] = AppState.PlaidSecret,
                    ["access_token"] = accessToken,
                    ["cursor"] = next,
                    ["count"] = 100
                };
                var json = await PostAsync("/transactions/sync", body).ConfigureAwait(false);
                // Keep the last good cursor so a later retry does not skip already-imported pages.
                if (!json.Ok)
                    return (false, added, next, json.Error);

                var root = json.Doc.RootElement;
                // Only newly posted transactions; modified/removed are ignored for Banking.
                if (root.TryGetProperty("added", out var batch))
                {
                    foreach (var txn in batch.EnumerateArray())
                        TryAdd(txn, accountId, added);
                }

                next = root.TryGetProperty("next_cursor", out var c) ? c.GetString() ?? next : next;
                more = root.TryGetProperty("has_more", out var h) && h.ValueKind == JsonValueKind.True;
            }

            return (true, added, next, "");
        }

        /// <summary>Convert one Plaid transaction into a bank row for the chosen account.</summary>
        private static void TryAdd(JsonElement txn, string accountId, List<BankFeed.Parsed> added)
        {
            // Multi-account Items can return other accounts we did not connect.
            if (accountId.Length > 0 &&
                txn.TryGetProperty("account_id", out var aid) &&
                !string.Equals(aid.GetString(), accountId, StringComparison.Ordinal))
                return;

            // Banking rows require a posted date.
            if (!txn.TryGetProperty("date", out var dateEl) ||
                !DateTime.TryParse(dateEl.GetString(), out var date))
                return;
            // Skip lines Plaid sent without a money amount.
            if (!txn.TryGetProperty("amount", out var amtEl) ||
                !amtEl.TryGetDecimal(out decimal plaidAmount))
                return;

            // Plaid: positive amount leaves the account. We store deposits as positive.
            decimal amount = -plaidAmount;
            string name = txn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
            string merchant = txn.TryGetProperty("merchant_name", out var m) ? m.GetString() ?? "" : "";
            string id = txn.TryGetProperty("transaction_id", out var tid) ? tid.GetString() ?? "" : "";
            string check = txn.TryGetProperty("check_number", out var cn) ? cn.GetString() ?? "" : "";
            string description = merchant.Length > 0 && !merchant.Equals(name, StringComparison.OrdinalIgnoreCase)
                ? merchant + " · " + name
                : name;
            added.Add(new BankFeed.Parsed
            {
                Date = date.Date,
                Amount = amount,
                Description = description,
                Reference = check,
                Method = "Plaid",
                ExternalId = id,
                Type = amount >= 0 ? "Deposit" : "Withdrawal"
            });
        }

        /// <summary>POST JSON to Plaid and return the body, or a user-facing error.</summary>
        private static async Task<(bool Ok, JsonDocument Doc, string Error)> PostAsync(
            string path,
            Dictionary<string, object?> body)
        {
            try
            {
                string payload = JsonSerializer.Serialize(body);
                using var content = new StringContent(payload, Encoding.UTF8, "application/json");
                using var response = await Http.PostAsync(Host + path, content).ConfigureAwait(false);
                string text = await response.Content.ReadAsStringAsync().ConfigureAwait(false);
                var doc = JsonDocument.Parse(string.IsNullOrWhiteSpace(text) ? "{}" : text);
                // Plaid returns error_message on 4xx/5xx; surface that instead of a status code.
                if (!response.IsSuccessStatusCode)
                {
                    string error = "Plaid request failed.";
                    // Prefer Plaid's error_message so Connect/Sync shows a useful reason.
                    if (doc.RootElement.TryGetProperty("error_message", out var msg))
                        error = msg.GetString() ?? error;
                    return (false, doc, error);
                }

                return (true, doc, "");
            }
            // Timeouts and DNS failures should not crash Connect/Sync.
            catch (Exception ex)
            {
                // Timeouts and DNS failures should not crash Connect/Sync.
                return (false, JsonDocument.Parse("{}"), ex.Message);
            }
        }
    }

    /// <summary>One account on a Plaid Item (id, name, last-4, subtype).</summary>
    internal sealed record PlaidAccount(string Id, string Name, string Mask, string Subtype);
}
