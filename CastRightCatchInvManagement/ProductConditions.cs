using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Product Condition names for New Item. Fresh, Frozen, and PF are built in;
    /// administrators can add more under Admin management.
    /// </summary>
    internal static class ProductConditions
    {
        public const string SettingKey = "product_conditions";
        public static readonly string[] BuiltIn = { "Fresh", "Frozen", "PF" };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static List<string>? _cache;

        /// <summary>True for Fresh, Frozen, or PF, which cannot be removed from the catalog.</summary>
        public static bool IsBuiltIn(string? name) =>
            BuiltIn.Any(item => item.Equals((name ?? "").Trim(), StringComparison.OrdinalIgnoreCase));

        /// <summary>Condition names for the New Item dropdown, built-ins first.</summary>
        public static List<string> Load() => Names().ToList();

        /// <summary>Drop the in-memory list so the next Load reads settings again.</summary>
        public static void Reload() => _cache = null;

        /// <summary>Replace the catalog after Admin add/rename/delete.</summary>
        public static void Save(IEnumerable<string> names)
        {
            var list = Normalize(names);
            SqliteInventory.WriteSettings(new Dictionary<string, string>
            {
                [SettingKey] = JsonSerializer.Serialize(list, JsonOptions)
            });
            _cache = list;
        }

        /// <summary>Shared list from settings, cached until the next write or Reload.</summary>
        private static List<string> Names()
        {
            if (_cache != null)
                return _cache;
            _cache = Read();
            return _cache;
        }

        /// <summary>Load JSON names, or the built-in Fresh / Frozen / PF set.</summary>
        private static List<string> Read()
        {
            var map = SqliteInventory.ReadPublicSettings();
            // First run: Fresh, Frozen, and PF with nothing extra.
            if (!map.TryGetValue(SettingKey, out var json) || string.IsNullOrWhiteSpace(json))
                return Normalize(BuiltIn);

            try
            {
                var loaded = JsonSerializer.Deserialize<List<string>>(json.Trim(), JsonOptions);
                return loaded == null ? Normalize(BuiltIn) : Normalize(loaded);
            }
            // Corrupt settings must not block New Item.
            catch
            {
                return Normalize(BuiltIn);
            }
        }

        /// <summary>Keep Fresh, Frozen, and PF first, then extra names, unique and trimmed.</summary>
        private static List<string> Normalize(IEnumerable<string>? values)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string built in BuiltIn)
            {
                names.Add(built);
                seen.Add(built);
            }

            foreach (var raw in values ?? Array.Empty<string>())
            {
                string name = (raw ?? "").Trim();
                // Skip empty or duplicate names so the dropdown stays unique.
                if (name.Length == 0 || !seen.Add(name))
                    continue;
                names.Add(name);
            }

            return names;
        }
    }
}
