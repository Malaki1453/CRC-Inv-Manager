using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace CrcInventory.Protocol;

/// <summary>
/// Plaid REST calls. Keys are passed in by the caller (inventory server, or local SQLite mode).
/// Desktop remote sessions must not use this with a secret from the client.
/// </summary>
public static class PlaidHttp
{
    private static readonly HttpClient Http = new()
    {
        Timeout = TimeSpan.FromSeconds(45)
    };

    /// <summary>Plaid API base URL for sandbox or production. Old "development" values use production.</summary>
    public static string Host(string? env)
    {
        string value = (env ?? "sandbox").Trim().ToLowerInvariant();
        return value switch
        {
            "production" => "https://production.plaid.com",
            "development" => "https://production.plaid.com",
            _ => "https://sandbox.plaid.com"
        };
    }

    /// <summary>Create a Link token so a client can open Plaid Link.</summary>
    public static async Task<(bool Ok, string Value, string Error)> CreateLinkTokenAsync(
        string clientId,
        string secret,
        string env,
        string clientName,
        string userId)
    {
        var body = new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["secret"] = secret,
            ["client_name"] = string.IsNullOrWhiteSpace(clientName) ? "Cast Right Catch" : clientName,
            ["language"] = "en",
            ["country_codes"] = new[] { "US" },
            ["user"] = new Dictionary<string, string>
            {
                ["client_user_id"] = string.IsNullOrWhiteSpace(userId) ? "crc" : userId
            },
            ["products"] = new[] { "transactions" }
        };

        var json = await PostAsync(env, "/link/token/create", body).ConfigureAwait(false);
        if (!json.Ok)
            return (false, "", json.Error);
        if (json.Doc.RootElement.TryGetProperty("link_token", out var token))
            return (true, token.GetString() ?? "", "");
        return (false, "", "Plaid did not return a link token.");
    }

    /// <summary>Swap the public_token from Link for a long-lived access_token and item_id.</summary>
    public static async Task<(bool Ok, string AccessToken, string ItemId, string Error)> ExchangePublicTokenAsync(
        string clientId,
        string secret,
        string env,
        string publicToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["secret"] = secret,
            ["public_token"] = publicToken
        };
        var json = await PostAsync(env, "/item/public_token/exchange", body).ConfigureAwait(false);
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

    /// <summary>List bank accounts on the connected Item so we can pick one to sync.</summary>
    public static async Task<(bool Ok, List<PlaidAccountDto> Accounts, string Error)> GetAccountsAsync(
        string clientId,
        string secret,
        string env,
        string accessToken)
    {
        var body = new Dictionary<string, object?>
        {
            ["client_id"] = clientId,
            ["secret"] = secret,
            ["access_token"] = accessToken
        };
        var json = await PostAsync(env, "/accounts/get", body).ConfigureAwait(false);
        var list = new List<PlaidAccountDto>();
        if (!json.Ok)
            return (false, list, json.Error);
        if (!json.Doc.RootElement.TryGetProperty("accounts", out var accounts))
            return (true, list, "");
        foreach (var item in accounts.EnumerateArray())
        {
            list.Add(new PlaidAccountDto(
                item.TryGetProperty("account_id", out var id) ? id.GetString() ?? "" : "",
                item.TryGetProperty("name", out var name) ? name.GetString() ?? "" : "",
                item.TryGetProperty("mask", out var mask) ? mask.GetString() ?? "" : "",
                item.TryGetProperty("subtype", out var sub) ? sub.GetString() ?? "" : ""));
        }

        return (true, list, "");
    }

    /// <summary>Pull new transactions since the saved cursor, paging until has_more is false.</summary>
    public static async Task<(bool Ok, List<BankTxnDto> Added, string Cursor, string Error)> SyncTransactionsAsync(
        string clientId,
        string secret,
        string env,
        string accessToken,
        string accountId,
        string cursor)
    {
        var added = new List<BankTxnDto>();
        string next = cursor ?? "";
        bool more = true;
        while (more)
        {
            var body = new Dictionary<string, object?>
            {
                ["client_id"] = clientId,
                ["secret"] = secret,
                ["access_token"] = accessToken,
                ["cursor"] = next,
                ["count"] = 100
            };
            var json = await PostAsync(env, "/transactions/sync", body).ConfigureAwait(false);
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

    /// <summary>Convert one Plaid transaction into a bank row for the chosen account.</summary>
    private static void TryAdd(JsonElement txn, string accountId, List<BankTxnDto> added)
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

        decimal amount = -plaidAmount;
        string name = txn.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
        string merchant = txn.TryGetProperty("merchant_name", out var m) ? m.GetString() ?? "" : "";
        string id = txn.TryGetProperty("transaction_id", out var tid) ? tid.GetString() ?? "" : "";
        string check = txn.TryGetProperty("check_number", out var cn) ? cn.GetString() ?? "" : "";
        string description = merchant.Length > 0 && !merchant.Equals(name, StringComparison.OrdinalIgnoreCase)
            ? merchant + " · " + name
            : name;
        added.Add(new BankTxnDto
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
        string env,
        string path,
        Dictionary<string, object?> body)
    {
        try
        {
            string payload = JsonSerializer.Serialize(body);
            using var content = new StringContent(payload, Encoding.UTF8, "application/json");
            using var response = await Http.PostAsync(Host(env) + path, content).ConfigureAwait(false);
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

/// <summary>One account on a Plaid Item (id, name, last-4, subtype).</summary>
public sealed record PlaidAccountDto(string Id, string Name, string Mask, string Subtype);
