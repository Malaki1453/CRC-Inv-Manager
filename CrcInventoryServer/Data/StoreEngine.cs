using System.Data.Common;
using Microsoft.Data.Sqlite;

namespace CrcInventory.Server;

/// <summary>
/// SQLite files in a data folder: live crc_inventory.db, archive old_inventory.db.
/// </summary>
internal abstract class StoreEngine
{
    /// <summary>Human-readable engine name for the host log.</summary>
    public abstract string Name { get; }
    /// <summary>SQL fragment that declares the auto-increment primary key column.</summary>
    public abstract string IdColumn { get; }
    /// <summary>SQL type used for stored PDF bytes.</summary>
    public abstract string BlobType { get; }
    /// <summary>SQL type for case-insensitive username/key text.</summary>
    public abstract string NoCaseText { get; }

    /// <summary>Opens live or archive SQLite file.</summary>
    public abstract DbConnection Open(bool archive);
    /// <summary>Lists column names on <paramref name="table"/>.</summary>
    public abstract IReadOnlyList<string> ListColumns(DbConnection db, string table);
    /// <summary>Rewrites command text/parameters for this engine before execute.</summary>
    public abstract void Prepare(DbCommand cmd);

    /// <summary>Double-quote identifier, doubling inner quotes.</summary>
    public static string Quote(string name) => "\"" + name.Replace("\"", "\"\"") + "\"";

    /// <summary>SQLite engine with current and old databases in separate folders.</summary>
    public static StoreEngine Sqlite(string liveFolder, string archiveFolder) =>
        new SqliteStoreEngine(liveFolder, archiveFolder);
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

/// <summary>Current crc_inventory.db in one folder; dated old_inventory files in another.</summary>
internal sealed class SqliteStoreEngine : StoreEngine
{
    private readonly string _liveFolder;
    private readonly string _archiveFolder;
    private string _archivePath;

    /// <summary>Binds live and old folders; creates them if missing.</summary>
    public SqliteStoreEngine(string liveFolder, string archiveFolder)
    {
        _liveFolder = Path.GetFullPath(liveFolder);
        _archiveFolder = Path.GetFullPath(archiveFolder);
        Directory.CreateDirectory(_liveFolder);
        Directory.CreateDirectory(_archiveFolder);
        _archivePath = ResolveWorkingArchive();
    }

    /// <inheritdoc />
    public override string Name => "sqlite (local files)";
    /// <inheritdoc />
    public override string IdColumn => "id INTEGER PRIMARY KEY AUTOINCREMENT";
    /// <inheritdoc />
    public override string BlobType => "BLOB";
    /// <inheritdoc />
    public override string NoCaseText => "TEXT NOT NULL COLLATE NOCASE";

    /// <summary>Full path of crc_inventory.db.</summary>
    public string LivePath => Path.Combine(_liveFolder, Schema.LiveFileName);
    /// <summary>Folder that holds dated old databases.</summary>
    public string ArchiveFolder => _archiveFolder;
    /// <summary>Old database currently written for View Old / Next Term.</summary>
    public string ArchivePath => _archivePath;

    /// <inheritdoc />
    public override DbConnection Open(bool archive)
    {
        string path = archive ? _archivePath : LivePath;
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

    /// <summary>
    /// Prefer a working old_inventory.db; otherwise the newest dated file so View Old still works
    /// after a stamp. If the operator removed the file, start a fresh old_inventory.db.
    /// </summary>
    public string ResolveWorkingArchive()
    {
        string working = Path.Combine(_archiveFolder, Schema.ArchiveFileName);
        if (File.Exists(working))
            return working;
        var dated = Directory.GetFiles(_archiveFolder, "old_inventory *.db");
        if (dated.Length > 0)
            return dated.OrderByDescending(File.GetLastWriteTimeUtc).First();
        return working;
    }

    /// <summary>
    /// Rename the working old database to include MM yyyy - MM yyyy so off-system copies sort by dates.
    /// </summary>
    public void StampArchiveFileName(DateTime start, DateTime end)
    {
        CheckpointArchive();
        string dest = Path.Combine(_archiveFolder, Schema.DatedArchiveFileName(start, end));
        // Already named for this range.
        if (string.Equals(_archivePath, dest, StringComparison.OrdinalIgnoreCase))
            return;
        // Do not overwrite another file that already has this range.
        if (File.Exists(dest))
            return;
        if (!File.Exists(_archivePath))
            return;
        try
        {
            File.Move(_archivePath, dest);
            _archivePath = dest;
        }
        catch (IOException)
        {
            // File in use (View Old / Next Term); keep the working name until the next stamp.
        }
    }

    /// <summary>Flush WAL so the .db file can be renamed or copied off the machine.</summary>
    public void CheckpointArchive()
    {
        if (!File.Exists(_archivePath))
            return;
        using var db = Open(archive: true);
        using var cmd = db.CreateCommand();
        cmd.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
        cmd.ExecuteNonQuery();
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
