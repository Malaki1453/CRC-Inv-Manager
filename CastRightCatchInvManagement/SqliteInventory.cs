using Microsoft.Data.Sqlite;

namespace CastRightCatchInvManagement
{
    internal static class SqliteInventory
    {
        public const string FileName = "crc_inventory.db";

        private static readonly HashSet<string> MasterTables = new(StringComparer.OrdinalIgnoreCase)
        {
            DataFiles.Customers,
            DataFiles.Vendors,
            DataFiles.ItemCodes
        };

        public static string? GetPath()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, FileName);
        }

        public static bool Exists()
        {
            string? path = GetPath();
            return path != null && File.Exists(path);
        }

        public static void EnsureCreated()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            Directory.CreateDirectory(AppState.InventoryFolder);
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = IsSharedLocation(AppState.InventoryFolder)
                ? "PRAGMA journal_mode=DELETE;"
                : "PRAGMA journal_mode=WAL;";
            cmd.ExecuteNonQuery();

            foreach (var table in DataFiles.All)
            {
                var columns = DataFiles.GetExpectedHeader(table).Split(',')
                    .Select(h => h.Trim())
                    .Where(h => h.Length > 0)
                    .ToList();
                var defs = new List<string>
                {
                    "id INTEGER PRIMARY KEY AUTOINCREMENT",
                    "term_start TEXT NOT NULL"
                };
                defs.AddRange(columns.Select(c => $"{Quote(c)} TEXT"));
                cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} ({string.Join(", ", defs)});";
                cmd.ExecuteNonQuery();
                foreach (var column in columns)
                    EnsureTextColumn(table, column);
            }

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
                CREATE TABLE IF NOT EXISTS app_users (
                    windows_user TEXT PRIMARY KEY NOT NULL,
                    email TEXT NOT NULL DEFAULT '',
                    updated_at TEXT NOT NULL
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
        }

        public static void ImportCsvsIfEmpty()
        {
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

        public static string[] Headers(string table)
        {
            EnsureCreated();
            var expected = DataFiles.GetExpectedHeader(table).Split(',')
                .Select(h => h.Trim())
                .Where(h => h.Length > 0)
                .ToList();
            var actual = TableColumns(table)
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

        public static List<Dictionary<string, string>> Read(string table)
        {
            EnsureCreated();
            var result = new List<Dictionary<string, string>>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            bool master = MasterTables.Contains(table);
            cmd.CommandText = master
                ? $"SELECT * FROM {Quote(table)} ORDER BY id;"
                : $"SELECT * FROM {Quote(table)} WHERE term_start = $term ORDER BY id;";
            if (!master)
                cmd.Parameters.AddWithValue("$term", TermKey());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                result.Add(ReadRow(reader));

            return result;
        }

        public static List<(long Id, Dictionary<string, string> Fields)> ReadWithIds(string table)
        {
            EnsureCreated();
            var result = new List<(long, Dictionary<string, string>)>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            bool master = MasterTables.Contains(table);
            cmd.CommandText = master
                ? $"SELECT * FROM {Quote(table)} ORDER BY id;"
                : $"SELECT * FROM {Quote(table)} WHERE term_start = $term ORDER BY id;";
            if (!master)
                cmd.Parameters.AddWithValue("$term", TermKey());

            using var reader = cmd.ExecuteReader();
            while (reader.Read())
            {
                long id = reader.GetInt64(reader.GetOrdinal("id"));
                result.Add((id, ReadRow(reader)));
            }

            return result;
        }

        public static void Insert(string table, Dictionary<string, string> values, DateTime? term = null)
        {
            EnsureCreated();
            EnsureTerm();
            var headers = Headers(table);
            using var db = Open();
            using var cmd = db.CreateCommand();
            var cols = new List<string> { Quote("term_start") };
            var pars = new List<string> { "$term" };
            cmd.Parameters.AddWithValue("$term", (term ?? AppState.TermStartDate ?? DateTime.Today).ToString("yyyy-MM-dd"));
            for (int i = 0; i < headers.Length; i++)
            {
                string name = headers[i];
                cols.Add(Quote(name));
                string p = "$c" + i;
                pars.Add(p);
                cmd.Parameters.AddWithValue(p, Lookup(values, name));
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
            EnsureCreated();
            EnsureTerm();
            var headers = Headers(table);
            string termKey = (term ?? AppState.TermStartDate ?? DateTime.Today).ToString("yyyy-MM-dd");
            int count = 0;

            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var values in rows)
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                var cols = new List<string> { Quote("term_start") };
                var pars = new List<string> { "$term" };
                cmd.Parameters.AddWithValue("$term", termKey);
                for (int i = 0; i < headers.Length; i++)
                {
                    string name = headers[i];
                    cols.Add(Quote(name));
                    string p = "$c" + i;
                    pars.Add(p);
                    cmd.Parameters.AddWithValue(p, Lookup(values, name));
                }

                cmd.CommandText =
                    $"INSERT INTO {Quote(table)} ({string.Join(",", cols)}) VALUES ({string.Join(",", pars)});";
                cmd.ExecuteNonQuery();
                count++;
            }

            tx.Commit();
            return count;
        }

        public static bool UpdateById(string table, long id, Dictionary<string, string> values)
        {
            EnsureCreated();
            var headers = Headers(table);
            using var db = Open();
            using var cmd = db.CreateCommand();
            var sets = new List<string>();
            for (int i = 0; i < headers.Length; i++)
            {
                string name = headers[i];
                string p = "$c" + i;
                sets.Add($"{Quote(name)} = {p}");
                cmd.Parameters.AddWithValue(p, Lookup(values, name));
            }

            cmd.Parameters.AddWithValue("$id", id);
            cmd.CommandText = $"UPDATE {Quote(table)} SET {string.Join(",", sets)} WHERE id = $id;";
            return cmd.ExecuteNonQuery() > 0;
        }

        public static void EnsureColumns(string table, params string[] columns)
        {
            if (columns.Length == 0)
                return;

            EnsureCreated();
            var existing = new HashSet<string>(TableColumns(table), StringComparer.OrdinalIgnoreCase);
            using var db = Open();
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

        public static int Count(string table, bool currentTermOnly = true)
        {
            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            if (currentTermOnly && !MasterTables.Contains(table))
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)} WHERE term_start = $term;";
                cmd.Parameters.AddWithValue("$term", TermKey());
            }
            else
            {
                cmd.CommandText = $"SELECT COUNT(*) FROM {Quote(table)};";
            }

            return Convert.ToInt32(cmd.ExecuteScalar());
        }

        public static DateTime? LatestTerm()
        {
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
            EnsureCreated();
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

        public static void WriteSettings(Dictionary<string, string> values)
        {
            EnsureCreated();
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var pair in values)
            {
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO app_settings (key, value)
                    VALUES ($key, $value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                    """;
                cmd.Parameters.AddWithValue("$key", pair.Key);
                cmd.Parameters.AddWithValue("$value", pair.Value ?? "");
                cmd.ExecuteNonQuery();
            }

            tx.Commit();
        }

        public static string? ReadUserEmail(string windowsUser)
        {
            windowsUser = (windowsUser ?? "").Trim();
            if (windowsUser.Length == 0)
                return null;

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

        public static List<(string Username, string DisplayName, string Email, bool IsAdmin, bool IsIt)> ListAccounts()
        {
            EnsureCreated();
            var list = new List<(string, string, string, bool, bool)>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT username, display_name, email,
                       COALESCE(is_admin, 0), COALESCE(is_it, 0)
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
                    !reader.IsDBNull(4) && reader.GetInt32(4) != 0));
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
            return cmd.ExecuteNonQuery() > 0;
        }

        public static void SetMustChangePassword(string username, bool mustChange)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;

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

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET username = $new WHERE username = $old;";
            cmd.Parameters.AddWithValue("$new", newUsername);
            cmd.Parameters.AddWithValue("$old", oldUsername);
            try
            {
                return cmd.ExecuteNonQuery() > 0;
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

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET email = $email WHERE username = $user;";
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static bool TryGetSecurityQuestions(
            string username,
            out string q1,
            out string q2,
            out string q3)
        {
            q1 = q2 = q3 = "";
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT security_q1, security_q2, security_q3 FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return false;

            q1 = reader.IsDBNull(0) ? "" : reader.GetString(0);
            q2 = reader.IsDBNull(1) ? "" : reader.GetString(1);
            q3 = reader.IsDBNull(2) ? "" : reader.GetString(2);
            return q1.Length > 0 && q2.Length > 0 && q3.Length > 0;
        }

        public static bool TryGetSecurityAnswerHashes(
            string username,
            out string a1,
            out string a2,
            out string a3)
        {
            a1 = a2 = a3 = "";
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT security_a1, security_a2, security_a3 FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return false;

            a1 = reader.IsDBNull(0) ? "" : reader.GetString(0);
            a2 = reader.IsDBNull(1) ? "" : reader.GetString(1);
            a3 = reader.IsDBNull(2) ? "" : reader.GetString(2);
            return true;
        }

        public static void SetSecurityQuestions(
            string username,
            string q1, string a1,
            string q2, string a2,
            string q3, string a3)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE app_accounts SET
                    security_q1 = $q1, security_a1 = $a1,
                    security_q2 = $q2, security_a2 = $a2,
                    security_q3 = $q3, security_a3 = $a3
                WHERE username = $user;
                """;
            cmd.Parameters.AddWithValue("$q1", q1 ?? "");
            cmd.Parameters.AddWithValue("$a1", a1 ?? "");
            cmd.Parameters.AddWithValue("$q2", q2 ?? "");
            cmd.Parameters.AddWithValue("$a2", a2 ?? "");
            cmd.Parameters.AddWithValue("$q3", q3 ?? "");
            cmd.Parameters.AddWithValue("$a3", a3 ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        public static bool DeleteAccount(string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;

            EnsureCreated();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            return cmd.ExecuteNonQuery() > 0;
        }

        public static void SetAccountRoles(string username, bool isAdmin, bool isIt)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;

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

        private static void EnsureTextColumn(string table, string column)
        {
            EnsureAccountColumn(table, column, "TEXT");
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

        public static bool HasPdf(string kind, string key)
        {
            kind = (kind ?? "").Trim();
            key = (key ?? "").Trim();
            if (kind.Length == 0 || key.Length == 0)
                return false;

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
            if (GetPath() == null)
                return;

            EnsureCreated();
            ImportPdfFolder(DataFiles.GetStoredInvoicesFolder(), DataFiles.PdfKindInvoice, "Invoice ");
            ImportPdfFolder(DataFiles.GetStoredSalesOrdersFolder(), DataFiles.PdfKindSalesOrder, "Sales Order ");
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

        private static Dictionary<string, string> ReadRow(SqliteDataReader reader)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            for (int i = 0; i < reader.FieldCount; i++)
            {
                string name = reader.GetName(i);
                if (name.Equals("id", StringComparison.OrdinalIgnoreCase) ||
                    name.Equals("term_start", StringComparison.OrdinalIgnoreCase))
                    continue;
                map[name] = reader.IsDBNull(i) ? "" : reader.GetValue(i)?.ToString() ?? "";
            }

            return map;
        }

        private static List<string> TableColumns(string table)
        {
            var list = new List<string>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = $"PRAGMA table_info({Quote(table)});";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                list.Add(reader.GetString(1));
            return list;
        }

        private static SqliteConnection Open()
        {
            string? path = GetPath();
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

        private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";
    }
}
