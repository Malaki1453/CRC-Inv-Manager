using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Per-user table restrictions set by an administrator.
    /// Admins and IT always have full access. Missing keys mean allowed.
    /// </summary>
    internal static class TableAccess
    {
        public const string Purchases = "purchases";
        public const string Sales = "sales";
        public const string Invoices = "invoices";
        public const string Customers = "customers";
        public const string Vendors = "vendors";
        public const string Items = "items";
        public const string Banking = "banking";
        public const string Debits = "debits";
        public const string Credits = "credits";
        public const string Reports = "reports";

        public static readonly (string Key, string Label)[] All =
        {
            (Purchases, "Purchases (product)"),
            (Sales, "Sales and sales orders"),
            (Invoices, "Invoices"),
            (Customers, "Customers"),
            (Vendors, "Vendors"),
            (Items, "Item codes"),
            (Banking, "Banking"),
            (Debits, "Debits"),
            (Credits, "Credits"),
            (Reports, "Reports")
        };

        /// <summary>Load this user’s denied tables into AppState.</summary>
        public static void Apply(string username)
        {
            AppState.DeniedTables = ParseDenied(SqliteInventory.GetTableAccess(username));
        }

        public static bool Can(string key)
        {
            if (AppState.IsAdmin || AppState.IsIt)
                return true;
            if (string.IsNullOrWhiteSpace(key))
                return true;
            return !AppState.DeniedTables.Contains(key);
        }

        public static bool CanPage(AppPage page)
        {
            return page switch
            {
                AppPage.PurchaseSales or AppPage.AddPurchase => Can(Purchases),
                AppPage.Sales or AppPage.AddSale or AppPage.SalesOrder => Can(Sales),
                AppPage.Invoicing or AppPage.InvoicePdf => Can(Invoices),
                AppPage.Customers => Can(Customers),
                AppPage.Vendors => Can(Vendors),
                AppPage.ItemCodes => Can(Items),
                AppPage.Banking => Can(Banking),
                AppPage.Debits => Can(Debits),
                AppPage.Credits => Can(Credits),
                AppPage.Reports => Can(Reports),
                AppPage.Admin or AppPage.ItUsers or AppPage.ItAccess => AppState.IsAdmin || AppState.IsIt,
                _ => true
            };
        }

        public static HashSet<string> ParseDenied(string json)
        {
            var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            json = (json ?? "").Trim();
            if (json.Length == 0)
                return denied;

            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var pair in doc.RootElement.EnumerateObject())
                {
                    if (pair.Value.ValueKind == JsonValueKind.False)
                        denied.Add(pair.Name);
                }
            }
            catch
            {
                // treat a bad document as “no extra restrictions”
            }

            return denied;
        }

        public static string ToJson(IEnumerable<string> denied)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in denied)
            {
                if (key.Length > 0)
                    map[key] = false;
            }

            return map.Count == 0 ? "" : JsonSerializer.Serialize(map);
        }

        public static string Summary(string json)
        {
            var denied = ParseDenied(json);
            if (denied.Count == 0)
                return "All tables";
            return "Blocked: " + string.Join(", ",
                All.Where(item => denied.Contains(item.Key)).Select(item => item.Label));
        }
    }
}
