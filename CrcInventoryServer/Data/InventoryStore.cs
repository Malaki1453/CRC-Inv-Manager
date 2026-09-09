using System.Data.Common;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

internal sealed partial class InventoryStore
{
    private readonly string _folder;
    private readonly StoreEngine _engine;
    private readonly object _gate = new();

    public InventoryStore(string dataFolder, string? postgres = null)
    {
        _folder = Path.GetFullPath(dataFolder);
        Directory.CreateDirectory(_folder);
        _engine = string.IsNullOrWhiteSpace(postgres)
            ? StoreEngine.Sqlite(_folder)
            : StoreEngine.Postgres(postgres);
        Roles = new RolesFile(_folder);
        Roles.Load();
        SecretProtect.UseFolder(_folder);
        EnsureCreated();
    }

    public RolesFile Roles { get; }

    public string Folder => _folder;

    public string EngineName => _engine.Name;

    public bool UsesPostgres => _engine.IsPostgres;

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
        if (!_engine.IsPostgres)
        {
            cmd.CommandText = "PRAGMA journal_mode=WAL;";
            cmd.Exec(_engine);
        }

        IEnumerable<string> tables = archive ? Schema.ProcessTables : Schema.All;
        foreach (var table in tables)
        {
            var columns = Schema.Headers(table);
            var defs = new List<string>
            {
                _engine.IdColumn,
                "term_start TEXT NOT NULL DEFAULT ''"
            };
            defs.AddRange(columns.Select(c => $"{Quote(c)} TEXT"));
            cmd.CommandText = $"CREATE TABLE IF NOT EXISTS {Quote(table)} ({string.Join(", ", defs)});";
            cmd.Exec(_engine);
            foreach (var column in columns)
                EnsureTextColumn(table, column, archive);
            BackfillLiveStatus(table, archive);
            if (table.Equals(Schema.PurchaseSales, StringComparison.OrdinalIgnoreCase))
                DropTextColumn(table, "Vendor Invoice #", archive);
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
        cmd.Exec(_engine);

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
        cmd.Exec(_engine);

        cmd.CommandText =
            """
            CREATE TABLE IF NOT EXISTS app_users (
                windows_user TEXT PRIMARY KEY NOT NULL,
                email TEXT NOT NULL DEFAULT '',
                updated_at TEXT NOT NULL
            );
            """;
        cmd.Exec(_engine);

        cmd.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS app_prefs (
                username {_engine.NoCaseText} NOT NULL,
                key TEXT NOT NULL,
                value TEXT NOT NULL,
                PRIMARY KEY (username, key)
            );
            """;
        cmd.Exec(_engine);

        cmd.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS stored_pdfs (
                kind TEXT NOT NULL,
                doc_key {_engine.NoCaseText},
                file_name TEXT NOT NULL,
                content {_engine.BlobType} NOT NULL,
                stored_at TEXT NOT NULL,
                PRIMARY KEY (kind, doc_key)
            );
            """;
        cmd.Exec(_engine);

        cmd.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS app_accounts (
                username {_engine.NoCaseText} PRIMARY KEY,
                display_name TEXT NOT NULL DEFAULT '',
                password_hash TEXT NOT NULL,
                password_salt TEXT NOT NULL,
                email TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL
            );
            """;
        cmd.Exec(_engine);
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
        EnsureColumn("app_accounts", "access_group", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "recover_fails", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("app_accounts", "recover_lock_until", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("app_accounts", "login_fails", "INTEGER NOT NULL DEFAULT 0");
        EnsureColumn("app_accounts", "login_lock_until", "TEXT NOT NULL DEFAULT ''");

        cmd.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS access_groups (
                name {_engine.NoCaseText} PRIMARY KEY,
                table_access TEXT NOT NULL DEFAULT ''
            );
            """;
        cmd.Exec(_engine);
        SeedAccessGroups(cmd);

        cmd.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS app_sessions (
                token_hash TEXT PRIMARY KEY NOT NULL,
                username {_engine.NoCaseText},
                expires_at TEXT NOT NULL,
                created_at TEXT NOT NULL
            );
            """;
        cmd.Exec(_engine);

        cmd.CommandText =
            $"""
            CREATE TABLE IF NOT EXISTS bank_accounts (
                {_engine.IdColumn},
                name TEXT NOT NULL,
                bank TEXT NOT NULL DEFAULT '',
                last4 TEXT NOT NULL DEFAULT '',
                notes TEXT NOT NULL DEFAULT '',
                created_at TEXT NOT NULL
            );
            """;
        cmd.Exec(_engine);
        EnsureColumn("bank_accounts", "plaid_access_token", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("bank_accounts", "plaid_item_id", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("bank_accounts", "plaid_account_id", "TEXT NOT NULL DEFAULT ''");
        EnsureColumn("bank_accounts", "plaid_cursor", "TEXT NOT NULL DEFAULT ''");
        UpgradeSecrets();
    }

    private void SeedAccessGroups(DbCommand cmd)
    {
        cmd.Parameters.Clear();
        cmd.CommandText =
            """
            INSERT INTO access_groups (name, table_access)
            VALUES ($name, $json)
            ON CONFLICT(name) DO UPDATE SET table_access = excluded.table_access;
            """;
        cmd.AddParam("$name", Schema.AdminGroup);
        cmd.AddParam("$json", Schema.AdminGroupAccessJson);
        cmd.Exec(_engine);

        cmd.Parameters.Clear();
        cmd.CommandText =
            """
            INSERT INTO access_groups (name, table_access)
            VALUES ($name, '')
            ON CONFLICT(name) DO NOTHING;
            """;
        cmd.AddParam("$name", Schema.ItGroup);
        cmd.Exec(_engine);
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
            return Convert.ToInt32(cmd.Scalar(_engine)) > 0;
        }
    }

    public Dictionary<string, string> ReadSettings(bool revealSecrets = false)
    {
        lock (_gate)
        {
            var map = ReadSettingsUnlocked();
            return revealSecrets
                ? SecretProtect.RevealSettings(map)
                : SecretProtect.WithoutSecrets(map);
        }
    }

    public Dictionary<string, string> ReadPublicSettings() => ReadSettings(revealSecrets: false);

    public void WriteSettings(Dictionary<string, string> values)
    {
        lock (_gate)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var pair in values)
            {
                if (SecretProtect.IsSecretSetting(pair.Key) && string.IsNullOrEmpty(pair.Value))
                    continue;
                using var cmd = db.CreateCommand();
                cmd.Transaction = tx;
                cmd.CommandText =
                    """
                    INSERT INTO app_settings (key, value)
                    VALUES ($key, $value)
                    ON CONFLICT(key) DO UPDATE SET value = excluded.value;
                    """;
                cmd.AddParam("$key", pair.Key);
                cmd.AddParam("$value", SecretProtect.StoreSetting(pair.Key, pair.Value));
                cmd.Exec(_engine);
            }

            tx.Commit();
        }
    }

    public Dictionary<string, string> ReadPrefs(string username)
    {
        username = (username ?? "").Trim();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (username.Length == 0)
            return map;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT key, value FROM app_prefs WHERE username = $user;";
            cmd.AddParam("$user", username);
            using var reader = cmd.Query(_engine);
            while (reader.Read())
                map[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
            return map;
        }
    }

    public void WritePrefs(string username, Dictionary<string, string> values)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0 || values.Count == 0)
            return;
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
                    INSERT INTO app_prefs (username, key, value)
                    VALUES ($user, $key, $value)
                    ON CONFLICT(username, key) DO UPDATE SET value = excluded.value;
                    """;
                cmd.AddParam("$user", username);
                cmd.AddParam("$key", pair.Key);
                cmd.AddParam("$value", pair.Value ?? "");
                cmd.Exec(_engine);
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
            cmd.AddParam("$user", windowsUser);
            return cmd.Scalar(_engine)?.ToString();
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
            cmd.AddParam("$user", windowsUser);
            cmd.AddParam("$email", email ?? "");
            cmd.AddParam("$at", NowStamp());
            cmd.Exec(_engine);
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

    private Dictionary<string, string> ReadSettingsUnlocked()
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "SELECT key, value FROM app_settings;";
        using var reader = cmd.Query(_engine);
        while (reader.Read())
            map[reader.GetString(0)] = reader.IsDBNull(1) ? "" : reader.GetString(1);
        return map;
    }

