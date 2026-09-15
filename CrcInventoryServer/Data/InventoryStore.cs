using System.Data.Common;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

/// <summary>
/// Hosted inventory database: SQLite files or Postgres schemas, plus accounts, settings, PDFs, and bank links.
/// </summary>
internal sealed partial class InventoryStore
{
    private readonly string _folder;
    private readonly StoreEngine _engine;
    private readonly object _gate = new();

    /// <summary>Creates the data folder, picks SQLite or Postgres, loads roles, and ensures schema.</summary>
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

    /// <summary>admins.json wrapper for administrator and IT usernames.</summary>
    public RolesFile Roles { get; }

    /// <summary>Resolved data folder (SQLite files, PFX, admins.json, crc.key).</summary>
    public string Folder => _folder;

    /// <summary>Human-readable engine name for the host log.</summary>
    public string EngineName => _engine.Name;

    /// <summary>True when this store is Postgres rather than local SQLite files.</summary>
    public bool UsesPostgres => _engine.IsPostgres;

    /// <summary>Path of crc_inventory.db (unused on Postgres except for logging).</summary>
    public string LivePath => Path.Combine(_folder, Schema.LiveFileName);

    /// <summary>Path of old_inventory.db (unused on Postgres except for logging).</summary>
    public string ArchivePath => Path.Combine(_folder, Schema.ArchiveFileName);

    /// <summary>Creates live and archive tables, app tables, and upgrades plaintext secrets.</summary>
    public void EnsureCreated()
    {
        lock (_gate)
        {
            EnsureCreated(archive: false);
            EnsureCreated(archive: true);
        }
    }

    /// <summary>Creates tables for one side (live or archive) and, on live, the app/account/PDF tables.</summary>
    private void EnsureCreated(bool archive)
    {
        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        // WAL is a SQLite pragma; Postgres does not accept it.
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
            // Drop purchase columns the desktop schema no longer uses so hosted files stay in sync.
            if (table.Equals(Schema.PurchaseSales, StringComparison.OrdinalIgnoreCase))
            {
                DropTextColumn(table, "Vendor Invoice #", archive);
                DropTextColumn(table, "Volume Received", archive);
            }
            // PDF Created was removed from invoices; drop it if an older file still has it.
            if (table.Equals(Schema.Invoices, StringComparison.OrdinalIgnoreCase))
                DropTextColumn(table, "PDF Created", archive);
        }

        // Archive databases only hold process tables; skip live-only app tables.
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

    /// <summary>Ensures the built-in Admin (deny-all tables) and IT (empty policy) access groups exist.</summary>
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

    /// <summary>True when admins.json or app_accounts already has an IT user (bootstrap is done).</summary>
    public bool HasItUser()
    {
        lock (_gate)
        {
            // Roles file is the source of truth after bootstrap; skip the table scan when it already lists IT.
            if (Roles.HasItUser())
                return true;
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM app_accounts WHERE COALESCE(is_it, 0) <> 0;";
            return Convert.ToInt32(cmd.Scalar(_engine)) > 0;
        }
    }

    /// <summary>Reads app_settings; secrets are revealed for admins or omitted for everyone else.</summary>
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

    /// <summary>Public settings with secret keys omitted.</summary>
    public Dictionary<string, string> ReadPublicSettings() => ReadSettings(revealSecrets: false);

