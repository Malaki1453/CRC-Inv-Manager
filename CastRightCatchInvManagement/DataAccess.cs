using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>Outcome of an insert, update, or delete: saved, queued for review, or denied.</summary>
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

    /// <summary>How a user may change a table: view only, queue for review, or write immediately.</summary>
    internal enum DataWriteMode
    {
        View,
        Confirm,
        Auto
    }

    /// <summary>Write mode and hidden columns for one table in table_access JSON.</summary>
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

        /// <summary>Load write modes, hidden columns, and blocked companies from table_access JSON.</summary>
        public static void Apply(string json)
        {
            Policies.Clear();
            BlockedCompanies.Clear();
            BlockedKeys.Clear();
            Parse(json, Policies, BlockedCompanies);
            ExpandBlockedKeys();
        }

        /// <summary>Fill <paramref name="policies"/> and <paramref name="blocked"/> from $write, $hide, and $block.</summary>
        public static void Parse(
            string json,
            Dictionary<string, DataTablePolicy> policies,
            HashSet<string> blocked)
        {
            json = (json ?? "").Trim();
            // Empty JSON (empty LockedAdminJson) means ALL tables, auto-write, and no extra blocks.
            if (json.Length == 0)
                return;

            try
            {
                using var doc = JsonDocument.Parse(json);
                var root = doc.RootElement;
                // $write is optional; missing means Auto for every allowed table.
                if (root.TryGetProperty(WriteKey, out var write) &&
                    write.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in write.EnumerateObject())
                    {
                        string key = TableKey(pair.Name);
                        // First mention of a table creates the policy bag.
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        policy.Write = ParseMode(pair.Value.GetString());
                    }
                }

                // $hide is optional; missing means every column stays visible.
                if (root.TryGetProperty(HideKey, out var hide) &&
                    hide.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in hide.EnumerateObject())
                    {
                        // Hide lists are string arrays; skip a malformed value.
                        if (pair.Value.ValueKind != JsonValueKind.Array)
                            continue;
                        string key = TableKey(pair.Name);
                        // Hide can appear before $write for the same table.
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            string name = item.GetString()?.Trim() ?? "";
                            // Blank hide entries would match every column name.
                            if (name.Length > 0)
                                policy.HideColumns.Add(name);
                        }
                    }
                }

                // $block is optional; missing means no companies are hidden.
                if (root.TryGetProperty(BlockKey, out var block) &&
                    block.ValueKind == JsonValueKind.Array)
                {
                    foreach (var item in block.EnumerateArray())
                    {
                        string name = item.GetString()?.Trim() ?? "";
                        // Blank block entries would hide empty party fields.
                        if (name.Length > 0)
                            blocked.Add(name);
                    }
                }
            }
            // Keep defaults (all tables, auto-write) when the document cannot be parsed.
            catch
            {
                // keep defaults
            }
        }

        /// <summary>Drop in-memory policies, used on sign-out.</summary>
        public static void Clear()
        {
            Policies.Clear();
            BlockedCompanies.Clear();
            BlockedKeys.Clear();
        }

        /// <summary>Write mode for a table. Review accepts skip Confirm so pending rows can be applied.</summary>
        public static DataWriteMode WriteMode(string table)
        {
            // Pending Accept must write through even when the reviewer is on Confirm.
            if (ApplyingReview)
                return DataWriteMode.Auto;
            table = TableKey(table);
            return Policies.TryGetValue(table, out var policy) ? policy.Write : DataWriteMode.Auto;
        }

        /// <summary>True when the user may insert, update, or delete (including queued Confirm).</summary>
        public static bool CanMutate(string table) =>
            WriteMode(table) != DataWriteMode.View;

        /// <summary>True when the user may read this table, including pending_changes for reviewers.</summary>
        public static bool CanReadTable(string table)
        {
            // Review loads the real row, including blocked parties, to apply or restore it.
            if (ApplyingReview)
                return true;
            table = TableKey(table);
            // Pending queue is not a regular table; only auto-write users may review it.
            if (table.Equals(DataFiles.PendingChanges, StringComparison.OrdinalIgnoreCase))
                return CanReview();
            foreach (var item in TableAccess.All)
            {
                // Match either the access key or the SQLite table name.
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
            // Review needs the unfiltered row to accept or restore it.
            if (ApplyingReview)
                return rows;
            // Denied tables return no rows rather than throwing.
            if (!CanReadTable(table))
                return new List<Dictionary<string, string>>();

            var result = new List<Dictionary<string, string>>();
            foreach (var row in rows)
            {
                // Blocked parties and products must not appear in grids or lookups.
                if (IsRecordBlocked(row))
                    continue;
                result.Add(StripHidden(table, row));
            }

            return result;
        }

        /// <summary>Same as the dictionary overload, keeping SQLite row ids for updates.</summary>
        public static List<(long Id, Dictionary<string, string> Fields)> RestrictRows(
            string table,
            List<(long Id, Dictionary<string, string> Fields)> rows)
        {
            // Review needs the unfiltered row to accept or restore it.
            if (ApplyingReview)
                return rows;
            // Denied tables return no rows rather than throwing.
            if (!CanReadTable(table))
                return new List<(long, Dictionary<string, string>)>();

            var result = new List<(long, Dictionary<string, string>)>();
            foreach (var (id, fields) in rows)
            {
                // Blocked parties and products must not appear in grids or lookups.
                if (IsRecordBlocked(fields))
                    continue;
                result.Add((id, StripHidden(table, fields)));
            }

            return result;
        }

        /// <summary>Copy the row without columns this user is not allowed to see.</summary>
        private static Dictionary<string, string> StripHidden(
            string table,
            Dictionary<string, string> row)
        {
            table = TableKey(table);
            // No hide list means the full row is allowed.
            if (!Policies.TryGetValue(table, out var policy) || policy.HideColumns.Count == 0)
                return row;

            var copy = new Dictionary<string, string>(row, StringComparer.OrdinalIgnoreCase);
            foreach (var column in policy.HideColumns)
            {
                foreach (var key in copy.Keys.ToList())
                {
                    // Hide lists are case-insensitive so "Account Number" matches "account number".
                    if (key.Equals(column, StringComparison.OrdinalIgnoreCase))
                        copy.Remove(key);
                }
            }

            return copy;
        }

        /// <summary>True when this user has auto-write on at least one allowed table and may review pending changes.</summary>
        public static bool CanReview()
        {
            foreach (var item in TableAccess.All)
            {
                // Reviewers need auto-write on at least one table they can open.
                if (TableAccess.Can(item.Key) && WriteMode(TableFile(item.Key)) == DataWriteMode.Auto)
                    return true;
            }

            return false;
        }

        /// <summary>True when this column is in the user's hide list for the table.</summary>
        public static bool IsColumnHidden(string table, string column)
        {
            // Blank column names are not in any hide list.
            if (string.IsNullOrWhiteSpace(column))
                return false;
            table = TableKey(table);
            // No policy for this table means every column is visible.
            if (!Policies.TryGetValue(table, out var policy))
                return false;
            return policy.HideColumns.Any(name =>
                name.Equals(column, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>True when the row's vendor, customer, or item is on the block list.</summary>
        public static bool IsCompanyBlocked(Dictionary<string, string>? record) =>
            IsRecordBlocked(record);

        /// <summary>True when any party or product field on the row matches a blocked key.</summary>
        public static bool IsRecordBlocked(Dictionary<string, string>? record)
        {
            // Null rows are not blocked parties.
            if (record == null)
                return false;
            // No blocks configured: every row is visible.
            if (BlockedCompanies.Count == 0 && BlockedKeys.Count == 0)
                return false;
            // Expand related codes/names the first time we check a row.
            if (BlockedKeys.Count == 0)
                ExpandBlockedKeys();

            foreach (var key in RecordFields)
            {
                string value = DataFiles.GetRecord(record, key).Trim();
                // Empty cells never match a blocked company or product.
                if (value.Length > 0 && BlockedKeys.Contains(value))
                    return true;
            }

            return false;
        }

        /// <summary>True when a typed code or name is a blocked company or an expanded related key.</summary>
        public static bool IsCompanyBlocked(string? value)
        {
            value = (value ?? "").Trim();
            // Blank typed values are not blocked companies.
            if (value.Length == 0)
                return false;
            // Expand related codes when the seed list exists but keys were not built yet.
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
            // Nothing to expand without seeds or a database to read related codes from.
            if (BlockedKeys.Count == 0 || string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            Absorb(DataFiles.ReadAllRecords(DataFiles.Customers), PartyFields);
            Absorb(DataFiles.ReadAllRecords(DataFiles.Vendors), PartyFields);
            Absorb(DataFiles.ReadAllRecords(DataFiles.ItemCodes), ItemFields);
        }

        /// <summary>If a master-table row matches a blocked seed, add all of its codes and names to BlockedKeys.</summary>
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
                    // Skip empty master-table cells so they are not treated as blocked keys.
                    if (value.Length == 0)
                        continue;
                    tokens.Add(value);
                    // Any matching seed (code, name, or company) blocks the whole row's tokens.
                    if (BlockedCompanies.Contains(value))
                        hit = true;
                }

                // Unrelated vendors/items stay visible.
                if (!hit)
                    continue;
                foreach (var token in tokens)
                    BlockedKeys.Add(token);
            }
        }

        /// <summary>Companies listed in $block, before related codes are expanded.</summary>
        public static IReadOnlyCollection<string> BlockedList() => BlockedCompanies;

        /// <summary>Policy for a table, or defaults (auto-write, no hidden columns) when unset.</summary>
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
            // No groups: empty JSON means ALL tables allowed, not only Settings/Users.
            if (list.Count == 0)
                return "";
            // One group needs no intersect; keep its JSON as-is.
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
                // Denied by every group stays denied; skip write/hide merge.
                if (mergedDenied.Contains(table))
                    continue;

                var allowing = parsed.Where(item => !item.Denied.Contains(table)).ToList();
                // Every group denied this table; skip write/hide (already in mergedDenied).
                if (allowing.Count == 0)
                    continue;

                var write = DataWriteMode.View;
                foreach (var item in allowing)
                {
                    var mode = item.Policies.TryGetValue(table, out var policy)
                        ? policy.Write
                        : DataWriteMode.Auto;
                    // Allowed wins: Auto beats Confirm beats View.
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
                    // First allowing group seeds the hide intersect.
                    if (hidden == null)
                        hidden = set;
                    // A column stays hidden only when every allowing group hides it.
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
                // First group seeds the blocked intersect.
                if (blockedMerge == null)
                    blockedMerge = new HashSet<string>(item.Blocked, StringComparer.OrdinalIgnoreCase);
                // A company stays blocked only when every group blocks it.
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
            // No per-user JSON: group settings stand alone.
            if (overlay.Length == 0)
                return baseline ?? "";
            baseline = (baseline ?? "").Trim();
            // No groups: the user override is the whole policy.
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
                    // $write / $hide / $block are handled below, not as table flags.
                    if (pair.Name.StartsWith('$'))
                        continue;
                    // Explicit false on the user overlay denies the table.
                    if (pair.Value.ValueKind == JsonValueKind.False)
                        denied.Add(TableKey(pair.Name));
                    // Explicit true on the user overlay re-allows a group-denied table.
                    else if (pair.Value.ValueKind == JsonValueKind.True)
                        denied.Remove(TableKey(pair.Name));
                }

                // User $write replaces group write mode for named tables.
                if (root.TryGetProperty(WriteKey, out var write) && write.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in write.EnumerateObject())
                    {
                        string key = TableKey(pair.Name);
                        // Overlay can mention a table the groups never listed.
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        policy.Write = ParseMode(pair.Value.GetString());
                    }
                }

                // User $hide replaces the group's hide list for named tables.
                if (root.TryGetProperty(HideKey, out var hide) && hide.ValueKind == JsonValueKind.Object)
                {
                    foreach (var pair in hide.EnumerateObject())
                    {
                        string key = TableKey(pair.Name);
                        // Overlay can hide columns on a table the groups never listed.
                        if (!policies.TryGetValue(key, out var policy))
                        {
                            policy = new DataTablePolicy();
                            policies[key] = policy;
                        }

                        policy.HideColumns = new List<string>();
                        // A non-array $hide entry clears the list and skips names.
                        if (pair.Value.ValueKind != JsonValueKind.Array)
                            continue;
                        foreach (var item in pair.Value.EnumerateArray())
                        {
                            string name = item.GetString()?.Trim() ?? "";
                            // Blank hide entries would match every column name.
                            if (name.Length > 0)
                                policy.HideColumns.Add(name);
                        }
                    }
                }

                // User $block replaces the group's blocked-company list when present.
                if (root.TryGetProperty(BlockKey, out var block) && block.ValueKind == JsonValueKind.Array)
                {
                    blocked.Clear();
                    foreach (var item in block.EnumerateArray())
                    {
                        string name = item.GetString()?.Trim() ?? "";
                        // Blank block entries would hide empty party fields.
                        if (name.Length > 0)
                            blocked.Add(name);
                    }
                }
            }
            // Bad overlay JSON must not wipe group access.
            catch
            {
                // Bad overlay JSON must not wipe group access.
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
                    // Omit tables that still match the group allow/deny.
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
                    // Store write mode only when it no longer matches the group.
                    if (basePolicy.Write != newPolicy.Write)
                        writeDiff[key] = newPolicy.Write;
                    // Store hide columns only when the list no longer matches the group.
                    if (!SameNames(basePolicy.HideColumns, newPolicy.HideColumns))
                        hideDiff[key] = newPolicy.HideColumns;
                }

                // Omit $write when every table still uses the group mode.
                if (writeDiff.Count > 0)
                {
                    writer.WritePropertyName(WriteKey);
                    writer.WriteStartObject();
                    foreach (var pair in writeDiff)
                        writer.WriteString(pair.Key, ModeName(pair.Value));
                    writer.WriteEndObject();
                    any = true;
                }

                // Omit $hide when every table still uses the group hide list.
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
                            // Skip blank typed hide names.
                            if (col.Trim().Length > 0)
                                writer.WriteStringValue(col.Trim());
                        }

                        writer.WriteEndArray();
                    }

                    writer.WriteEndObject();
                    any = true;
                }

                // Omit $block when the blocked-company list still matches the group.
                if (!SameNames(baseBlocked, newBlocked))
                {
                    writer.WritePropertyName(BlockKey);
                    writer.WriteStartArray();
                    foreach (var name in newBlocked)
                    {
                        // Skip blank typed company names.
                        if (name.Trim().Length > 0)
                            writer.WriteStringValue(name.Trim());
                    }

                    writer.WriteEndArray();
                    any = true;
                }

                writer.WriteEndObject();
                // Identical to the group baseline: store no override.
                if (!any)
                    return "";
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>True when two name lists contain the same entries, ignoring order and blanks.</summary>
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

        /// <summary>Serialize denied tables, write modes, hidden columns, and blocked companies.</summary>
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
                    // Blank keys would serialize as an unnamed false flag.
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
                        // Skip blank hide names so JSON stays a clean string array.
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
                    // Skip blank blocked-company names.
                    if (name.Length > 0)
                        writer.WriteStringValue(name);
                }

                writer.WriteEndArray();
                writer.WriteEndObject();
            }

            return System.Text.Encoding.UTF8.GetString(stream.ToArray());
        }

        /// <summary>Map an access key (purchases, items, …) to the SQLite table name.</summary>
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

        /// <summary>Normalize a file name or access key to the TableAccess key used in JSON.</summary>
        public static string TableKey(string fileOrKey)
        {
            fileOrKey = (fileOrKey ?? "").Trim();
            // Purchases table and "purchases" key both map to the purchases policy.
            if (fileOrKey.Equals(DataFiles.PurchaseSales, StringComparison.OrdinalIgnoreCase) ||
                fileOrKey.Equals(TableAccess.Purchases, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Purchases;
            // Sales SQLite table name maps to the sales access key.
            if (fileOrKey.Equals(DataFiles.Sales, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Sales;
            // Invoices SQLite table name maps to the invoices access key.
            if (fileOrKey.Equals(DataFiles.Invoices, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Invoices;
            // Customers SQLite table name maps to the customers access key.
            if (fileOrKey.Equals(DataFiles.Customers, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Customers;
            // Vendors SQLite table name maps to the vendors access key.
            if (fileOrKey.Equals(DataFiles.Vendors, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Vendors;
            // Item codes table and "items" key both map to the items policy.
            if (fileOrKey.Equals(DataFiles.ItemCodes, StringComparison.OrdinalIgnoreCase) ||
                fileOrKey.Equals(TableAccess.Items, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Items;
            // Bank transactions table and "banking" key both map to the banking policy.
            if (fileOrKey.Equals(DataFiles.BankTransactions, StringComparison.OrdinalIgnoreCase) ||
                fileOrKey.Equals(TableAccess.Banking, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Banking;
            // Debits SQLite table name maps to the debits access key.
            if (fileOrKey.Equals(DataFiles.Debits, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Debits;
            // Credits SQLite table name maps to the credits access key.
            if (fileOrKey.Equals(DataFiles.Credits, StringComparison.OrdinalIgnoreCase))
                return TableAccess.Credits;
            return fileOrKey;
        }

        /// <summary>User-facing write-mode label for access editors.</summary>
        public static string ModeLabel(DataWriteMode mode) => mode switch
        {
            DataWriteMode.View => "View only",
            DataWriteMode.Confirm => "Confirm first",
            _ => "Add / edit / delete"
        };

        /// <summary>Parse stored or UI text into View, Confirm, or Auto (default).</summary>
        public static DataWriteMode ParseMode(string? text)
        {
            text = (text ?? "").Trim();
            // Stored "view" or UI "View only" both mean read-only.
            if (text.Equals("view", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("View", StringComparison.OrdinalIgnoreCase))
                return DataWriteMode.View;
            // Stored "confirm" or UI "Confirm first" both queue writes for Review.
            if (text.Equals("confirm", StringComparison.OrdinalIgnoreCase) ||
                text.StartsWith("Confirm", StringComparison.OrdinalIgnoreCase))
                return DataWriteMode.Confirm;
            return DataWriteMode.Auto;
        }

        /// <summary>Stored write-mode token in table_access JSON.</summary>
        private static string ModeName(DataWriteMode mode) => mode switch
        {
            DataWriteMode.View => "view",
            DataWriteMode.Confirm => "confirm",
            _ => "auto"
        };
    }
}