    private void UpgradeSecrets()
    {
        UpgradeSetting("smtp_password");
        UpgradeSetting("plaid_secret");
        UpgradeSmtpPassword();
        UpgradeColumn("bank_accounts", "plaid_access_token", archive: false);
        UpgradeColumn(Schema.Customers, "Routing Number", archive: false);
        UpgradeColumn(Schema.Customers, "Account Number", archive: false);
        UpgradeColumn(Schema.Vendors, "Routing Number", archive: false);
        UpgradeColumn(Schema.Vendors, "Account Number", archive: false);
    }

    private void UpgradeSetting(string key)
    {
        using var db = Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        read.AddParam("$key", key);
        string? value = read.Scalar(_engine)?.ToString();
        if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
            return;
        using var write = db.CreateCommand();
        write.CommandText = "UPDATE app_settings SET value = $value WHERE key = $key;";
        write.AddParam("$value", SecretProtect.Seal(value));
        write.AddParam("$key", key);
        write.Exec(_engine);
    }

    private void UpgradeSmtpPassword()
    {
        using var db = Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT password FROM admin_smtp WHERE id = 1;";
        string? value = read.Scalar(_engine)?.ToString();
        if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
            return;
        using var write = db.CreateCommand();
        write.CommandText = "UPDATE admin_smtp SET password = $value WHERE id = 1;";
        write.AddParam("$value", SecretProtect.Seal(value));
        write.Exec(_engine);
    }

