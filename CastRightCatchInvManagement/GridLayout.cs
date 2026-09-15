using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>Remembers which grid columns are shown, hidden, and in what order.</summary>
    internal static class GridLayout
    {
        private static bool _applying;

        /// <summary>Pause persistence while columns are being applied so display-index events do not rewrite prefs.</summary>
        public static void BeginUpdate() => _applying = true;

        /// <summary>Resume saving column layout after a bulk apply.</summary>
        public static void EndUpdate() => _applying = false;

        /// <summary>Restore this user's last visible columns and order for the table, if any.</summary>
        public static void Apply(DataGridView grid, string baseName)
        {
            // No table or folder means there is nothing to restore.
            if (string.IsNullOrWhiteSpace(baseName) ||
                string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return;

            var names = Load(baseName);
            // First visit uses the page's built-in summary columns.
            if (names.Count == 0)
                return;

            ApplyNames(grid, names);
        }

        /// <summary>Restore the saved default layout for this table and persist it as the current layout.</summary>
        public static bool ApplyDefault(DataGridView grid)
        {
            // The grid's ColumnSearch tag holds the table name used as the prefs key.
            if (grid.Tag is not ColumnSearch search ||
                string.IsNullOrWhiteSpace(search.FileBaseName))
                return false;

            var names = LoadDefault(search.FileBaseName);
            // No saved default: callers fall back to the page's summary columns.
            if (names.Count == 0)
                return false;

            ApplyNames(grid, names);
            Save(grid);
            return true;
        }

        /// <summary>Store the current visible columns as this user's default layout for the table.</summary>
        public static bool SaveDefault(DataGridView grid)
        {
            // Ignore events fired while ApplyNames is rearranging columns.
            if (_applying)
                return false;
            // Need a ColumnSearch tag, table name, and inventory folder to write the default layout.
            if (grid.Tag is not ColumnSearch search ||
                string.IsNullOrWhiteSpace(search.FileBaseName) ||
                string.IsNullOrWhiteSpace(AppState.InventoryFolder))
                return false;
            // Layout defaults belong to a signed-in user, not a locked kiosk.
            if (!AppState.SignedIn)
                return false;

            var names = VisibleNames(grid);
            // Do not overwrite a saved default with an empty column set.
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
            // Prefs write can fail on a locked or remote-down database; keep the grid usable.
            catch
            {
                return false;
            }
        }

        /// <summary>Persist the current visible columns and order for this user and table.</summary>
        public static void Save(DataGridView grid)
        {
            // Ignore events fired while ApplyNames is rearranging columns.
            if (_applying)
                return;
            // Need a ColumnSearch tag, table name, and inventory folder to persist the current layout.
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
            // Prefs write can fail on a locked or remote-down database; keep using the table.
            catch
            {
            }
        }

        /// <summary>Show only the named columns, in that order, leaving the add-column button last.</summary>
        private static void ApplyNames(DataGridView grid, List<string> names)
        {
            _applying = true;
            try
            {
                var wanted = new HashSet<string>(names, StringComparer.OrdinalIgnoreCase);
                foreach (DataGridViewColumn col in grid.Columns)
                {
                    // The trailing "+" column is not part of saved layouts.
                    if (Theme.IsAddColumn(col))
                        continue;
                    col.Visible = wanted.Contains(Key(col)) || wanted.Contains(col.HeaderText);
                }

                int index = 0;
                foreach (var name in names)
                {
                    var col = Find(grid, name);
                    // Saved names may refer to columns that this table no longer has.
                    if (col == null)
                        continue;
                    col.Visible = true;
                    col.DisplayIndex = index++;
                }

                var add = grid.Columns.Cast<DataGridViewColumn>().FirstOrDefault(Theme.IsAddColumn);
                // Keep the add-column button at the far right after restoring order.
                if (add != null)
                    add.DisplayIndex = grid.Columns.Count - 1;
            }
            finally
            {
                _applying = false;
            }
        }

        /// <summary>Save layout whenever the user drags a data column to a new position.</summary>
        public static void Attach(DataGridView grid)
        {
            grid.ColumnDisplayIndexChanged += (_, e) =>
            {
                // The add-column button is not a user layout column.
                if (e.Column == null || Theme.IsAddColumn(e.Column))
                    return;
                Save(grid);
            };
        }

        /// <summary>Visible data-column keys in display order, excluding the add-column button.</summary>
        private static List<string> VisibleNames(DataGridView grid)
        {
            return grid.Columns.Cast<DataGridViewColumn>()
                .Where(col => col.Visible && !Theme.IsAddColumn(col))
                .OrderBy(col => col.DisplayIndex)
                .Select(Key)
                .Where(name => name.Length > 0)
                .ToList();
        }

        /// <summary>Load this user's current layout, then the shared public layout if none is stored.</summary>
        private static List<string> Load(string baseName)
        {
            try
            {
                var settings = SqliteInventory.ReadPrefs();
                // Fall back to a folder-wide layout when this user has never arranged the grid.
                if (!settings.TryGetValue(CurrentKey(baseName), out var json) ||
                    string.IsNullOrWhiteSpace(json))
                {
                    var shared = SqliteInventory.ReadPublicSettings();
                    // Shared folder layout is also missing: first visit uses the page's summary columns.
                    if (!shared.TryGetValue(CurrentKey(baseName), out json) ||
                        string.IsNullOrWhiteSpace(json))
                        return new List<string>();
                }

                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            // Corrupt or unreadable prefs should not block opening the table.
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>Load this user's saved default column set, or empty if none exists.</summary>
        private static List<string> LoadDefault(string baseName)
        {
            try
            {
                var settings = SqliteInventory.ReadPrefs();
                // No saved default JSON for this user/table: callers fall back to summary columns.
                if (!settings.TryGetValue(DefaultKey(baseName), out var json) ||
                    string.IsNullOrWhiteSpace(json))
                    return new List<string>();

                return JsonSerializer.Deserialize<List<string>>(json) ?? new List<string>();
            }
            // Corrupt default JSON is treated as "no default saved".
            catch
            {
                return new List<string>();
            }
        }

        /// <summary>Prefs key for this user's last visible columns on the table.</summary>
        private static string CurrentKey(string baseName) => "grid_columns_" + baseName;

        /// <summary>Prefs key for this user's saved default column set on the table.</summary>
        private static string DefaultKey(string baseName) => "grid_columns_default_" + baseName;

        /// <summary>Find a data column by stored key or header text, skipping the add-column button.</summary>
        private static DataGridViewColumn? Find(DataGridView grid, string name)
        {
            foreach (DataGridViewColumn col in grid.Columns)
            {
                // The add-column button is never a layout target.
                if (Theme.IsAddColumn(col))
                    continue;
                // Match the stored key (file header / Name) or the visible HeaderText.
                if (Key(col).Equals(name, StringComparison.OrdinalIgnoreCase) ||
                    col.HeaderText.Equals(name, StringComparison.OrdinalIgnoreCase))
                    return col;
            }

            return null;
        }

        /// <summary>Stable column id: tag (file header), else Name, else HeaderText.</summary>
        private static string Key(DataGridViewColumn col)
        {
            string tag = col.Tag as string ?? "";
            // Tag holds the file header; skip the add-column sentinel.
            if (tag.Length > 0 && tag != Theme.AddColumnTag)
                return tag;
            return col.Name.Length > 0 ? col.Name : col.HeaderText;
        }
    }
}
