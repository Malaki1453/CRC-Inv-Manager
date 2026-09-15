using System.Data.Common;
using System.Text.RegularExpressions;
using Microsoft.Data.Sqlite;
using Npgsql;

namespace CrcInventory.Server;

/// <summary>
/// SQLite files by default. Postgres (Digital Ocean) when a connection string is set.
/// Live and archive map to crc_inventory.db / old_inventory.db, or schemas live / archive.
/// </summary>
internal abstract class StoreEngine
{
    /// <summary>Human-readable engine name for the host log.</summary>
    public abstract string Name { get; }
    /// <summary>True when this engine is Postgres rather than local SQLite files.</summary>
    public abstract bool IsPostgres { get; }
    /// <summary>SQL fragment that declares the auto-increment primary key column.</summary>
    public abstract string IdColumn { get; }
    /// <summary>SQL type used for stored PDF bytes.</summary>
    public abstract string BlobType { get; }
    /// <summary>SQL type for case-insensitive username/key text.</summary>
    public abstract string NoCaseText { get; }

    /// <summary>Opens live or archive (SQLite file or Postgres search_path).</summary>
    public abstract DbConnection Open(bool archive);
    /// <summary>Lists column names on <paramref name="table"/> in the current schema/file.</summary>
    public abstract IReadOnlyList<string> ListColumns(DbConnection db, string table);
    /// <summary>Rewrites command text/parameters for this engine before execute.</summary>
    public abstract void Prepare(DbCommand cmd);

    /// <summary>Double-quote identifier, doubling inner quotes.</summary>
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    /// <summary>Local SQLite engine using crc_inventory.db and old_inventory.db in <paramref name="folder"/>.</summary>
    public static StoreEngine Sqlite(string folder) => new SqliteStoreEngine(folder);

    /// <summary>Postgres engine using live/archive schemas on the given connection string.</summary>
    public static StoreEngine Postgres(string connectionString) =>
        new PostgresStoreEngine(connectionString);
}

/// <summary>Helpers that bind parameters and run commands after <see cref="StoreEngine.Prepare"/>.</summary>
internal static class DbCmd
{
    /// <summary>Adds a named parameter, mapping null to DBNull and byte[] to Binary.</summary>
    public static void AddParam(this DbCommand cmd, string name, object? value)
    {
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = name;
        parameter.Value = value ?? DBNull.Value;
        // PDF blobs must be sent as binary, not as a string of bytes.
        if (value is byte[])
            parameter.DbType = System.Data.DbType.Binary;
        cmd.Parameters.Add(parameter);
    }

    /// <summary>Prepares then ExecuteNonQuery.</summary>
    public static int Exec(this DbCommand cmd, StoreEngine engine)
    {
        engine.Prepare(cmd);
        return cmd.ExecuteNonQuery();
    }

    /// <summary>Prepares then ExecuteReader.</summary>
    public static DbDataReader Query(this DbCommand cmd, StoreEngine engine)
    {
        engine.Prepare(cmd);
        return cmd.ExecuteReader();
    }

    /// <summary>Prepares then ExecuteScalar.</summary>
    public static object? Scalar(this DbCommand cmd, StoreEngine engine)
    {
        engine.Prepare(cmd);
        return cmd.ExecuteScalar();
    }
}

/// <summary>SQLite files in a data folder: live crc_inventory.db, archive old_inventory.db.</summary>
internal sealed class SqliteStoreEngine : StoreEngine
{
    private readonly string _folder;

    /// <summary>Binds this engine to SQLite files under <paramref name="folder"/>.</summary>
    public SqliteStoreEngine(string folder) => _folder = folder;

    /// <inheritdoc />
    public override string Name => "sqlite (local files)";
    /// <inheritdoc />
    public override bool IsPostgres => false;
    /// <inheritdoc />
    public override string IdColumn => "id INTEGER PRIMARY KEY AUTOINCREMENT";
    /// <inheritdoc />
    public override string BlobType => "BLOB";
    /// <inheritdoc />
    public override string NoCaseText => "TEXT NOT NULL COLLATE NOCASE";

    /// <inheritdoc />
    public override DbConnection Open(bool archive)
    {
        string path = Path.Combine(
            _folder,
            archive ? Schema.ArchiveFileName : Schema.LiveFileName);
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

    /// <inheritdoc />
    public override IReadOnlyList<string> ListColumns(DbConnection db, string table)
    {
        var list = new List<string>();
        using var cmd = db.CreateCommand();
        cmd.CommandText = $"PRAGMA table_info({Quote(table)});";
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(1));
        return list;
    }

    /// <summary>SQLite already accepts $params and COLLATE NOCASE; nothing to rewrite.</summary>
    public override void Prepare(DbCommand cmd)
    {
    }
}

/// <summary>Postgres (Digital Ocean): live and archive schemas, CITEXT, BYTEA, lastval().</summary>
internal sealed class PostgresStoreEngine : StoreEngine
{
    private static readonly Regex DollarParams = new(@"\$([A-Za-z_][A-Za-z0-9_]*)", RegexOptions.Compiled);
    private readonly string _connectionString;
    private readonly object _bootLock = new();
    private bool _bootstrapped;