    private void UpgradeColumn(string table, string column, bool archive)
    {
        var columns = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        if (!columns.Contains(column))
            return;
        using var db = Open(archive);
        using var read = db.CreateCommand();
        read.CommandText = $"SELECT id, {Quote(column)} FROM {Quote(table)};";
        var updates = new List<(object Id, string Value)>();
        using (var reader = read.Query(_engine))
        {
            while (reader.Read())
            {
                string value = reader.IsDBNull(1) ? "" : reader.GetValue(1)?.ToString() ?? "";
                if (value.Length == 0 || SecretProtect.IsSealed(value))
                    continue;
                updates.Add((reader.GetValue(0)!, SecretProtect.Seal(value)));
            }
        }

        foreach (var row in updates)
        {
            using var write = db.CreateCommand();
            write.CommandText = $"UPDATE {Quote(table)} SET {Quote(column)} = $value WHERE id = $id;";
            write.AddParam("$value", row.Value);
            write.AddParam("$id", row.Id);
            write.Exec(_engine);
        }
    }

    private DbConnection Open(bool archive = false) => _engine.Open(archive);

    private void EnsureTextColumn(string table, string column, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        if (existing.Contains(column))
            return;

        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
        cmd.Exec(_engine);
        if (column.Equals(Schema.RecordStatus, StringComparison.OrdinalIgnoreCase))
            BackfillLiveStatus(table, archive);
    }

    private void BackfillLiveStatus(string table, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(Schema.RecordStatus))
            return;

        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            $"UPDATE {Quote(table)} SET {Quote(Schema.RecordStatus)} = $live " +
            $"WHERE {Quote(Schema.RecordStatus)} IS NULL OR TRIM({Quote(Schema.RecordStatus)}) = '';";
        cmd.AddParam("$live", Schema.RecordLive);
        cmd.Exec(_engine);
    }

    private void DropTextColumn(string table, string column, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        if (!existing.Contains(column))
            return;

        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} DROP COLUMN {Quote(column)};";
        cmd.Exec(_engine);
    }

    private void EnsureColumn(string table, string column, string definition)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive: false), StringComparer.OrdinalIgnoreCase);
        if (existing.Contains(column))
            return;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {definition};";
        cmd.Exec(_engine);
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
        using var db = Open(archive);
        return _engine.ListColumns(db, table).ToList();
    }

    private static string Quote(string name) => StoreEngine.Quote(name);

    private static string NowStamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    private static string TermKey(DateTime? term) => (term ?? DateTime.Today).ToString("yyyy-MM-dd");
}
