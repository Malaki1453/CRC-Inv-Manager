using System.Text.Json;

namespace CrcInventory.Server;

/// <summary>Applies the signed-in user's table_access JSON so blocked rows and hidden columns are not returned.</summary>
internal static class AccessFilter
{
    private const string WriteKey = "$write";
    private const string HideKey = "$hide";
    private const string BlockKey = "$block";

    public static List<Dictionary<string, string>> Restrict(
        InventoryStore store,
        string table,
        List<Dictionary<string, string>> rows,
        string username,
        bool fullAccess)
    {
        if (fullAccess)
            return rows;

        var policy = Load(store, username);
        if (!policy.CanRead(table))
            return new List<Dictionary<string, string>>();

        var result = new List<Dictionary<string, string>>();
        foreach (var row in rows)
        {
            if (policy.IsBlocked(row))
                continue;
            result.Add(policy.Strip(table, row));
        }

        return result;
    }

    public static List<(long Id, Dictionary<string, string> Fields)> Restrict(
        InventoryStore store,
        string table,
        List<(long Id, Dictionary<string, string> Fields)> rows,
        string username,
        bool fullAccess)
    {
        if (fullAccess)
            return rows;

        var policy = Load(store, username);
        if (!policy.CanRead(table))
            return new List<(long, Dictionary<string, string>)>();

        var result = new List<(long, Dictionary<string, string>)>();
        foreach (var (id, fields) in rows)
        {
            if (policy.IsBlocked(fields))
                continue;
            result.Add((id, policy.Strip(table, fields)));
        }

        return result;
    }

    public static bool CanWriteRow(
        InventoryStore store,
        string table,
        Dictionary<string, string> values,
        string username,
        bool fullAccess)
    {
        if (fullAccess)
            return true;
        var policy = Load(store, username);
        if (!policy.CanRead(table))
            return false;
        if (policy.WriteMode(table) == "view")
            return false;
        return !policy.IsBlocked(values);
    }

    /// <summary>Combine group policies so allowed wins over blocked.</summary>
    public static string Merge(IEnumerable<string> jsons)
    {
        var list = (jsons ?? Array.Empty<string>()).Select(json => json ?? "").ToList();
        if (list.Count == 0)
            return "";
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
            if (denied.Contains(table))
                continue;
            var allowing = parsed.Where(part => !part.Denied.Contains(table)).ToList();
            if (allowing.Count == 0)
                continue;

            int best = -1;
            string mode = "auto";
            foreach (var part in allowing)
            {
                string value = part.Write.TryGetValue(table, out var stored) ? stored : "auto";
                int rank = WriteRank(value);
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
                if (cols == null)
                    cols = set;
                else
                    cols.IntersectWith(set);
            }

            hide[table] = cols?.ToList() ?? new List<string>();
        }

        HashSet<string>? blocked = null;
        foreach (var part in parsed)
        {
            if (blocked == null)
                blocked = new HashSet<string>(part.Blocked, StringComparer.OrdinalIgnoreCase);
            else
                blocked.IntersectWith(part.Blocked);
        }

