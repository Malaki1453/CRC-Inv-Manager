using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>Remembers which grid columns are shown, hidden, and in what order.</summary>
    internal static class GridLayout
    {
        private static bool _applying;

        public static void BeginUpdate() => _applying = true;

        public static void EndUpdate() => _applying = false;

        public static void Apply(DataGridView grid, string baseName)
        {
            if (string.IsNullOrWhiteSpace(baseName) ||
                string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            var names = Load(baseName);
            if (names.Count == 0)
                return;

            ApplyNames(grid, names);
        }

        public static bool ApplyDefault(DataGridView grid)
        {
            if (grid.Tag is not ColumnSearch search ||
                string.IsNullOrWhiteSpace(search.FileBaseName))
                return false;

            var names = LoadDefault(search.FileBaseName);
            if (names.Count == 0)
                return false;

            ApplyNames(grid, names);
            Save(grid);
            return true;
        }

        public static bool SaveDefault(DataGridView grid)
        {
            if (_applying)
                return false;
            if (grid.Tag is not ColumnSearch search ||
                string.IsNullOrWhiteSpace(search.FileBaseName) ||
                string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return false;
            if (!AppState.SignedIn)
                return false;

            var names = VisibleNames(grid);
            if (names.Count == 0)
                return false;

            try
            {
                SqliteInventory.WritePrefs(new Dictionary<string, string>
                {
                    [DefaultKey(search.FileBaseName)] = JsonSerializer.Serialize(names)
                });
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static void Save(DataGridView grid)
        {
            if (_applying)
                return;
            if (grid.Tag is not ColumnSearch search ||
                string.IsNullOrWhiteSpace(search.FileBaseName) ||
                string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            var names = VisibleNames(grid);
            try
            {
                SqliteInventory.WritePrefs(new Dictionary<string, string>
                {
                    [CurrentKey(search.FileBaseName)] = JsonSerializer.Serialize(names)
                });
            }
            catch
            {
                // keep using the table even if settings cannot be written
            }
        }

        private static void ApplyNames(DataGridView grid, List<string> names)
        {
            _applying = true;
            try
            {
                var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    if (Theme.IsAddColumn(col))
                        continue;
                    col.Visible = wanted.Contains(Key(col)) || wanted.Contains(col.HeaderText);
                }

                int index = 0;
                foreach (var name in names)
                {
                    var col = Find(grid, name);
                    if (col == null)
                        continue;
                    col.Visible = true;
                    col.DisplayIndex = index++;
                }

                var add = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(Theme.IsAddColumn);
                if (add != null)
                    add.DisplayIndex = grid.Columns.Count - 1;
            }
            finally
            {
                _applying = false;
            }
        }

        public static void Attach(DataGridView grid)
        {
            grid.ColumnDisplayIndexChanged += (_, e) =>
            {
                if (e.Column == null || Theme.IsAddColumn(e.Column))
                    return;
                Save(grid);
            };
        }

        private static List<string> VisibleNames(DataGridView grid)
        {
            return grid.Columns.Cast<DataGridViewColumn>()
                .Where(col => col.Visible && !Theme.IsAddColumn(col))
                .OrderBy(col => col.DisplayIndex)
                .Select(Key)
                .Where(name => name.Length > 0)
                .ToList();
        }

        private static List<string> Load(string baseName)
        {
            try
            {
                var settings = SqliteInventory.ReadPrefs();
                if (!settings.TryGetValue(CurrentKey(baseName), out var json) ||
                    string.IsNullOrWhiteSpace(json))
                {
                    var shared = SqliteInventory.ReadPublicSettings();
                    if (!shared.TryGetValue(CurrentKey(baseName), out json) ||
                        string.IsNullOrWhiteSpace(json))
                        return new List<string>();
                }

                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static List<string> LoadDefault(string baseName)
        {
            try
            {
                var settings = SqliteInventory.ReadPrefs();
                if (!settings.TryGetValue(DefaultKey(baseName), out var json) ||
                    string.IsNullOrWhiteSpace(json))
                    return new List<string>();

                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            catch
            {
                return new List<string>();
            }
        }

        private static string CurrentKey(string baseName) => "grid_columns_" + baseName;

        private static string DefaultKey(string baseName) => "grid_columns_default_" + baseName;

        private static DataGridViewColumn? Find(DataGridView grid, string name)
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                if (Theme.IsAddColumn(col))
                    continue;
                if (Key(col).Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    col.HeaderText.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return col;
            }

            return null;
        }

        private static string Key(DataGridViewColumn col)
        {
            string tag = col.Tag as string ?? "";
            if (tag.Length > 0 && tag != Theme.AddColumnTag)
                return tag;
            return col.Name.Length > 0 ? col.Name : col.HeaderText;
        }
    }
}
