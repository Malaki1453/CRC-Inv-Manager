using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Built-in access groups. Admin is locked: Settings and User management, no tables.
    /// Only an administrator can change IT group permissions.
    /// </summary>
    internal static class AccessGroups
    {
        public const string Admin = "Admin";
        public const string IT = "IT";

        /// <summary>True when the name is the locked Admin group.</summary>
        public static bool IsAdmin(string? name) =>
            Admin.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>True when the name is the built-in IT group.</summary>
        public static bool IsIt(string? name) =>
            IT.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>True for Admin or IT, which cannot be deleted.</summary>
        public static bool IsBuiltIn(string? name) => IsAdmin(name) || IsIt(name);

        /// <summary>Admin cannot be edited; IT can be edited only by an administrator.</summary>
        public static bool CanEdit(string? name)
        {
            // Admin group JSON is locked. Empty LockedAdminJson means ALL tables allowed, not only Settings/Users.
            if (IsAdmin(name))
                return false;
            // Only an administrator may change the IT group's table rights.
            if (IsIt(name))
                return AppState.IsAdmin;
            return true;
        }

        /// <summary>Built-in groups cannot be removed from the list.</summary>
        public static bool CanDelete(string? name) => !IsBuiltIn(name);

        /// <summary>JSON that denies every inventory table; Admin uses this locked set.</summary>
        public static string LockedAdminJson() =>
            TableAccess.ToJson(TableAccess.All.Select(item => item.Key));

        /// <summary>Split a stored comma/semicolon group list into unique names.</summary>
        public static List<string> Parse(string? stored)
        {
            // Blank stored membership means no groups, not a default Admin/IT assignment.
            if (string.IsNullOrWhiteSpace(stored))
                return new List<string>();
            return stored
                .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Join group names for storage on the account row.</summary>
        public static string Join(IEnumerable<string>? groups)
        {
            return string.Join(", ", (groups ?? Array.Empty<string>())
                .Select(name => (name ?? "").Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase));
        }

        /// <summary>True when the stored list includes <paramref name="name"/>.</summary>
        public static bool Contains(string? stored, string? name)
        {
            name = (name ?? "").Trim();
            return name.Length > 0 &&
                   Parse(stored).Any(group => group.Equals(name, StringComparison.OrdinalIgnoreCase));
        }
    }

    /// <summary>
    /// Per-user table restrictions set by an administrator.
    /// Missing keys mean allowed. Admin and IT no longer bypass table rules;
    /// their access comes from the Admin / IT groups (or a custom group).
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
            (Items, "Inventory"),
            (Banking, "Banking"),
            (Debits, "Debits"),
            (Credits, "Credits"),
            (Reports, "Reports")
        };

        /// <summary>Load this user’s denied tables into AppState.</summary>
        public static void Apply(string username)
        {
            string json = SqliteInventory.GetEffectiveTableAccess(username);
            AppState.DeniedTables = ParseDenied(json);
            DataAccess.Apply(json);
        }

        /// <summary>True when this signed-in user may open the named table.</summary>
        public static bool Can(string key)
        {
            // Unknown or blank keys are not restricted.
            if (string.IsNullOrWhiteSpace(key))
                return true;
            return !AppState.DeniedTables.Contains(key);
        }

        /// <summary>True when the current user may open this navigation page.</summary>
        public static bool CanPage(AppPage page)
        {
            return page switch
            {
                AppPage.PurchaseSales or AppPage.AddPurchase => Can(Purchases),
                AppPage.Sales or AppPage.SalesOrder => Can(Sales),
                AppPage.Invoicing or AppPage.InvoicePdf => Can(Invoices),
                AppPage.Customers => Can(Customers),
                AppPage.Vendors => Can(Vendors),
                AppPage.ItemCodes => Can(Items),
                AppPage.Banking => Can(Banking),
                AppPage.Debits => Can(Debits),
                AppPage.Credits => Can(Credits),
                AppPage.Reports => Can(Reports),
                AppPage.PendingChanges => DataAccess.CanReview(),
                AppPage.Admin or AppPage.ItUsers or AppPage.ItAccess => AppState.IsAdmin || AppState.IsIt,
                _ => true
            };
        }

        /// <summary>Parse denied-table flags from table_access JSON. Missing keys mean allowed.</summary>
        public static HashSet<string> ParseDenied(string json)
        {
            var denied = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            json = (json ?? "").Trim();
            // Empty JSON (empty LockedAdminJson) means ALL tables allowed, not only Settings/Users.
            if (json.Length == 0)
                return denied;

            try
            {
                using var doc = JsonDocument.Parse(json);
                foreach (var pair in doc.RootElement.EnumerateObject())
                {
                    // Only explicit false entries deny a table; true/omitted stay allowed.
                    if (pair.Value.ValueKind == JsonValueKind.False)
                        denied.Add(pair.Name);
                }
            }
            // Bad JSON is treated as empty: ALL tables allowed, not only Settings/Users.
            catch
            {
                // treat a bad document as “no extra restrictions”
            }

            return denied;
        }

        /// <summary>Serialize denied table keys as JSON booleans set to false.</summary>
        public static string ToJson(IEnumerable<string> denied)
        {
            var map = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            foreach (var key in denied)
            {
                // Blank keys would serialize as an unnamed false flag.
                if (key.Length > 0)
                    map[key] = false;
            }

            return map.Count == 0 ? "" : JsonSerializer.Serialize(map);
        }

        /// <summary>Short label of which tables are blocked, for user lists.</summary>
        public static string Summary(string json)
        {
            var denied = ParseDenied(json);
            // Empty denied set (empty LockedAdminJson) is full table access, not Settings/Users only.
            if (denied.Count == 0)
                return "All tables";
            // Every inventory table is explicitly false.
            if (All.All(item => denied.Contains(item.Key)))
                return "No tables";
            return "Blocked: " + string.Join(", ",
                All.Where(item => denied.Contains(item.Key)).Select(item => item.Label));
        }

        /// <summary>Group names plus table summary, noting a per-user override when present.</summary>
        public static string UserSummary(string username)
        {
            var groups = SqliteInventory.GetAccessGroups(username);
            string json = SqliteInventory.GetEffectiveTableAccess(username);
            string access = Summary(json);
            bool overrides = SqliteInventory.HasAccessOverride(username);
            // No groups: show only the table summary (or Custom when a per-user overlay exists).
            if (groups.Count == 0)
                return overrides ? "Custom  ·  " + access : access;
            string prefix = AccessGroups.Join(groups);
            return overrides ? prefix + "  ·  overrides  ·  " + access : prefix + "  ·  " + access;
        }
    }
}