        return BuildJson(denied, write, hide, blocked ?? new HashSet<string>());
    }

    /// <summary>User-specific allow/deny wins over the group baseline. Missing keys follow the groups.</summary>
    public static string Overlay(string baseline, string overlay)
    {
        overlay = (overlay ?? "").Trim();
        if (overlay.Length == 0)
            return baseline ?? "";
        baseline = (baseline ?? "").Trim();
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

    private static PolicyParts ParseParts(string json)
    {
        var parts = new PolicyParts();
        json = (json ?? "").Trim();
        if (json.Length == 0)
            return parts;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            foreach (var pair in root.EnumerateObject())
            {
                if (pair.Name.StartsWith('$'))
                    continue;
                if (pair.Value.ValueKind == JsonValueKind.False)
                    parts.Denied.Add(pair.Name);
                else if (pair.Value.ValueKind == JsonValueKind.True)
                    parts.Allowed.Add(pair.Name);
            }

            if (root.TryGetProperty(WriteKey, out var write) && write.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in write.EnumerateObject())
                    parts.Write[pair.Name] = pair.Value.GetString() ?? "auto";
            }

            if (root.TryGetProperty(HideKey, out var hide) && hide.ValueKind == JsonValueKind.Object)
            {
                foreach (var pair in hide.EnumerateObject())
                {
                    var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                    if (pair.Value.ValueKind == JsonValueKind.Array)
                    {
                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            string name = item.GetString()?.Trim() ?? "";
                            if (name.Length > 0)
                                set.Add(name);
                        }
                    }

                    parts.Hide[pair.Name] = set;
                }
            }

            if (root.TryGetProperty(BlockKey, out var block) && block.ValueKind == JsonValueKind.Array)
            {
                parts.HasBlock = true;
                foreach (var item in block.EnumerateArray())
                {
                    string name = item.GetString()?.Trim() ?? "";
                    if (name.Length > 0)
                        parts.Blocked.Add(name);
                }
            }
        }
        catch
        {
            // keep empty
        }

        return parts;
    }

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

    private static int WriteRank(string? mode)
    {
        mode = (mode ?? "").Trim();
        if (mode.Equals("view", StringComparison.OrdinalIgnoreCase))
            return 0;
        if (mode.Equals("confirm", StringComparison.OrdinalIgnoreCase))
            return 1;
        return 2;
    }

    private static string WriteName(string? mode) => WriteRank(mode) switch
    {
        0 => "view",
        1 => "confirm",
        _ => "auto"
    };

    private sealed class PolicyParts
    {
        public HashSet<string> Denied { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Allowed { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, string> Write { get; } = new(StringComparer.OrdinalIgnoreCase);
        public Dictionary<string, HashSet<string>> Hide { get; } = new(StringComparer.OrdinalIgnoreCase);
        public HashSet<string> Blocked { get; } = new(StringComparer.OrdinalIgnoreCase);
        public bool HasBlock { get; set; }
    }

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

    private sealed class UserPolicy
    {
        private readonly HashSet<string> _denied = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, string> _write = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, HashSet<string>> _hide = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _block = new(StringComparer.OrdinalIgnoreCase);
        private readonly HashSet<string> _keys = new(StringComparer.OrdinalIgnoreCase);

        public void Parse(string json)
        {
            json = (json ?? "").Trim();
            if (json.Length == 0)
                return;
            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                foreach (var pair in root.EnumerateObject())
                {
                    if (pair.Value.ValueKind == JsonValueKind.False)
                        _denied.Add(pair.Name);
                }

                if (root.TryGetProperty(WriteKey, out var write) && write.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in write.EnumerateObject())
                        _write[pair.Name] = pair.Value.GetString() ?? "auto";
                }

                if (root.TryGetProperty(HideKey, out var hide) && hide.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in hide.EnumerateObject())
                    {
                        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                        if (pair.Value.ValueKind == JsonValueKind.Array)
                        {
                            foreach (var item in pair.Value.EnumerateArray())
                            {
                                string name = item.GetString()?.Trim() ?? "";
                                if (name.Length > 0)
                                    set.Add(name);
                            }
                        }

                        _hide[pair.Name] = set;
                    }
                }

                if (root.TryGetProperty(BlockKey, out var block) && block.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in block.EnumerateArray())
                    {
                        string name = item.GetString()?.Trim() ?? "";
                        if (name.Length > 0)
                            _block.Add(name);
                    }
                }
            }
            catch
            {
                // keep empty policy
            }
        }

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

        public bool CanRead(string table)
        {
            string key = TableKey(table);
            return !_denied.Contains(key);
        }

        public string WriteMode(string table)
        {
            string key = TableKey(table);
            return _write.TryGetValue(key, out var mode) ? mode : "auto";
        }

        public bool IsBlocked(Dictionary<string, string> record)
        {
            if (_keys.Count == 0)
                return false;
            foreach (var key in new[]
                     {
                         "Vendor Code", "Vendor", "Customer Code", "Customer",
                         "Code", "Name", "Company", "Item Code", "Description",
                         "Species", "Scientific Name"
                     })
            {
                if (record.TryGetValue(key, out var value) &&
                    !string.IsNullOrWhiteSpace(value) &&
                    _keys.Contains(value.Trim()))
                    return true;
            }

            return false;
        }

        public Dictionary<string, string> Strip(string table, Dictionary<string, string> row)
        {
            string key = TableKey(table);
            if (!_hide.TryGetValue(key, out var cols) || cols.Count == 0)
                return row;
            var copy = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
            foreach (var column in cols)
            {
                foreach (var name in copy.Keys.ToList())
                {
                    if (name.Equals(column, StringComparison.OrdinalIgnoreCase))
                        copy.Remove(name);
                }
            }

            return copy;
        }

        private void Absorb(List<Dictionary<string, string>> rows, params string[] fields)
        {
            foreach (var record in rows)
            {
                var tokens = new List<string>();
                bool hit = false;
                foreach (var field in fields)
                {
                    if (!record.TryGetValue(field, out var value))
                        continue;
                    value = (value ?? "").Trim();
                    if (value.Length == 0)
                        continue;
                    tokens.Add(value);
                    if (_block.Contains(value))
                        hit = true;
                }

                if (!hit)
                    continue;
                foreach (var token in tokens)
                    _keys.Add(token);
            }
        }

        private static string TableKey(string table)
        {
            table = (table ?? "").Trim();
            if (table.Equals(Schema.PurchaseSales, StringComparison.OrdinalIgnoreCase))
                return "purchases";
            if (table.Equals(Schema.Sales, StringComparison.OrdinalIgnoreCase))
                return "sales";
            if (table.Equals(Schema.Invoices, StringComparison.OrdinalIgnoreCase))
                return "invoices";
            if (table.Equals(Schema.Customers, StringComparison.OrdinalIgnoreCase))
                return "customers";
            if (table.Equals(Schema.Vendors, StringComparison.OrdinalIgnoreCase))
                return "vendors";
            if (table.Equals(Schema.ItemCodes, StringComparison.OrdinalIgnoreCase))
                return "items";
            if (table.Equals(Schema.BankTransactions, StringComparison.OrdinalIgnoreCase))
                return "banking";
            if (table.Equals(Schema.Debits, StringComparison.OrdinalIgnoreCase))
                return "debits";
            if (table.Equals(Schema.Credits, StringComparison.OrdinalIgnoreCase))
                return "credits";
            return table;
        }
    }
}
