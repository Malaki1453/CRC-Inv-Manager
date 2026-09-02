using System.Text.Json;
using System.Text.Json.Serialization;

namespace CrcInventory.Server;

internal sealed class RolesFile
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };

    private readonly string _path;
    private readonly object _gate = new();

    public RolesFile(string dataFolder)
    {
        _path = Path.Combine(dataFolder, Schema.RolesFileName);
    }

    public List<string> Admins { get; private set; } = new();
    public List<string> It { get; private set; } = new();

    public void Load()
    {
        lock (_gate)
        {
            if (!File.Exists(_path))
            {
                Admins = new List<string>();
                It = new List<string>();
                return;
            }

            try
            {
                var file = JsonSerializer.Deserialize<FileShape>(File.ReadAllText(_path), JsonOptions)
                    ?? new FileShape();
                Admins = Clean(file.Admins);
                It = Clean(file.It);
            }
            catch
            {
                Admins = new List<string>();
                It = new List<string>();
            }
        }
    }

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

    public bool IsAdmin(string? username) => Contains(Admins, username);

    public bool IsIt(string? username) => Contains(It, username);

    public bool HasItUser() => It.Count > 0;

    public void Replace(IEnumerable<string> admins, IEnumerable<string> it)
    {
        lock (_gate)
        {
            Admins = Clean(admins);
            It = Clean(it);
        }
        Save();
    }

    public void Ensure(string username, bool admin, bool it)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;

        lock (_gate)
        {
            SetMembership(Admins, username, admin);
            SetMembership(It, username, it);
        }
        Save();
    }

    public void Rename(string oldUsername, string newUsername)
    {
        lock (_gate)
        {
            ReplaceName(Admins, oldUsername, newUsername);
            ReplaceName(It, oldUsername, newUsername);
        }
        Save();
    }

    public void Remove(string username)
    {
        lock (_gate)
        {
            Admins.RemoveAll(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
            It.RemoveAll(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
        }
        Save();
    }

    private static void SetMembership(List<string> names, string username, bool include)
    {
        names.RemoveAll(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
        if (include)
            names.Add(username);
    }

    private static void ReplaceName(List<string> names, string oldUsername, string newUsername)
    {
        for (int i = 0; i < names.Count; i++)
        {
            if (names[i].Equals(oldUsername, StringComparison.OrdinalIgnoreCase))
                names[i] = newUsername;
        }
    }

    private static bool Contains(IEnumerable<string> names, string? username)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return false;
        return names.Any(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
    }

    private static List<string> Clean(IEnumerable<string>? names) =>
        (names ?? Array.Empty<string>())
            .Select(name => (name ?? "").Trim())
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();

    private sealed class FileShape
    {
        [JsonPropertyName("admins")]
        public List<string> Admins { get; set; } = new();

        [JsonPropertyName("it")]
        public List<string> It { get; set; } = new();
    }
}