    /// <summary>Normalizes the connection string (URI or Npgsql) and requires SSL.</summary>
    public PostgresStoreEngine(string connectionString)
    {
        _connectionString = Normalize(connectionString);
    }

    /// <inheritdoc />
    public override string Name => "postgres (Digital Ocean)";
    /// <inheritdoc />
    public override bool IsPostgres => true;
    /// <inheritdoc />
    public override string IdColumn => "id BIGINT GENERATED BY DEFAULT AS IDENTITY PRIMARY KEY";
    /// <inheritdoc />
    public override string BlobType => "BYTEA";
    /// <inheritdoc />
    public override string NoCaseText => "CITEXT NOT NULL";

    /// <inheritdoc />
    public override DbConnection Open(bool archive)
    {
        var db = new NpgsqlConnection(_connectionString);
        db.Open();
        Bootstrap(db);
        using var path = db.CreateCommand();
        path.CommandText = archive
            ? "SET search_path TO archive, public"
            : "SET search_path TO live, public";
        path.ExecuteNonQuery();
        return db;
    }

    /// <inheritdoc />
    public override IReadOnlyList<string> ListColumns(DbConnection db, string table)
    {
        var list = new List<string>();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            """
            SELECT column_name
            FROM information_schema.columns
            WHERE table_schema = current_schema()
              AND table_name = @table
            ORDER BY ordinal_position;
            """;
        var parameter = cmd.CreateParameter();
        parameter.ParameterName = "@table";
        parameter.Value = table;
        cmd.Parameters.Add(parameter);
        using var reader = cmd.ExecuteReader();
        while (reader.Read())
            list.Add(reader.GetString(0));
        return list;
    }

    /// <summary>Rewrites $name parameters and SQLite-only SQL so the same commands run on Postgres.</summary>
    public override void Prepare(DbCommand cmd)
    {
        string sql = cmd.CommandText ?? "";
        sql = DollarParams.Replace(sql, "@$1");
        sql = sql.Replace("COLLATE NOCASE", "", StringComparison.OrdinalIgnoreCase);
        sql = sql.Replace("last_insert_rowid()", "lastval()", StringComparison.OrdinalIgnoreCase);
        cmd.CommandText = sql;
        foreach (DbParameter parameter in cmd.Parameters)
        {
            string name = parameter.ParameterName ?? "";
            // Npgsql binds @name; leftover $name from SQLite-style commands must be rewritten.
            if (name.StartsWith('$'))
                parameter.ParameterName = "@" + name[1..];
        }
    }

    /// <summary>Once per process: enable CITEXT and create live/archive schemas.</summary>
    private void Bootstrap(NpgsqlConnection db)
    {
        // Already ran on this connection-string engine; skip the lock and DDL.
        if (_bootstrapped)
            return;
        lock (_bootLock)
        {
            // Double-check after waiting so two Open calls do not both run CREATE.
            if (_bootstrapped)
                return;
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                CREATE EXTENSION IF NOT EXISTS citext;
                CREATE SCHEMA IF NOT EXISTS live;
                CREATE SCHEMA IF NOT EXISTS archive;
                """;
            cmd.ExecuteNonQuery();
            _bootstrapped = true;
        }
    }

    /// <summary>Accepts a URI or Npgsql string; forces SSL and Digital Ocean's 25060 port when needed.</summary>
    internal static string Normalize(string raw)
    {
        raw = (raw ?? "").Trim();
        // An empty string would open a default local server, which is never what we want.
        if (raw.Length == 0)
            throw new InvalidOperationException("Postgres connection string is empty.");

        // DATABASE_URL style URIs from Digital Ocean / Heroku.
        if (raw.StartsWith("postgres://", StringComparison.OrdinalIgnoreCase) ||
            raw.StartsWith("postgresql://", StringComparison.OrdinalIgnoreCase))
        {
            var uri = new Uri(raw);
            string userInfo = uri.UserInfo ?? "";
            int colon = userInfo.IndexOf(':');
            string user = Uri.UnescapeDataString(colon < 0 ? userInfo : userInfo[..colon]);
            string password = colon < 0 ? "" : Uri.UnescapeDataString(userInfo[(colon + 1)..]);
            string database = uri.AbsolutePath.Trim('/');
            var fromUri = new NpgsqlConnectionStringBuilder
            {
                Host = uri.Host,
                Port = uri.IsDefaultPort
                    ? (uri.Host.Contains("ondigitalocean.com", StringComparison.OrdinalIgnoreCase) ? 25060 : 5432)
                    : uri.Port,
                Database = database.Length > 0 ? database : "defaultdb",
                Username = user,
                Password = password,
                SslMode = SslMode.Require
            };
            return fromUri.ToString();
        }

        var builder = new NpgsqlConnectionStringBuilder(raw);
        // Managed Postgres requires TLS; weaker modes would fail or leak.
        if (builder.SslMode is SslMode.Disable or SslMode.Allow or SslMode.Prefer)
            builder.SslMode = SslMode.Require;
        // Digital Ocean advertises 25060, not the default 5432.
        if (builder.Port == 5432 &&
            (builder.Host ?? "").Contains("ondigitalocean.com", StringComparison.OrdinalIgnoreCase))
            builder.Port = 25060;
        return builder.ToString();
    }
}
