using Microsoft.Data.Sqlite;

namespace CrcInventory.Server;

internal sealed partial class InventoryStore
{
    private readonly string _folder;
    private readonly object _gate = new();

    public InventoryStore(string dataFolder)
    {
        _folder = Path.GetFullPath(dataFolder);
        Directory.CreateDirectory(_folder);
        Roles = new RolesFile(_folder);
        Roles.Load();
        EnsureCreated();
    }

    public RolesFile Roles { get; }

    public string Folder => _folder;

    public string LivePath => Path.Combine(_folder, Schema.LiveFileName);

    public string ArchivePath => Path.Combine(_folder, Schema.ArchiveFileName);

    public void EnsureCreated()
    {
        lock (_gate)
        {
            EnsureCreated(archive: false);
            EnsureCreated(archive: true);
        }
    }

    private void EnsureCreated(bool archive)
    {
        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA journal_mode=WAL;";
        cmd.ExecuteNonQuery();

        IEnumerable<string> tables = archive ? Schema.ProcessTables : Schema.All;
        foreach (var table in tables)
        {
            var columns = Schema.Headers(table);
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
        EnsureColumn("app_accounts", "is_it", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("app_accounts", "is_admin", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("app_accounts", "must_change_password", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("app_accounts", "security_q1", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "security_a1", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "security_q2", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "security_a2", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "security_q3", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "security_a3", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "stay_signed_in", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("app_accounts", "table_access", "TEXT NOT NULL DEFAULT ''");

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
        EnsureColumn("bank_accounts", "plaid_access_token", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("bank_accounts", "plaid_item_id", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("bank_accounts", "plaid_account_id", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("bank_accounts", "plaid_cursor", "TEXT NOT NULL DEFAULT ''");
    }

    public bool HasItUser()
    {
        lock (_gate)
        {
            if (Roles.HasItUser())
                return true;
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM app_accounts WHERE COALESCE(is_it, 0) <> 0;";
            return Convert.ToInt32(cmd.ExecuteScalar()) > 0;
        }
    }

    public Dictionary<string, string> ReadSettings()
    {
        lock (_gate)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_settings;";
            using var reader = cmd.ExecuteReader();
            while (reader.Read())
                map[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
            return map;
        }
    }

    public void WriteSettings(Dictionary<string, string> values)
    {
        lock (_gate)
        {
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
    }

    public string? ReadUserEmail(string windowsUser)
    {
        windowsUser = (windowsUser ?? "").Trim();
        if (windowsUser.Length == 0)
            return null;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT email FROM app_users WHERE windows_user = $user;";
            cmd.Parameters.AddWithValue("$user", windowsUser);
            return cmd.ExecuteScalar()?.ToString();
        }
    }

    public void WriteUserEmail(string windowsUser, string? email)
    {
        windowsUser = (windowsUser ?? "").Trim();
        if (windowsUser.Length == 0)
            return;
        lock (_gate)
        {
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
            cmd.Parameters.AddWithValue("$at", NowStamp());
            cmd.ExecuteNonQuery();
        }
    }

    public DateTime TermStart()
    {
        var settings = ReadSettings();
        if (settings.TryGetValue("term_start", out var text) && DateTime.TryParse(text, out var date))
            return date;
        return DateTime.Today;
    }

    public int StaySignedInDays()
    {
        var settings = ReadSettings();
        if (settings.TryGetValue("stay_signed_in_days", out var text) &&
            int.TryParse(text, out int days) && days > 0)
            return days;
        return 30;
    }

    public int IdleCloseHours()
    {
        var settings = ReadSettings();
        if (settings.TryGetValue("idle_close_hours", out var text) &&
            int.TryParse(text, out int hours) && hours > 0)
            return hours;
        return 5;
    }

    public bool StaySignedInEnabled()
    {
        var settings = ReadSettings();
        if (settings.TryGetValue("stay_signed_in_enabled", out var text))
            return text != "0";
        return true;
    }

    private SqliteConnection Open(bool archive = false)
    {
        string path = archive ? ArchivePath : LivePath;
        var db = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path,
            Mode = SqliteOpenMode.ReadWriteCreate,
            DefaultTimeout = 8
        }.ToString());
        db.Open();
        using var pragma = db.CreateCommand();
        pragma.CommandText = "PRAGMA busy_timeout=8000;";
        pragma.ExecuteNonQuery();
        return db;
    }

    private void EnsureTextColumn(string table, string column, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        if (existing.Contains(column))
            return;

        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
        cmd.ExecuteNonQuery();
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive: false), StringComparer.OrdinalIgnoreCase);
        if (existing.Contains(column))
            return;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {definition};";
        cmd.ExecuteNonQuery();
    }

    private List<string> TableColumns(string table, bool viewOld)
    {
        if (viewOld && Schema.IsProcessTable(table))
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

        return TableColumnsFrom(table, archive: false);
    }

    private List<string> TableColumnsFrom(string table, bool archive)
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

    private static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    private static string NowStamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static string TermKey(DateTime? term) => (term ?? DateTime.Today).ToString("yyyy-MM-dd");
}
