using CrcInventory.Protocol;
using Microsoft.Data.Sqlite;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// SQLite access for the live database (crc_inventory.db) and Old Inventory (old_inventory.db).
    /// Master tables, accounts, settings, and PDFs stay in the live file. Process tables can be
    /// archived on roll-over. The Old sidebar toggle reads archive rows plus live rows together.
    /// </summary>
    internal static class SqliteInventory
    {
        public const string FileName = "crc_inventory.db";
        public const string ArchiveFileName = "old_inventory.db";

        /// <summary>Lookups that never move to Old Inventory.</summary>
        private static readonly HashSet<string> MasterTables = new(StringComparer.OrdinalIgnoreCase)
        {
            DataFiles.Customers,
            DataFiles.Vendors,
            DataFiles.ItemCodes
        };

        /// <summary>Term work that rolls into old_inventory.db when completed.</summary>
        public static readonly string[] ProcessTables =
        {
            DataFiles.PurchaseSales,
            DataFiles.Sales,
            DataFiles.Invoices,
            DataFiles.BankTransactions,
            DataFiles.Debits,
            DataFiles.Credits
        };

        /// <summary>Full path of crc_inventory.db, or null when no inventory folder is selected.</summary>
        public static string? GetPath()
        {
            // AppState.InventoryFolder is blank or not on disk; no live database path to return.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, FileName);
        }

        /// <summary>Full path of old_inventory.db, or null when no inventory folder is selected.</summary>
        public static string? GetArchivePath()
        {
            // AppState.InventoryFolder is blank or not on disk; no archive database path to return.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, ArchiveFileName);
        }

        /// <summary>True when the live database file exists, or when a remote session is connected.</summary>
        public static bool Exists()
        {
            // Remote clients talk to the server, so treat the inventory as present without a local file.
            if (DataLink.IsRemote)
                return true;
            // path: full filesystem path of crc_inventory.db, or null if no folder is selected.
            string? path = GetPath();
            return path != null && File.Exists(path);
        }

        /// <summary>Create both database files and any missing tables or columns.</summary>
        public static void EnsureCreated()
        {
            // Point SecretProtect at this folder so sealed values use the same DPAPI scope as the files.
            if (!string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                SecretProtect.UseFolder(AppState.InventoryFolder);
            // Server already created tables; skip the local live/archive files.
            if (DataLink.Try(ServerOps.TableEnsure, new { }, out bool _))
                return;
            EnsureCreated(archive: false);
            EnsureCreated(archive: true);
        }

        /// <summary>Create one database (live or archive) and its tables, then live-only settings/accounts/PDFs.</summary>
        public static void EnsureCreated(bool archive)
        {
            // No folder selected yet; skip creating either database file.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            Directory.CreateDirectory(AppState.InventoryFolder);
            using var db = Open(archive);
            // cmd: reused SQLite command on this live or archive file (PRAGMA, CREATE, later settings).
            using var cmd = db.CreateCommand();
            // WAL is faster locally. Shared/cloud folders need DELETE so two PCs do not fight over -wal files.
            cmd.CommandText = IsSharedLocation(AppState.InventoryFolder)
                ? "PRAGMA journal_mode=DELETE;"
                : "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();

            IEnumerable<string> tables = archive ? ProcessTables : DataFiles.All;
            foreach (var table in tables)
            {
                var columns = DataFiles.GetExpectedHeader(table).Split(',')
                    .Select(h => h.Trim())
                    .Where(h => h.Length > 0)
                    .ToList();
                var defs = new List<string>
                {
                    "id INTEGER PRIMARY KEY AUTOINCREMENT",
                    "term_start TEXT NOT NULL DEFAULT ''"
                };
                defs.AddRange(columns.Select(c => $"{Quote(c)} TEXT"));
                cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} ({string.Join(", ", defs)});";
                cmd.ExecuteNonQuery();
                foreach (var column in columns)
                    EnsureTextColumn(table, column, archive);
                BackfillLiveStatus(table, archive);
                // Purchase/Sales dropped Vendor Invoice # and Volume Received; remove leftover columns.
                if (table == DataFiles.PurchaseSales)
                {
                    DropTextColumn(table, "Vendor Invoice #", archive);
                    DropTextColumn(table, "Volume Received", archive);
                }

                // Sales used Lot # as the purchase PO; remap PO # / Invoice # then drop Lot #.
                if (table == DataFiles.Sales)
                    MigrateSalesLotToPo(archive);
                // Invoices no longer store a PDF Created flag; PDFs live in stored_pdfs.
                if (table == DataFiles.Invoices)
                    DropTextColumn(table, "PDF Created", archive);
            }

            // Archive files hold process tables only; settings and accounts stay in live.
            if (archive)
                return;

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS app_settings (
                    key TEXT PRIMARY KEY NOT NULL,
                    value TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS admin_smtp (
                    id INTEGER PRIMARY KEY CHECK (id = 1),
                    login_email TEXT NOT NULL DEFAULT '',
                    password TEXT NOT NULL DEFAULT '',
                    host TEXT NOT NULL DEFAULT '',
                    port TEXT NOT NULL DEFAULT '587',
                    ssl INTEGER NOT NULL DEFAULT 1
                );
                """;
            cmd.ExecuteNonQuery();
            SeedAdminSmtpFromSettings();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS app_users (
                    windows_user TEXT PRIMARY KEY NOT NULL,
                    email TEXT NOT NULL DEFAULT '',
                    updated_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS app_prefs (
                    username TEXT NOT NULL,
                    key TEXT NOT NULL,
                    value TEXT NOT NULL,
                    PRIMARY KEY (username, key)
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS stored_pdfs (
                    kind TEXT NOT NULL,
                    doc_key TEXT NOT NULL COLLATE NOCASE,
                    file_name TEXT NOT NULL,
                    content BLOB NOT NULL,
                    stored_at TEXT NOT NULL,
                    PRIMARY KEY (kind, doc_key)
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS app_accounts (
                    username TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
                    display_name TEXT NOT NULL DEFAULT '',
                    password_hash TEXT NOT NULL,
                    password_salt TEXT NOT NULL,
                    email TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            EnsureAccountColumn("is_it", "INTEGER NOT NULL DEFAULT 0");
            EnsureAccountColumn("is_admin", "INTEGER NOT NULL DEFAULT 0");
            EnsureAccountColumn("must_change_password", "INTEGER NOT NULL DEFAULT 0");
            EnsureAccountColumn("security_q1", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("security_a1", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("security_q2", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("security_a2", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("security_q3", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("security_a3", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("stay_signed_in", "INTEGER NOT NULL DEFAULT 0");
            EnsureAccountColumn("table_access", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("access_group", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("recover_fails", "INTEGER NOT NULL DEFAULT 0");
            EnsureAccountColumn("recover_lock_until", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("login_fails", "INTEGER NOT NULL DEFAULT 0");
            EnsureAccountColumn("login_lock_until", "TEXT NOT NULL DEFAULT ''");

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS login_attempts (
                    username TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
                    login_fails INTEGER NOT NULL DEFAULT 0,
                    login_lock_until TEXT NOT NULL DEFAULT ''
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS access_groups (
                    name TEXT PRIMARY KEY NOT NULL COLLATE NOCASE,
                    table_access TEXT NOT NULL DEFAULT ''
                );
                """;
            cmd.ExecuteNonQuery();
            SeedAccessGroups(cmd);

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS "pending_changes" (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    term_start TEXT NOT NULL DEFAULT '',
                    "Table" TEXT NOT NULL DEFAULT '',
                    "Action" TEXT NOT NULL DEFAULT '',
                    "Summary" TEXT NOT NULL DEFAULT '',
                    "Match Json" TEXT NOT NULL DEFAULT '',
                    "Before Json" TEXT NOT NULL DEFAULT '',
                    "After Json" TEXT NOT NULL DEFAULT '',
                    "Requested By" TEXT NOT NULL DEFAULT '',
                    "Requested At" TEXT NOT NULL DEFAULT '',
                    "Status" TEXT NOT NULL DEFAULT 'pending',
                    "Reviewed By" TEXT NOT NULL DEFAULT '',
                    "Reviewed At" TEXT NOT NULL DEFAULT ''
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS app_sessions (
                    token_hash TEXT PRIMARY KEY NOT NULL,
                    username TEXT NOT NULL COLLATE NOCASE,
                    expires_at TEXT NOT NULL,
                    created_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();

            cmd.CommandText =
                """
                CREATE TABLE IF NOT EXISTS bank_accounts (
                    id INTEGER PRIMARY KEY AUTOINCREMENT,
                    name TEXT NOT NULL,
                    bank TEXT NOT NULL DEFAULT '',
                    last4 TEXT NOT NULL DEFAULT '',
                    notes TEXT NOT NULL DEFAULT '',
                    created_at TEXT NOT NULL
                );
                """;
            cmd.ExecuteNonQuery();
            EnsureAccountColumn("bank_accounts", "plaid_access_token", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("bank_accounts", "plaid_item_id", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("bank_accounts", "plaid_account_id", "TEXT NOT NULL DEFAULT ''");
            EnsureAccountColumn("bank_accounts", "plaid_cursor", "TEXT NOT NULL DEFAULT ''");
            UpgradeSecrets();
        }

        /// <summary>Load leftover table_*.csv files into empty tables so a first open keeps old spreadsheet data.</summary>
        public static void ImportCsvsIfEmpty()
        {
            // Remote clients have no leftover CSV files on this PC.
            if (DataLink.IsRemote)
                return;
            // No inventory folder selected, so there is nowhere to look for CSVs.
            if (GetPath() == null)
                return;

            EnsureCreated();

            foreach (var table in DataFiles.All)
            {
                // Table already has rows; skip so a first open does not duplicate spreadsheet data.
                if (Count(table, currentTermOnly: false) > 0)
                    continue;

                var matches = Directory.GetFiles(AppState.InventoryFolder!, table + "_*.csv");
                // path: leftover table_yyyy-MM-dd.csv file from the old spreadsheet export.
                foreach (var path in matches)
                {
                    var rows = CsvIO.Read(path);
                    // Header-only or empty file; nothing to insert.
                    if (rows.Count < 2)
                        continue;

                    // header: CSV column names from the first row.
                    var header = rows[0].Select(h => h.Trim()).ToArray();
                    DateTime term = AppState.TermStartDate ?? DateTime.Today;
                    DataFiles.TryParseStartDate(Path.GetFileName(path), table, out term);

                    var batch = new List<Dictionary<string, string>>();
                    for (int i = 1; i < rows.Count; i++)
                    {
                        var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                        for (int c = 0; c < header.Length; c++)
                            values[header[c]] = c < rows[i].Length ? rows[i][c] : "";
                        batch.Add(values);
                    }

                    InsertMany(table, batch, term);
                }
            }
        }

        /// <summary>
        /// True when the Old toggle is on and this table exists in both databases.
        /// Reads then include archive rows plus live rows.
        /// </summary>
        public static bool UsingArchive(string table) =>
            AppState.ViewingOldInventory && IsProcessTable(table);

        /// <summary>Expected columns plus any extra columns already on the table, omitting dropped leftovers.</summary>
        public static string[] Headers(string table)
        {
            // Prefer the server's column list when this client is connected remotely.
            // headers: visible column names from the server (id/term_start/dropped leftovers already omitted).
            if (DataLink.Try(ServerOps.TableHeaders, DataLink.Table(table), out string[]? headers) && headers != null)
                return headers;
            EnsureCreated();
            var expected = DataFiles.GetExpectedHeader(table).Split(',')
                .Select(h => h.Trim())
                .Where(h => h.Length > 0)
                .ToList();
            // Hide id/term_start and dropped leftovers (PDF Created, Volume Received, Lot #).
            var actual = TableColumns(table)
                .Where(c => c != "id" && c != "term_start" &&
                            !c.Equals("PDF Created", StringComparison.OrdinalIgnoreCase) &&
                            !c.Equals("Volume Received", StringComparison.OrdinalIgnoreCase) &&
                            !c.Equals("Lot #", StringComparison.OrdinalIgnoreCase))
                .ToList();

            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var list = new List<string>();
            foreach (var col in expected.Concat(actual))
            {
                // Duplicate name (expected already listed it); keep the first occurrence.
                if (!used.Add(col))
                    continue;
                list.Add(col);
            }

            return list.ToArray();
        }

        /// <summary>
        /// Current view: live rows only. Old view: archived process rows, then live rows.
        /// </summary>
        public static List<Dictionary<string, string>> Read(string table) =>
            DataAccess.RestrictRows(table, ReadUnrestricted(table));

        /// <summary>
        /// Walk rows where <paramref name="column"/> equals <paramref name="value"/> (case-insensitive)
        /// without loading the whole table first. Safe to call off the UI thread. Old view includes archive rows.
        /// </summary>
        public static void ForEachWhere(
            string table,
            string column,
            string value,
            Action<Dictionary<string, string>> each,
            CancellationToken cancel = default)
        {
            column = (column ?? "").Trim();
            // value: the cell text to match (case-insensitive), e.g. a PO # or customer name.
            value = (value ?? "").Trim();
            // Empty column name or match text; nothing to scan.
            if (column.Length == 0 || value.Length == 0)
                return;

            // Remote server has no streamed WHERE; filter the full table in memory.
            if (DataLink.IsRemote)
            {
                foreach (var row in Read(table))
                {
                    cancel.ThrowIfCancellationRequested();
                    // This row's cell equals value; yield it to the caller.
                    if (DataFiles.GetRecord(row, column).Equals(value, StringComparison.OrdinalIgnoreCase))
                        each(row);
                }

                return;
            }

            EnsureCreated();
            // Old view: archive rows first, then live.
            if (UsingArchive(table))
                ForEachWhereOn(table, archive: true, column, value, each, cancel);
            ForEachWhereOn(table, archive: false, column, value, each, cancel);
        }

        /// <summary>Every row, ignoring the signed-in user's blocks. For numbering and access expansion.</summary>
        public static List<Dictionary<string, string>> ReadUnrestricted(string table)
        {
            // Remote session: use the server's full table instead of opening local files.
            if (DataLink.Try(ServerOps.TableRead, DataLink.Table(table), out List<Dictionary<string, string>>? rows) &&
                rows != null)
                return rows;
            EnsureCreated();
            var result = new List<Dictionary<string, string>>();
            // Old toggle is on for a process table; include archived rows before live rows.
            if (UsingArchive(table))
                AppendRows(table, archive: true, result);
            AppendRows(table, archive: false, result);
            return result;
        }

        /// <summary>
        /// Same as <see cref="Read"/>, with row ids. Archive ids are stored negative so they
        /// cannot collide with live ids when both databases are shown.
        /// </summary>
        public static List<(long Id, Dictionary<string, string> Fields)> ReadWithIds(string table) =>
            DataAccess.RestrictRows(table, ReadWithIdsUnrestricted(table));

        /// <summary>Every row with ids, ignoring user blocks. Archive ids are negative.</summary>
        public static List<(long Id, Dictionary<string, string> Fields)> ReadWithIdsUnrestricted(string table)
        {
            // Remote session: ids come from the server (archive ids already encoded negative).
            if (DataLink.Try(ServerOps.TableReadIds, DataLink.Table(table), out List<IdFieldsDto>? remote) &&
                remote != null)
            {
                return remote.Select(row => (row.Id, row.Fields ?? new Dictionary<string, string>())).ToList();
            }
            EnsureCreated();
            var result = new List<(long, Dictionary<string, string>)>();
            // Old toggle is on; prepend archive rows (negative ids) then live rows.
            if (UsingArchive(table))
                AppendRowsWithIds(table, archive: true, result);
            AppendRowsWithIds(table, archive: false, result);
            return result;
        }

        /// <summary>Always inserts into the live database. Process rows stay undated until complete.</summary>
        public static void Insert(string table, Dictionary<string, string> values, DateTime? term = null)
        {
            // Remote client: the server writes the live row; skip local SQLite.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.TableInsert, DataLink.Table(table, values, term: term));
                return;
            }
            EnsureCreated();
            EnsureTerm();
            var headers = Headers(table);
            using var db = Open();
            // cmd: INSERT into the live table with term_start plus each header column.
            using var cmd = db.CreateCommand();
            var cols = new List<string> { Quote("term_start") };
            var pars = new List<string> { "$term" };
            cmd.Parameters.AddWithValue("$term", CompletionStamp(table, values, term));
            for (int i = 0; i < headers.Length; i++)
            {
                string name = headers[i];
                cols.Add(Quote(name));
                string p = "$c" + i;
                pars.Add(p);
                cmd.Parameters.AddWithValue(p, CellValue(table, values, name));
            }

            cmd.CommandText =
                $"INSERT INTO {Quote(table)} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
            cmd.ExecuteNonQuery();
        }

        /// <summary>Bulk-insert rows in one transaction (or via the server) and return how many were written.</summary>
        public static int InsertMany(
            string table,
            IEnumerable<Dictionary<string, string>> rows,
            DateTime? term = null)
        {
            // Remote session: bulk insert on the server and return how many it wrote.
            if (DataLink.Try(ServerOps.TableInsertMany, DataLink.Table(table, rows: rows, term: term), out int inserted))
                return inserted;
            EnsureCreated();
            EnsureTerm();
            var headers = Headers(table);
            int count = 0;

            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var values in rows)
            {
                // cmd: one INSERT per incoming row inside the live-database transaction.
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                var cols = new List<string> { Quote("term_start") };
                var pars = new List<string> { "$term" };
                cmd.Parameters.AddWithValue("$term", CompletionStamp(table, values, term));
                for (int i = 0; i < headers.Length; i++)
                {
                    string name = headers[i];
                    cols.Add(Quote(name));
                    string p = "$c" + i;
                    pars.Add(p);
                    cmd.Parameters.AddWithValue(p, CellValue(table, values, name));
                }

                cmd.CommandText =
                    $"INSERT INTO {Quote(table)} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
                cmd.ExecuteNonQuery();
                count++;
            }

            tx.Commit();
            return count;
        }

        /// <summary>Updates live or archive based on the sign of <paramref name="id"/> (negative = archive).</summary>
        public static bool UpdateById(string table, long id, Dictionary<string, string> values)
        {
            // Remote session: the server routes live vs archive from the signed id.
            if (DataLink.Try(ServerOps.TableUpdate, DataLink.Table(table, values, id), out bool updated))
                return updated;
            EnsureCreated();
            bool archive = IsArchiveRowId(id);
            long rawId = DecodeRowId(id);
            // Combined-view id did not decode to a real SQLite row id; nothing to update.
            if (rawId <= 0)
                return false;

            var headers = Headers(table);
            using var db = Open(archive);
            // cmd: UPDATE the live or archive row whose id matches $id.
            using var cmd = db.CreateCommand();
            var sets = new List<string>
            {
                $"{Quote("term_start")} = $term"
            };
            cmd.Parameters.AddWithValue("$term", CompletionStamp(table, values, term: null));
            for (int i = 0; i < headers.Length; i++)
            {
                string name = headers[i];
                string p = "$c" + i;
                sets.Add($"{Quote(name)} = {p}");
                cmd.Parameters.AddWithValue(p, CellValue(table, values, name));
            }

            cmd.Parameters.AddWithValue("$id", rawId);
            cmd.CommandText = $"UPDATE {Quote(table)} SET {string.Join(",", sets)} WHERE id = $id;";
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>Delete one row by id (live or archive). Remote still uses the local delete path today.</summary>
        public static bool DeleteById(string table, long id)
        {
            // DataLink.IsRemote is true, but there is no table.delete op; fall through to local delete.
            if (DataLink.IsRemote)
            {
                // Remote updates go through table.update; delete uses a blank-row replace locally first.
            }

            EnsureCreated();
            DeleteByIds(table, new List<long> { id });
            return true;
        }

        /// <summary>Add missing TEXT columns on live, and on archive when Old view is combining both.</summary>
        public static void EnsureColumns(string table, params string[] columns)
        {
            // Caller passed no column names; nothing to add.
            if (columns.Length == 0)
                return;
            // Remote session: ask the server to add the columns on its databases.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.TableEnsureColumns, DataLink.Table(table, columns: columns));
                return;
            }

            EnsureCreated();
            EnsureColumnsOn(table, columns, archive: false);
            // Old view is combining both files; keep archive schema in sync with live.
            if (UsingArchive(table))
                EnsureColumnsOn(table, columns, archive: true);
        }

        /// <summary>Row count in the current view (live, plus archive when Old is on).</summary>
        public static int Count(string table, bool currentTermOnly = true)
        {
            // Remote session: use the server's count for this table and term filter.
            if (DataLink.Try(ServerOps.TableCount, DataLink.Table(table, currentTermOnly: currentTermOnly), out int count))
                return count;
            EnsureCreated();
            _ = currentTermOnly;
            int total = CountIn(table, archive: false);
            // Old toggle is on; add archived process rows to the live count.
            if (UsingArchive(table))
                total += CountIn(table, archive: true);
            return total;
        }

        /// <summary>
        /// Move completed process rows into Old Inventory and leave unfinished rows in live, undated.
        /// </summary>
        public static int ArchiveCompleted(DateTime? term = null)
        {
            // Remote session: the server moves completed process rows into Old Inventory.
            if (DataLink.Try(ServerOps.TableArchive, DataLink.Table("", term: term), out int movedRemote))
                return movedRemote;
            EnsureCreated();
            string fallbackTerm = (term ?? AppState.TermStartDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            int moved = 0;

            foreach (var table in ProcessTables)
            {
                var liveColumns = TableColumns(table, archive: false)
                    .Where(c => !c.Equals("id", StringComparison.OrdinalIgnoreCase))
                    .ToList();
                EnsureArchiveColumns(table, liveColumns);

                var completeIds = new List<long>();
                var incompleteIds = new List<long>();
                var toArchive = new List<(Dictionary<string, string> Fields, string TermStart)>();

                foreach (var (id, termStart, fields) in ReadLiveRows(table))
                {
                    // This live process row is finished; copy it to old_inventory.db and delete it from live.
                    if (IsProcessComplete(table, fields))
                    {
                        string stamp = string.IsNullOrWhiteSpace(termStart) ? fallbackTerm : termStart.Trim();
                        completeIds.Add(id);
                        toArchive.Add((fields, stamp));
                    }
                    // Still in progress; keep it in live and clear any leftover term_start later.
                    else
                    {
                        incompleteIds.Add(id);
                    }
                }

                // At least one finished row; insert into archive then remove those ids from live.
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

        /// <summary>Newest term_start date across live tables, used to restore TermStartDate.</summary>
        public static DateTime? LatestTerm()
        {
            // Remote session: parse the server's newest term_start, or null if it is blank.
            if (DataLink.Try(ServerOps.TableLatestTerm, new { }, out string? text))
                return DateTime.TryParse(text, out var parsed) ? parsed : null;
            // Live database file is missing; there is no term to restore.
            if (!Exists())
                return null;

            DateTime? latest = null;
            using var db = Open();
            foreach (var table in DataFiles.All)
            {
                // cmd: MAX(term_start) for this live table.
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"SELECT MAX(term_start) FROM {Quote(table)};";
                // value: newest term_start text in this table, or null if empty.
                var value = cmd.ExecuteScalar()?.ToString();
                // Parsed date is newer than latest (or first valid date); keep it as the restored term.
                if (DateTime.TryParse(value, out var date) && (latest == null || date > latest))
                    latest = date;
            }

            return latest;
        }

        /// <summary>True for UNC, network drives, and common cloud-sync folders that cannot share WAL files.</summary>
        public static bool IsSharedLocation(string? folder)
        {
            // No folder path; treat as not shared so callers can still pick WAL.
            if (string.IsNullOrWhiteSpace(folder))
                return false;

            // UNC path (\\server\share or //server/share); WAL files cannot be shared across PCs.
            if (folder.StartsWith(@"\\", StringComparison.Ordinal) ||
                folder.StartsWith("//", StringComparison.Ordinal))
                return true;

            try
            {
                string? root = Path.GetPathRoot(folder);
                // Drive letter path (C:\...); check whether that drive is a mapped network drive.
                if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
                {
                    var drive = new DriveInfo(root);
                    // Mapped network drive; same WAL conflict as UNC.
                    if (drive.DriveType == DriveType.Network)
                        return true;
                }
            }
            // DriveInfo failed (unknown or disconnected drive); fall through to the cloud-folder name check.
            catch
            {
                // ignore unknown drives
            }

            string lower = folder.Replace('/', '\\').ToLowerInvariant();
            return lower.Contains(@"\onedrive") ||
                   lower.Contains(@"\dropbox") ||
                   lower.Contains(@"\google drive") ||
                   lower.Contains(@"\icloud");
        }

        /// <summary>App settings; admins see revealed secrets, everyone else gets public keys only.</summary>
        public static Dictionary<string, string> ReadSettings()
        {
            // Signed-in admin on a remote session: return revealed secrets from the server.
            if (AppState.IsAdmin &&
                DataLink.Try(ServerOps.SettingsRead, new { }, out Dictionary<string, string>? admin) &&
                admin != null)
                return admin;
            // Remote non-admin (or admin call failed): public keys only, no secret values.
            if (DataLink.Try(ServerOps.SettingsReadPublic, new { }, out Dictionary<string, string>? pub) &&
                pub != null)
                return pub;
            EnsureCreated();
            var map = ReadSettingsRaw();
            return AppState.IsAdmin
                ? SecretProtect.RevealSettings(map)
                : SecretProtect.WithoutSecrets(map);
        }

        /// <summary>Settings with secret values stripped, for non-admin callers and shared layout fallbacks.</summary>
        public static Dictionary<string, string> ReadPublicSettings()
        {
            // Remote session: public settings from the server, secrets already stripped.
            if (DataLink.Try(ServerOps.SettingsReadPublic, new { }, out Dictionary<string, string>? remote) &&
                remote != null)
                return remote;
            EnsureCreated();
            return SecretProtect.WithoutSecrets(ReadSettingsRaw());
        }

        /// <summary>Read app_settings as stored, including sealed secret values.</summary>
        private static Dictionary<string, string> ReadSettingsRaw()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var db = Open();
            // cmd: SELECT every app_settings key/value, including sealed secrets.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_settings;";
            // reader: one row per setting.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // key: setting name (smtp_user, plaid_secret, …).
                string key = reader.GetString(0);
                // value: stored text, still sealed if it is a secret.
                string value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                map[key] = value;
            }

            return map;
        }

        /// <summary>Per-user prefs (grid layouts, etc.) for the signed-in account.</summary>
        public static Dictionary<string, string> ReadPrefs()
        {
            string user = (AppState.CurrentUsername ?? "").Trim();
            // Nobody is signed in; there are no prefs to load.
            if (user.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            // Remote session: prefs live on the server for this username.
            if (DataLink.Try(ServerOps.PrefsRead, new { }, out Dictionary<string, string>? remote) &&
                remote != null)
                return remote;
            EnsureCreated();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var db = Open();
            // cmd: SELECT this user's app_prefs key/value rows.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_prefs WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", user);
            // reader: one row per pref (grid layout, etc.).
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
            return map;
        }

        /// <summary>Upsert prefs for the signed-in user; no-op when nobody is signed in.</summary>
        public static void WritePrefs(Dictionary<string, string> values)
        {
            string user = (AppState.CurrentUsername ?? "").Trim();
            // No signed-in user or empty payload; skip the write.
            if (user.Length == 0 || values.Count == 0)
                return;
            // Remote session: persist prefs on the server for this username.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.PrefsWrite, new PrefsWriteRequest { Values = values });
                return;
            }

            EnsureCreated();
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var pair in values)
            {
                // cmd: upsert one pref (username + key) inside the transaction.
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO app_prefs (username, key, value)
                    VALUES ($user, $key, $value)
                    ON CONFLICT(username, key) DO UPDATE SET value = excluded.value;
                    """;
                cmd.Parameters.AddWithValue("$user", user);
                // $key: pref name (grid layout, column widths, …).
                cmd.Parameters.AddWithValue("$key", pair.Key);
                cmd.Parameters.AddWithValue("$value", pair.Value ?? "");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>Load the single admin SMTP row, revealing the sealed password.</summary>
        public static (string Email, string Password, string Host, int Port) LoadAdminSmtp()
        {
            EnsureCreated();
            using var db = Open();
            // cmd: SELECT the single admin_smtp row (id = 1).
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT login_email, password, host, port FROM admin_smtp WHERE id = 1;";
            // reader: that one SMTP row, or empty if never seeded.
            using var reader = cmd.ExecuteReader();
            // No admin_smtp row yet; return empty credentials with Mailer's defaults.
            if (!reader.Read())
                return ("", "", Mailer.DefaultHost, Mailer.DefaultPort);

            string email = reader.IsDBNull(0) ? "" : reader.GetString(0);
            string password = SecretProtect.Open(reader.IsDBNull(1) ? "" : reader.GetString(1));
            string host = reader.IsDBNull(2) ? "" : reader.GetString(2);
            int port = Mailer.DefaultPort;
            // Stored port text parses as a positive integer; use it instead of Mailer.DefaultPort.
            if (!reader.IsDBNull(3) && int.TryParse(reader.GetString(3), out int parsed) && parsed > 0)
                port = parsed;
            return (email ?? "", password ?? "", host ?? "", port);
        }

        /// <summary>Copy stored SMTP credentials into AppState for Mailer.</summary>
        public static void ApplyAdminSmtp()
        {
            var row = LoadAdminSmtp();
            // Stored login email is present; copy it into AppState for Mailer.
            if (row.Email.Length > 0)
                AppState.SmtpUser = row.Email;
            // Sealed password opened successfully; Mailer needs the plaintext.
            if (row.Password.Length > 0)
                AppState.SmtpPassword = row.Password;
            // Host is set; override Mailer's default SMTP server.
            if (row.Host.Length > 0)
                AppState.SmtpHost = row.Host;
            // Port is a positive number; override Mailer's default port.
            if (row.Port > 0)
                AppState.SmtpPort = row.Port;
        }

        /// <summary>Empty password keeps the password already in the table.</summary>
        public static bool SaveAdminSmtp(string email, string password, string host, int port, out string error)
        {
            error = "";
            try
            {
                EnsureCreated();
                using var db = Open();
                // cmd: upsert the single admin_smtp row; empty password keeps the existing sealed value.
                using var cmd = db.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO admin_smtp (id, login_email, password, host, port, ssl)
                    VALUES (1, $email, $password, $host, $port, 1)
                    ON CONFLICT(id) DO UPDATE SET
                        login_email = excluded.login_email,
                        password = CASE
                            WHEN excluded.password = '' THEN admin_smtp.password
                            ELSE excluded.password
                        END,
                        host = excluded.host,
                        port = excluded.port;
                    """;
                cmd.Parameters.AddWithValue("$email", email ?? "");
                cmd.Parameters.AddWithValue("$password", SecretProtect.Seal(password));
                cmd.Parameters.AddWithValue("$host", string.IsNullOrWhiteSpace(host) ? Mailer.DefaultHost : host.Trim());
                cmd.Parameters.AddWithValue("$port", (port > 0 ? port : Mailer.DefaultPort).ToString());
                cmd.ExecuteNonQuery();

                var check = LoadAdminSmtp();
                // Round-trip email does not match what we just wrote; treat the save as failed.
                if (!string.Equals(check.Email, email ?? "", StringComparison.Ordinal))
                {
                    error = "The database did not keep the login email.";
                    return false;
                }

                // Caller supplied a new password, but the stored/revealed value does not match it.
                if ((password ?? "").Length > 0 &&
                    !string.Equals(check.Password, password, StringComparison.Ordinal))
                {
                    error = "The database did not keep the password.";
                    return false;
                }

                ApplyAdminSmtp();
                return true;
            }
            // SQLite or DPAPI failed while writing SMTP; surface the exception text.
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Copy leftover smtp_* app_settings into admin_smtp once, if that table is still empty.</summary>
        private static void SeedAdminSmtpFromSettings()
        {
            using var db = Open();
            using var exists = db.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM admin_smtp WHERE id = 1;";
            // admin_smtp already has the id=1 row; do not overwrite it from leftover settings.
            if (Convert.ToInt32(exists.ExecuteScalar()) > 0)
                return;

            string email = "", password = "", host = Mailer.DefaultHost, port = Mailer.DefaultPort.ToString();
            using var read = db.CreateCommand();
            read.CommandText = "SELECT key, value FROM app_settings WHERE key LIKE 'smtp_%';";
            // reader: leftover smtp_* app_settings rows from before admin_smtp existed.
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                // key: smtp_user / smtp_password / smtp_host / smtp_port.
                string key = reader.GetString(0);
                // value: stored setting text (password may still be plaintext or sealed).
                string value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                // Old smtp_user setting is the login email for admin_smtp.
                if (key.Equals("smtp_user", StringComparison.OrdinalIgnoreCase))
                    email = value;
                // Old smtp_password setting; will be sealed on insert.
                else if (key.Equals("smtp_password", StringComparison.OrdinalIgnoreCase))
                    password = value;
                // Non-empty smtp_host; keep it instead of Mailer's default.
                else if (key.Equals("smtp_host", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    host = value;
                // Non-empty smtp_port text; keep it instead of the default port string.
                else if (key.Equals("smtp_port", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    port = value;
            }

            using var insert = db.CreateCommand();
            insert.CommandText =
                """
                INSERT INTO admin_smtp (id, login_email, password, host, port, ssl)
                VALUES (1, $email, $password, $host, $port, 1);
                """;
            insert.Parameters.AddWithValue("$email", email);
            insert.Parameters.AddWithValue("$password", SecretProtect.Seal(password));
            insert.Parameters.AddWithValue("$host", host);
            insert.Parameters.AddWithValue("$port", port);
            insert.ExecuteNonQuery();
        }

        /// <summary>Upsert app_settings, sealing secrets; non-admins cannot overwrite secret keys.</summary>
        public static void WriteSettings(Dictionary<string, string> values)
        {
            // Remote session: the server upserts app_settings (and enforces admin-only secrets).
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.SettingsWrite, new SettingsWriteRequest { Values = values });
                return;
            }
            EnsureCreated();
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var pair in values)
            {
                // Secret key and the caller is not admin or sent a blank value; skip so secrets stay intact.
                if (SecretProtect.IsSecretSetting(pair.Key) &&
                    (!AppState.IsAdmin || string.IsNullOrEmpty(pair.Value)))
                    continue;
                // cmd: upsert one app_settings key/value, sealing if this key is a secret.
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO app_settings (key, value)
                    VALUES ($key, $value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                    """;
                // $key: app_settings name (may be a secret key that gets sealed below).
                cmd.Parameters.AddWithValue("$key", pair.Key);
                cmd.Parameters.AddWithValue("$value", SecretProtect.StoreSetting(pair.Key, pair.Value));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>Email stored for a Windows user on this PC, used before an inventory account exists.</summary>
        public static string? ReadUserEmail(string windowsUser)
        {
            windowsUser = (windowsUser ?? "").Trim();
            // Blank Windows user name; there is no app_users row to look up.
            if (windowsUser.Length == 0)
                return null;
            // Remote session: email is stored on the server for this Windows login.
            if (DataLink.Try(ServerOps.UserEmailRead, new UserEmailRequest { WindowsUser = windowsUser }, out string? email))
                return email;
            EnsureCreated();
            using var db = Open();
            // cmd: SELECT email from app_users for this Windows login.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT email FROM app_users WHERE windows_user = $user;";
            cmd.Parameters.AddWithValue("$user", windowsUser);
            // value: stored email text, or null if this Windows user has no row yet.
            var value = cmd.ExecuteScalar()?.ToString();
            return value;
        }

        /// <summary>Remember the Windows user's email for first-run account setup.</summary>
        public static void WriteUserEmail(string windowsUser, string? email)
        {
            windowsUser = (windowsUser ?? "").Trim();
            // Blank Windows user name; skip so we do not insert an empty primary key.
            if (windowsUser.Length == 0)
                return;
            // Remote session: persist the email on the server for first-run setup.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.UserEmailWrite, new UserEmailRequest { WindowsUser = windowsUser, Email = email });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: upsert app_users email for this Windows login.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO app_users (windows_user, email, updated_at)
                VALUES ($user, $email, $at)
                ON CONFLICT(windows_user) DO UPDATE SET
                    email = excluded.email,
                    updated_at = excluded.updated_at;
                """;
            cmd.Parameters.AddWithValue("$user", windowsUser);
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>How many app_accounts rows exist (local or remote).</summary>
        public static int CountAccounts()
        {
            // Remote session: account count comes from the server, not local SQLite.
            if (DataLink.Try(ServerOps.AccountsCount, new { }, out int count))
                return count;
            EnsureCreated();
            using var db = Open();
            // cmd: COUNT(*) of app_accounts rows.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM app_accounts;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>Load hash, salt, display name, email, and must-change flag for a username.</summary>
        public static bool TryGetAccount(
            string username,
            out string passwordHash,
            out string passwordSalt,
            out string displayName,
            out string email,
            out bool mustChangePassword)
        {
            passwordHash = "";
            passwordSalt = "";
            displayName = "";
            email = "";
            mustChangePassword = false;
            username = (username ?? "").Trim();
            // Blank username; no account to load.
            if (username.Length == 0)
                return false;
            // Remote session: hash/salt stay on the server; copy the public fields from the DTO.
            if (DataLink.Try(ServerOps.AccountsGet, new AccountWriteRequest { Username = username }, out AccountGetDto? dto) &&
                dto != null)
            {
                displayName = dto.DisplayName;
                email = dto.Email;
                mustChangePassword = dto.MustChangePassword;
                return dto.Found;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: SELECT hash, salt, display name, email, and must-change flag for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT password_hash, password_salt, display_name, email,
                       COALESCE(must_change_password, 0)
                FROM app_accounts WHERE username = $user;
                """;
            cmd.Parameters.AddWithValue("$user", username);
            // reader: the matching app_accounts row, if any.
            using var reader = cmd.ExecuteReader();
            // No row for this username; caller should treat the login as unknown.
            if (!reader.Read())
                return false;

            passwordHash = reader.IsDBNull(0) ? "" : reader.GetString(0);
            passwordSalt = reader.IsDBNull(1) ? "" : reader.GetString(1);
            displayName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            email = reader.IsDBNull(3) ? "" : reader.GetString(3);
            mustChangePassword = !reader.IsDBNull(4) && reader.GetInt32(4) != 0;
            return true;
        }

        /// <summary>Insert a new account; returns false on a duplicate username.</summary>
        public static bool InsertAccount(
            string username,
            string displayName,
            string passwordHash,
            string passwordSalt,
            string email)
        {
            username = (username ?? "").Trim();
            // Blank username cannot be a primary key; reject the insert.
            if (username.Length == 0)
                return false;

            EnsureCreated();
            using var db = Open();
            // cmd: INSERT a new app_accounts row (fails if username is taken).
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO app_accounts
                    (username, display_name, password_hash, password_salt, email, created_at)
                VALUES ($user, $name, $hash, $salt, $email, $at);
                """;
            cmd.Parameters.AddWithValue("$user", username);
            cmd.Parameters.AddWithValue("$name", displayName ?? "");
            cmd.Parameters.AddWithValue("$hash", passwordHash ?? "");
            cmd.Parameters.AddWithValue("$salt", passwordSalt ?? "");
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            try
            {
                return cmd.ExecuteNonQuery() > 0;
            }
            // UNIQUE username conflict: the account already exists.
            catch (SqliteException)
            {
                return false;
            }
        }

        /// <summary>All accounts for user management, including lock and stay-signed-in flags.</summary>
        public static List<(string Username, string DisplayName, string Email, bool IsAdmin, bool IsIt, bool StaySignedIn, bool LoginLocked)> ListAccounts()
        {
            // Remote session: user-management list comes from the server.
            if (DataLink.Try(ServerOps.AccountsList, new { }, out List<AccountListDto>? remote) && remote != null)
            {
                return remote.Select(a => (
                    a.Username, a.DisplayName, a.Email, a.IsAdmin, a.IsIt, a.StaySignedIn, a.LoginLocked)).ToList();
            }
            EnsureCreated();
            var list = new List<(string, string, string, bool, bool, bool, bool)>();
            using var db = Open();
            // cmd: SELECT every app_accounts row for the user-management grid.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT username, display_name, email,
                       COALESCE(is_admin, 0), COALESCE(is_it, 0),
                       COALESCE(stay_signed_in, 0),
                       COALESCE(login_lock_until, '')
                FROM app_accounts
                ORDER BY username COLLATE NOCASE;
                """;
            // reader: one app_accounts row per user (roles and lock flags).
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add((
                    reader.IsDBNull(0) ? "" : reader.GetString(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    !reader.IsDBNull(3) && reader.GetInt32(3) != 0,
                    !reader.IsDBNull(4) && reader.GetInt32(4) != 0,
                    !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                    RecoveryGuard.IsItLock(reader.IsDBNull(6) ? "" : reader.GetValue(6)?.ToString())));
            }

            return list;
        }

        /// <summary>Update display name and email for an existing username.</summary>
        public static bool UpdateAccount(
            string username,
            string displayName,
            string email)
        {
            username = (username ?? "").Trim();
            // Blank username; no account row to update.
            if (username.Length == 0)
                return false;
            // Remote session: the server updates display name and email.
            if (DataLink.Try(ServerOps.AccountsUpdate, new AccountWriteRequest
            {
                Username = username,
                DisplayName = displayName,
                Email = email
            }, out bool updatedAccount))
                return updatedAccount;

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE display_name and email for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE app_accounts
                SET display_name = $name, email = $email
                WHERE username = $user;
                """;
            cmd.Parameters.AddWithValue("$name", displayName ?? "");
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>Replace hash and salt, then drop sessions and clear lockout counters.</summary>
        public static bool UpdateAccountPassword(string username, string passwordHash, string passwordSalt)
        {
            username = (username ?? "").Trim();
            // Blank username; nothing to change.
            if (username.Length == 0)
                return false;

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE password_hash and password_salt for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE app_accounts
                SET password_hash = $hash, password_salt = $salt
                WHERE username = $user;
                """;
            cmd.Parameters.AddWithValue("$hash", passwordHash ?? "");
            cmd.Parameters.AddWithValue("$salt", passwordSalt ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            bool updated = cmd.ExecuteNonQuery() > 0;
            // Password actually changed; drop stay-signed-in tokens and reset lockout counters.
            if (updated)
            {
                DeleteSessionsForUser(username);
                ClearRecoveryFails(username);
                ClearLoginFails(username);
            }
            return updated;
        }

        /// <summary>Force or clear the must-change-password flag after a reset.</summary>
        public static void SetMustChangePassword(string username, bool mustChange)
        {
            username = (username ?? "").Trim();
            // Blank username; skip the flag write.
            if (username.Length == 0)
                return;
            // Remote session: the server sets must_change_password.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.AccountsMustChange, new AccountWriteRequest
                {
                    Username = username,
                    MustChange = mustChange
                });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE must_change_password for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET must_change_password = $flag WHERE username = $user;";
            cmd.Parameters.AddWithValue("$flag", mustChange ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Rename an account and its stay-signed-in sessions; fails on a taken name.</summary>
        public static bool RenameAccount(string oldUsername, string newUsername)
        {
            oldUsername = (oldUsername ?? "").Trim();
            newUsername = (newUsername ?? "").Trim();
            // Either name is blank; a rename needs both sides.
            if (oldUsername.Length == 0 || newUsername.Length == 0)
                return false;
            // Same name ignoring case; treat as already renamed.
            if (oldUsername.Equals(newUsername, StringComparison.OrdinalIgnoreCase))
                return true;
            // Remote session: the server renames the account and its sessions.
            if (DataLink.Try(ServerOps.AccountsRename, new AccountWriteRequest
            {
                OldUsername = oldUsername,
                NewUsername = newUsername
            }, out bool renamed))
                return renamed;

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE app_accounts.username from $old to $new.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET username = $new WHERE username = $old;";
            cmd.Parameters.AddWithValue("$new", newUsername);
            cmd.Parameters.AddWithValue("$old", oldUsername);
            try
            {
                // No row matched oldUsername; the account is missing.
                if (cmd.ExecuteNonQuery() <= 0)
                    return false;
                RenameSessions(oldUsername, newUsername);
                return true;
            }
            // UNIQUE conflict: the new username is already taken.
            catch (SqliteException)
            {
                return false;
            }
        }

        /// <summary>Set only the email on an account row.</summary>
        public static void UpdateAccountEmail(string username, string email)
        {
            username = (username ?? "").Trim();
            // Blank username; skip the email write.
            if (username.Length == 0)
                return;
            // Remote session: the server updates only the email column.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.AccountsEmail, new AccountWriteRequest
                {
                    Username = username,
                    Email = email
                });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE email for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET email = $email WHERE username = $user;";
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Reset recovery-question lockout after a successful password change.</summary>
        private static void ClearRecoveryFails(string username)
        {
            username = (username ?? "").Trim();
            // Blank username; skip the recovery-lock reset.
            if (username.Length == 0)
                return;
            EnsureCreated();
            using var db = Open();
            // cmd: clear recover_fails and recover_lock_until for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET recover_fails = 0, recover_lock_until = '' WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        /// <summary>False when the account is IT-locked or still in a timed lockout from failed logins.</summary>
        public static bool AllowLogin(string username, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            // Blank username; ask the caller to fill in both fields.
            if (username.Length == 0)
            {
                error = "Enter a username and password.";
                return false;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: lock text from the account row, or from login_attempts when the name is not a user.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT COALESCE(login_lock_until, '') FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            string untilText = cmd.ExecuteScalar()?.ToString() ?? "";
            // No app_accounts row: still honor a lock recorded for this typed name.
            if (untilText.Length == 0)
            {
                using var ghost = db.CreateCommand();
                ghost.CommandText =
                    "SELECT COALESCE(login_lock_until, '') FROM login_attempts WHERE username = $user;";
                ghost.Parameters.AddWithValue("$user", username);
                untilText = ghost.ExecuteScalar()?.ToString() ?? "";
            }
            // untilText is the IT-lock sentinel; refuse login until an admin unlocks.
            if (RecoveryGuard.IsItLock(untilText))
            {
                error = RecoveryGuard.ItLockMessage;
                return false;
            }

            // untilText is a future timestamp; the timed lockout is still active.
            if (DateTime.TryParse(untilText, out var until) && until > DateTime.Now)
            {
                error = RecoveryGuard.LockedMessage(until);
                return false;
            }

            // Stale lock text remains after it expired; clear it so the next attempt is clean.
            if (untilText.Length > 0)
                ClearLoginTimeLock(username);
            return true;
        }

        /// <summary>Count a failed login, apply the next lockout penalty, and return the message to show.</summary>
        public static string NoteLoginFailure(string username)
        {
            username = (username ?? "").Trim();
            // Blank username; same generic message as a bad password so we do not leak existence.
            if (username.Length == 0)
                return "That username or password is not right.";

            EnsureCreated();
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText =
                "SELECT COALESCE(login_fails, 0) FROM app_accounts WHERE username = $user;";
            read.Parameters.AddWithValue("$user", username);
            object? raw = read.ExecuteScalar();
            // Unknown username: count against login_attempts so tries-left still shows.
            if (raw == null)
                return NoteUnknownLoginFailure(db, username);

            int fails = Convert.ToInt32(raw) + 1;
            var penalty = RecoveryGuard.NextLoginPenalty(fails);

            using var write = db.CreateCommand();
            write.CommandText =
                """
                UPDATE app_accounts
                SET login_fails = $fails, login_lock_until = $until
                WHERE username = $user;
                """;
            write.Parameters.AddWithValue("$fails", fails);
            write.Parameters.AddWithValue("$until", penalty.Until);
            write.Parameters.AddWithValue("$user", username);
            write.ExecuteNonQuery();

            return penalty.Message;
        }

        /// <summary>Same 5-try / 15-minute / IT lock as a real account, keyed by the typed name.</summary>
        private static string NoteUnknownLoginFailure(SqliteConnection db, string username)
        {
            using var read = db.CreateCommand();
            read.CommandText =
                "SELECT COALESCE(login_fails, 0) FROM login_attempts WHERE username = $user;";
            read.Parameters.AddWithValue("$user", username);
            int fails = Convert.ToInt32(read.ExecuteScalar() ?? 0) + 1;
            var penalty = RecoveryGuard.NextLoginPenalty(fails);

            using var write = db.CreateCommand();
            write.CommandText =
                """
                INSERT INTO login_attempts (username, login_fails, login_lock_until)
                VALUES ($user, $fails, $until)
                ON CONFLICT(username) DO UPDATE SET
                    login_fails = $fails,
                    login_lock_until = $until;
                """;
            write.Parameters.AddWithValue("$user", username);
            write.Parameters.AddWithValue("$fails", fails);
            write.Parameters.AddWithValue("$until", penalty.Until);
            write.ExecuteNonQuery();
            return penalty.Message;
        }

        /// <summary>Clear an expired timed lock so the next login is not still blocked.</summary>
        private static void ClearLoginTimeLock(string username)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET login_lock_until = '' WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
            using var ghost = db.CreateCommand();
            ghost.CommandText =
                "UPDATE login_attempts SET login_lock_until = '' WHERE username = $user;";
            ghost.Parameters.AddWithValue("$user", username);
            ghost.ExecuteNonQuery();
        }

        /// <summary>Reset failed-login count and lock after a successful sign-in.</summary>
        public static void ClearLoginFails(string username)
        {
            username = (username ?? "").Trim();
            // Blank username; skip the failed-login reset.
            if (username.Length == 0)
                return;
            EnsureCreated();
            using var db = Open();
            // cmd: clear login_fails and login_lock_until after a successful sign-in.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET login_fails = 0, login_lock_until = '' WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
            using var ghost = db.CreateCommand();
            ghost.CommandText = "DELETE FROM login_attempts WHERE username = $user;";
            ghost.Parameters.AddWithValue("$user", username);
            ghost.ExecuteNonQuery();
        }

        /// <summary>Delete an account and its stay-signed-in sessions.</summary>
        public static bool DeleteAccount(string username)
        {
            username = (username ?? "").Trim();
            // Blank username; nothing to delete.
            if (username.Length == 0)
                return false;
            // Remote session: the server deletes the account and its sessions.
            if (DataLink.Try(ServerOps.AccountsDelete, new AccountWriteRequest { Username = username }, out bool deleted))
                return deleted;

            EnsureCreated();
            DeleteSessionsForUser(username);
            using var db = Open();
            // cmd: DELETE the app_accounts row for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            return cmd.ExecuteNonQuery() > 0;
        }

        /// <summary>True when this account may keep a hashed stay-signed-in token.</summary>
        public static bool GetStaySignedIn(string username)
        {
            username = (username ?? "").Trim();
            // Blank username cannot have a stay-signed-in flag.
            if (username.Length == 0)
                return false;
            // Remote session: stay-signed-in flag lives on the server.
            if (DataLink.Try(ServerOps.AccountsStayGet, new AccountWriteRequest { Username = username }, out bool stay))
                return stay;

            EnsureCreated();
            using var db = Open();
            // cmd: SELECT stay_signed_in for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT COALESCE(stay_signed_in, 0) FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            // value: 0/1 (or null if the account is missing).
            var value = cmd.ExecuteScalar();
            return value != null && value != DBNull.Value && Convert.ToInt32(value) != 0;
        }

        /// <summary>Turn stay-signed-in on or off; turning it off drops existing sessions.</summary>
        public static void SetStaySignedIn(string username, bool enabled)
        {
            username = (username ?? "").Trim();
            // Blank username; skip the flag write.
            if (username.Length == 0)
                return;
            // Remote session: the server sets stay_signed_in (and drops sessions when turning it off).
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.AccountsStaySet, new AccountWriteRequest
                {
                    Username = username,
                    Enabled = enabled
                });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE stay_signed_in for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET stay_signed_in = $flag WHERE username = $user;";
            cmd.Parameters.AddWithValue("$flag", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
            // Stay-signed-in was turned off; drop existing hashed tokens so this PC cannot auto-login.
            if (!enabled)
                DeleteSessionsForUser(username);
        }

        /// <summary>Store a hashed stay-signed-in token. Plain tokens never go in the database.</summary>
        public static void InsertSession(string username, string tokenHash, DateTime expiresAt)
        {
            username = (username ?? "").Trim();
            // tokenHash: SHA of the stay-signed-in cookie; never store the plaintext token.
            tokenHash = (tokenHash ?? "").Trim();
            // Missing username or hash; a session row needs both.
            if (username.Length == 0 || tokenHash.Length == 0)
                return;
            // Remote clients do not persist stay-signed-in tokens locally.
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            DeleteExpiredSessions();
            using var db = Open();
            // cmd: INSERT one hashed stay-signed-in session.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO app_sessions (token_hash, username, expires_at, created_at)
                VALUES ($hash, $user, $exp, $at);
                """;
            cmd.Parameters.AddWithValue("$hash", tokenHash);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.Parameters.AddWithValue("$exp", expiresAt.ToString("o"));
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Username for a still-valid stay-signed-in token, or null if expired or remote.</summary>
        public static string? FindSessionUsername(string tokenHash)
        {
            // tokenHash: SHA of the stay-signed-in cookie from this PC.
            tokenHash = (tokenHash ?? "").Trim();
            // Empty hash; no session to look up.
            if (tokenHash.Length == 0)
                return null;
            // Remote clients do not keep local stay-signed-in sessions.
            if (DataLink.IsRemote)
                return null;

            EnsureCreated();
            DeleteExpiredSessions();
            using var db = Open();
            // cmd: SELECT username for a still-valid hashed token.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT username FROM app_sessions
                WHERE token_hash = $hash AND expires_at >= $now
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$hash", tokenHash);
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            return cmd.ExecuteScalar()?.ToString();
        }

        /// <summary>Drop one stay-signed-in token (sign-out on this PC).</summary>
        public static void DeleteSession(string tokenHash)
        {
            // tokenHash: SHA of the stay-signed-in cookie being signed out.
            tokenHash = (tokenHash ?? "").Trim();
            // Empty hash; nothing to delete.
            if (tokenHash.Length == 0)
                return;
            // Remote clients have no local session rows.
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            using var db = Open();
            // cmd: DELETE this hashed stay-signed-in token.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_sessions WHERE token_hash = $hash;";
            cmd.Parameters.AddWithValue("$hash", tokenHash);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Drop every stay-signed-in token for a user (password change, disable, or delete).</summary>
        public static void DeleteSessionsForUser(string username)
        {
            username = (username ?? "").Trim();
            // Blank username; skip so we do not delete every session.
            if (username.Length == 0)
                return;
            // Remote clients have no local session rows.
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            using var db = Open();
            // cmd: DELETE every stay-signed-in token for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_sessions WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Point existing stay-signed-in tokens at the renamed username.</summary>
        public static void RenameSessions(string oldUsername, string newUsername)
        {
            oldUsername = (oldUsername ?? "").Trim();
            newUsername = (newUsername ?? "").Trim();
            // Either name is blank; skip so we do not rewrite every session.
            if (oldUsername.Length == 0 || newUsername.Length == 0)
                return;
            // Remote clients have no local session rows.
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE app_sessions.username after an account rename.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_sessions SET username = $new WHERE username = $old;";
            cmd.Parameters.AddWithValue("$new", newUsername);
            cmd.Parameters.AddWithValue("$old", oldUsername);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Remove stay-signed-in tokens whose expires_at has passed.</summary>
        private static void DeleteExpiredSessions()
        {
            using var db = Open();
            // cmd: DELETE stay-signed-in tokens whose expires_at has passed.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_sessions WHERE expires_at < $now;";
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Set is_admin and is_it flags on an account.</summary>
        public static void SetAccountRoles(string username, bool isAdmin, bool isIt)
        {
            username = (username ?? "").Trim();
            // Blank username; skip the role write.
            if (username.Length == 0)
                return;
            // Remote session: the server sets is_admin and is_it.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.AccountsRoles, new AccountWriteRequest
                {
                    Username = username,
                    IsAdmin = isAdmin,
                    IsIt = isIt
                });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE is_admin and is_it for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET is_admin = $admin, is_it = $it WHERE username = $user;";
            cmd.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
            cmd.Parameters.AddWithValue("$it", isIt ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Company bank accounts for Banking, without Plaid tokens.</summary>
        public static List<(long Id, string Name, string Bank, string Last4, string Notes)> ListBankAccounts()
        {
            // Remote session: bank list comes from the server without Plaid tokens.
            if (DataLink.Try(ServerOps.BankList, new { }, out List<BankRowDto>? banks) && banks != null)
                return banks.Select(a => (a.Id, a.Name, a.Bank, a.Last4, a.Notes)).ToList();
            EnsureCreated();
            var list = new List<(long, string, string, string, string)>();
            using var db = Open();
            // cmd: SELECT display fields from bank_accounts (no Plaid columns).
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT id, name, bank, last4, notes FROM bank_accounts ORDER BY name COLLATE NOCASE;";
            // reader: one company bank-account row.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                list.Add((
                    reader.GetInt64(0),
                    reader.IsDBNull(1) ? "" : reader.GetString(1),
                    reader.IsDBNull(2) ? "" : reader.GetString(2),
                    reader.IsDBNull(3) ? "" : reader.GetString(3),
                    reader.IsDBNull(4) ? "" : reader.GetString(4)));
            }

            return list;
        }

        /// <summary>Insert a bank account row and return its id.</summary>
        public static long InsertBankAccount(string name, string bank, string last4, string notes)
        {
            // Remote session: the server inserts the bank row and returns its id.
            if (DataLink.Try(ServerOps.BankInsert, new BankWriteRequest
            {
                Name = name, Bank = bank, Last4 = last4, Notes = notes
            }, out long id))
                return id;
            EnsureCreated();
            using var db = Open();
            // cmd: INSERT a bank_accounts row, then SELECT last_insert_rowid().
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO bank_accounts (name, bank, last4, notes, created_at)
                VALUES ($name, $bank, $last4, $notes, $at);
                """;
            cmd.Parameters.AddWithValue("$name", name ?? "");
            cmd.Parameters.AddWithValue("$bank", bank ?? "");
            cmd.Parameters.AddWithValue("$last4", last4 ?? "");
            cmd.Parameters.AddWithValue("$notes", notes ?? "");
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
            cmd.Parameters.Clear();
            cmd.CommandText = "SELECT last_insert_rowid();";
            return Convert.ToInt64(cmd.ExecuteScalar());
        }

        /// <summary>Update display fields on a bank account; Plaid tokens are set separately.</summary>
        public static void UpdateBankAccount(long id, string name, string bank, string last4, string notes)
        {
            // Remote session: the server updates display fields only.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.BankUpdate, new BankWriteRequest
                {
                    Id = id, Name = name, Bank = bank, Last4 = last4, Notes = notes
                });
                return;
            }
            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE name/bank/last4/notes for this bank_accounts id.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE bank_accounts
                SET name = $name, bank = $bank, last4 = $last4, notes = $notes
                WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$name", name ?? "");
            cmd.Parameters.AddWithValue("$bank", bank ?? "");
            cmd.Parameters.AddWithValue("$last4", last4 ?? "");
            cmd.Parameters.AddWithValue("$notes", notes ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Delete a bank account row and its stored Plaid link.</summary>
        public static void DeleteBankAccount(long id)
        {
            // Remote session: the server deletes the bank row (and its Plaid link).
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.BankDelete, new BankWriteRequest { Id = id });
                return;
            }
            EnsureCreated();
            using var db = Open();
            // cmd: DELETE this bank_accounts row.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM bank_accounts WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Ensure Admin (locked, no tables) and IT (empty access until set) groups exist.</summary>
        private static void SeedAccessGroups(SqliteCommand cmd)
        {
            // cmd: reused live-database command; first upserts the locked Admin group, then inserts IT if missing.
            cmd.Parameters.Clear();
            cmd.CommandText =
                """
                INSERT INTO access_groups (name, table_access)
                VALUES ('Admin', $admin)
                ON CONFLICT(name) DO UPDATE SET table_access = excluded.table_access;
                """;
            cmd.Parameters.AddWithValue("$admin", AccessGroups.LockedAdminJson());
            cmd.ExecuteNonQuery();

            cmd.Parameters.Clear();
            cmd.CommandText =
                """
                INSERT INTO access_groups (name, table_access)
                VALUES ('IT', '')
                ON CONFLICT(name) DO NOTHING;
                """;
            cmd.ExecuteNonQuery();
        }

        /// <summary>Per-user table_access JSON overlay, or empty when the user follows groups only.</summary>
        public static string GetTableAccess(string username)
        {
            username = (username ?? "").Trim();
            // Blank username; no overlay JSON to return.
            if (username.Length == 0)
                return "";
            // Remote session: per-user table_access overlay comes from the server.
            if (DataLink.Try(ServerOps.AccountsAccessGet, new AccountWriteRequest { Username = username }, out string? access))
                return access ?? "";

            EnsureCreated();
            using var db = Open();
            // cmd: SELECT table_access JSON for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(table_access, '') FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            return cmd.ExecuteScalar()?.ToString() ?? "";
        }

        /// <summary>Merged table_access JSON from every group this user belongs to (allowed wins).</summary>
        public static string GetGroupAccessMerged(string username)
        {
            var groups = GetAccessGroups(username);
            // User belongs to no groups; merged access is empty (overlay may still apply later).
            if (groups.Count == 0)
                return "";
            // Single group; skip Merge and return that group's JSON directly.
            if (groups.Count == 1)
                return GetGroupAccess(groups[0]);
            return DataAccess.Merge(groups.Select(GetGroupAccess));
        }

        /// <summary>Group access with this user's overlay applied.</summary>
        public static string GetEffectiveTableAccess(string username) =>
            DataAccess.Overlay(GetGroupAccessMerged(username), GetTableAccess(username));

        /// <summary>True when the account stores its own table_access JSON on top of groups.</summary>
        public static bool HasAccessOverride(string username) =>
            !string.IsNullOrWhiteSpace(GetTableAccess(username));

        /// <summary>Comma-joined group names for display.</summary>
        public static string GetAccessGroup(string username) =>
            AccessGroups.Join(GetAccessGroups(username));

        /// <summary>Parse the account's access_group list.</summary>
        public static List<string> GetAccessGroups(string username)
        {
            username = (username ?? "").Trim();
            // Blank username; return an empty membership list.
            if (username.Length == 0)
                return new List<string>();
            EnsureCreated();
            using var db = Open();
            // cmd: SELECT the comma-joined access_group string for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(access_group, '') FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            return AccessGroups.Parse(cmd.ExecuteScalar()?.ToString());
        }

        /// <summary>Replace the account's group list from a stored comma/semicolon string.</summary>
        public static void SetAccessGroup(string username, string group) =>
            SetAccessGroups(username, AccessGroups.Parse(group));

        /// <summary>Replace the account's group membership list.</summary>
        public static void SetAccessGroups(string username, IEnumerable<string> groups)
        {
            username = (username ?? "").Trim();
            // Blank username; skip so we do not update every account.
            if (username.Length == 0)
                return;
            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE access_group membership for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET access_group = $group WHERE username = $user;";
            cmd.Parameters.AddWithValue("$group", AccessGroups.Join(groups));
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Add a group to the account if it is not already listed.</summary>
        public static void AddAccessGroup(string username, string group)
        {
            group = (group ?? "").Trim();
            // Blank group name; nothing to add.
            if (group.Length == 0)
                return;
            var groups = GetAccessGroups(username);
            // Already a member (case-insensitive); skip the write.
            if (groups.Any(name => name.Equals(group, StringComparison.OrdinalIgnoreCase)))
                return;
            groups.Add(group);
            SetAccessGroups(username, groups);
        }

        /// <summary>Remove a group from the account's membership list.</summary>
        public static void RemoveAccessGroup(string username, string group)
        {
            group = (group ?? "").Trim();
            // Blank group name; nothing to remove.
            if (group.Length == 0)
                return;
            SetAccessGroups(username, GetAccessGroups(username)
                .Where(name => !name.Equals(group, StringComparison.OrdinalIgnoreCase)));
        }

        /// <summary>All access groups, with Admin then IT first, then the rest A–Z.</summary>
        public static List<(string Name, string Access)> ListAccessGroups()
        {
            EnsureCreated();
            var list = new List<(string Name, string Access)>();
            using var db = Open();
            // cmd: SELECT every access_groups name and table_access JSON.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT name, COALESCE(table_access, '') FROM access_groups ORDER BY name COLLATE NOCASE;";
            // reader: one access group per row.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add((reader.IsDBNull(0) ? "" : reader.GetString(0), reader.IsDBNull(1) ? "" : reader.GetString(1)));
            list.Sort((a, b) =>
            {
                int Rank(string name) =>
                    AccessGroups.IsAdmin(name) ? 0 : AccessGroups.IsIt(name) ? 1 : 2;
                int rank = Rank(a.Name).CompareTo(Rank(b.Name));
                return rank != 0
                    ? rank
                    : string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
            });
            return list;
        }

        /// <summary>table_access JSON stored on an access group.</summary>
        public static string GetGroupAccess(string name)
        {
            name = (name ?? "").Trim();
            // Blank group name; no JSON to return.
            if (name.Length == 0)
                return "";
            EnsureCreated();
            using var db = Open();
            // cmd: SELECT table_access JSON for this access group.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(table_access, '') FROM access_groups WHERE name = $name;";
            cmd.Parameters.AddWithValue("$name", name);
            return cmd.ExecuteScalar()?.ToString() ?? "";
        }

        /// <summary>Insert or update a group. Admin is locked; only an administrator can change IT.</summary>
        public static bool SaveAccessGroup(string name, string json, out string error)
        {
            error = "";
            name = (name ?? "").Trim();
            // Blank group name; the UI must supply one.
            if (name.Length == 0)
            {
                error = "Enter a group name.";
                return false;
            }

            // Comma or semicolon would break the stored membership list format.
            if (name.Contains(',') || name.Contains(';'))
            {
                error = "Group names cannot contain commas.";
                return false;
            }

            // Admin is a built-in locked group; its table_access must stay empty/locked.
            if (AccessGroups.IsAdmin(name))
            {
                error = "The Admin group cannot be changed.";
                return false;
            }

            // IT group can only be edited by an administrator, not by IT itself.
            if (AccessGroups.IsIt(name) && !AppState.IsAdmin)
            {
                error = "Only an administrator can change the IT group.";
                return false;
            }

            try
            {
                EnsureCreated();
                using var db = Open();
                // cmd: upsert access_groups.table_access for this name.
                using var cmd = db.CreateCommand();
                cmd.CommandText =
                    """
                    INSERT INTO access_groups (name, table_access)
                    VALUES ($name, $json)
                    ON CONFLICT(name) DO UPDATE SET table_access = excluded.table_access;
                    """;
                cmd.Parameters.AddWithValue("$name", name);
                cmd.Parameters.AddWithValue("$json", json ?? "");
                cmd.ExecuteNonQuery();
                return true;
            }
            // SQLite failed while upserting the group; surface the exception text.
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Delete a custom group and strip it from every account; built-in groups cannot be removed.</summary>
        public static bool DeleteAccessGroup(string name, out string error)
        {
            error = "";
            name = (name ?? "").Trim();
            // Blank group name; nothing to delete.
            if (name.Length == 0)
                return false;
            // Admin and IT are built-in; refuse so they stay available.
            if (AccessGroups.IsBuiltIn(name))
            {
                error = "The " + name + " group cannot be deleted.";
                return false;
            }

            try
            {
                EnsureCreated();
                using var db = Open();
                using (var list = db.CreateCommand())
                {
                    list.CommandText = "SELECT username, COALESCE(access_group, '') FROM app_accounts;";
                    // reader: every account's username and stored group list.
                    using var reader = list.ExecuteReader();
                    var updates = new List<(string User, string Groups)>();
                    while (reader.Read())
                    {
                        string user = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        string stored = reader.IsDBNull(1) ? "" : reader.GetString(1);
                        // This account is not in the deleted group; leave its membership alone.
                        if (!AccessGroups.Contains(stored, name))
                            continue;
                        updates.Add((user, AccessGroups.Join(AccessGroups.Parse(stored)
                            .Where(group => !group.Equals(name, StringComparison.OrdinalIgnoreCase)))));
                    }

                    reader.Close();
                    foreach (var (user, groups) in updates)
                    {
                        using var update = db.CreateCommand();
                        update.CommandText = "UPDATE app_accounts SET access_group = $group WHERE username = $user;";
                        update.Parameters.AddWithValue("$group", groups);
                        update.Parameters.AddWithValue("$user", user);
                        update.ExecuteNonQuery();
                    }
                }
                // cmd: DELETE the custom access_groups row after memberships are stripped.
                using var cmd = db.CreateCommand();
                cmd.CommandText = "DELETE FROM access_groups WHERE name = $name;";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.ExecuteNonQuery();
                return true;
            }
            // SQLite failed while stripping memberships or deleting the group.
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>How many accounts currently list this group.</summary>
        public static int CountGroupMembers(string name)
        {
            name = (name ?? "").Trim();
            // Blank group name; there are no members to count.
            if (name.Length == 0)
                return 0;
            EnsureCreated();
            using var db = Open();
            // cmd: SELECT every account's access_group list so we can count members in memory.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(access_group, '') FROM app_accounts;";
            // reader: one access_group string per account.
            using var reader = cmd.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                // This account lists the named group; include it in the member count.
                if (AccessGroups.Contains(reader.IsDBNull(0) ? "" : reader.GetString(0), name))
                    count++;
            }

            return count;
        }

        /// <summary>Store a per-user table_access overlay (empty string means follow groups only).</summary>
        public static void SetTableAccess(string username, string json)
        {
            username = (username ?? "").Trim();
            // Blank username; skip so we do not update every account.
            if (username.Length == 0)
                return;
            // Remote session: the server stores the per-user overlay JSON.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.AccountsAccessSet, new AccountWriteRequest
                {
                    Username = username,
                    Json = json
                });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE table_access overlay for this username.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET table_access = $json WHERE username = $user;";
            cmd.Parameters.AddWithValue("$json", json ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Plaid tokens for a bank account; the access token is sealed at rest.</summary>
        public static (string AccessToken, string ItemId, string AccountId, string Cursor) GetBankLiveLink(long id)
        {
            // Remote session: Plaid tokens come from the server already unsealed.
            if (DataLink.Try(ServerOps.BankLinkGet, new BankWriteRequest { Id = id }, out BankLinkDto? link) &&
                link != null)
                return (link.AccessToken, link.ItemId, link.AccountId, link.Cursor);
            EnsureCreated();
            using var db = Open();
            // cmd: SELECT sealed Plaid token, item, account, and cursor for this bank row.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT COALESCE(plaid_access_token, ''), COALESCE(plaid_item_id, ''),
                       COALESCE(plaid_account_id, ''), COALESCE(plaid_cursor, '')
                FROM bank_accounts WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            // reader: the matching bank_accounts row, if any.
            using var reader = cmd.ExecuteReader();
            // No bank row for this id; return empty Plaid fields.
            if (!reader.Read())
                return ("", "", "", "");
            return (
                SecretProtect.Open(reader.IsDBNull(0) ? "" : reader.GetString(0)),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3));
        }

        /// <summary>Store a Plaid item link, sealing the access token.</summary>
        public static void SetBankLiveLink(
            long id,
            string accessToken,
            string itemId,
            string accountId,
            string cursor)
        {
            // Remote session: the server stores the sealed Plaid link.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.BankLinkSet, new BankWriteRequest
                {
                    Id = id,
                    AccessToken = accessToken,
                    ItemId = itemId,
                    AccountId = accountId,
                    Cursor = cursor
                });
                return;
            }
            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE Plaid token/item/account/cursor for this bank_accounts id.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE bank_accounts SET
                    plaid_access_token = $token,
                    plaid_item_id = $item,
                    plaid_account_id = $account,
                    plaid_cursor = $cursor
                WHERE id = $id;
                """;
            // $token: sealed Plaid access token (plaintext never stored).
            cmd.Parameters.AddWithValue("$token", SecretProtect.Seal(accessToken));
            cmd.Parameters.AddWithValue("$item", itemId ?? "");
            cmd.Parameters.AddWithValue("$account", accountId ?? "");
            cmd.Parameters.AddWithValue("$cursor", cursor ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Update the Plaid transactions cursor after a successful sync.</summary>
        public static void SetBankLiveCursor(long id, string cursor)
        {
            // Remote session: the server stores the new Plaid transactions cursor.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.BankCursor, new BankWriteRequest { Id = id, Cursor = cursor });
                return;
            }
            EnsureCreated();
            using var db = Open();
            // cmd: UPDATE plaid_cursor after a successful sync.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE bank_accounts SET plaid_cursor = $cursor WHERE id = $id;";
            cmd.Parameters.AddWithValue("$cursor", cursor ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Add a missing TEXT column and backfill Record Status to Live when that column is new.</summary>
        private static void EnsureTextColumn(string table, string column, bool archive = false)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            // Column already exists on this live or archive table; skip ALTER TABLE.
            if (existing.Contains(column))
                return;

            using var db = Open(archive);
            // cmd: ALTER TABLE ADD the missing TEXT column.
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
            cmd.ExecuteNonQuery();
            // Newly added Record Status column; fill blanks with Live so old rows join the workflow.
            if (column.Equals(DataFiles.RecordStatus, StringComparison.OrdinalIgnoreCase))
                BackfillLiveStatus(table, archive);
        }

        /// <summary>Set blank Record Status cells to Live so older rows take part in the workflow.</summary>
        private static void BackfillLiveStatus(string table, bool archive = false)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            // Record Status is not on this table yet; nothing to backfill.
            if (!existing.Contains(DataFiles.RecordStatus))
                return;

            using var db = Open(archive);
            // cmd: SET blank Record Status cells to Live on this live or archive table.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                $"UPDATE {Quote(table)} SET {Quote(DataFiles.RecordStatus)} = $live " +
                $"WHERE {Quote(DataFiles.RecordStatus)} IS NULL OR TRIM({Quote(DataFiles.RecordStatus)}) = '';";
            cmd.Parameters.AddWithValue("$live", DataFiles.RecordLive);
            cmd.ExecuteNonQuery();
        }

        /// <summary>
        /// Lot # was the purchase PO and PO # was the customer PO. Copy lot into PO #,
        /// customer PO into Invoice #, then drop Lot #.
        /// </summary>
        private static void MigrateSalesLotToPo(bool archive)
        {
            var columns = new HashSet<string>(TableColumns(DataFiles.Sales, archive), StringComparer.OrdinalIgnoreCase);
            // Lot # already dropped on this live or archive sales table; migration already ran.
            if (!columns.Contains("Lot #"))
                return;

            using var db = Open(archive);
            // cmd: remap old Lot # (purchase PO) into PO #, and old PO # (customer PO) into Invoice #.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE sales
                SET "Invoice #" = "PO #"
                WHERE TRIM(COALESCE("Lot #", '')) != '';
                """;
            cmd.ExecuteNonQuery();
            cmd.CommandText =
                """
                UPDATE sales
                SET "PO #" = "Lot #"
                WHERE TRIM(COALESCE("Lot #", '')) != '';
                """;
            cmd.ExecuteNonQuery();
            DropTextColumn(DataFiles.Sales, "Lot #", archive);
        }

        /// <summary>Drop a leftover column that is no longer in the schema, if it still exists.</summary>
        private static void DropTextColumn(string table, string column, bool archive = false)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            // Column is already gone from this live or archive table; skip DROP COLUMN.
            if (!existing.Contains(column))
                return;

            using var db = Open(archive);
            // cmd: DROP the leftover column (Lot #, PDF Created, Volume Received, …).
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Quote(table)} DROP COLUMN {Quote(column)};";
            cmd.ExecuteNonQuery();
        }

        /// <summary>Add a missing column on app_accounts.</summary>
        private static void EnsureAccountColumn(string column, string definition)
        {
            EnsureAccountColumn("app_accounts", column, definition);
        }

        /// <summary>Add a missing column on an accounts-related table (app_accounts or bank_accounts).</summary>
        private static void EnsureAccountColumn(string table, string column, string definition)
        {
            var existing = new HashSet<string>(TableColumns(table), StringComparer.OrdinalIgnoreCase);
            // Column already exists on this accounts-related table; skip ALTER TABLE.
            if (existing.Contains(column))
                return;

            using var db = Open();
            // cmd: ALTER TABLE ADD the missing account/bank column with its typed definition.
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {definition};";
            cmd.ExecuteNonQuery();
        }

        /// <summary>Insert or replace a stored PDF blob keyed by kind and document number.</summary>
        public static void SavePdf(string kind, string key, string fileName, byte[] content)
        {
            kind = (kind ?? "").Trim();
            // key: document number (invoice # or sales-order #) used as stored_pdfs.doc_key.
            key = (key ?? "").Trim();
            fileName = (fileName ?? "").Trim();
            // Missing kind, document number, file name, or bytes; skip so we do not store an empty PDF.
            if (kind.Length == 0 || key.Length == 0 || fileName.Length == 0 || content.Length == 0)
                return;
            // Remote session: the server upserts the blob in stored_pdfs.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.PdfSave, new PdfRequest
                {
                    Kind = kind, Key = key, FileName = fileName, Content = content
                });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: INSERT or replace the PDF blob keyed by kind + doc_key (live database only).
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO stored_pdfs (kind, doc_key, file_name, content, stored_at)
                VALUES ($kind, $key, $name, $content, $at)
                ON CONFLICT(kind, doc_key) DO UPDATE SET
                    file_name = excluded.file_name,
                    content = excluded.content,
                    stored_at = excluded.stored_at;
                """;
            cmd.Parameters.AddWithValue("$kind", kind);
            // $key: document number stored as stored_pdfs.doc_key.
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$name", fileName);
            // blob: PDF file bytes bound as a SQLite BLOB parameter.
            var blob = cmd.CreateParameter();
            blob.ParameterName = "$content";
            blob.SqliteType = SqliteType.Blob;
            blob.Value = content;
            cmd.Parameters.Add(blob);
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        /// <summary>Remove one stored PDF by kind and document number.</summary>
        public static void DeletePdf(string kind, string key)
        {
            kind = (kind ?? "").Trim();
            // key: document number (invoice # or sales-order #) matching stored_pdfs.doc_key.
            key = (key ?? "").Trim();
            // Missing kind or document number; nothing to delete.
            if (kind.Length == 0 || key.Length == 0)
                return;
            // Remote session: the server deletes the stored blob.
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.PdfDelete, new PdfRequest { Kind = kind, Key = key });
                return;
            }

            EnsureCreated();
            using var db = Open();
            // cmd: DELETE one stored_pdfs row by kind and document number.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM stored_pdfs WHERE kind = $kind AND doc_key = $key;";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.ExecuteNonQuery();
        }

        /// <summary>True when stored_pdfs already has this kind and key.</summary>
        public static bool HasPdf(string kind, string key)
        {
            kind = (kind ?? "").Trim();
            // key: document number (invoice # or sales-order #) matching stored_pdfs.doc_key.
            key = (key ?? "").Trim();
            // Missing kind or document number; treat as not stored.
            if (kind.Length == 0 || key.Length == 0)
                return false;
            // Remote session: the server answers whether the blob exists.
            if (DataLink.Try(ServerOps.PdfHas, new PdfRequest { Kind = kind, Key = key }, out bool has))
                return has;

            EnsureCreated();
            using var db = Open();
            // cmd: EXISTS-style SELECT 1 for this kind and document number.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT 1 FROM stored_pdfs WHERE kind = $kind AND doc_key = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() != null;
        }

        /// <summary>Load a PDF by exact key, or the newest file_name that contains the key.</summary>
        public static (string FileName, byte[] Content)? TryGetPdf(string kind, string key)
        {
            kind = (kind ?? "").Trim();
            // key: document number (invoice # or sales-order #); also matched inside file_name.
            key = (key ?? "").Trim();
            // Missing kind or document number; nothing to look up.
            if (kind.Length == 0 || key.Length == 0)
                return null;
            // Remote session: the server returns the blob (exact key preferred).
            if (DataLink.IsRemote)
            {
                var pdf = DataLink.Call<PdfDto?>(ServerOps.PdfGet, new PdfRequest { Kind = kind, Key = key });
                // Server had no file or an empty blob; treat as missing.
                if (pdf == null || pdf.Content == null || pdf.Content.Length == 0)
                    return null;
                return (pdf.FileName, pdf.Content);
            }

            EnsureCreated();
            using var db = Open();
            // cmd: SELECT the PDF whose doc_key matches, else the newest file_name containing the key.
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT file_name, content FROM stored_pdfs
                WHERE kind = $kind AND (
                    doc_key = $key OR
                    file_name LIKE '%' || $key || '%'
                )
                ORDER BY CASE WHEN doc_key = $key THEN 0 ELSE 1 END, stored_at DESC
                LIMIT 1;
                """;
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            // reader: the preferred stored_pdfs row (exact doc_key first).
            using var reader = cmd.ExecuteReader();
            // No matching PDF by doc_key or file_name; treat as missing.
            if (!reader.Read())
                return null;

            string name = reader.GetString(0);
            byte[] bytes = reader.IsDBNull(1)
                ? Array.Empty<byte>()
                : reader.GetFieldValue<byte[]>(1);
            // Blob is empty; do not return a zero-length PDF.
            if (bytes.Length == 0)
                return null;

            return (name, bytes);
        }

        /// <summary>Import leftover invoice and sales-order PDFs from disk folders into stored_pdfs.</summary>
        public static void ImportPdfsFromFolders()
        {
            // Remote clients have no leftover PDF folders on this PC.
            if (DataLink.IsRemote)
                return;
            // No inventory folder selected; nowhere to look for leftover PDFs.
            if (GetPath() == null)
                return;

            EnsureCreated();
            ImportPdfFolder(DataFiles.GetStoredInvoicesFolder(), DataFiles.PdfKindInvoice, "Invoice ");
            ImportPdfFolder(DataFiles.GetStoredSalesOrdersFolder(), DataFiles.PdfKindSalesOrder, "Sales Order ");
        }

        /// <summary>Every stored PDF of one kind (invoice, sales_order, …).</summary>
        public static List<(string Key, string FileName, byte[] Content)> ListPdfs(string kind)
        {
            kind = (kind ?? "").Trim();
            var list = new List<(string, string, byte[])>();
            // Missing kind or no live database path; return an empty list.
            if (kind.Length == 0 || GetPath() == null)
                return list;

            EnsureCreated();
            using var db = Open();
            // cmd: SELECT every stored_pdfs row of this kind (invoice, sales_order, …).
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT doc_key, file_name, content FROM stored_pdfs WHERE kind = $kind;";
            cmd.Parameters.AddWithValue("$kind", kind);
            // reader: one stored PDF (document number, file name, blob).
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                // key: stored_pdfs.doc_key (invoice # or sales-order #).
                string key = reader.IsDBNull(0) ? "" : reader.GetString(0);
                string name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                byte[] bytes = reader.IsDBNull(2)
                    ? Array.Empty<byte>()
                    : reader.GetFieldValue<byte[]>(2);
                list.Add((key, name, bytes));
            }

            return list;
        }

        /// <summary>Delete every stored PDF of one kind.</summary>
        public static void DeletePdfs(string kind)
        {
            kind = (kind ?? "").Trim();
            // Missing kind or no live database path; nothing to delete.
            if (kind.Length == 0 || GetPath() == null)
                return;

            EnsureCreated();
            using var db = Open();
            // cmd: DELETE every stored_pdfs row of this kind.
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM stored_pdfs WHERE kind = $kind;";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.ExecuteNonQuery();
        }

        /// <summary>Import each PDF in a leftover folder unless that document key is already stored.</summary>
        private static void ImportPdfFolder(string? folder, string kind, string prefix)
        {
            // Folder path is missing or not on disk; nothing to import.
            if (folder == null || !Directory.Exists(folder))
                return;

            // path: leftover Invoice / Sales Order PDF file on disk.
            foreach (var path in Directory.GetFiles(folder, "*.pdf"))
            {
                string name = Path.GetFileName(path);
                string stem = Path.GetFileNameWithoutExtension(name);
                // key: document number parsed from the file name (after prefix, before " - ").
                string key = stem;
                // File name starts with "Invoice " or "Sales Order "; strip that prefix for the doc_key.
                if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    key = stem[prefix.Length..].Trim();
                    int dash = key.IndexOf(" - ", StringComparison.Ordinal);
                    // "Invoice 123 - Customer.pdf" style; keep only the number before " - ".
                    if (dash >= 0)
                        key = key[..dash].Trim();
                }

                // Empty document number or already stored; skip so we do not duplicate blobs.
                if (key.Length == 0 || HasPdf(kind, key))
                    continue;

                try
                {
                    SavePdf(kind, key, name, File.ReadAllBytes(path));
                }
                // File is locked or unreadable; skip it and keep importing the rest.
                catch
                {
                    // skip a locked or unreadable file
                }
            }
        }

        /// <summary>Map a SQLite row to named fields, skipping id/term_start and revealing sealed cells.</summary>
        private static Dictionary<string, string> ReadRow(string table, SqliteDataReader reader)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string name = reader.GetName(i);
                // Skip SQLite id and term_start; callers only want visible table columns.
                if (name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("term_start", StringComparison.OrdinalIgnoreCase))
                    continue;
                // value: cell text, still sealed if this column stores a secret.
                string value = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
                map[name] = SecretProtect.RevealField(table, name, value);
            }

            return map;
        }

        /// <summary>Append every row from live or archive into <paramref name="result"/>.</summary>
        private static void AppendRows(
            string table,
            bool archive,
            List<Dictionary<string, string>> result)
        {
            using var db = Open(archive);
            // cmd: SELECT every row from this live or archive table.
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
            // reader: one SQLite row to map into named fields.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(ReadRow(table, reader));
        }

        /// <summary>One database (live or archive). No-ops if the column is missing on that table.</summary>
        private static void ForEachWhereOn(
            string table,
            bool archive,
            string column,
            string value,
            Action<Dictionary<string, string>> each,
            CancellationToken cancel)
        {
            var columns = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            // Schema on this database may not have the filter column yet.
            if (!columns.Contains(column))
                return;

            using var db = Open(archive);
            // cmd: SELECT matching rows from this live or archive file (case-insensitive cell match).
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                $"SELECT * FROM {Quote(table)} WHERE {Quote(column)} = $v COLLATE NOCASE;";
            cmd.Parameters.AddWithValue("$v", value);
            // reader: each matching SQLite row for the callback.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                cancel.ThrowIfCancellationRequested();
                each(ReadRow(table, reader));
            }
        }

        /// <summary>Append rows with ids; archive ids are negated so they cannot collide with live ids.</summary>
        private static void AppendRowsWithIds(
            string table,
            bool archive,
            List<(long Id, Dictionary<string, string> Fields)> result)
        {
            using var db = Open(archive);
            // cmd: SELECT every row from this live or archive table, including SQLite id.
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
            // reader: one SQLite row; archive ids are negated below so they cannot collide with live.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(reader.GetOrdinal("id"));
                // Archive row: encode as a negative combined-view id so UpdateById can route the write.
                if (archive)
                    id = EncodeArchiveRowId(id);
                result.Add((id, ReadRow(table, reader)));
            }
        }

        /// <summary>Add any missing TEXT columns on one database file.</summary>
        private static void EnsureColumnsOn(string table, IEnumerable<string> columns, bool archive)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            using var db = Open(archive);
            foreach (var column in columns)
            {
                // Column already exists on this live or archive table; skip ALTER TABLE.
                if (existing.Contains(column))
                    continue;
                // cmd: ALTER TABLE ADD the missing TEXT column.
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
                cmd.ExecuteNonQuery();
                existing.Add(column);
            }
        }

        /// <summary>COUNT(*) on live or archive for one table.</summary>
        private static int CountIn(string table, bool archive)
        {
            using var db = Open(archive);
            // cmd: COUNT(*) of all rows in this live or archive table.
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)};";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        /// <summary>True when a combined-view id is negative, meaning the row lives in old_inventory.db.</summary>
        // Combined view encodes archive SQLite ids as negative so UpdateById can route the write.
        private static bool IsArchiveRowId(long id) => id < 0;

        /// <summary>Negate a positive SQLite id so archive rows cannot collide with live ids.</summary>
        private static long EncodeArchiveRowId(long id) => id > 0 ? -id : id;

        /// <summary>Absolute SQLite id: strip the negative archive encoding used in the combined view.</summary>
        private static long DecodeRowId(long id) => id < 0 ? -id : id;

        /// <summary>Column names for one file, or the union of live and archive when Old view is on.</summary>
        private static List<string> TableColumns(string table, bool? archive = null)
        {
            // Caller did not pick a file, and Old view is on; union live columns with archive columns.
            if (archive == null && UsingArchive(table))
            {
                var combined = new List<string>();
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var column in TableColumnsFrom(table, false).Concat(TableColumnsFrom(table, true)))
                {
                    // Duplicate name already listed from live; keep the first occurrence.
                    if (!used.Add(column))
                        continue;
                    combined.Add(column);
                }

                return combined;
            }

            return TableColumnsFrom(table, archive ?? false);
        }

        /// <summary>PRAGMA table_info column names for one database file.</summary>
        private static List<string> TableColumnsFrom(string table, bool archive)
        {
            var list = new List<string>();
            using var db = Open(archive);
            // cmd: PRAGMA table_info for this live or archive table.
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({Quote(table)});";
            // reader: one column definition; name is field 1.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(reader.GetString(1));
            return list;
        }

        /// <summary>Open live or archive. Shared/cloud folders use DELETE journal mode instead of WAL.</summary>
        private static SqliteConnection Open(bool archive = false)
        {
            // path: full filesystem path of crc_inventory.db or old_inventory.db.
            string? path = archive ? GetArchivePath() : GetPath();
            // No inventory folder selected; cannot open either database file.
            if (path == null)
                throw new InvalidOperationException("Select a data folder first.");

            bool shared = IsSharedLocation(AppState.InventoryFolder);
            var db = new SqliteConnection(new SqliteConnectionStringBuilder
            {
                DataSource = path,
                Mode = SqliteOpenMode.ReadWriteCreate,
                DefaultTimeout = shared ? 15 : 5
            }.ToString());
            db.Open();
            // pragma: busy_timeout so shared/cloud folders wait longer on a locked database.
            using (var pragma = db.CreateCommand())
            {
                pragma.CommandText = shared
                    ? "PRAGMA busy_timeout=8000;"
                    : "PRAGMA busy_timeout=5000;";
                pragma.ExecuteNonQuery();
            }
            return db;
        }

        /// <summary>Seal any leftover plaintext SMTP, Plaid, routing, and account-number values.</summary>
        private static void UpgradeSecrets()
        {
            UpgradeSetting("smtp_password");
            UpgradeSetting("plaid_secret");
            UpgradeSmtpPassword();
            UpgradeColumn("bank_accounts", "plaid_access_token");
            UpgradeColumn(DataFiles.Customers, DataFiles.RoutingNumber);
            UpgradeColumn(DataFiles.Customers, DataFiles.AccountNumber);
            UpgradeColumn(DataFiles.Vendors, DataFiles.RoutingNumber);
            UpgradeColumn(DataFiles.Vendors, DataFiles.AccountNumber);
        }

        /// <summary>Seal one app_settings value if it is still stored in plaintext.</summary>
        private static void UpgradeSetting(string key)
        {
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
            // $key: setting name to upgrade (smtp_password or plaid_secret).
            read.Parameters.AddWithValue("$key", key);
            // value: stored setting text, still plaintext if this DB predates sealing.
            string? value = read.ExecuteScalar()?.ToString();
            // Missing or already sealed; nothing to rewrite.
            if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
                return;
            // write: seal the leftover plaintext setting and store it back under the same key.
            using var write = db.CreateCommand();
            write.CommandText = "UPDATE app_settings SET value = $value WHERE key = $key;";
            write.Parameters.AddWithValue("$value", SecretProtect.Seal(value));
            write.Parameters.AddWithValue("$key", key);
            write.ExecuteNonQuery();
        }

        /// <summary>Seal admin_smtp.password if it is still stored in plaintext.</summary>
        private static void UpgradeSmtpPassword()
        {
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText = "SELECT password FROM admin_smtp WHERE id = 1;";
            // value: stored SMTP password, still plaintext if this DB predates sealing.
            string? value = read.ExecuteScalar()?.ToString();
            // Missing or already sealed; nothing to rewrite.
            if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
                return;
            // write: seal the leftover plaintext SMTP password on the id=1 row.
            using var write = db.CreateCommand();
            write.CommandText = "UPDATE admin_smtp SET password = $value WHERE id = 1;";
            write.Parameters.AddWithValue("$value", SecretProtect.Seal(value));
            write.ExecuteNonQuery();
        }

        /// <summary>Seal every plaintext cell in a sensitive column (routing, account, Plaid token).</summary>
        private static void UpgradeColumn(string table, string column)
        {
            var columns = TableColumns(table);
            // Sensitive column is not on this table; skip the upgrade.
            if (!columns.Contains(column, StringComparer.OrdinalIgnoreCase))
                return;
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText = $"SELECT id, {Quote(column)} FROM {Quote(table)};";
            var updates = new List<(long Id, string Value)>();
            using (var reader = read.ExecuteReader())
            {
                while (reader.Read())
                {
                    // value: cell text, still plaintext if this DB predates sealing.
                    string value = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
                    // Empty or already sealed; leave this row alone.
                    if (value.Length == 0 || SecretProtect.IsSealed(value))
                        continue;
                    updates.Add((reader.GetInt64(0), SecretProtect.Seal(value)));
                }
            }

            foreach (var row in updates)
            {
                // write: store the sealed cell back on this SQLite id.
                using var write = db.CreateCommand();
                write.CommandText = $"UPDATE {Quote(table)} SET {Quote(column)} = $value WHERE id = $id;";
                write.Parameters.AddWithValue("$value", row.Value);
                write.Parameters.AddWithValue("$id", row.Id);
                write.ExecuteNonQuery();
            }
        }

        /// <summary>Value to store for a column: Live if Record Status is blank, otherwise sealed when needed.</summary>
        private static string CellValue(string table, Dictionary<string, string> values, string name)
        {
            string value = Lookup(values, name);
            // Record Status is blank; store Live so the row takes part in the workflow.
            if (name.Equals(DataFiles.RecordStatus, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(value))
                return DataFiles.RecordLive;
            return SecretProtect.StoreField(table, name, value);
        }

        /// <summary>Case-insensitive field lookup, with Customer PO / Lot # aliases for older sale rows.</summary>
        private static string Lookup(Dictionary<string, string> values, string name)
        {
            foreach (var pair in values)
            {
                // Dictionary key matches the requested column name (case-insensitive).
                if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return pair.Value ?? "";
            }

            // Older sale rows stored the customer PO as PO #; Invoice # is now that field.
            if (name.Equals("Customer PO", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in values)
                {
                    // Fall back to PO # when looking up Customer PO on a pre-migration row.
                    if (pair.Key.Equals("PO #", StringComparison.OrdinalIgnoreCase))
                        return pair.Value ?? "";
                }
            }

            // Lot # was the purchase PO; PO # is now that field after MigrateSalesLotToPo.
            if (name.Equals("PO #", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in values)
                {
                    // Fall back to Lot # when looking up PO # on a pre-migration sale row.
                    if (pair.Key.Equals("Lot #", StringComparison.OrdinalIgnoreCase))
                        return pair.Value ?? "";
                }
            }

            return "";
        }

        /// <summary>Default TermStartDate to today when a write happens before Settings has a term.</summary>
        private static void EnsureTerm()
        {
            // Term is already set in Settings; do not overwrite it with today.
            if (AppState.TermStartDate != null)
                return;
            AppState.TermStartDate = DateTime.Today;
            AppLock.SaveSettings();
        }

        /// <summary>Current term as yyyy-MM-dd for term_start stamps.</summary>
        private static string TermKey() =>
            (AppState.TermStartDate ?? DateTime.Today).ToString("yyyy-MM-dd");

        /// <summary>True for tables that roll into old_inventory.db when completed.</summary>
        private static bool IsProcessTable(string table) =>
            ProcessTables.Any(t => t.Equals(table, StringComparison.OrdinalIgnoreCase));

        /// <summary>Empty until the process is complete; then the current term date.</summary>
        private static string CompletionStamp(
            string table,
            Dictionary<string, string> values,
            DateTime? term)
        {
            // Master lookup or a finished process row; stamp term_start so it can roll to Old Inventory.
            if (MasterTables.Contains(table) || IsProcessComplete(table, values))
                return (term ?? AppState.TermStartDate)?.ToString("yyyy-MM-dd") ?? TermKey();

            return "";
        }

        /// <summary>
        /// Purchases: shipped, arrived, or closed.
        /// Sales: invoiced, paid, or closed.
        /// Invoices: paid/closed, payment date, or paid covers the amount.
        /// Banking: has a date or amount.
        /// Debits/credits: approved.
        /// </summary>
        public static bool IsProcessComplete(string table, Dictionary<string, string> values)
        {
            // Purchase/Sales row: shipped, arrived, or closed — ready to leave live.
            if (table.Equals(DataFiles.PurchaseSales, StringComparison.OrdinalIgnoreCase))
            {
                return IsClosedStatus(Lookup(values, "Status")) ||
                       HasText(values, "Ship Date") ||
                       HasText(values, "Arrival Date");
            }

            // Sales row: Invoice # (customer PO filled after invoicing), closed status, or a paid amount.
            if (table.Equals(DataFiles.Sales, StringComparison.OrdinalIgnoreCase))
            {
                return HasText(values, "Invoice #") ||
                       IsClosedStatus(Lookup(values, "Status")) ||
                       HasPositiveNumber(values, "Paid");
            }

            // Invoice row: paid/closed, a payment date, or Paid covering Amount.
            if (table.Equals(DataFiles.Invoices, StringComparison.OrdinalIgnoreCase))
            {
                return IsClosedStatus(Lookup(values, "Status")) ||
                       HasText(values, "Payment Date") ||
                       PaidCoversAmount(values);
            }

            // Bank transaction: has a date or amount, so it is a real posted line.
            if (table.Equals(DataFiles.BankTransactions, StringComparison.OrdinalIgnoreCase))
                return HasText(values, "Date") || HasText(values, "Amount");

            // Debit row: vendor approved it.
            if (table.Equals(DataFiles.Debits, StringComparison.OrdinalIgnoreCase))
                return IsApproved(Lookup(values, "Vendor Approved"));

            // Credit row: approved.
            if (table.Equals(DataFiles.Credits, StringComparison.OrdinalIgnoreCase))
                return IsApproved(Lookup(values, "Approved"));

            return !IsProcessTable(table);
        }

        /// <summary>True when the named cell has any non-blank text.</summary>
        private static bool HasText(Dictionary<string, string> values, string column) =>
            Lookup(values, column).Trim().Length > 0;

        /// <summary>True when the named money/qty cell parses as a number greater than zero.</summary>
        private static bool HasPositiveNumber(Dictionary<string, string> values, string column)
        {
            string raw = Lookup(values, column).Trim().Replace("$", "").Replace(",", "");
            return decimal.TryParse(raw, out var amount) && amount > 0;
        }

        /// <summary>True when Paid is at least Amount, or Paid is set and Outstanding is zero or less.</summary>
        private static bool PaidCoversAmount(Dictionary<string, string> values)
        {
            // Paid parses as at least Amount (both > 0); the invoice is fully paid.
            if (HasPositiveNumber(values, "Paid") &&
                decimal.TryParse(Lookup(values, "Amount").Trim().Replace("$", "").Replace(",", ""), out var amount) &&
                decimal.TryParse(Lookup(values, "Paid").Trim().Replace("$", "").Replace(",", ""), out var paid) &&
                amount > 0 && paid >= amount)
                return true;

            string outstanding = Lookup(values, "Outstanding").Trim().Replace("$", "").Replace(",", "");
            return HasPositiveNumber(values, "Paid") &&
                   decimal.TryParse(outstanding, out var left) &&
                   left <= 0;
        }

        /// <summary>True for paid, closed, complete, completed, finished, or settled.</summary>
        private static bool IsClosedStatus(string status)
        {
            string value = (status ?? "").Trim();
            return value.Equals("paid", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("closed", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("complete", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("completed", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("finished", StringComparison.OrdinalIgnoreCase) ||
                   value.Equals("settled", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>True for yes/y/true/1/approved/x on debit and credit approval cells.</summary>
        private static bool IsApproved(string value)
        {
            string trimmed = (value ?? "").Trim();
            // Empty approval cell; treat as not approved.
            if (trimmed.Length == 0)
                return false;

            return trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("x", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>Live-database rows with id and term_start, used when deciding what to archive.</summary>
        private static List<(long Id, string TermStart, Dictionary<string, string> Fields)> ReadLiveRows(string table)
        {
            var result = new List<(long, string, Dictionary<string, string>)>();
            using var db = Open();
            // cmd: SELECT every live row including id and term_start for archive decisions.
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
            // reader: one live SQLite row.
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(reader.GetOrdinal("id"));
                int termOrd = reader.GetOrdinal("term_start");
                string term = reader.IsDBNull(termOrd) ? "" : reader.GetValue(termOrd)?.ToString() ?? "";
                result.Add((id, term, ReadRow(table, reader)));
            }

            return result;
        }

        /// <summary>Add live columns that are missing on old_inventory.db before copying completed rows.</summary>
        private static void EnsureArchiveColumns(string table, IEnumerable<string> columns)
        {
            var existing = new HashSet<string>(TableColumns(table, archive: true), StringComparer.OrdinalIgnoreCase);
            using var db = Open(archive: true);
            foreach (var column in columns)
            {
                // Archive already has this live column; skip ALTER TABLE.
                if (existing.Contains(column))
                    continue;

                // cmd: ALTER TABLE ADD the missing TEXT column on old_inventory.db.
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
                cmd.ExecuteNonQuery();
                existing.Add(column);
            }
        }

        /// <summary>Insert one archived process row inside an open transaction.</summary>
        private static void InsertRow(
            SqliteConnection db,
            SqliteTransaction tx,
            string table,
            IEnumerable<string> columns,
            Dictionary<string, string> values,
            string termStart)
        {
            // cmd: INSERT one archived process row using the open archive transaction.
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            var cols = new List<string> { Quote("term_start") };
            var pars = new List<string> { "$term" };
            cmd.Parameters.AddWithValue("$term", termStart ?? "");
            int i = 0;
            foreach (var name in columns)
            {
                // Skip SQLite id (new autoincrement) and term_start (already added as $term).
                if (name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("term_start", StringComparison.OrdinalIgnoreCase))
                    continue;

                cols.Add(Quote(name));
                string p = "$c" + i;
                pars.Add(p);
                cmd.Parameters.AddWithValue(p, SecretProtect.StoreField(table, name, Lookup(values, name)));
                i++;
            }

            cmd.CommandText =
                $"INSERT INTO {Quote(table)} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
            cmd.ExecuteNonQuery();
        }

        /// <summary>Delete live rows by id after they have been copied to the archive.</summary>
        private static void DeleteByIds(string table, List<long> ids)
        {
            // No ids to delete; skip opening a transaction.
            if (ids.Count == 0)
                return;

            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var id in ids)
            {
                // cmd: DELETE this live SQLite id after it was copied to archive.
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText = $"DELETE FROM {Quote(table)} WHERE id = $id;";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>Clear term_start on unfinished process rows so they stay live and undated.</summary>
        private static void ClearTermStart(string table, List<long> ids)
        {
            // No unfinished rows; skip opening a transaction.
            if (ids.Count == 0)
                return;

            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var id in ids)
            {
                // cmd: blank term_start on this live unfinished process row.
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    $"UPDATE {Quote(table)} SET term_start = '' WHERE id = $id AND term_start <> '';";
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        /// <summary>Quote an identifier so headers with spaces (PO #) are valid SQL.</summary>
        private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    }
}
