using System.Text.Json;

namespace CrcInventory.Server;

/// <summary>
/// Live vs old SQLite folders, saved next to the TLS cert so the window remembers them.
/// </summary>
internal sealed class HostSettings
{
    /// <summary>Folder that holds crc_inventory.db (current term).</summary>
    public string LiveFolder { get; set; } = "";
    /// <summary>Folder that holds dated old_inventory files.</summary>
    public string ArchiveFolder { get; set; } = "";

    /// <summary>Load host-settings.json from <paramref name="dataFolder"/>, or empty paths.</summary>
    public static HostSettings Load(string dataFolder)
    {
        string path = Path.Combine(dataFolder, "host-settings.json");
        if (!File.Exists(path))
            return new HostSettings();
        try
        {
            return JsonSerializer.Deserialize<HostSettings>(File.ReadAllText(path)) ?? new HostSettings();
        }
        catch
        {
            return new HostSettings();
        }
    }

    /// <summary>Write live/old folders to host-settings.json in the data folder.</summary>
    public void Save(string dataFolder)
    {
        Directory.CreateDirectory(dataFolder);
        string json = JsonSerializer.Serialize(this, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(Path.Combine(dataFolder, "host-settings.json"), json);
    }
}
