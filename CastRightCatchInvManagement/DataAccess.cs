using System.Text.Json;

namespace CastRightCatchInvManagement
{
    public readonly struct MutateResult
    {
        public bool Ok { get; init; }
        public bool Queued { get; init; }
        public string Message { get; init; }

        public static MutateResult Saved(string message = "") =>
            new() { Ok = true, Message = message };
        public static MutateResult QueuedForReview() =>
            new() { Ok = true, Queued = true, Message = "Submitted for review." };
        public static MutateResult Deny(string message) =>
            new() { Ok = false, Message = message };
    }

    internal enum DataWriteMode
    {
        View,
        Confirm,
        Auto
    }

    internal sealed class DataTablePolicy
    {
        public DataWriteMode Write { get; set; } = DataWriteMode.Auto;
        public List<string> HideColumns { get; set; } = new();
    }

    /// <summary>
    /// Per-user write mode, hidden columns, and blocked companies.
    /// Stored in the same table_access JSON as denied tables.
    /// </summary>
    internal static class DataAccess
    {
        public const string WriteKey = "$write";
        public const string HideKey = "$hide";
        public const string BlockKey = "$block";

        private static readonly Dictionary<string, DataTablePolicy> Policies =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> BlockedCompanies =
            new(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<string> BlockedKeys =
            new(StringComparer.OrdinalIgnoreCase);

        private static readonly string[] PartyFields =
        {
            "Code", "Name", "Company"
        };
        private static readonly string[] ItemFields =
        {
            "Code", "Description", "Species", "Scientific Name"
        };
        private static readonly string[] RecordFields =
        {
            "Vendor Code", "Vendor", "Customer Code", "Customer",
            "Code", "Name", "Company", "Item Code", "Description", "Species",
            "Scientific Name"
        };

        /// <summary>When true, pending Accept writes skip the confirm queue.</summary>
        public static bool ApplyingReview { get; set; }

        public static void Apply(string json)
        {
            Policies.Clear();
            BlockedCompanies.Clear();
            BlockedKeys.Clear();
            Parse(json, Policies, BlockedCompanies);
            ExpandBlockedKeys();
        }

        public static void Parse(
            string json,
            Dictionary<string, DataTablePolicy> policies,
            HashSet<string> blocked)
        {
            json = (json ?? "").Trim();
            if (json.Length == 0)
                return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty(WriteKey, out var write) &&
                    write.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in write.EnumerateObject())
                    {
                        string key = TableKey(pair.Name);
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        policy.Write = ParseMode(pair.Value.GetString());
                    }
                }

