using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>One reusable type-group used by lookup fields (Forwarder, Logistics, and later slots).</summary>
    internal sealed class VendorTypeFilter
    {
        public string Id { get; set; } = "";
        public string Name { get; set; } = "";
        public List<string> Types { get; set; } = new();

        /// <summary>List boxes show the filter name, falling back to the stored id.</summary>
        public override string ToString() => string.IsNullOrWhiteSpace(Name) ? Id : Name;
    }

    /// <summary>Saved vendor Type names, named filters, and which filter each form field uses.</summary>
    internal sealed class VendorTypeCatalog
    {
        public List<string> Types { get; set; } = new();
        public List<VendorTypeFilter> Filters { get; set; } = new();
        public Dictionary<string, string> Slots { get; set; } = new(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Vendor Type dropdown plus reusable lookup filters.
    /// A filter is a set of types (Logistics can include Trucking). Form fields pick a filter by slot.
    /// </summary>
    internal static class VendorTypes
    {
        public const string SettingKey = "vendor_types";
        public const string Forwarder = "Forwarder";
        public const string Logistics = "Logistics";
        public const string SlotPurchaseForwarder = "purchase.forwarder";
        public const string SlotPurchaseLogistics = "purchase.logistics";

        public static readonly (string Slot, string Label, string DefaultFilterId)[] Slots =
        {
            (SlotPurchaseForwarder, "New Purchase · Forwarder", "forwarder"),
            (SlotPurchaseLogistics, "New Purchase · Logistics", "logistics")
        };

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true
        };

        private static VendorTypeCatalog? _cache;

        /// <summary>Copy of the Type names used by the Edit Vendor dropdown.</summary>
        public static List<string> Load() => Catalog().Types.ToList();

        /// <summary>Shared catalog from settings, cached until the next write.</summary>
        public static VendorTypeCatalog Catalog()
        {
            // Avoid re-reading JSON on every lookup field paint.
            if (_cache != null)
                return _cache;
            _cache = Read();
            return _cache;
        }

        /// <summary>Replace Type names and drop filter members that no longer exist.</summary>
        public static void SaveTypes(IEnumerable<string> types)
        {
            var catalog = Catalog();
            catalog.Types = DistinctNames(types);
            foreach (var filter in catalog.Filters)
                filter.Types = filter.Types.Where(type => ContainsType(catalog.Types, type)).ToList();
            Write(catalog);
        }

        /// <summary>Persist a full catalog after Admin edits types, filters, or slots.</summary>
        public static void SaveCatalog(VendorTypeCatalog catalog) => Write(Normalize(catalog));

        /// <summary>True when this vendor's Type is in the filter assigned to the form slot.</summary>
        public static bool MatchesSlot(Dictionary<string, string> record, string slot)
        {
            var filter = FilterForSlot(slot);
            // Empty or unassigned filters match nobody so lookups stay blank.
            if (filter == null || filter.Types.Count == 0)
                return false;
            string type = DataFiles.GetRecord(record, "Type");
            return filter.Types.Any(item => item.Equals(type, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>The named type-group bound to a form field, or null if the slot is unset.</summary>
        public static VendorTypeFilter? FilterForSlot(string slot)
        {
            var catalog = Catalog();
            // Slot has no filter id yet — the field should not guess a group.
            if (!catalog.Slots.TryGetValue(slot, out var id) || string.IsNullOrWhiteSpace(id))
                return null;
            return catalog.Filters.FirstOrDefault(filter =>
                filter.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Short unique id for a new lookup filter.</summary>
        public static string NewFilterId() => Guid.NewGuid().ToString("N")[..10];

        /// <summary>Rename a Type everywhere it is listed so filters stay in sync.</summary>
        public static void RenameType(string from, string to)
        {
            from = (from ?? "").Trim();
            to = (to ?? "").Trim();
            // Blank names would wipe types from the dropdown.
            if (from.Length == 0 || to.Length == 0)
                return;
            var catalog = Catalog();
            for (int i = 0; i < catalog.Types.Count; i++)
            {
                // Keep list order; only replace the matching name.
                if (catalog.Types[i].Equals(from, StringComparison.OrdinalIgnoreCase))
                    catalog.Types[i] = to;
            }

            foreach (var filter in catalog.Filters)
            {
                for (int i = 0; i < filter.Types.Count; i++)
                {
                    // Filters store type names, so they must follow the rename.
                    if (filter.Types[i].Equals(from, StringComparison.OrdinalIgnoreCase))
                        filter.Types[i] = to;
                }
            }

            Write(catalog);
        }

        /// <summary>Load catalog JSON, migrating the old name-only array if needed.</summary>
        private static VendorTypeCatalog Read()
        {
            var map = SqliteInventory.ReadPublicSettings();
            // First run: Forwarder and Logistics with matching default filters.
            if (!map.TryGetValue(SettingKey, out var json) || string.IsNullOrWhiteSpace(json))
                return Seed();

            json = json.Trim();
            // Settings JSON can be an old name array or the full catalog object.
            try
            {
                // Older builds stored only a string array of type names.
                if (json.StartsWith('['))
                {
                    var names = JsonSerializer.Deserialize<List<string>>(json, JsonOptions);
                    return Seed(names);
                }

                var loaded = JsonSerializer.Deserialize<VendorTypeCatalog>(json, JsonOptions);
                return loaded == null ? Seed() : Normalize(loaded);
            }
            // Corrupt settings must not block vendor forms.
            catch
            {
                return Seed();
            }
        }

        /// <summary>Default Forwarder/Logistics types, filters, and purchase slots.</summary>
        private static VendorTypeCatalog Seed(IEnumerable<string>? extra = null)
        {
            var catalog = new VendorTypeCatalog
            {
                Types = DistinctNames(new[] { Forwarder, Logistics }.Concat(extra ?? Array.Empty<string>())),
                Filters =
                {
                    new VendorTypeFilter { Id = "forwarder", Name = Forwarder, Types = { Forwarder } },
                    new VendorTypeFilter { Id = "logistics", Name = Logistics, Types = { Logistics } }
                }
            };
            foreach (var slot in Slots)
                catalog.Slots[slot.Slot] = slot.DefaultFilterId;
            return catalog;
        }

        /// <summary>Fill missing built-in filters and repair empty slot assignments.</summary>
        private static VendorTypeCatalog Normalize(VendorTypeCatalog catalog)
        {
            catalog.Types = DistinctNames(catalog.Types);
            catalog.Filters ??= new List<VendorTypeFilter>();
            catalog.Slots ??= new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

            // Keep the original Forwarder filter even if it was deleted from JSON.
            if (!catalog.Filters.Any(filter => filter.Id.Equals("forwarder", StringComparison.OrdinalIgnoreCase)))
                catalog.Filters.Insert(0, new VendorTypeFilter { Id = "forwarder", Name = Forwarder, Types = { Forwarder } });
            // Same for Logistics so purchase lookups always have a group.
            if (!catalog.Filters.Any(filter => filter.Id.Equals("logistics", StringComparison.OrdinalIgnoreCase)))
                catalog.Filters.Add(new VendorTypeFilter { Id = "logistics", Name = Logistics, Types = { Logistics } });

            foreach (var filter in catalog.Filters)
            {
                filter.Id = string.IsNullOrWhiteSpace(filter.Id) ? NewFilterId() : filter.Id.Trim();
                filter.Name = string.IsNullOrWhiteSpace(filter.Name) ? filter.Id : filter.Name.Trim();
                filter.Types = DistinctNames(filter.Types).Where(type => ContainsType(catalog.Types, type)).ToList();
            }

            foreach (var slot in Slots)
            {
                // Point the form field at the default filter when the stored id is gone.
                if (!catalog.Slots.TryGetValue(slot.Slot, out var id) ||
                    string.IsNullOrWhiteSpace(id) ||
                    catalog.Filters.All(filter => !filter.Id.Equals(id, StringComparison.OrdinalIgnoreCase)))
                    catalog.Slots[slot.Slot] = slot.DefaultFilterId;
            }

            return catalog;
        }

        /// <summary>Write the catalog to shared settings and refresh the in-memory cache.</summary>
        private static void Write(VendorTypeCatalog catalog)
        {
            catalog = Normalize(catalog);
            SqliteInventory.WriteSettings(new Dictionary<string, string>
            {
                [SettingKey] = JsonSerializer.Serialize(catalog, JsonOptions)
            });
            _cache = catalog;
        }

        /// <summary>Case-insensitive membership check for Type names.</summary>
        private static bool ContainsType(List<string> types, string name) =>
            types.Any(item => item.Equals(name, StringComparison.OrdinalIgnoreCase));

        /// <summary>Trim, drop blanks, and keep the first spelling of each Type name.</summary>
        private static List<string> DistinctNames(IEnumerable<string>? values)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
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
