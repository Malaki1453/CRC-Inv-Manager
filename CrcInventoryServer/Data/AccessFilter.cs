using System.Text.Json;

namespace CrcInventory.Server;

/// <summary>Applies the signed-in user's table_access JSON so blocked rows and hidden columns are not returned.</summary>
internal static class AccessFilter
{
    private const string WriteKey = "$write";
    private const string HideKey = "$hide";
    private const string BlockKey = "$block";

    /// <summary>Drops blocked rows and hidden columns unless <paramref name="fullAccess"/> skips the policy.</summary>
    public static List<Dictionary<string, string>> Restrict(
        InventoryStore store,
        string table,
        List<Dictionary<string, string>> rows,
        string username,
        bool fullAccess)
    {
        // Admins/IT calling with fullAccess must see every column and row.
        if (fullAccess)
            return rows;

        var policy = Load(store, username);
        // A denied table returns no rows rather than an error, matching the desktop filter.
        if (!policy.CanRead(table))
            return new List<Dictionary<string, string>>();

        var result = new List<Dictionary<string, string>>();
        foreach (var row in rows) // column map for one inventory record
        {
            // Skip parties/items the user is blocked from seeing.
            if (policy.IsBlocked(row))
                continue;
            result.Add(policy.Strip(table, row));
        }

        return result;
    }

    /// <summary>Same as the other Restrict, keeping each row's id for update/delete paths.</summary>
    public static List<(long Id, Dictionary<string, string> Fields)> Restrict(
        InventoryStore store,
        string table,
        List<(long Id, Dictionary<string, string> Fields)> rows,
        string username,
        bool fullAccess)
    {
        // Admins/IT calling with fullAccess must see every column and row.
        if (fullAccess)
            return rows;

        var policy = Load(store, username);
        // A denied table returns no rows rather than an error, matching the desktop filter.
        if (!policy.CanRead(table))
            return new List<(long, Dictionary<string, string>)>();

        var result = new List<(long, Dictionary<string, string>)>();
        foreach (var (id, fields) in rows) // live/archive id plus column map
        {
            // Skip parties/items the user is blocked from seeing.
            if (policy.IsBlocked(fields))
                continue;
            result.Add((id, policy.Strip(table, fields)));
        }

        return result;
    }

    /// <summary>True when the user may insert/update this row (not view-only, not blocked, table allowed).</summary>
    public static bool CanWriteRow(
        InventoryStore store,
        string table,
        Dictionary<string, string> values,
        string username,
        bool fullAccess)
    {
        // Callers that already passed an admin/IT check skip per-row policy.
        if (fullAccess)
            return true;
        var policy = Load(store, username);
        // Cannot write a table that is not readable.
        if (!policy.CanRead(table))
            return false;
        // View mode is read-only even when the table is allowed.
        if (policy.WriteMode(table) == "view")
            return false;
        return !policy.IsBlocked(values);
    }

    /// <summary>Combine group policies so allowed wins over blocked.</summary>
    public static string Merge(IEnumerable<string> jsons)
    {
        var list = (jsons ?? Array.Empty<string>()).Select(json => json ?? "").ToList();
        // No groups means no baseline policy.
        if (list.Count == 0)
            return "";
        // A single group is already the merged result.
        if (list.Count == 1)
            return list[0];

        var parsed = list.Select(ParseParts).ToList();
        var denied = new HashSet<string>(parsed[0].Denied, StringComparer.OrdinalIgnoreCase);
        foreach (var part in parsed.Skip(1))
            denied.IntersectWith(part.Denied);

        var tables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var part in parsed)
        {
            foreach (var key in part.Denied)
                tables.Add(key);
            foreach (var key in part.Write.Keys)
                tables.Add(key);
            foreach (var key in part.Hide.Keys)
                tables.Add(key);
        }