                if (root.TryGetProperty(HideKey, out var hide) &&
                    hide.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in hide.EnumerateObject())
                    {
                        if (pair.Value.ValueKind != JsonValueKind.Array)
                            continue;
                        string key = TableKey(pair.Name);
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            string name = item.GetString()?.Trim() ?? "";
                            if (name.Length > 0)
                                policy.HideColumns.Add(name);
                        }
                    }
                }

                if (root.TryGetProperty(BlockKey, out var block) &&
                    block.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in block.EnumerateArray())
                    {
                        string name = item.GetString()?.Trim() ?? "";
                        if (name.Length > 0)
                            blocked.Add(name);
                    }
                }
            }
            catch
            {
                // keep defaults
            }
        }

        public static void Clear()
        {
            Policies.Clear();
            BlockedCompanies.Clear();
            BlockedKeys.Clear();
        }

        public static DataWriteMode WriteMode(string table)
        {
            if (ApplyingReview)
                return DataWriteMode.Auto;
            table = TableKey(table);
            return Policies.TryGetValue(table, out var policy) ? policy.Write : DataWriteMode.Auto;
        }

        public static bool CanMutate(string table) =>
            WriteMode(table) != DataWriteMode.View;

        public static bool CanReadTable(string table)
        {
            if (ApplyingReview)
                return true;
            table = TableKey(table);
            if (table.Equals(DataFiles.PendingChanges, StringComparison.OrdinalIgnoreCase))
                return CanReview();
            foreach (var item in TableAccess.All)
            {
                if (table.Equals(item.Key, StringComparison.OrdinalIgnoreCase) ||
                    table.Equals(TableFile(item.Key), StringComparison.OrdinalIgnoreCase))
                    return TableAccess.Can(item.Key);
            }

            return true;
        }

        /// <summary>Drop blocked rows and hidden columns so callers never receive that data.</summary>
        public static List<Dictionary<string, string>> RestrictRows(
            string table,
            List<Dictionary<string, string>> rows)
        {
            if (ApplyingReview)
                return rows;
            if (!CanReadTable(table))
                return new List<Dictionary<string, string>>();

            var result = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                if (IsRecordBlocked(row))
                    continue;
                result.Add(StripHidden(table, row));
            }

            return result;
        }

        public static List<(long Id, Dictionary<string, string> Fields)> RestrictRows(
            string table,
            List<(long Id, Dictionary<string, string> Fields)> rows)
        {
            if (ApplyingReview)
                return rows;
            if (!CanReadTable(table))
                return new List<(long, Dictionary<string, string>)>();

            var result = new List<(long, Dictionary<string, string>)>();
            foreach (var (id, fields) in rows)
            {
                if (IsRecordBlocked(fields))
                    continue;
                result.Add((id, StripHidden(table, fields)));
            }

            return result;
        }

        private static Dictionary<string, string> StripHidden(
            string table,
            Dictionary<string, string> row)
        {
            table = TableKey(table);
            if (!Policies.TryGetValue(table, out var policy) || policy.HideColumns.Count == 0)
                return row;

            var copy = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
            foreach (var column in policy.HideColumns)
            {
                foreach (var key in copy.Keys.ToList())
                {
                    if (key.Equals(column, StringComparison.OrdinalIgnoreCase))
                        copy.Remove(key);
                }
            }

            return copy;
        }

        public static bool CanReview()
        {
            foreach (var item in TableAccess.All)
            {
                if (TableAccess.Can(item.Key) && WriteMode(TableFile(item.Key)) == DataWriteMode.Auto)
                    return true;
            }

            return false;
        }

        public static bool IsColumnHidden(string table, string column)
        {
            if (string.IsNullOrWhiteSpace(column))
                return false;
            table = TableKey(table);
            if (!Policies.TryGetValue(table, out var policy))
                return false;
            return policy.HideColumns.Any(name =>
                name.Equals(column, StringComparison.OrdinalIgnoreCase));
        }

        public static bool IsCompanyBlocked(Dictionary<string, string>? record) =>
            IsRecordBlocked(record);

        public static bool IsRecordBlocked(Dictionary<string, string>? record)
        {
            if (record == null)
                return false;
            if (BlockedCompanies.Count == 0 && BlockedKeys.Count == 0)
                return false;
            if (BlockedKeys.Count == 0)
                ExpandBlockedKeys();

            foreach (var key in RecordFields)
            {
                string value = DataFiles.GetRecord(record, key).Trim();
                if (value.Length > 0 && BlockedKeys.Contains(value))
                    return true;
            }

            return false;
        }

        public static bool IsCompanyBlocked(string? value)
        {
            value = (value ?? "").Trim();
            if (value.Length == 0)
                return false;
            if (BlockedKeys.Count == 0 && BlockedCompanies.Count > 0)
                ExpandBlockedKeys();
            return BlockedKeys.Contains(value) || BlockedCompanies.Contains(value);
        }

        /// <summary>
        /// If ACME is blocked, also block its vendor/customer/item codes and names
        /// so purchases, sales, and invoices for that party or product stay hidden.
        /// </summary>
        private static void ExpandBlockedKeys()
        {
            BlockedKeys.Clear();
            foreach (var seed in BlockedCompanies)
                BlockedKeys.Add(seed);
            if (BlockedKeys.Count == 0 || string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            Absorb(DataFiles.ReadAllRecords(DataFiles.Customers), PartyFields);
            Absorb(DataFiles.ReadAllRecords(DataFiles.Vendors), PartyFields);
            Absorb(DataFiles.ReadAllRecords(DataFiles.ItemCodes), ItemFields);
        }

        private static void Absorb(
            List<Dictionary<string, string>> rows,
            string[] fields)
        {
            foreach (var record in rows)
            {
                var tokens = new List<string>();
                bool hit = false;
                foreach (var field in fields)
                {
                    string value = DataFiles.GetRecord(record, field).Trim();
                    if (value.Length == 0)
                        continue;
                    tokens.Add(value);
                    if (BlockedCompanies.Contains(value))
                        hit = true;
                }

                if (!hit)
                    continue;
                foreach (var token in tokens)
                    BlockedKeys.Add(token);
            }
        }

        public static IReadOnlyCollection<string> BlockedList() => BlockedCompanies;

        public static DataTablePolicy PolicyFor(string tableKey)
        {
            tableKey = TableKey(tableKey);
            return Policies.TryGetValue(tableKey, out var policy)
                ? policy
                : new DataTablePolicy();
        }

        /// <summary>
        /// Combine several group policies. Allowed wins: a table, column, company, or write
        /// action is blocked only when every group blocks it.
        /// </summary>
        public static string Merge(IEnumerable<string> jsons)
        {
            var list = (jsons ?? Array.Empty<string>()).Select(json => json ?? "").ToList();
            if (list.Count == 0)
                return "";
            if (list.Count == 1)
                return list[0];

            var parsed = new List<(HashSet<string> Denied, Dictionary<string, DataTablePolicy> Policies, HashSet<string> Blocked)>();
            foreach (var json in list)
            {
                var denied = TableAccess.ParseDenied(json);
                var policies = new Dictionary<string, DataTablePolicy>(StringComparer.OrdinalIgnoreCase);
                var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                Parse(json, policies, blocked);
                parsed.Add((denied, policies, blocked));
            }

            var mergedDenied = new HashSet<string>(parsed[0].Denied, StringComparer.OrdinalIgnoreCase);
            foreach (var item in parsed.Skip(1))
                mergedDenied.IntersectWith(item.Denied);

            var tables = new HashSet<string>(
                TableAccess.All.Select(item => item.Key),
                StringComparer.OrdinalIgnoreCase);
            foreach (var item in parsed)
            {
                foreach (var key in item.Policies.Keys)
                    tables.Add(key);
            }

            var mergedPolicies = new Dictionary<string, DataTablePolicy>(StringComparer.OrdinalIgnoreCase);
            foreach (var table in tables)
            {
                if (mergedDenied.Contains(table))
                    continue;

                var allowing = parsed.Where(item => !item.Denied.Contains(table)).ToList();
                if (allowing.Count == 0)
                    continue;

                var write = DataWriteMode.View;
                foreach (var item in allowing)
                {
                    var mode = item.Policies.TryGetValue(table, out var policy)
                        ? policy.Write
                        : DataWriteMode.Auto;
                    if ((int)mode > (int)write)
                        write = mode;
                }

                HashSet<string>? hidden = null;
                foreach (var item in allowing)
                {
                    var cols = item.Policies.TryGetValue(table, out var policy)
                        ? policy.HideColumns
                        : new List<string>();
                    var set = new HashSet<string>(
                        cols.Select(name => name.Trim()).Where(name => name.Length > 0),
                        StringComparer.OrdinalIgnoreCase);
                    if (hidden == null)
                        hidden = set;
                    else
                        hidden.IntersectWith(set);
                }

                mergedPolicies[table] = new DataTablePolicy
                {
                    Write = write,
                    HideColumns = hidden?.ToList() ?? new List<string>()
                };
            }

            HashSet<string>? blockedMerge = null;
            foreach (var item in parsed)
            {
                if (blockedMerge == null)
                    blockedMerge = new HashSet<string>(item.Blocked, StringComparer.OrdinalIgnoreCase);
                else
                    blockedMerge.IntersectWith(item.Blocked);
            }

            return BuildJson(mergedDenied, mergedPolicies, blockedMerge ?? new HashSet<string>());
        }

        /// <summary>
        /// Apply a user's specific overrides on top of group settings. An explicit
        /// allow or deny on the user wins. Missing keys still follow the groups.
        /// </summary>
        public static string Overlay(string baseline, string overlay)
        {
            overlay = (overlay ?? "").Trim();
            if (overlay.Length == 0)
                return baseline ?? "";
            baseline = (baseline ?? "").Trim();
            if (baseline.Length == 0)
                return overlay;

            var denied = TableAccess.ParseDenied(baseline);
            var policies = new Dictionary<string, DataTablePolicy>(StringComparer.OrdinalIgnoreCase);
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Parse(baseline, policies, blocked);

            try
            {
                using var doc = JsonDocument.Parse(overlay);
                var root = doc.RootElement;
                foreach (var pair in root.EnumerateObject())
                {
                    if (pair.Name.StartsWith('$'))
                        continue;
                    if (pair.Value.ValueKind == JsonValueKind.False)
                        denied.Add(TableKey(pair.Name));
                    else if (pair.Value.ValueKind == JsonValueKind.True)
                        denied.Remove(TableKey(pair.Name));
                }

                if (root.TryGetProperty(WriteKey, out var write) && write.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in write.EnumerateObject())
                    {
                        string key = TableKey(pair.Name);
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        policy.Write = ParseMode(pair.Value.GetString());
                    }
                }

                if (root.TryGetProperty(HideKey, out var hide) && hide.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in hide.EnumerateObject())
                    {
                        string key = TableKey(pair.Name);
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        policy.HideColumns = new List<string>();
                        if (pair.Value.ValueKind != JsonValueKind.Array)
                            continue;
                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            string name = item.GetString()?.Trim() ?? "";
                            if (name.Length > 0)
                                policy.HideColumns.Add(name);
                        }
                    }
                }

                if (root.TryGetProperty(BlockKey, out var block) && block.ValueKind == JsonValueKind.Array)
                {
                    blocked.Clear();
                    foreach (var item in block.EnumerateArray())
                    {
                        string name = item.GetString()?.Trim() ?? "";
                        if (name.Length > 0)
                            blocked.Add(name);
                    }
                }
            }
            catch
            {
                return baseline;
            }

            return BuildJson(denied, policies, blocked);
        }

        /// <summary>
        /// Store only the settings that differ from the group baseline so later group
        /// changes still apply to everything the user did not set themselves.
        /// </summary>
        public static string Diff(string baseline, string updated, IEnumerable<string>? tables = null)
        {
            var keys = (tables ?? TableAccess.All.Select(item => item.Key)).ToList();
            var baseDenied = TableAccess.ParseDenied(baseline);
            var newDenied = TableAccess.ParseDenied(updated);
            var basePolicies = new Dictionary<string, DataTablePolicy>(StringComparer.OrdinalIgnoreCase);
            var newPolicies = new Dictionary<string, DataTablePolicy>(StringComparer.OrdinalIgnoreCase);
            var baseBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var newBlocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Parse(baseline, basePolicies, baseBlocked);
            Parse(updated, newPolicies, newBlocked);

            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                bool any = false;
                foreach (var key in keys)
                {
                    if (baseDenied.Contains(key) == newDenied.Contains(key))
                        continue;
                    writer.WriteBoolean(key, !newDenied.Contains(key));
                    any = true;
                }

                var writeDiff = new Dictionary<string, DataWriteMode>(StringComparer.OrdinalIgnoreCase);
                var hideDiff = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
                foreach (var key in keys)
                {
                    var basePolicy = basePolicies.TryGetValue(key, out var left) ? left : new DataTablePolicy();
                    var newPolicy = newPolicies.TryGetValue(key, out var right) ? right : new DataTablePolicy();
                    if (basePolicy.Write != newPolicy.Write)
                        writeDiff[key] = newPolicy.Write;
                    if (!SameNames(basePolicy.HideColumns, newPolicy.HideColumns))
                        hideDiff[key] = newPolicy.HideColumns;
                }

                if (writeDiff.Count > 0)
                {
                    writer.WritePropertyName(WriteKey);
                    writer.WriteStartObject();
                    foreach (var pair in writeDiff)
                        writer.WriteString(pair.Key, ModeName(pair.Value));
                    writer.WriteEndObject();
                    any = true;
                }

                if (hideDiff.Count > 0)
                {
                    writer.WritePropertyName(HideKey);
                    writer.WriteStartObject();
                    foreach (var pair in hideDiff)
                    {
                        writer.WritePropertyName(pair.Key);
                        writer.WriteStartArray();
                        foreach (var col in pair.Value)
                        {
                            if (col.Trim().Length > 0)
                                writer.WriteStringValue(col.Trim());
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                    any = true;
                }

                if (!SameNames(baseBlocked, newBlocked))
                {
                    writer.WritePropertyName(BlockKey);
                    writer.WriteStartArray();
                    foreach (var name in newBlocked)
                    {
                        if (name.Trim().Length > 0)
                            writer.WriteStringValue(name.Trim());
                    }

                    writer.WriteEndArray();
                    any = true;
                }

                writer.WriteEndObject();
                if (!any)
                    return "";
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        private static bool SameNames(IEnumerable<string> left, IEnumerable<string> right)
        {
            var a = new HashSet<string>(
                left.Select(name => name.Trim()).Where(name => name.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            var b = new HashSet<string>(
                right.Select(name => name.Trim()).Where(name => name.Length > 0),
                StringComparer.OrdinalIgnoreCase);
            return a.SetEquals(b);
        }

        public static string BuildJson(
            IEnumerable<string> denied,
            IReadOnlyDictionary<string, DataTablePolicy> policies,
            IEnumerable<string> blocked)
        {
            using var stream = new MemoryStream();
            using (var writer = new Utf8JsonWriter(stream))
            {
                writer.WriteStartObject();
                foreach (var key in denied)
                {
                    if (key.Length > 0)
                        writer.WriteBoolean(key, false);
                }

                writer.WritePropertyName(WriteKey);
                writer.WriteStartObject();
                foreach (var pair in policies)
                    writer.WriteString(TableKey(pair.Key), ModeName(pair.Value.Write));
                writer.WriteEndObject();

                writer.WritePropertyName(HideKey);
                writer.WriteStartObject();
                foreach (var pair in policies)
                {
                    writer.WritePropertyName(TableKey(pair.Key));
                    writer.WriteStartArray();
                    foreach (var col in pair.Value.HideColumns)
                    {
                        if (col.Trim().Length > 0)
                            writer.WriteStringValue(col.Trim());
                    }

                    writer.WriteEndArray();
                }

                writer.WriteEndObject();

                writer.WritePropertyName(BlockKey);
                writer.WriteStartArray();
                foreach (var company in blocked)
                {
                    string name = company.Trim();
                    if (name.Length > 0)
                        writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        public static string TableFile(string accessKey) => accessKey switch
        {
            TableAccess.Purchases => DataFiles.PurchaseSales,
            TableAccess.Sales => DataFiles.Sales,
            TableAccess.Invoices => DataFiles.Invoices,
            TableAccess.Customers => DataFiles.Customers,
            TableAccess.Vendors => DataFiles.Vendors,
            TableAccess.Items => DataFiles.ItemCodes,
            TableAccess.Banking => DataFiles.BankTransactions,
            TableAccess.Debits => DataFiles.Debits,
            TableAccess.Credits => DataFiles.Credits,
            _ => accessKey
        };

        public static string TableKey(string fileOrKey)
        {
            fileOrKey = (fileOrKey ?? "").Trim();
            if (fileOrKey.Equals(DataFiles.PurchaseSales, StringComparison.OrdinalIgnoreCase) ||
                fileOrKey.Equals(TableAccess.Purchases, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Purchases;
            if (fileOrKey.Equals(DataFiles.Sales, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Sales;
            if (fileOrKey.Equals(DataFiles.Invoices, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Invoices;
            if (fileOrKey.Equals(DataFiles.Customers, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Customers;
            if (fileOrKey.Equals(DataFiles.Vendors, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Vendors;
            if (fileOrKey.Equals(DataFiles.ItemCodes, StringComparison.OrdinalIgnoreCase) ||
                fileOrKey.Equals(TableAccess.Items, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Items;
            if (fileOrKey.Equals(DataFiles.BankTransactions, StringComparison.OrdinalIgnoreCase) ||
                fileOrKey.Equals(TableAccess.Banking, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Banking;
            if (fileOrKey.Equals(DataFiles.Debits, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Debits;
            if (fileOrKey.Equals(DataFiles.Credits, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Credits;
            return fileOrKey;
        }

        public static string ModeLabel(DataWriteMode mode) => mode switch
        {
            DataWriteMode.View => "View only",
            DataWriteMode.Confirm => "Confirm first",
            _ => "Add / edit / delete"
        };

        public static DataWriteMode ParseMode(string? text)
        {
            text = (text ?? "").Trim();
            if (text.Equals("view", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("View", StringComparison.OrdinalIgnoreCase))
                return DataWriteMode.View;
            if (text.Equals("confirm", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Confirm", StringComparison.OrdinalIgnoreCase))
                return DataWriteMode.Confirm;
            return DataWriteMode.Auto;
        }

        private static string ModeName(DataWriteMode mode) => mode switch
        {
            DataWriteMode.View => "view",
            DataWriteMode.Confirm => "confirm",
            _ => "auto"
        };
    }
}
