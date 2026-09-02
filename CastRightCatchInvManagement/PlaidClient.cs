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

        public static string Host
        {
            get
            {
                string env = (AppState.PlaidEnv ?? "sandbox").Trim().ToLowerInvariant();
                return env switch
                {
                    "production" => "https://production.plaid.com",
                    "development" => "https://development.plaid.com",
                    _ => "https://sandbox.plaid.com"
                };
            }
        }

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
            if (!json.Ok)
                return (false, "", json.Error);
            if (json.Doc.RootElement.TryGetProperty("link_token", out var token))
                return (true, token.GetString() ?? "", "");
            return (false, "", "Plaid did not return a link token.");
        }

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
            if (!json.Ok)
                return (false, "", "", json.Error);
            string access = json.Doc.RootElement.TryGetProperty("access_token", out var a)
                ? a.GetString() ?? "" : "";
            string item = json.Doc.RootElement.TryGetProperty("item_id", out var i)
                ? i.GetString() ?? "" : "";
            if (access.Length == 0)
                return (false, "", "", "Plaid did not return an access token.");
            return (true, access, item, "");
        }

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
            if (!json.Ok)
                return (false, list, json.Error);
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
                if (!json.Ok)
                    return (false, added, next, json.Error);

                var root = json.Doc.RootElement;
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

        private static void TryAdd(JsonElement txn, string accountId, List<BankFeed.Parsed> added)
        {
            if (accountId.Length > 0 &&
                txn.TryGetProperty("account_id", out var aid) &&
                !string.Equals(aid.GetString(), accountId, StringComparison.Ordinal))
                return;

            if (!txn.TryGetProperty("date", out var dateEl) ||
                !DateTime.TryParse(dateEl.GetString(), out var date))
                return;
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
                if (!response.IsSuccessStatusCode)
                {
                    string error = "Plaid request failed.";
                    if (doc.RootElement.TryGetProperty("error_message", out var msg))
                        error = msg.GetString() ?? error;
                    return (false, doc, error);
                }

                return (true, doc, "");
            }
            catch (Exception ex)
            {
                return (false, JsonDocument.Parse("{}"), ex.Message);
            }
        }
    }

    internal sealed record PlaidAccount(string Id, string Name, string Mask, string Subtype);
}
