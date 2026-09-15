using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrcInventory.Server;

/// <summary>admins.json on disk: administrator and IT usernames, kept in sync with account flags.</summary>
internal sealed class RolesFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly object _gate = new();

    /// <summary>Binds this store to <c>admins.json</c> under <paramref name="dataFolder"/>.</summary>
    public RolesFile(string dataFolder)
    {
        _path = Path.Combine(dataFolder, Schema.RolesFileName);
    }

    /// <summary>Administrator usernames loaded from disk.</summary>
    public List<string> Admins { get; private set; } = new();
    /// <summary>IT usernames loaded from disk.</summary>
    public List<string> It { get; private set; } = new();

    /// <summary>Reads admins.json, or starts empty when the file is missing or invalid.</summary>
    public void Load()
    {
        lock (_gate)
        {
            // No file yet means no roles have been assigned on this host.
            if (!File.Exists(_path))
            {
                Admins = new List<string>();
                It = new List<string>();
                return;
            }

            // A corrupt admins.json must not block startup.
            try
            {
                var file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), JsonOptions)
                    ?? new FileShape();
                Admins = Clean(file.Admins);
                It = Clean(file.It);
            }
            // Treat roles as empty until the next Save rewrites a valid file.
            catch
            {
                Admins = new List<string>();
                It = new List<string>();
            }
        }
    }

    /// <summary>Writes admins.json atomically via a temp file so a crash cannot leave a half-written list.</summary>
    public void Save()
    {
        lock (_gate)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_path)!);
            var file = new FileShape { Admins = Clean(Admins), It = Clean(It) };
            string json = JsonSerializer.Serialize(file, JsonOptions);
            string temp = _path + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, _path, overwrite: true);
            File.Delete(temp);
            Admins = file.Admins;
            It = file.It;
        }
    }

    /// <summary>True when <paramref name="username"/> is in the administrator list.</summary>
    public bool IsAdmin(string? username) => Contains(Admins, username);

    /// <summary>True when <paramref name="username"/> is in the IT list.</summary>
    public bool IsIt(string? username) => Contains(It, username);

    /// <summary>True when at least one IT username is recorded (bootstrap is done).</summary>
    public bool HasItUser() => It.Count > 0;

    /// <summary>Replaces both lists and persists them.</summary>
    public void Replace(IEnumerable<string> admins, IEnumerable<string> it)
    {
        lock (_gate)
        {
            Admins = Clean(admins);
            It = Clean(it);
        }
        Save();
    }

    /// <summary>Adds or removes <paramref name="username"/> from admin/IT lists and persists.</summary>
    public void Ensure(string username, bool admin, bool it)
    {
        username = (username ?? "").Trim();
        // Blank names are not valid role members.
        if (username.Length == 0)
            return;

        lock (_gate)
        {
            SetMembership(Admins, username, admin);
            SetMembership(It, username, it);
        }
        Save();
    }

    /// <summary>Renames a user in both lists when an account is renamed.</summary>
    public void Rename(string oldUsername, string newUsername)
    {
        lock (_gate)
        {
            ReplaceName(Admins, oldUsername, newUsername);
            ReplaceName(It, oldUsername, newUsername);
        }
        Save();
    }

    /// <summary>Drops a user from both lists when an account is deleted.</summary>
    public void Remove(string username)
    {
        lock (_gate)
        {
            Admins.RemoveAll(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
            It.RemoveAll(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
        }
        Save();
    }

    /// <summary>Ensures <paramref name="username"/> is present or absent in <paramref name="names"/>.</summary>
    private static void SetMembership(List<string> names, string username, bool include)
    {
        names.RemoveAll(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
        // Re-add after remove so casing matches the latest write.
        if (include)
            names.Add(username);
    }

    /// <summary>Replaces the first case-insensitive match of <paramref name="oldUsername"/>.</summary>
    private static void ReplaceName(List<string> names, string oldUsername, string newUsername)
    {
        for (int i = 0; i < names.Count; i++)
        {
            // Keep list position; only the spelling of the username changes.
            if (names[i].Equals(oldUsername, StringComparison.OrdinalIgnoreCase))
                names[i] = newUsername;
        }
    }

    /// <summary>Case-insensitive membership test; blank usernames never match.</summary>
    private static bool Contains(IEnumerable<string> names, string? username)
    {
        username = (username ?? "").Trim();
        // Empty names are not stored, so they cannot be in a role.
        if (username.Length == 0)
            return false;
        return names.Any(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Trims, drops blanks, and de-duplicates names case-insensitively.</summary>
    private static List<string> Clean(IEnumerable<string>? names) =>
        (names ?? Array.Empty<string>())
            .Select(name => (name ?? "").Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    /// <summary>On-disk JSON shape for admins.json.</summary>
    private sealed class FileShape
    {
        /// <summary>Administrator usernames.</summary>
        [JsonPropertyName("admins")]
        public List<string> Admins { get; set; } = new();

        /// <summary>IT usernames.</summary>
        [JsonPropertyName("it")]
        public List<string> It { get; set; } = new();
    }
}