    /// <summary>Upserts settings; empty secret values are skipped so a blank field does not wipe the stored secret.</summary>
    public void WriteSettings(Dictionary<string, string> values)
    {
        lock (_gate)
        {
            using var db = Open();
            using var tx = db.BeginTransaction();
            foreach (var pair in values)
            {
                // An empty secret field means "leave the stored secret alone", not "clear it".
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

    /// <summary>Reads UI preferences for <paramref name="username"/>; empty name returns an empty map.</summary>
    public Dictionary<string, string> ReadPrefs(string username)
    {
        username = (username ?? "").Trim();
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        // Prefs are per account; a blank name has none.
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

    /// <summary>Upserts UI preferences for <paramref name="username"/>; no-ops on a blank name or empty map.</summary>
    public void WritePrefs(string username, Dictionary<string, string> values)
    {
        username = (username ?? "").Trim();
        // Nothing to store without a user or a map.
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

    /// <summary>Looks up the email stored for a Windows user name, or null when missing.</summary>
    public string? ReadUserEmail(string windowsUser)
    {
        windowsUser = (windowsUser ?? "").Trim();
        // Blank Windows names are not stored.
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

    /// <summary>Upserts the email for a Windows user name; no-ops on a blank name.</summary>
    public void WriteUserEmail(string windowsUser, string? email)
    {
        windowsUser = (windowsUser ?? "").Trim();
        // Blank Windows names are not stored.
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

    /// <summary>Current term start from settings, or today when unset/invalid.</summary>
    public DateTime TermStart()
    {
        var settings = ReadSettings();
        // Use the stored term when it parses; otherwise default to today.
        if (settings.TryGetValue("term_start", out var text) && DateTime.TryParse(text, out var date))
            return date;
        return DateTime.Today;
    }

    /// <summary>Stay-signed-in lifetime in days from settings, or 30 when unset/invalid.</summary>
    public int StaySignedInDays()
    {
        var settings = ReadSettings();
        // Reject zero/negative so a bad setting cannot issue a zero-length token.
        if (settings.TryGetValue("stay_signed_in_days", out var text) &&
            int.TryParse(text, out int days) && days > 0)
            return days;
        return 30;
    }

    /// <summary>Idle-close hours from settings, or 5 when unset/invalid.</summary>
    public int IdleCloseHours()
    {
        var settings = ReadSettings();
        // Reject zero/negative so a bad setting cannot disable idle close.
        if (settings.TryGetValue("idle_close_hours", out var text) &&
            int.TryParse(text, out int hours) && hours > 0)
            return hours;
        return 5;
    }

    /// <summary>Whether stay-signed-in is offered; missing setting means enabled.</summary>
    public bool StaySignedInEnabled()
    {
        var settings = ReadSettings();
        // Only an explicit "0" turns the feature off; missing means on.
        if (settings.TryGetValue("stay_signed_in_enabled", out var text))
            return text != "0";
        return true;
    }

    /// <summary>Reads all app_settings rows without revealing or stripping secrets.</summary>
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

    /// <summary>Seals any leftover plaintext secrets in settings, SMTP, bank tokens, and routing/account columns.</summary>
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

    /// <summary>Seals one app_settings value when it is still plaintext.</summary>
    private void UpgradeSetting(string key)
    {
        using var db = Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT value FROM app_settings WHERE key = $key;";
        read.AddParam("$key", key);
        string? value = read.Scalar(_engine)?.ToString();
        // Missing or already-sealed values need no rewrite.
        if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
            return;
        using var write = db.CreateCommand();
        write.CommandText = "UPDATE app_settings SET value = $value WHERE key = $key;";
        write.AddParam("$value", SecretProtect.Seal(value));
        write.AddParam("$key", key);
        write.Exec(_engine);
    }

    /// <summary>Seals admin_smtp.password when it is still plaintext.</summary>
    private void UpgradeSmtpPassword()
    {
        using var db = Open();
        using var read = db.CreateCommand();
        read.CommandText = "SELECT password FROM admin_smtp WHERE id = 1;";
        string? value = read.Scalar(_engine)?.ToString();
        // Missing or already-sealed values need no rewrite.
        if (string.IsNullOrEmpty(value) || SecretProtect.IsSealed(value))
            return;
        using var write = db.CreateCommand();
        write.CommandText = "UPDATE admin_smtp SET password = $value WHERE id = 1;";
        write.AddParam("$value", SecretProtect.Seal(value));
        write.Exec(_engine);
    }

    /// <summary>Seals plaintext cells in one secret column, skipping empty and already-sealed values.</summary>
    private void UpgradeColumn(string table, string column, bool archive)
    {
        var columns = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        // Older files may not have this column yet.
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
                // Empty and already-sealed cells stay as-is.
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

    /// <summary>Opens live (default) or archive through the current engine.</summary>
    private DbConnection Open(bool archive = false) => _engine.Open(archive);

    /// <summary>Adds a missing TEXT column, and backfills Record Status when that column is new.</summary>
    private void EnsureTextColumn(string table, string column, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        // Column already exists; ALTER would fail.
        if (existing.Contains(column))
            return;

        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} TEXT;";
        cmd.Exec(_engine);
        // New Record Status columns should mark existing rows Live.
        if (column.Equals(Schema.RecordStatus, StringComparison.OrdinalIgnoreCase))
            BackfillLiveStatus(table, archive);
    }

    /// <summary>Sets blank Record Status cells to Live so older rows display consistently.</summary>
    private void BackfillLiveStatus(string table, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        // Nothing to backfill until the column exists.
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

    /// <summary>Drops a leftover TEXT column that the current desktop schema no longer uses.</summary>
    private void DropTextColumn(string table, string column, bool archive)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive), StringComparer.OrdinalIgnoreCase);
        // Column already gone; DROP would fail.
        if (!existing.Contains(column))
            return;

        using var db = Open(archive);
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} DROP COLUMN {Quote(column)};";
        cmd.Exec(_engine);
    }

    /// <summary>Adds a missing column with the given SQL definition on the live database.</summary>
    private void EnsureColumn(string table, string column, string definition)
    {
        var existing = new HashSet<string>(TableColumnsFrom(table, archive: false), StringComparer.OrdinalIgnoreCase);
        // Column already exists; ALTER would fail.
        if (existing.Contains(column))
            return;

        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"ALTER TABLE {Quote(table)} ADD COLUMN {Quote(column)} {definition};";
        cmd.Exec(_engine);
    }

    /// <summary>Live columns, or live+archive union when viewing old process tables.</summary>
    private List<string> TableColumns(string table, bool viewOld)
    {
        // Archive process tables may have extra columns; merge them for "view old".
        if (viewOld && Schema.IsProcessTable(table))
        {
            var combined = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var column in TableColumnsFrom(table, false).Concat(TableColumnsFrom(table, true)))
            {
                // Keep first-seen casing; skip duplicates from the other file/schema.
                if (!used.Add(column))
                    continue;
                combined.Add(column);
            }

            return combined;
        }

        return TableColumnsFrom(table, archive: false);
    }

    /// <summary>Column names on one side (live or archive) of <paramref name="table"/>.</summary>
    private List<string> TableColumnsFrom(string table, bool archive)
    {
        using var db = Open(archive);
        return _engine.ListColumns(db, table).ToList();
    }

    /// <summary>Double-quote identifier for the current engine.</summary>
    private static string Quote(string name) => StoreEngine.Quote(name);

    /// <summary>Local timestamp used for created_at / stored_at / updated_at.</summary>
    private static string NowStamp() => DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss");

    /// <summary>yyyy-MM-dd term key, defaulting to today when <paramref name="term"/> is null.</summary>
    private static string TermKey(DateTime? term) => (term ?? DateTime.Today).ToString("yyyy-MM-dd");
}
