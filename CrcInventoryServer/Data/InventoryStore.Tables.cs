using CrcInventory.Protocol;
using System.Data.Common;

namespace CrcInventory.Server;

internal sealed partial class InventoryStore
{
    public string[] Headers(string table, bool viewOld)
    {
        RequireTable(table);
        lock (_gate)
        {
            var expected = Schema.Headers(table).ToList();
            var actual = TableColumns(table, viewOld)
                .Where(c => c != "id" && c != "term_start")
                .ToList();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var col in expected.Concat(actual))
            {
                if (!used.Add(col))
                    continue;
                list.Add(col);
            }

            return list.ToArray();
        }
    }

    public List<Dictionary<string, string>> Read(string table, bool viewOld)
    {
        RequireTable(table);
        lock (_gate)
        {
            var result = new List<Dictionary<string, string>>();
            if (viewOld && Schema.IsProcessTable(table))
                AppendRows(table, archive: true, result);
            AppendRows(table, archive: false, result);
            return result;
        }
    }

    public List<(long Id, Dictionary<string, string> Fields)> ReadWithIds(string table, bool viewOld)
    {
        RequireTable(table);
        lock (_gate)
        {
            var result = new List<(long, Dictionary<string, string>)>();
            if (viewOld && Schema.IsProcessTable(table))
                AppendRowsWithIds(table, archive: true, result);
            AppendRowsWithIds(table, archive: false, result);
            return result;
        }
    }

    public void Insert(string table, Dictionary<string, string> values)
    {
        RequireTable(table);
        lock (_gate)
        {
            var headers = HeadersUnlocked(table, viewOld: false);
            using var db = Open();
            using var cmd = db.CreateCommand();
            var cols = new List<string> { Quote("term_start") };
            var pars = new List<string> { "$term" };
            cmd.AddParam("$term", CompletionStamp(table, values));
            for (int i = 0; i < headers.Length; i++)
            {
                string name = headers[i];
                cols.Add(Quote(name));
                string p = "$c" + i;
                pars.Add(p);
                cmd.AddParam(p, SecretProtect.StoreField(table, name, Schema.CellValue(values, name)));
            }

            cmd.CommandText =
                $"INSERT INTO {Quote(table)} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
            cmd.Exec(_engine);
        }
    }

    public int InsertMany(string table, IEnumerable<Dictionary<string, string>> rows)
    {
        RequireTable(table);
        lock (_gate)
        {
            var headers = HeadersUnlocked(table, viewOld: false);
            int count = 0;
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var values in rows)
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                var cols = new List<string> { Quote("term_start") };
                var pars = new List<string> { "$term" };
                cmd.AddParam("$term", CompletionStamp(table, values));
                for (int i = 0; i < headers.Length; i++)
                {
                    string name = headers[i];
                    cols.Add(Quote(name));
                    string p = "$c" + i;
                    pars.Add(p);
                    cmd.AddParam(p, SecretProtect.StoreField(table, name, Schema.CellValue(values, name)));
                }

                cmd.CommandText =
                    $"INSERT INTO {Quote(table)} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
                cmd.Exec(_engine);
                count++;
            }

            tx.Commit();
            return count;
        }
    }

    public bool UpdateById(string table, long id, Dictionary<string, string> values)
    {
        RequireTable(table);
        lock (_gate)
        {
            bool archive = id < 0;
            long rawId = archive ? -id : id;
            if (rawId <= 0)
                return false;

            var headers = HeadersUnlocked(table, viewOld: archive);
            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            var sets = new List<string> { $"{Quote("term_start")} = $term" };
            cmd.AddParam("$term", CompletionStamp(table, values));
            for (int i = 0; i < headers.Length; i++)
            {
                string name = headers[i];
                string p = "$c" + i;
                sets.Add($"{Quote(name)} = {p}");
                cmd.AddParam(p, SecretProtect.StoreField(table, name, Schema.CellValue(values, name)));
            }

            cmd.AddParam("$id", rawId);
            cmd.CommandText = $"UPDATE {Quote(table)} SET {string.Join(",", sets)} WHERE id = $id;";
            return cmd.Exec(_engine) > 0;
        }
    }

    public void EnsureColumns(string table, IEnumerable<string> columns, bool viewOld)
    {
        RequireTable(table);
        lock (_gate)
        {
            EnsureColumnsOn(table, columns, archive: false);
            if (viewOld && Schema.IsProcessTable(table))
                EnsureColumnsOn(table, columns, archive: true);
        }
    }

    public int Count(string table, bool viewOld)
    {
        RequireTable(table);
        lock (_gate)
        {
            int total = CountIn(table, archive: false);
            if (viewOld && Schema.IsProcessTable(table))
                total += CountIn(table, archive: true);
            return total;
        }
    }

    public int ArchiveCompleted(DateTime? term = null)
    {
        lock (_gate)
        {
            string fallbackTerm = TermKey(term ?? TermStartUnlocked());
            int moved = 0;

            foreach (var table in Schema.ProcessTables)
            {
                var liveColumns = TableColumnsFrom(table, archive: false)
                    .Where(c => !c.Equals("id", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                EnsureArchiveColumns(table, liveColumns);

                var completeIds = new List<long>();
                var incompleteIds = new List<long>();
                var toArchive = new List<(Dictionary<string, string> Fields, string TermStart)>();

                foreach (var (id, termStart, fields) in ReadLiveRows(table))
                {
                    if (Schema.IsProcessComplete(table, fields))
                    {
                        string stamp = string.IsNullOrWhiteSpace(termStart) ? fallbackTerm : termStart.Trim();
                        completeIds.Add(id);
                        toArchive.Add((fields, stamp));
                    }
                    else
                    {
                        incompleteIds.Add(id);
                    }
                }

                if (toArchive.Count > 0)
                {
                    using var archive = Open(archive: true);
                    using var tx = archive.BeginTransaction();
                    foreach (var (fields, stamp) in toArchive)
                        InsertRow(archive, tx, table, liveColumns, fields, stamp);
                    tx.Commit();
                    DeleteByIds(table, completeIds);
                    moved += toArchive.Count;
                }

                ClearTermStart(table, incompleteIds);
            }

            return moved;
        }
    }

    public DateTime? LatestTerm()
    {
        lock (_gate)
        {
            DateTime? latest = null;
            using var db = Open();
            foreach (var table in Schema.All)
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"SELECT MAX(term_start) FROM {Quote(table)};";
                var value = cmd.Scalar(_engine)?.ToString();
                if (DateTime.TryParse(value, out var date) && (latest == null || date > latest))
                    latest = date;
            }

            return latest;
        }
    }

    private string[] HeadersUnlocked(string table, bool viewOld)
    {
        var expected = Schema.Headers(table).ToList();
        var actual = TableColumns(table, viewOld)
            .Where(c => c != "id" && c != "term_start" &&
                        !c.Equals("PDF Created", StringComparison.OrdinalIgnoreCase) &&
                        !c.Equals("Volume Received", StringComparison.OrdinalIgnoreCase))
            .ToList();
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var list = new List<string>();
        foreach (var col in expected.Concat(actual))
        {
            if (!used.Add(col))
                continue;
            list.Add(col);
        }

        return list.ToArray();
    }

    private DateTime TermStartUnlocked()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT value FROM app_settings WHERE key = 'term_start';";
        var value = cmd.Scalar(_engine)?.ToString();
        return DateTime.TryParse(value, out var date) ? date : DateTime.Today;
    }

    private string CompletionStamp(string table, Dictionary<string, string> values)
    {
        if (Schema.MasterTables.Contains(table) || Schema.IsProcessComplete(table, values))
            return TermKey(TermStartUnlocked());
        return "";
    }

    private void AppendRows(string table, bool archive, List<Dictionary<string, string>> result)
    {
        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
        using var reader = cmd.Query(_engine);
        while (reader.Read())
            result.Add(ReadRow(table, reader));
    }

    private void AppendRowsWithIds(
        string table,
        bool archive,
        List<(long Id, Dictionary<string, string> Fields)> result)
    {
        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
        using var reader = cmd.Query(_engine);
        while (reader.Read())
        {
            long id = reader.GetInt64(reader.GetOrdinal("id"));
            if (archive)
                id = -id;
            result.Add((id, ReadRow(table, reader)));
        }
    }

    private static Dictionary<string, string> ReadRow(string table, DbDataReader reader)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (int i = 0; i < reader.FieldCount; i++)
        {
            string name = reader.GetName(i);
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("term_start", StringComparison.OrdinalIgnoreCase))
                continue;
            string value = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
            map[name] = SecretProtect.RevealField(table, name, value);
        }

        return map;
    }

    private int CountIn(string table, bool archive)
    {
        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)};";
        return Convert.ToInt32(cmd.Scalar(_engine));
    }

    private void EnsureColumnsOn(string table, IEnumerable<string> columns, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        using var db = Open(archive);
        foreach (var column in columns)
        {
            if (string.IsNullOrWhiteSpace(column) || existing.Contains(column))
                continue;
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
            cmd.Exec(_engine);
            existing.Add(column);
        }
    }

    private void EnsureArchiveColumns(string table, IEnumerable<string> columns)
    {
        EnsureColumnsOn(table, columns, archive: true);
    }

    private List<(long Id, string TermStart, Dictionary<string, string> Fields)> ReadLiveRows(string table)
    {
        var result = new List<(long, string, Dictionary<string, string>)>();
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
        using var reader = cmd.Query(_engine);
        while (reader.Read())
        {
            long id = reader.GetInt64(reader.GetOrdinal("id"));
            int termOrd = reader.GetOrdinal("term_start");
            string term = reader.IsDBNull(termOrd) ? "" : reader.GetValue(termOrd)?.ToString() ?? "";
            result.Add((id, term, ReadRow(table, reader)));
        }

        return result;
    }

    private void InsertRow(
        DbConnection db,
        DbTransaction tx,
        string table,
        IEnumerable<string> columns,
        Dictionary<string, string> values,
        string termStart)
    {
        using var cmd = db.CreateCommand();
        cmd.Transaction = tx;
        var cols = new List<string> { Quote("term_start") };
        var pars = new List<string> { "$term" };
        cmd.AddParam("$term", termStart ?? "");
        int i = 0;
        foreach (var name in columns)
        {
            if (name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("term_start", StringComparison.OrdinalIgnoreCase))
                continue;

            cols.Add(Quote(name));
            string p = "$c" + i;
            pars.Add(p);
            cmd.AddParam(p, SecretProtect.StoreField(table, name, Schema.Lookup(values, name)));
            i++;
        }

        cmd.CommandText =
            $"INSERT INTO {Quote(table)} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
        cmd.Exec(_engine);
    }

    private void DeleteByIds(string table, List<long> ids)
    {
        if (ids.Count == 0)
            return;
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText = $"DELETE FROM {Quote(table)} WHERE id = $id;";
            cmd.AddParam("$id", id);
            cmd.Exec(_engine);
        }

        tx.Commit();
    }

    private void ClearTermStart(string table, List<long> ids)
    {
        if (ids.Count == 0)
            return;
        using var db = Open();
        using var tx = db.BeginTransaction();
        foreach (var id in ids)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            cmd.CommandText =
                $"UPDATE {Quote(table)} SET term_start = '' WHERE id = $id AND term_start <> '';";
            cmd.AddParam("$id", id);
            cmd.Exec(_engine);
        }

        tx.Commit();
    }

    private static void RequireTable(string table)
    {
        if (!Schema.IsKnownTable(table))
            throw new InvalidOperationException("Unknown table.");
    }
}
