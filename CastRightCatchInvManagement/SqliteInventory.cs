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

        public static string? GetPath()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, FileName);
        }

        public static string? GetArchivePath()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, ArchiveFileName);
        }

        public static bool Exists()
        {
            if (DataLink.IsRemote)
                return true;
            string? path = GetPath();
            return path != null && File.Exists(path);
        }

        /// <summary>Create both database files and any missing tables or columns.</summary>
        public static void EnsureCreated()
        {
            if (!string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                SecretProtect.UseFolder(AppState.InventoryFolder);
            if (DataLink.Try(ServerOps.TableEnsure, new { }, out bool _))
                return;
            EnsureCreated(archive: false);
            EnsureCreated(archive: true);
        }

        public static void EnsureCreated(bool archive)
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            Directory.CreateDirectory(AppState.InventoryFolder);
            using var db = Open(archive);
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
                if (table == DataFiles.PurchaseSales)
                {
                    DropTextColumn(table, "Vendor Invoice #", archive);
                    DropTextColumn(table, "Volume Received", archive);
                }
                if (table == DataFiles.Invoices)
                    DropTextColumn(table, "PDF Created", archive);
            }

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

        public static void ImportCsvsIfEmpty()
        {
            if (DataLink.IsRemote)
                return;
            if (GetPath() == null)
                return;

            EnsureCreated();

            foreach (var table in DataFiles.All)
            {
                if (Count(table, currentTermOnly: false) > 0)
                    continue;

                var matches = Directory.GetFiles(AppState.InventoryFolder!, table + "_*.csv");
                foreach (var path in matches)
                {
                    var rows = CsvIO.Read(path);
                    if (rows.Count < 2)
                        continue;

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

        public static string[] Headers(string table)
        {
            if (DataLink.Try(ServerOps.TableHeaders, DataLink.Table(table), out string[]? headers) && headers != null)
                return headers;
            EnsureCreated();
            var expected = DataFiles.GetExpectedHeader(table).Split(',')
                .Select(h => h.Trim())
                .Where(h => h.Length > 0)
                .ToList();
            var actual = TableColumns(table)
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

        /// <summary>
        /// Current view: live rows only. Old view: archived process rows, then live rows.
        /// </summary>
        public static List<Dictionary<string, string>> Read(string table) =>
            DataAccess.RestrictRows(table, ReadUnrestricted(table));

        /// <summary>Every row, ignoring the signed-in user's blocks. For numbering and access expansion.</summary>
        public static List<Dictionary<string, string>> ReadUnrestricted(string table)
        {
            if (DataLink.Try(ServerOps.TableRead, DataLink.Table(table), out List<Dictionary<string, string>>? rows) &&
                rows != null)
                return rows;
            EnsureCreated();
            var result = new List<Dictionary<string, string>>();
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

        public static List<(long Id, Dictionary<string, string> Fields)> ReadWithIdsUnrestricted(string table)
        {
            if (DataLink.Try(ServerOps.TableReadIds, DataLink.Table(table), out List<IdFieldsDto>? remote) &&
                remote != null)
            {
                return remote.Select(row => (row.Id, row.Fields ?? new Dictionary<string, string>())).ToList();
            }
            EnsureCreated();
            var result = new List<(long, Dictionary<string, string>)>();
            if (UsingArchive(table))
                AppendRowsWithIds(table, archive: true, result);
            AppendRowsWithIds(table, archive: false, result);
            return result;
        }

        /// <summary>Always inserts into the live database. Process rows stay undated until complete.</summary>
        public static void Insert(string table, Dictionary<string, string> values, DateTime? term = null)
        {
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.TableInsert, DataLink.Table(table, values, term: term));
                return;
            }
            EnsureCreated();
            EnsureTerm();
            var headers = Headers(table);
            using var db = Open();
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

        public static int InsertMany(
            string table,
            IEnumerable<Dictionary<string, string>> rows,
            DateTime? term = null)
        {
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
            if (DataLink.Try(ServerOps.TableUpdate, DataLink.Table(table, values, id), out bool updated))
                return updated;
            EnsureCreated();
            bool archive = IsArchiveRowId(id);
            long rawId = DecodeRowId(id);
            if (rawId <= 0)
                return false;

            var headers = Headers(table);
            using var db = Open(archive);
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

        public static bool DeleteById(string table, long id)
        {
            if (DataLink.IsRemote)
            {
                // Remote updates go through table.update; delete uses a blank-row replace locally first.
            }

            EnsureCreated();
            DeleteByIds(table, new List<long> { id });
            return true;
        }

        public static void EnsureColumns(string table, params string[] columns)
        {
            if (columns.Length == 0)
                return;
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.TableEnsureColumns, DataLink.Table(table, columns: columns));
                return;
            }

            EnsureCreated();
            EnsureColumnsOn(table, columns, archive: false);
            if (UsingArchive(table))
                EnsureColumnsOn(table, columns, archive: true);
        }

        public static int Count(string table, bool currentTermOnly = true)
        {
            if (DataLink.Try(ServerOps.TableCount, DataLink.Table(table, currentTermOnly: currentTermOnly), out int count))
                return count;
            EnsureCreated();
            _ = currentTermOnly;
            int total = CountIn(table, archive: false);
            if (UsingArchive(table))
                total += CountIn(table, archive: true);
            return total;
        }

        /// <summary>
        /// Move completed process rows into Old Inventory and leave unfinished rows in live, undated.
        /// </summary>
        public static int ArchiveCompleted(DateTime? term = null)
        {
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
                    if (IsProcessComplete(table, fields))
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

        public static DateTime? LatestTerm()
        {
            if (DataLink.Try(ServerOps.TableLatestTerm, new { }, out string? text))
                return DateTime.TryParse(text, out var parsed) ? parsed : null;
            if (!Exists())
                return null;

            DateTime? latest = null;
            using var db = Open();
            foreach (var table in DataFiles.All)
            {
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"SELECT MAX(term_start) FROM {Quote(table)};";
                var value = cmd.ExecuteScalar()?.ToString();
                if (DateTime.TryParse(value, out var date) && (latest == null || date > latest))
                    latest = date;
            }

            return latest;
        }

        public static bool IsSharedLocation(string? folder)
        {
            if (string.IsNullOrWhiteSpace(folder))
                return false;

            if (folder.StartsWith(@"\\", StringComparison.Ordinal) ||
                folder.StartsWith("//", StringComparison.Ordinal))
                return true;

            try
            {
                string? root = Path.GetPathRoot(folder);
                if (!string.IsNullOrEmpty(root) && root.Length >= 2 && root[1] == ':')
                {
                    var drive = new DriveInfo(root);
                    if (drive.DriveType == DriveType.Network)
                        return true;
                }
            }
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

        public static Dictionary<string, string> ReadSettings()
        {
            if (AppState.IsAdmin &&
                DataLink.Try(ServerOps.SettingsRead, new { }, out Dictionary<string, string>? admin) &&
                admin != null)
                return admin;
            if (DataLink.Try(ServerOps.SettingsReadPublic, new { }, out Dictionary<string, string>? pub) &&
                pub != null)
                return pub;
            EnsureCreated();
            var map = ReadSettingsRaw();
            return AppState.IsAdmin
                ? SecretProtect.RevealSettings(map)
                : SecretProtect.WithoutSecrets(map);
        }

        public static Dictionary<string, string> ReadPublicSettings()
        {
            if (DataLink.Try(ServerOps.SettingsReadPublic, new { }, out Dictionary<string, string>? remote) &&
                remote != null)
                return remote;
            EnsureCreated();
            return SecretProtect.WithoutSecrets(ReadSettingsRaw());
        }

        private static Dictionary<string, string> ReadSettingsRaw()
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_settings;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                string value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                map[key] = value;
            }

            return map;
        }

        public static Dictionary<string, string> ReadPrefs()
        {
            string user = (AppState.CurrentUsername ?? "").Trim();
            if (user.Length == 0)
                return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (DataLink.Try(ServerOps.PrefsRead, new { }, out Dictionary<string, string>? remote) &&
                remote != null)
                return remote;
            EnsureCreated();
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_prefs WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", user);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
            return map;
        }

        public static void WritePrefs(Dictionary<string, string> values)
        {
            string user = (AppState.CurrentUsername ?? "").Trim();
            if (user.Length == 0 || values.Count == 0)
                return;
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
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO app_prefs (username, key, value)
                    VALUES ($user, $key, $value)
                    ON CONFLICT(username, key) DO UPDATE SET value = excluded.value;
                    """;
                cmd.Parameters.AddWithValue("$user", user);
                cmd.Parameters.AddWithValue("$key", pair.Key);
                cmd.Parameters.AddWithValue("$value", pair.Value ?? "");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public static (string Email, string Password, string Host, int Port) LoadAdminSmtp()
        {
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT login_email, password, host, port FROM admin_smtp WHERE id = 1;";
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return ("", "", Mailer.DefaultHost, Mailer.DefaultPort);

            string email = reader.IsDBNull(0) ? "" : reader.GetString(0);
            string password = SecretProtect.Open(reader.IsDBNull(1) ? "" : reader.GetString(1));
            string host = reader.IsDBNull(2) ? "" : reader.GetString(2);
            int port = Mailer.DefaultPort;
            if (!reader.IsDBNull(3) && int.TryParse(reader.GetString(3), out int parsed) && parsed > 0)
                port = parsed;
            return (email ?? "", password ?? "", host ?? "", port);
        }

        public static void ApplyAdminSmtp()
        {
            var row = LoadAdminSmtp();
            if (row.Email.Length > 0)
                AppState.SmtpUser = row.Email;
            if (row.Password.Length > 0)
                AppState.SmtpPassword = row.Password;
            if (row.Host.Length > 0)
                AppState.SmtpHost = row.Host;
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
                if (!string.Equals(check.Email, email ?? "", StringComparison.Ordinal))
                {
                    error = "The database did not keep the login email.";
                    return false;
                }

                if ((password ?? "").Length > 0 &&
                    !string.Equals(check.Password, password, StringComparison.Ordinal))
                {
                    error = "The database did not keep the password.";
                    return false;
                }

                ApplyAdminSmtp();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        private static void SeedAdminSmtpFromSettings()
        {
            using var db = Open();
            using var exists = db.CreateCommand();
            exists.CommandText = "SELECT COUNT(*) FROM admin_smtp WHERE id = 1;";
            if (Convert.ToInt32(exists.ExecuteScalar()) > 0)
                return;

            string email = "", password = "", host = Mailer.DefaultHost, port = Mailer.DefaultPort.ToString();
            using var read = db.CreateCommand();
            read.CommandText = "SELECT key, value FROM app_settings WHERE key LIKE 'smtp_%';";
            using var reader = read.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.GetString(0);
                string value = reader.IsDBNull(1) ? "" : reader.GetString(1);
                if (key.Equals("smtp_user", StringComparison.OrdinalIgnoreCase))
                    email = value;
                else if (key.Equals("smtp_password", StringComparison.OrdinalIgnoreCase))
                    password = value;
                else if (key.Equals("smtp_host", StringComparison.OrdinalIgnoreCase) && value.Length > 0)
                    host = value;
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

        public static void WriteSettings(Dictionary<string, string> values)
        {
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
                if (SecretProtect.IsSecretSetting(pair.Key) &&
                    (!AppState.IsAdmin || string.IsNullOrEmpty(pair.Value)))
                    continue;
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO app_settings (key, value)
                    VALUES ($key, $value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                    """;
                cmd.Parameters.AddWithValue("$key", pair.Key);
                cmd.Parameters.AddWithValue("$value", SecretProtect.StoreSetting(pair.Key, pair.Value));
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public static string? ReadUserEmail(string windowsUser)
        {
            windowsUser = (windowsUser ?? "").Trim();
            if (windowsUser.Length == 0)
                return null;
            if (DataLink.Try(ServerOps.UserEmailRead, new UserEmailRequest { WindowsUser = windowsUser }, out string? email))
                return email;
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT email FROM app_users WHERE windows_user = $user;";
            cmd.Parameters.AddWithValue("$user", windowsUser);
            var value = cmd.ExecuteScalar()?.ToString();
            return value;
        }

        public static void WriteUserEmail(string windowsUser, string? email)
        {
            windowsUser = (windowsUser ?? "").Trim();
            if (windowsUser.Length == 0)
                return;
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.UserEmailWrite, new UserEmailRequest { WindowsUser = windowsUser, Email = email });
                return;
            }

            EnsureCreated();
            using var db = Open();
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

        public static int CountAccounts()
        {
            if (DataLink.Try(ServerOps.AccountsCount, new { }, out int count))
                return count;
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM app_accounts;";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

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
            if (username.Length == 0)
                return false;
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
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT password_hash, password_salt, display_name, email,
                       COALESCE(must_change_password, 0)
                FROM app_accounts WHERE username = $user;
                """;
            cmd.Parameters.AddWithValue("$user", username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return false;

            passwordHash = reader.IsDBNull(0) ? "" : reader.GetString(0);
            passwordSalt = reader.IsDBNull(1) ? "" : reader.GetString(1);
            displayName = reader.IsDBNull(2) ? "" : reader.GetString(2);
            email = reader.IsDBNull(3) ? "" : reader.GetString(3);
            mustChangePassword = !reader.IsDBNull(4) && reader.GetInt32(4) != 0;
            return true;
        }

        public static bool InsertAccount(
            string username,
            string displayName,
            string passwordHash,
            string passwordSalt,
            string email)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;

            EnsureCreated();
            using var db = Open();
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
            catch (SqliteException)
            {
                return false;
            }
        }

        public static List<(string Username, string DisplayName, string Email, bool IsAdmin, bool IsIt, bool StaySignedIn, bool LoginLocked)> ListAccounts()
        {
            if (DataLink.Try(ServerOps.AccountsList, new { }, out List<AccountListDto>? remote) && remote != null)
            {
                return remote.Select(a => (
                    a.Username, a.DisplayName, a.Email, a.IsAdmin, a.IsIt, a.StaySignedIn, a.LoginLocked)).ToList();
            }
            EnsureCreated();
            var list = new List<(string, string, string, bool, bool, bool, bool)>();
            using var db = Open();
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

        public static bool UpdateAccount(
            string username,
            string displayName,
            string email)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;
            if (DataLink.Try(ServerOps.AccountsUpdate, new AccountWriteRequest
            {
                Username = username,
                DisplayName = displayName,
                Email = email
            }, out bool updatedAccount))
                return updatedAccount;

            EnsureCreated();
            using var db = Open();
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

        public static bool UpdateAccountPassword(string username, string passwordHash, string passwordSalt)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;

            EnsureCreated();
            using var db = Open();
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
            if (updated)
            {
                DeleteSessionsForUser(username);
                ClearRecoveryFails(username);
                ClearLoginFails(username);
            }
            return updated;
        }

        public static void SetMustChangePassword(string username, bool mustChange)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
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
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET must_change_password = $flag WHERE username = $user;";
            cmd.Parameters.AddWithValue("$flag", mustChange ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static bool RenameAccount(string oldUsername, string newUsername)
        {
            oldUsername = (oldUsername ?? "").Trim();
            newUsername = (newUsername ?? "").Trim();
            if (oldUsername.Length == 0 || newUsername.Length == 0)
                return false;
            if (oldUsername.Equals(newUsername, StringComparison.OrdinalIgnoreCase))
                return true;
            if (DataLink.Try(ServerOps.AccountsRename, new AccountWriteRequest
            {
                OldUsername = oldUsername,
                NewUsername = newUsername
            }, out bool renamed))
                return renamed;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET username = $new WHERE username = $old;";
            cmd.Parameters.AddWithValue("$new", newUsername);
            cmd.Parameters.AddWithValue("$old", oldUsername);
            try
            {
                if (cmd.ExecuteNonQuery() <= 0)
                    return false;
                RenameSessions(oldUsername, newUsername);
                return true;
            }
            catch (SqliteException)
            {
                return false;
            }
        }

        public static void UpdateAccountEmail(string username, string email)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
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
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET email = $email WHERE username = $user;";
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        private static void ClearRecoveryFails(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET recover_fails = 0, recover_lock_until = '' WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static bool AllowLogin(string username, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            if (username.Length == 0)
            {
                error = "Enter a username and password.";
                return false;
            }

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT COALESCE(login_lock_until, '') FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            string untilText = cmd.ExecuteScalar()?.ToString() ?? "";
            if (RecoveryGuard.IsItLock(untilText))
            {
                error = RecoveryGuard.ItLockMessage;
                return false;
            }

            if (DateTime.TryParse(untilText, out var until) && until > DateTime.Now)
            {
                error = RecoveryGuard.LockedMessage(until);
                return false;
            }

            if (untilText.Length > 0)
                ClearLoginTimeLock(username);
            return true;
        }

        public static string NoteLoginFailure(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return "That username or password is not right.";

            EnsureCreated();
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText =
                "SELECT COALESCE(login_fails, 0) FROM app_accounts WHERE username = $user;";
            read.Parameters.AddWithValue("$user", username);
            object? raw = read.ExecuteScalar();
            if (raw == null)
                return "That username or password is not right.";

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

        private static void ClearLoginTimeLock(string username)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET login_lock_until = '' WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static void ClearLoginFails(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET login_fails = 0, login_lock_until = '' WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static bool DeleteAccount(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;
            if (DataLink.Try(ServerOps.AccountsDelete, new AccountWriteRequest { Username = username }, out bool deleted))
                return deleted;

            EnsureCreated();
            DeleteSessionsForUser(username);
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static bool GetStaySignedIn(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;
            if (DataLink.Try(ServerOps.AccountsStayGet, new AccountWriteRequest { Username = username }, out bool stay))
                return stay;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT COALESCE(stay_signed_in, 0) FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            var value = cmd.ExecuteScalar();
            return value != null && value != DBNull.Value && Convert.ToInt32(value) != 0;
        }

        public static void SetStaySignedIn(string username, bool enabled)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
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
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET stay_signed_in = $flag WHERE username = $user;";
            cmd.Parameters.AddWithValue("$flag", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
            if (!enabled)
                DeleteSessionsForUser(username);
        }

        /// <summary>Store a hashed stay-signed-in token. Plain tokens never go in the database.</summary>
        public static void InsertSession(string username, string tokenHash, DateTime expiresAt)
        {
            username = (username ?? "").Trim();
            tokenHash = (tokenHash ?? "").Trim();
            if (username.Length == 0 || tokenHash.Length == 0)
                return;
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            DeleteExpiredSessions();
            using var db = Open();
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

        public static string? FindSessionUsername(string tokenHash)
        {
            tokenHash = (tokenHash ?? "").Trim();
            if (tokenHash.Length == 0)
                return null;
            if (DataLink.IsRemote)
                return null;

            EnsureCreated();
            DeleteExpiredSessions();
            using var db = Open();
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

        public static void DeleteSession(string tokenHash)
        {
            tokenHash = (tokenHash ?? "").Trim();
            if (tokenHash.Length == 0)
                return;
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_sessions WHERE token_hash = $hash;";
            cmd.Parameters.AddWithValue("$hash", tokenHash);
            cmd.ExecuteNonQuery();
        }

        public static void DeleteSessionsForUser(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_sessions WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static void RenameSessions(string oldUsername, string newUsername)
        {
            oldUsername = (oldUsername ?? "").Trim();
            newUsername = (newUsername ?? "").Trim();
            if (oldUsername.Length == 0 || newUsername.Length == 0)
                return;
            if (DataLink.IsRemote)
                return;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_sessions SET username = $new WHERE username = $old;";
            cmd.Parameters.AddWithValue("$new", newUsername);
            cmd.Parameters.AddWithValue("$old", oldUsername);
            cmd.ExecuteNonQuery();
        }

        private static void DeleteExpiredSessions()
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_sessions WHERE expires_at < $now;";
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            cmd.ExecuteNonQuery();
        }

        public static void SetAccountRoles(string username, bool isAdmin, bool isIt)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
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
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET is_admin = $admin, is_it = $it WHERE username = $user;";
            cmd.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
            cmd.Parameters.AddWithValue("$it", isIt ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static List<(long Id, string Name, string Bank, string Last4, string Notes)> ListBankAccounts()
        {
            if (DataLink.Try(ServerOps.BankList, new { }, out List<BankRowDto>? banks) && banks != null)
                return banks.Select(a => (a.Id, a.Name, a.Bank, a.Last4, a.Notes)).ToList();
            EnsureCreated();
            var list = new List<(long, string, string, string, string)>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT id, name, bank, last4, notes FROM bank_accounts ORDER BY name COLLATE NOCASE;";
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

        public static long InsertBankAccount(string name, string bank, string last4, string notes)
        {
            if (DataLink.Try(ServerOps.BankInsert, new BankWriteRequest
            {
                Name = name, Bank = bank, Last4 = last4, Notes = notes
            }, out long id))
                return id;
            EnsureCreated();
            using var db = Open();
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

        public static void UpdateBankAccount(long id, string name, string bank, string last4, string notes)
        {
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

        public static void DeleteBankAccount(long id)
        {
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.BankDelete, new BankWriteRequest { Id = id });
                return;
            }
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM bank_accounts WHERE id = $id;";
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        private static void SeedAccessGroups(SqliteCommand cmd)
        {
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

        public static string GetTableAccess(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return "";
            if (DataLink.Try(ServerOps.AccountsAccessGet, new AccountWriteRequest { Username = username }, out string? access))
                return access ?? "";

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(table_access, '') FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            return cmd.ExecuteScalar()?.ToString() ?? "";
        }

        public static string GetGroupAccessMerged(string username)
        {
            var groups = GetAccessGroups(username);
            if (groups.Count == 0)
                return "";
            if (groups.Count == 1)
                return GetGroupAccess(groups[0]);
            return DataAccess.Merge(groups.Select(GetGroupAccess));
        }

        public static string GetEffectiveTableAccess(string username) =>
            DataAccess.Overlay(GetGroupAccessMerged(username), GetTableAccess(username));

        public static bool HasAccessOverride(string username) =>
            !string.IsNullOrWhiteSpace(GetTableAccess(username));

        public static string GetAccessGroup(string username) =>
            AccessGroups.Join(GetAccessGroups(username));

        public static List<string> GetAccessGroups(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return new List<string>();
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(access_group, '') FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            return AccessGroups.Parse(cmd.ExecuteScalar()?.ToString());
        }

        public static void SetAccessGroup(string username, string group) =>
            SetAccessGroups(username, AccessGroups.Parse(group));

        public static void SetAccessGroups(string username, IEnumerable<string> groups)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET access_group = $group WHERE username = $user;";
            cmd.Parameters.AddWithValue("$group", AccessGroups.Join(groups));
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static void AddAccessGroup(string username, string group)
        {
            group = (group ?? "").Trim();
            if (group.Length == 0)
                return;
            var groups = GetAccessGroups(username);
            if (groups.Any(name => name.Equals(group, StringComparison.OrdinalIgnoreCase)))
                return;
            groups.Add(group);
            SetAccessGroups(username, groups);
        }

        public static void RemoveAccessGroup(string username, string group)
        {
            group = (group ?? "").Trim();
            if (group.Length == 0)
                return;
            SetAccessGroups(username, GetAccessGroups(username)
                .Where(name => !name.Equals(group, StringComparison.OrdinalIgnoreCase)));
        }

        public static List<(string Name, string Access)> ListAccessGroups()
        {
            EnsureCreated();
            var list = new List<(string Name, string Access)>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT name, COALESCE(table_access, '') FROM access_groups ORDER BY name COLLATE NOCASE;";
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

        public static string GetGroupAccess(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
                return "";
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(table_access, '') FROM access_groups WHERE name = $name;";
            cmd.Parameters.AddWithValue("$name", name);
            return cmd.ExecuteScalar()?.ToString() ?? "";
        }

        public static bool SaveAccessGroup(string name, string json, out string error)
        {
            error = "";
            name = (name ?? "").Trim();
            if (name.Length == 0)
            {
                error = "Enter a group name.";
                return false;
            }

            if (name.Contains(',') || name.Contains(';'))
            {
                error = "Group names cannot contain commas.";
                return false;
            }

            if (AccessGroups.IsAdmin(name))
            {
                error = "The Admin group cannot be changed.";
                return false;
            }

            if (AccessGroups.IsIt(name) && !AppState.IsAdmin)
            {
                error = "Only an administrator can change the IT group.";
                return false;
            }

            try
            {
                EnsureCreated();
                using var db = Open();
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
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static bool DeleteAccessGroup(string name, out string error)
        {
            error = "";
            name = (name ?? "").Trim();
            if (name.Length == 0)
                return false;
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
                    using var reader = list.ExecuteReader();
                    var updates = new List<(string User, string Groups)>();
                    while (reader.Read())
                    {
                        string user = reader.IsDBNull(0) ? "" : reader.GetString(0);
                        string stored = reader.IsDBNull(1) ? "" : reader.GetString(1);
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
                using var cmd = db.CreateCommand();
                cmd.CommandText = "DELETE FROM access_groups WHERE name = $name;";
                cmd.Parameters.AddWithValue("$name", name);
                cmd.ExecuteNonQuery();
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        public static int CountGroupMembers(string name)
        {
            name = (name ?? "").Trim();
            if (name.Length == 0)
                return 0;
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(access_group, '') FROM app_accounts;";
            using var reader = cmd.ExecuteReader();
            int count = 0;
            while (reader.Read())
            {
                if (AccessGroups.Contains(reader.IsDBNull(0) ? "" : reader.GetString(0), name))
                    count++;
            }

            return count;
        }

        public static void SetTableAccess(string username, string json)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;
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
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET table_access = $json WHERE username = $user;";
            cmd.Parameters.AddWithValue("$json", json ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static (string AccessToken, string ItemId, string AccountId, string Cursor) GetBankLiveLink(long id)
        {
            if (DataLink.Try(ServerOps.BankLinkGet, new BankWriteRequest { Id = id }, out BankLinkDto? link) &&
                link != null)
                return (link.AccessToken, link.ItemId, link.AccountId, link.Cursor);
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT COALESCE(plaid_access_token, ''), COALESCE(plaid_item_id, ''),
                       COALESCE(plaid_account_id, ''), COALESCE(plaid_cursor, '')
                FROM bank_accounts WHERE id = $id;
                """;
            cmd.Parameters.AddWithValue("$id", id);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return ("", "", "", "");
            return (
                SecretProtect.Open(reader.IsDBNull(0) ? "" : reader.GetString(0)),
                reader.IsDBNull(1) ? "" : reader.GetString(1),
                reader.IsDBNull(2) ? "" : reader.GetString(2),
                reader.IsDBNull(3) ? "" : reader.GetString(3));
        }

        public static void SetBankLiveLink(
            long id,
            string accessToken,
            string itemId,
            string accountId,
            string cursor)
        {
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
            cmd.Parameters.AddWithValue("$token", SecretProtect.Seal(accessToken));
            cmd.Parameters.AddWithValue("$item", itemId ?? "");
            cmd.Parameters.AddWithValue("$account", accountId ?? "");
            cmd.Parameters.AddWithValue("$cursor", cursor ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        public static void SetBankLiveCursor(long id, string cursor)
        {
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.BankCursor, new BankWriteRequest { Id = id, Cursor = cursor });
                return;
            }
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE bank_accounts SET plaid_cursor = $cursor WHERE id = $id;";
            cmd.Parameters.AddWithValue("$cursor", cursor ?? "");
            cmd.Parameters.AddWithValue("$id", id);
            cmd.ExecuteNonQuery();
        }

        private static void EnsureTextColumn(string table, string column, bool archive = false)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            if (existing.Contains(column))
                return;

            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
            cmd.ExecuteNonQuery();
            if (column.Equals(DataFiles.RecordStatus, StringComparison.OrdinalIgnoreCase))
                BackfillLiveStatus(table, archive);
        }

        private static void BackfillLiveStatus(string table, bool archive = false)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(DataFiles.RecordStatus))
                return;

            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                $"UPDATE {Quote(table)} SET {Quote(DataFiles.RecordStatus)} = $live " +
                $"WHERE {Quote(DataFiles.RecordStatus)} IS NULL OR TRIM({Quote(DataFiles.RecordStatus)}) = '';";
            cmd.Parameters.AddWithValue("$live", DataFiles.RecordLive);
            cmd.ExecuteNonQuery();
        }

        private static void DropTextColumn(string table, string column, bool archive = false)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            if (!existing.Contains(column))
                return;

            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Quote(table)} DROP COLUMN {Quote(column)};";
            cmd.ExecuteNonQuery();
        }

        private static void EnsureAccountColumn(string column, string definition)
        {
            EnsureAccountColumn("app_accounts", column, definition);
        }

        private static void EnsureAccountColumn(string table, string column, string definition)
        {
            var existing = new HashSet<string>(TableColumns(table), StringComparer.OrdinalIgnoreCase);
            if (existing.Contains(column))
                return;

            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {definition};";
            cmd.ExecuteNonQuery();
        }

        public static void SavePdf(string kind, string key, string fileName, byte[] content)
        {
            kind = (kind ?? "").Trim();
            key = (key ?? "").Trim();
            fileName = (fileName ?? "").Trim();
            if (kind.Length == 0 || key.Length == 0 || fileName.Length == 0 || content.Length == 0)
                return;
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
            cmd.Parameters.AddWithValue("$key", key);
            cmd.Parameters.AddWithValue("$name", fileName);
            var blob = cmd.CreateParameter();
            blob.ParameterName = "$content";
            blob.SqliteType = SqliteType.Blob;
            blob.Value = content;
            cmd.Parameters.Add(blob);
            cmd.Parameters.AddWithValue("$at", DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"));
            cmd.ExecuteNonQuery();
        }

        public static void DeletePdf(string kind, string key)
        {
            kind = (kind ?? "").Trim();
            key = (key ?? "").Trim();
            if (kind.Length == 0 || key.Length == 0)
                return;
            if (DataLink.IsRemote)
            {
                DataLink.Send(ServerOps.PdfDelete, new PdfRequest { Kind = kind, Key = key });
                return;
            }

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM stored_pdfs WHERE kind = $kind AND doc_key = $key;";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            cmd.ExecuteNonQuery();
        }

        public static bool HasPdf(string kind, string key)
        {
            kind = (kind ?? "").Trim();
            key = (key ?? "").Trim();
            if (kind.Length == 0 || key.Length == 0)
                return false;
            if (DataLink.Try(ServerOps.PdfHas, new PdfRequest { Kind = kind, Key = key }, out bool has))
                return has;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT 1 FROM stored_pdfs WHERE kind = $kind AND doc_key = $key LIMIT 1;";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.Parameters.AddWithValue("$key", key);
            return cmd.ExecuteScalar() != null;
        }

        public static (string FileName, byte[] Content)? TryGetPdf(string kind, string key)
        {
            kind = (kind ?? "").Trim();
            key = (key ?? "").Trim();
            if (kind.Length == 0 || key.Length == 0)
                return null;
            if (DataLink.IsRemote)
            {
                var pdf = DataLink.Call<PdfDto?>(ServerOps.PdfGet, new PdfRequest { Kind = kind, Key = key });
                if (pdf == null || pdf.Content == null || pdf.Content.Length == 0)
                    return null;
                return (pdf.FileName, pdf.Content);
            }

            EnsureCreated();
            using var db = Open();
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
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return null;

            string name = reader.GetString(0);
            byte[] bytes = reader.IsDBNull(1)
                ? Array.Empty<byte>()
                : reader.GetFieldValue<byte[]>(1);
            if (bytes.Length == 0)
                return null;

            return (name, bytes);
        }

        public static void ImportPdfsFromFolders()
        {
            if (DataLink.IsRemote)
                return;
            if (GetPath() == null)
                return;

            EnsureCreated();
            ImportPdfFolder(DataFiles.GetStoredInvoicesFolder(), DataFiles.PdfKindInvoice, "Invoice ");
            ImportPdfFolder(DataFiles.GetStoredSalesOrdersFolder(), DataFiles.PdfKindSalesOrder, "Sales Order ");
        }

        public static List<(string Key, string FileName, byte[] Content)> ListPdfs(string kind)
        {
            kind = (kind ?? "").Trim();
            var list = new List<(string, string, byte[])>();
            if (kind.Length == 0 || GetPath() == null)
                return list;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT doc_key, file_name, content FROM stored_pdfs WHERE kind = $kind;";
            cmd.Parameters.AddWithValue("$kind", kind);
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                string key = reader.IsDBNull(0) ? "" : reader.GetString(0);
                string name = reader.IsDBNull(1) ? "" : reader.GetString(1);
                byte[] bytes = reader.IsDBNull(2)
                    ? Array.Empty<byte>()
                    : reader.GetFieldValue<byte[]>(2);
                list.Add((key, name, bytes));
            }

            return list;
        }

        public static void DeletePdfs(string kind)
        {
            kind = (kind ?? "").Trim();
            if (kind.Length == 0 || GetPath() == null)
                return;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM stored_pdfs WHERE kind = $kind;";
            cmd.Parameters.AddWithValue("$kind", kind);
            cmd.ExecuteNonQuery();
        }

        private static void ImportPdfFolder(string? folder, string kind, string prefix)
        {
            if (folder == null || !Directory.Exists(folder))
                return;

            foreach (var path in Directory.GetFiles(folder, "*.pdf"))
            {
                string name = Path.GetFileName(path);
                string stem = Path.GetFileNameWithoutExtension(name);
                string key = stem;
                if (stem.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                {
                    key = stem[prefix.Length..].Trim();
                    int dash = key.IndexOf(" - ", StringComparison.Ordinal);
                    if (dash >= 0)
                        key = key[..dash].Trim();
                }

                if (key.Length == 0 || HasPdf(kind, key))
                    continue;

                try
                {
                    SavePdf(kind, key, name, File.ReadAllBytes(path));
                }
                catch
                {
                    // skip a locked or unreadable file
                }
            }
        }

        private static Dictionary<string, string> ReadRow(string table, SqliteDataReader reader)
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

        private static void AppendRows(
            string table,
            bool archive,
            List<Dictionary<string, string>> result)
        {
            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(ReadRow(table, reader));
        }

        private static void AppendRowsWithIds(
            string table,
            bool archive,
            List<(long Id, Dictionary<string, string> Fields)> result)
        {
            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(reader.GetOrdinal("id"));
                if (archive)
                    id = EncodeArchiveRowId(id);
                result.Add((id, ReadRow(table, reader)));
            }
        }

        private static void EnsureColumnsOn(string table, IEnumerable<string> columns, bool archive)
        {
            var existing = new HashSet<string>(TableColumns(table, archive), StringComparer.OrdinalIgnoreCase);
            using var db = Open(archive);
            foreach (var column in columns)
            {
                if (existing.Contains(column))
                    continue;
                using var cmd = db.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
                cmd.ExecuteNonQuery();
                existing.Add(column);
            }
        }

        private static int CountIn(string table, bool archive)
        {
            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)};";
            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        // Combined view encodes archive SQLite ids as negative so UpdateById can route the write.
        private static bool IsArchiveRowId(long id) => id < 0;

        private static long EncodeArchiveRowId(long id) => id > 0 ? -id : id;

        private static long DecodeRowId(long id) => id < 0 ? -id : id;

        private static List<string> TableColumns(string table, bool? archive = null)
        {
            if (archive == null && UsingArchive(table))
            {
                var combined = new List<string>();
                var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var column in TableColumnsFrom(table, false).Concat(TableColumnsFrom(table, true)))
                {
                    if (!used.Add(column))
                        continue;
                    combined.Add(column);
                }

                return combined;
            }

            return TableColumnsFrom(table, archive ?? false);
        }

        private static List<string> TableColumnsFrom(string table, bool archive)
        {
            var list = new List<string>();
            using var db = Open(archive);
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({Quote(table)});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(reader.GetString(1));
            return list;
        }

        /// <summary>Open live or archive. Shared/cloud folders use DELETE journal mode instead of WAL.</summary>
        private static SqliteConnection Open(bool archive = false)
        {
            string? path = archive ? GetArchivePath() : GetPath();
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
            using (var pragma = db.CreateCommand())
            {
                pragma.CommandText = shared
                    ? "PRAGMA busy_timeout=8000;"
                    : "PRAGMA busy_timeout=5000;";
                pragma.ExecuteNonQuery();
            }
            return db;
        }

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

        private static void UpgradeSetting(string key)
        {
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
            read.Parameters.AddWithValue("$key", key);
            string? value = read.ExecuteScalar()?.ToString();
            if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
                return;
            using var write = db.CreateCommand();
            write.CommandText = "UPDATE app_settings SET value = $value WHERE key = $key;";
            write.Parameters.AddWithValue("$value", SecretProtect.Seal(value));
            write.Parameters.AddWithValue("$key", key);
            write.ExecuteNonQuery();
        }

        private static void UpgradeSmtpPassword()
        {
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText = "SELECT password FROM admin_smtp WHERE id = 1;";
            string? value = read.ExecuteScalar()?.ToString();
            if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
                return;
            using var write = db.CreateCommand();
            write.CommandText = "UPDATE admin_smtp SET password = $value WHERE id = 1;";
            write.Parameters.AddWithValue("$value", SecretProtect.Seal(value));
            write.ExecuteNonQuery();
        }

        private static void UpgradeColumn(string table, string column)
        {
            var columns = TableColumns(table);
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
                    string value = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
                    if (value.Length == 0 || SecretProtect.IsSealed(value))
                        continue;
                    updates.Add((reader.GetInt64(0), SecretProtect.Seal(value)));
                }
            }

            foreach (var row in updates)
            {
                using var write = db.CreateCommand();
                write.CommandText = $"UPDATE {Quote(table)} SET {Quote(column)} = $value WHERE id = $id;";
                write.Parameters.AddWithValue("$value", row.Value);
                write.Parameters.AddWithValue("$id", row.Id);
                write.ExecuteNonQuery();
            }
        }

        private static string CellValue(string table, Dictionary<string, string> values, string name)
        {
            string value = Lookup(values, name);
            if (name.Equals(DataFiles.RecordStatus, StringComparison.OrdinalIgnoreCase) &&
                string.IsNullOrWhiteSpace(value))
                return DataFiles.RecordLive;
            return SecretProtect.StoreField(table, name, value);
        }

        private static string Lookup(Dictionary<string, string> values, string name)
        {
            foreach (var pair in values)
            {
                if (pair.Key.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return pair.Value ?? "";
            }

            if (name.Equals("Customer PO", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in values)
                {
                    if (pair.Key.Equals("PO #", StringComparison.OrdinalIgnoreCase))
                        return pair.Value ?? "";
                }
            }

            if (name.Equals("PO #", StringComparison.OrdinalIgnoreCase))
            {
                foreach (var pair in values)
                {
                    if (pair.Key.Equals("Lot #", StringComparison.OrdinalIgnoreCase))
                        return pair.Value ?? "";
                }
            }

            return "";
        }

        private static void EnsureTerm()
        {
            if (AppState.TermStartDate != null)
                return;
            AppState.TermStartDate = DateTime.Today;
            AppLock.SaveSettings();
        }

        private static string TermKey() =>
            (AppState.TermStartDate ?? DateTime.Today).ToString("yyyy-MM-dd");

        private static bool IsProcessTable(string table) =>
            ProcessTables.Any(t => t.Equals(table, StringComparison.OrdinalIgnoreCase));

        /// <summary>Empty until the process is complete; then the current term date.</summary>
        private static string CompletionStamp(
            string table,
            Dictionary<string, string> values,
            DateTime? term)
        {
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
            if (table.Equals(DataFiles.PurchaseSales, StringComparison.OrdinalIgnoreCase))
            {
                return IsClosedStatus(Lookup(values, "Status")) ||
                       HasText(values, "Ship Date") ||
                       HasText(values, "Arrival Date");
            }

            if (table.Equals(DataFiles.Sales, StringComparison.OrdinalIgnoreCase))
            {
                return HasText(values, "Invoice #") ||
                       IsClosedStatus(Lookup(values, "Status")) ||
                       HasPositiveNumber(values, "Paid");
            }

            if (table.Equals(DataFiles.Invoices, StringComparison.OrdinalIgnoreCase))
            {
                return IsClosedStatus(Lookup(values, "Status")) ||
                       HasText(values, "Payment Date") ||
                       PaidCoversAmount(values);
            }

            if (table.Equals(DataFiles.BankTransactions, StringComparison.OrdinalIgnoreCase))
                return HasText(values, "Date") || HasText(values, "Amount");

            if (table.Equals(DataFiles.Debits, StringComparison.OrdinalIgnoreCase))
                return IsApproved(Lookup(values, "Vendor Approved"));

            if (table.Equals(DataFiles.Credits, StringComparison.OrdinalIgnoreCase))
                return IsApproved(Lookup(values, "Approved"));

            return !IsProcessTable(table);
        }

        private static bool HasText(Dictionary<string, string> values, string column) =>
            Lookup(values, column).Trim().Length > 0;

        private static bool HasPositiveNumber(Dictionary<string, string> values, string column)
        {
            string raw = Lookup(values, column).Trim().Replace("$", "").Replace(",", "");
            return decimal.TryParse(raw, out var amount) && amount > 0;
        }

        private static bool PaidCoversAmount(Dictionary<string, string> values)
        {
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

        private static bool IsApproved(string value)
        {
            string trimmed = (value ?? "").Trim();
            if (trimmed.Length == 0)
                return false;

            return trimmed.Equals("yes", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("y", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("true", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("1", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("approved", StringComparison.OrdinalIgnoreCase) ||
                   trimmed.Equals("x", StringComparison.OrdinalIgnoreCase);
        }

        private static List<(long Id, string TermStart, Dictionary<string, string> Fields)> ReadLiveRows(string table)
        {
            var result = new List<(long, string, Dictionary<string, string>)>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"SELECT * FROM {Quote(table)} ORDER BY id;";
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

        private static void EnsureArchiveColumns(string table, IEnumerable<string> columns)
        {
            var existing = new HashSet<string>(TableColumns(table, archive: true), StringComparer.OrdinalIgnoreCase);
            using var db = Open(archive: true);
            foreach (var column in columns)
            {
                if (existing.Contains(column))
                    continue;

                using var cmd = db.CreateCommand();
                cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
                cmd.ExecuteNonQuery();
                existing.Add(column);
            }
        }

        private static void InsertRow(
            SqliteConnection db,
            SqliteTransaction tx,
            string table,
            IEnumerable<string> columns,
            Dictionary<string, string> values,
            string termStart)
        {
            using var cmd = db.CreateCommand();
            cmd.Transaction = tx;
            var cols = new List<string> { Quote("term_start") };
            var pars = new List<string> { "$term" };
            cmd.Parameters.AddWithValue("$term", termStart ?? "");
            int i = 0;
            foreach (var name in columns)
            {
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

        private static void DeleteByIds(string table, List<long> ids)
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
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private static void ClearTermStart(string table, List<long> ids)
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
                cmd.Parameters.AddWithValue("$id", id);
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    }
}