        var write = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        var hide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var table in tables)
        {
            // A table still in the intersected deny set stays denied for every group.
            if (denied.Contains(table))
                continue;
            var allowing = parsed.Where(part => !part.Denied.Contains(table)).ToList();
            // Every group denied this table; skip write/hide (deny already recorded).
            if (allowing.Count == 0)
                continue;

            int best = -1;
            string mode = "auto";
            foreach (var part in allowing)
            {
                string value = part.Write.TryGetValue(table, out var stored) ? stored : "auto";
                int rank = WriteRank(value);
                // Keep the most permissive write mode among groups that allow this table.
                if (rank > best)
                {
                    best = rank;
                    mode = WriteName(value);
                }
            }

            write[table] = mode;
            HashSet<string>? cols = null;
            foreach (var part in allowing)
            {
                var set = part.Hide.TryGetValue(table, out var names)
                    ? new HashSet<string>(names, StringComparer.OrdinalIgnoreCase)
                    : new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                // First allowing group seeds the hidden-column intersection.
                if (cols == null)
                    cols = set;
                // Later groups keep only columns every allowing group still hides.
                else
                    cols.IntersectWith(set);
            }

            hide[table] = cols?.ToList() ?? new List<string>();
        }

        HashSet<string>? blocked = null;
        foreach (var part in parsed)
        {
            // First group seeds the blocked-party intersection.
            if (blocked == null)
                blocked = new HashSet<string>(part.Blocked, StringComparer.OrdinalIgnoreCase);
            // A party stays blocked only when every group still blocks it.
            else
                blocked.IntersectWith(part.Blocked);
        }

        return BuildJson(denied, write, hide, blocked ?? new HashSet<string>());
    }

    /// <summary>User-specific allow/deny wins over the group baseline. Missing keys follow the groups.</summary>
    public static string Overlay(string baseline, string overlay)
    {
        overlay = (overlay ?? "").Trim();
        // No user overlay: the group baseline is the whole policy (empty JSON allows all tables).
        if (overlay.Length == 0)
            return baseline ?? "";
        baseline = (baseline ?? "").Trim();
        // No group baseline: the user overlay is the whole policy (empty JSON allows all tables).
        if (baseline.Length == 0)
            return overlay;

        var baseParts = ParseParts(baseline);
        var over = ParseParts(overlay);
        var denied = new HashSet<string>(baseParts.Denied, StringComparer.OrdinalIgnoreCase);
        foreach (var key in over.Denied)
            denied.Add(key);
        foreach (var key in over.Allowed)
            denied.Remove(key);

        var write = new Dictionary<string, string>(baseParts.Write, StringComparer.OrdinalIgnoreCase);
        foreach (var pair in over.Write)
            write[pair.Key] = pair.Value;

        var hide = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in baseParts.Hide)
            hide[pair.Key] = pair.Value.ToList();
        foreach (var pair in over.Hide)
            hide[pair.Key] = pair.Value.ToList();

        var blocked = over.HasBlock
            ? new HashSet<string>(over.Blocked, StringComparer.OrdinalIgnoreCase)
            : new HashSet<string>(baseParts.Blocked, StringComparer.OrdinalIgnoreCase);

        return BuildJson(denied, write, hide, blocked);
    }

    /// <summary>Parses table_access JSON into deny/allow/write/hide/block sets.</summary>
    private static PolicyParts ParseParts(string json)
    {
        var parts = new PolicyParts();
        json = (json ?? "").Trim();
        // Empty JSON denies nothing, so all tables are allowed (IT default; Admin uses LockedAdminJson).
        if (json.Length == 0)
            return parts;
        // Invalid JSON is treated as an empty policy rather than failing the request.
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var pair in root.EnumerateObject())
            {
                // $write / $hide / $block are handled below, not as table flags.
                if (pair.Name.StartsWith('$'))
                    continue;
                // false means the table is denied.
                if (pair.Value.ValueKind == JsonValueKind.False)
                    parts.Denied.Add(pair.Name);
                // true means the user overlay explicitly allows a group-denied table.
                else if (pair.Value.ValueKind == JsonValueKind.True)
                    parts.Allowed.Add(pair.Name);
            }

            // Per-table write modes (view / confirm / auto).
            if (root.TryGetProperty(WriteKey, out var write) && write.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in write.EnumerateObject())
                    parts.Write[pair.Name] = pair.Value.GetString() ?? "auto";
            }

            // Per-table hidden column lists.
            if (root.TryGetProperty(HideKey, out var hide) && hide.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in hide.EnumerateObject())
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    // Only arrays of strings are hide lists; other JSON is ignored.
                    if (pair.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            string name = item.GetString()?.Trim() ?? "";
                            // Skip blanks so hide lists stay meaningful.
                            if (name.Length > 0)
                                set.Add(name);
                        }
                    }

                    parts.Hide[pair.Name] = set;
                }
            }

            // $block present (even empty) means the user overlay owns the block list.
            if (root.TryGetProperty(BlockKey, out var block) && block.ValueKind == JsonValueKind.Array)
            {
                parts.HasBlock = true;
                foreach (var item in block.EnumerateArray())
                {
                    string name = item.GetString()?.Trim() ?? "";
                    // Skip blanks so block lists stay meaningful.
                    if (name.Length > 0)
                        parts.Blocked.Add(name);
                }
            }
        }
        // keep empty
        catch
        {
            // keep empty
        }

        return parts;
    }

    /// <summary>Serializes deny flags plus $write, $hide, and $block into table_access JSON.</summary>
    private static string BuildJson(
        HashSet<string> denied,
        Dictionary<string, string> write,
        Dictionary<string, List<string>> hide,
        HashSet<string> blocked)
    {
        using var stream = new MemoryStream();
        using (var writer = new Utf8JsonWriter(stream))
        {
            writer.WriteStartObject();
            foreach (var key in denied)
                writer.WriteBoolean(key, false);

            writer.WritePropertyName(WriteKey);
            writer.WriteStartObject();
            foreach (var pair in write)
                writer.WriteString(pair.Key, pair.Value);
            writer.WriteEndObject();

            writer.WritePropertyName(HideKey);
            writer.WriteStartObject();
            foreach (var pair in hide)
            {
                writer.WritePropertyName(pair.Key);
                writer.WriteStartArray();
                foreach (var col in pair.Value)
                    writer.WriteStringValue(col);
                writer.WriteEndArray();
            }

            writer.WriteEndObject();
            writer.WritePropertyName(BlockKey);
            writer.WriteStartArray();
            foreach (var name in blocked)
                writer.WriteStringValue(name);
            writer.WriteEndArray();
            writer.WriteEndObject();
        }

        return System.Text.Encoding.UTF8.GetString(stream.ToArray());
    }

    /// <summary>Ranks write modes so merge can pick the most permissive (view &lt; confirm &lt; auto).</summary>
    private static int WriteRank(string? mode)
    {
        mode = (mode ?? "").Trim();
        // View is read-only, the least permissive.
        if (mode.Equals("view", StringComparison.OrdinalIgnoreCase))
            return 0;
        // Confirm is write-with-review, between view and auto.
        if (mode.Equals("confirm", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    /// <summary>Canonical write-mode name for a rank from <see cref="WriteRank"/>.</summary>
    private static string WriteName(string? mode) => WriteRank(mode) switch
    {
        // Rank 0 is view-only.
        0 => "view",
        // Rank 1 is write that needs confirmation.
        1 => "confirm",
        // Anything else is unrestricted write.
        _ => "auto"
    };

    /// <summary>Parsed pieces of one table_access JSON object.</summary>
    private sealed class PolicyParts
    {
        /// <summary>Tables explicitly set to false.</summary>
        public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Tables explicitly set to true (user overlay allow).</summary>
        public HashSet<string> Allowed { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Per-table write modes.</summary>
        public Dictionary<string, string> Write { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Per-table hidden column names.</summary>
        public Dictionary<string, HashSet<string>> Hide { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>Blocked party/item tokens.</summary>
        public HashSet<string> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);
        /// <summary>True when $block was present so Overlay should replace, not inherit, the list.</summary>
        public bool HasBlock { get; set; }
    }

    /// <summary>Loads the user's merged policy and expands blocked codes to related names.</summary>
    private static UserPolicy Load(InventoryStore store, string username)
    {
        var policy = new UserPolicy();
        policy.Parse(store.GetTableAccess(username));
        policy.Expand(
            store.Read(Schema.Customers, false),
            store.Read(Schema.Vendors, false),
            store.Read(Schema.ItemCodes, false));
        return policy;
    }

    /// <summary>Runtime policy used to filter rows and columns for one signed-in user.</summary>
    private sealed class UserPolicy
    {
        private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _write = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _hide = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _block = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

        /// <summary>Fills deny/write/hide/block from table_access JSON; invalid JSON leaves an empty policy.</summary>
        public void Parse(string json)
        {
            json = (json ?? "").Trim();
            // Empty JSON denies nothing, so all tables are allowed (same as an empty IT group policy).
            if (json.Length == 0)
                return;
            // Invalid JSON is treated as an empty policy rather than failing the request.
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                foreach (var pair in root.EnumerateObject())
                {
                    // Only explicit false denies a table; true/other are not denials.
                    if (pair.Value.ValueKind == JsonValueKind.False)
                        _denied.Add(pair.Name);
                }

                // Per-table write modes.
                if (root.TryGetProperty(WriteKey, out var write) && write.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in write.EnumerateObject())
                        _write[pair.Name] = pair.Value.GetString() ?? "auto";
                }

                // Per-table hidden columns.
                if (root.TryGetProperty(HideKey, out var hide) && hide.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in hide.EnumerateObject())
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        // Only arrays of strings are hide lists.
                        if (pair.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in pair.Value.EnumerateArray())
                            {
                                string name = item.GetString()?.Trim() ?? "";
                                // Skip blanks so hide lists stay meaningful.
                                if (name.Length > 0)
                                    set.Add(name);
                            }
                        }

                        _hide[pair.Name] = set;
                    }
                }

                // Blocked party/item tokens.
                if (root.TryGetProperty(BlockKey, out var block) && block.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in block.EnumerateArray())
                    {
                        string name = item.GetString()?.Trim() ?? "";
                        // Skip blanks so block lists stay meaningful.
                        if (name.Length > 0)
                            _block.Add(name);
                    }
                }
            }
            // keep empty policy
            catch
            {
                // keep empty policy
            }
        }

        /// <summary>Expands blocked codes to related name/company/description tokens from master tables.</summary>
        public void Expand(
            List<Dictionary<string, string>> customers,
            List<Dictionary<string, string>> vendors,
            List<Dictionary<string, string>> items)
        {
            foreach (var seed in _block)
                _keys.Add(seed);
            Absorb(customers, "Code", "Name", "Company");
            Absorb(vendors, "Code", "Name", "Company");
            Absorb(items, "Code", "Description", "Species", "Scientific Name");
        }

        /// <summary>True unless the table's policy key is in the deny set.</summary>
        public bool CanRead(string table)
        {
            string key = TableKey(table); // short policy name (purchases, sales, …)
            // Empty deny set (empty JSON) allows every table.
            return !_denied.Contains(key);
        }

        /// <summary>Write mode for the table, or auto when unset.</summary>
        public string WriteMode(string table)
        {
            string key = TableKey(table);
            return _write.TryGetValue(key, out var mode) ? mode : "auto";
        }

        /// <summary>True when any identifying cell on the row matches a blocked token.</summary>
        public bool IsBlocked(Dictionary<string, string> record)
        {
            // No blocked tokens means every row is visible.
            if (_keys.Count == 0)
                return false;
            foreach (var key in new[]
                     {
                         "Vendor Code", "Vendor", "Customer Code", "Customer",
                         "Code", "Name", "Company", "Item Code", "Description",
                         "Species", "Scientific Name"
                     })
            {
                // A match on any identifying field hides the whole row.
                if (record.TryGetValue(key, out var value) &&
                    !string.IsNullOrWhiteSpace(value) &&
                    _keys.Contains(value.Trim()))
                    return true;
            }

            return false;
        }

        /// <summary>Copies the row without columns listed in $hide for this table.</summary>
        public Dictionary<string, string> Strip(string table, Dictionary<string, string> row)
        {
            string key = TableKey(table);
            // No hide list means the row is returned unchanged.
            if (!_hide.TryGetValue(key, out var cols) || cols.Count == 0)
                return row;
            var copy = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
            foreach (var column in cols)
            {
                foreach (var name in copy.Keys.ToList())
                {
                    // Remove matching headers regardless of the casing the row used.
                    if (name.Equals(column, StringComparison.OrdinalIgnoreCase))
                        copy.Remove(name);
                }
            }

            return copy;
        }

        /// <summary>If any field on a master row is blocked, all of its identifying tokens become blocked too.</summary>
        private void Absorb(List<Dictionary<string, string>> rows, params string[] fields)
        {
            foreach (var record in rows)
            {
                var tokens = new List<string>();
                bool hit = false;
                foreach (var field in fields)
                {
                    // Missing columns are not tokens.
                    if (!record.TryGetValue(field, out var value))
                        continue;
                    value = (value ?? "").Trim();
                    // Blank cells are not identifying tokens.
                    if (value.Length == 0)
                        continue;
                    tokens.Add(value);
                    // A blocked code/name on this master row means the whole party/item is blocked.
                    if (_block.Contains(value))
                        hit = true;
                }

                // Unrelated master rows stay visible.
                if (!hit)
                    continue;
                foreach (var token in tokens)
                    _keys.Add(token);
            }
        }

        /// <summary>Maps a schema table name to the short key used in table_access JSON.</summary>
        private static string TableKey(string table)
        {
            table = (table ?? "").Trim();
            // purchase_sales is "purchases" in the policy JSON.
            if (table.Equals(Schema.PurchaseSales, StringComparison.OrdinalIgnoreCase))
                return "purchases";
            // sales table is "sales".
            if (table.Equals(Schema.Sales, StringComparison.OrdinalIgnoreCase))
                return "sales";
            // invoices table is "invoices".
            if (table.Equals(Schema.Invoices, StringComparison.OrdinalIgnoreCase))
                return "invoices";
            // customers master is "customers".
            if (table.Equals(Schema.Customers, StringComparison.OrdinalIgnoreCase))
                return "customers";
            // vendors master is "vendors".
            if (table.Equals(Schema.Vendors, StringComparison.OrdinalIgnoreCase))
                return "vendors";
            // item_codes is "items".
            if (table.Equals(Schema.ItemCodes, StringComparison.OrdinalIgnoreCase))
                return "items";
            // bank_transactions is "banking".
            if (table.Equals(Schema.BankTransactions, StringComparison.OrdinalIgnoreCase))
                return "banking";
            // debits table is "debits".
            if (table.Equals(Schema.Debits, StringComparison.OrdinalIgnoreCase))
                return "debits";
            // credits table is "credits".
            if (table.Equals(Schema.Credits, StringComparison.OrdinalIgnoreCase))
                return "credits";
            return table;
        }
    }
}
