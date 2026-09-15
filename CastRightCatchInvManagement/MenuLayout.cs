using System.Text.Json;
using System.Text.Json.Serialization;

namespace CastRightCatchInvManagement
{
    /// <summary>One sidebar folder or page, including nested children.</summary>
    internal sealed class MenuNode
    {
        public string Kind { get; set; } = "page";
        public string Name { get; set; } = "";
        public string Key { get; set; } = "";
        public bool On { get; set; } = true;
        public List<MenuNode> Children { get; set; } = new();

        [JsonIgnore]
        public bool IsFolder =>
            Kind.Equals("folder", StringComparison.OrdinalIgnoreCase);

        [JsonIgnore]
        public string Title =>
            !string.IsNullOrWhiteSpace(Name)
                ? Name.Trim()
                : IsFolder ? "Folder" : MenuLayout.CatalogLabel(Key);

        /// <summary>Named dropdown that can hold pages or nested folders.</summary>
        public static MenuNode Folder(string name, bool on = true) =>
            new() { Kind = "folder", Name = (name ?? "").Trim(), On = on, Children = new() };

        /// <summary>Leaf that opens a catalog page by key.</summary>
        public static MenuNode Page(string key, bool on = true) =>
            new() { Kind = "page", Key = key, On = on, Children = new() };
    }

    /// <summary>Legacy group JSON item: a page key or a nested group name.</summary>
    internal sealed class MenuPageItem
    {
        public string Key { get; set; } = "";
        public string Nested { get; set; } = "";
        public bool On { get; set; } = true;
        public bool IsNested => !string.IsNullOrWhiteSpace(Nested);
    }

    /// <summary>Legacy sidebar group from older menu_layout JSON.</summary>
    internal sealed class MenuGroup
    {
        public string Name { get; set; } = "";
        public bool On { get; set; } = true;
        public List<MenuPageItem> Pages { get; set; } = new();
    }

    /// <summary>
    /// Sidebar tree: folders (dropdowns) and pages. Old group JSON is migrated on load.
    /// </summary>
    internal sealed class MenuLayout
    {
        public const string SettingKey = "menu_layout";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            PropertyNameCaseInsensitive = true,
            WriteIndented = false,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };

        public static int Revision { get; private set; }

        private static readonly Dictionary<string, string> CustomLabels = new(StringComparer.OrdinalIgnoreCase);

        public List<MenuNode> Root { get; set; } = new();
        public List<MenuGroup> Groups { get; set; } = new();
        public Dictionary<string, bool> Standalone { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static readonly (string Key, string Label, AppPage Page)[] Catalog =
        {
            ("PurchaseSales", "Purchases", AppPage.PurchaseSales),
            ("AddPurchase", "New Purchase", AppPage.AddPurchase),
            ("Sales", "Sales", AppPage.Sales),
            ("SalesOrder", "New Sale", AppPage.SalesOrder),
            ("Customers", "Customers", AppPage.Customers),
            ("Vendors", "Vendors", AppPage.Vendors),
            ("ItemCodes", "Inventory", AppPage.ItemCodes),
            ("Invoicing", "Invoices", AppPage.Invoicing),
            ("InvoicePdf", "Create Invoice", AppPage.InvoicePdf),
            ("Debits", "Debits", AppPage.Debits),
            ("Credits", "Credits", AppPage.Credits),
            ("Banking", "Banking", AppPage.Banking),
            ("Reports", "Reports", AppPage.Reports),
            ("PendingChanges", "Review", AppPage.PendingChanges)
        };

        /// <summary>Pages that open a dedicated form instead of the nested workspace host.</summary>
        public static Action? OpenAction(AppPage page) => page switch
        {
            // New Purchase / New Sale are standalone windows, not nested pages.
            AppPage.AddPurchase => AddPurchase.OpenNew,
            AppPage.SalesOrder => SalesOrder.OpenNew,
            _ => null
        };

        /// <summary>Default sidebar label for a catalog key, or the raw key if unknown.</summary>
        public static string CatalogLabel(string key) =>
            Catalog.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Label
            is { Length: > 0 } label
                ? label
                : key;

        /// <summary>Admin-renamed label when set; otherwise the catalog default.</summary>
        public static string Label(string key)
        {
            // Admin renamed this page in the menu editor.
            if (CustomLabels.TryGetValue(key, out var custom) && custom.Length > 0)
                return custom;
            return CatalogLabel(key);
        }

        /// <summary>Sidebar title for a page, or empty when the page is not in the catalog.</summary>
        public static string LabelFor(AppPage page)
        {
            var hit = Catalog.FirstOrDefault(item => item.Page == page);
            return string.IsNullOrEmpty(hit.Key) ? "" : Label(hit.Key);
        }

        /// <summary>Map a catalog key to its AppPage. Unknown keys fail rather than guess.</summary>
        public static bool TryPage(string key, out AppPage page)
        {
            var hit = Catalog.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            // Unknown keys must not navigate to a real page.
            if (string.IsNullOrEmpty(hit.Key))
            {
                page = AppPage.Dashboard;
                return false;
            }

            page = hit.Page;
            return true;
        }

        /// <summary>Load the shared menu tree, or seed and save a default if JSON is missing or bad.</summary>
        public static MenuLayout Load()
        {
            var map = SqliteInventory.ReadPublicSettings();
            // Shared DB already has a saved tree.
            if (map.TryGetValue(SettingKey, out var json) && !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<MenuLayout>(json, JsonOptions);
                    // Null means the JSON was empty or the wrong shape.
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
                }
                // Bad JSON: seed a default tree instead of crashing the sidebar.
                catch
                {
                }
            }

            var seed = Seed();
            seed.Save(bump: false);
            return seed;
        }

        /// <summary>Factory default: Purchases, Sales, and Invoices folders plus remaining catalog pages.</summary>
        public static MenuLayout Seed()
        {
            var layout = new MenuLayout
            {
                Root =
                {
                    FolderWith("Purchases", "PurchaseSales", "AddPurchase"),
                    FolderWith("Sales", "Sales", "SalesOrder"),
                    FolderWith("Invoices", "Invoicing", "InvoicePdf")
                }
            };
            layout.Normalize();
            return layout;
        }

        /// <summary>Folder whose children are catalog pages in the given order.</summary>
        private static MenuNode FolderWith(string name, params string[] keys)
        {
            var folder = MenuNode.Folder(name);
            foreach (var key in keys)
                folder.Children.Add(MenuNode.Page(key));
            return folder;
        }

        /// <summary>Write the tree to the shared database. bump notifies sidebars to rebuild.</summary>
        public void Save(bool bump = true)
        {
            Normalize();
            Groups = new List<MenuGroup>();
            Standalone = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            SqliteInventory.WriteSettings(new Dictionary<string, string>
            {
                [SettingKey] = JsonSerializer.Serialize(this, JsonOptions)
            });
            // Load's first seed save should not force every sidebar to rebuild.
            if (bump)
                Revision++;
        }

        /// <summary>Catalog keys already placed in the tree (so Normalize can append the rest).</summary>
        public HashSet<string> AssignedKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Walk(Root, node =>
            {
                // Folders have display names, not catalog keys.
                if (!node.IsFolder && node.Key.Length > 0)
                    keys.Add(node.Key);
            });
            return keys;
        }

        /// <summary>Depth-first visit of every node, including nested folders.</summary>
        public static void Walk(IEnumerable<MenuNode> nodes, Action<MenuNode> visit)
        {
            foreach (var node in nodes)
            {
                visit(node);
                // Nested folders must be visited so custom labels and keys are complete.
                if (node.Children.Count > 0)
                    Walk(node.Children, visit);
            }
        }

        /// <summary>Find the sibling list and index of a node, or (-1) if it is not in the tree.</summary>
        public (List<MenuNode> Siblings, int Index) Locate(MenuNode target)
        {
            var found = Locate(Root, target);
            return found ?? (Root, -1);
        }

        /// <summary>Search this sibling list, then nested folders, for the target node.</summary>
        private static (List<MenuNode> Siblings, int Index)? Locate(List<MenuNode> siblings, MenuNode target)
        {
            int index = siblings.IndexOf(target);
            // Direct child of this list — stop walking.
            if (index >= 0)
                return (siblings, index);
            foreach (var node in siblings)
            {
                var nested = Locate(node.Children, target);
                // Found under this folder; stop walking siblings.
                if (nested != null)
                    return nested;
            }

            return null;
        }

        /// <summary>True when node sits anywhere under ancestor (blocks dropping a folder into itself).</summary>
        public bool IsDescendant(MenuNode ancestor, MenuNode node)
        {
            foreach (var child in ancestor.Children)
            {
                // Match this child or anything nested under it.
                if (child == node || IsDescendant(child, node))
                    return true;
            }

            return false;
        }

        /// <summary>How many folders wrap this node; used to cap nested dropdowns at two levels.</summary>
        public int DepthOf(MenuNode node)
        {
            int depth = 0;
            var current = node;
            while (true)
            {
                var parent = ParentOf(current);
                // Reached a root item.
                if (parent == null)
                    return depth;
                depth++;
                current = parent;
            }
        }

        /// <summary>Immediate parent folder, or null for a root item.</summary>
        public MenuNode? ParentOf(MenuNode node)
        {
            MenuNode? found = null;
            Walk(Root, candidate =>
            {
                // First match wins; a node has only one parent.
                if (found == null && candidate.Children.Contains(node))
                    found = candidate;
            });
            return found;
        }

        /// <summary>Toggle a node; folders cascade to children, and turning a page on also turns ancestors on.</summary>
        public void SetOn(MenuNode node, bool on)
        {
            node.On = on;
            // Hiding a folder must hide every page inside it.
            if (node.IsFolder)
            {
                foreach (var child in node.Children)
                    SetOn(child, on);
            }
            // A visible page is unreachable if its parent folder stays off.
            else if (on)
            {
                var parent = ParentOf(node);
                while (parent != null)
                {
                    parent.On = true;
                    parent = ParentOf(parent);
                }
            }
        }

        /// <summary>Move a node to dest at index, adjusting the index when it stays in the same list.</summary>
        public void Move(MenuNode source, List<MenuNode> dest, int index)
        {
            var from = Locate(source);
            // Source is not in this layout (stale drag).
            if (from.Index < 0)
                return;
            from.Siblings.RemoveAt(from.Index);
            // Removing an earlier sibling shifts dest indices down by one.
            if (ReferenceEquals(from.Siblings, dest) && from.Index < index)
                index--;
            index = Math.Clamp(index, 0, dest.Count);
            dest.Insert(index, source);
        }

        /// <summary>Replace a node with a new folder that contains it.</summary>
        public MenuNode WrapInFolder(MenuNode target, string folderName)
        {
            var loc = Locate(target);
            // Stale node from a previous layout instance.
            if (loc.Index < 0)
                return target;
            var folder = MenuNode.Folder(folderName, target.On);
            loc.Siblings[loc.Index] = folder;
            folder.Children.Add(target);
            return folder;
        }

        /// <summary>Migrate old groups, fix kinds, rename AddSale, and append missing catalog pages.</summary>
        private void Normalize()
        {
            Root ??= new List<MenuNode>();
            Groups ??= new List<MenuGroup>();
            Standalone ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            // Older JSON stored Groups instead of a tree.
            if (Root.Count == 0 && Groups.Count > 0)
                MigrateFromGroups();

            Walk(Root, node =>
            {
                node.Children ??= new List<MenuNode>();
                node.Kind = node.IsFolder || node.Children.Count > 0 ? "folder" : "page";
                node.Name = (node.Name ?? "").Trim();
            });

            IndexLabels();

            Walk(Root, node =>
            {
                // AddSale was renamed to SalesOrder; keep saved trees working.
                if (!node.IsFolder && node.Key.Equals("AddSale", StringComparison.OrdinalIgnoreCase))
                    node.Key = "SalesOrder";
            });
            DedupePages(Root);

            EnsureCompanions("Purchases", "PurchaseSales", "AddPurchase");
            EnsureCompanions("Sales", "Sales", "SalesOrder");
            EnsureCompanions("Invoices", "Invoicing", "InvoicePdf");

            var used = AssignedKeys();
            foreach (var item in Catalog)
            {
                // Already placed in a folder or as a root page.
                if (used.Contains(item.Key))
                    continue;
                bool on = !Standalone.TryGetValue(item.Key, out bool stored) || stored;
                Root.Add(MenuNode.Page(item.Key, on));
            }
        }

        /// <summary>Convert legacy Groups into Root folders, skipping groups that were nested under others.</summary>
        private void MigrateFromGroups()
        {
            var nested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups)
            {
                foreach (var page in group.Pages)
                {
                    // Nested names are groups that should not also become roots.
                    if (page.IsNested)
                        nested.Add(page.Nested.Trim());
                }
            }

            foreach (var group in Groups)
            {
                // Nested groups are added from their parent, not as extra roots.
                if (nested.Contains(group.Name))
                    continue;
                Root.Add(FromGroup(group, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            }
        }

        /// <summary>Build a folder from a legacy group, guarding against circular nested names.</summary>
        private MenuNode FromGroup(MenuGroup group, HashSet<string> stack)
        {
            var folder = MenuNode.Folder(group.Name, group.On);
            // Cycle in Nested names would recurse forever.
            if (!stack.Add(group.Name))
                return folder;
            foreach (var page in group.Pages)
            {
                // Nested name points at another group, not a catalog page.
                if (page.IsNested)
                {
                    var nested = Groups.FirstOrDefault(item =>
                        item.Name.Equals(page.Nested, StringComparison.OrdinalIgnoreCase));
                    // Stale nested name after a group was deleted.
                    if (nested != null)
                        folder.Children.Add(FromGroup(nested, stack));
                    continue;
                }

                // Drop keys that are no longer in the catalog.
                if (TryPage(page.Key, out _))
                    folder.Children.Add(MenuNode.Page(page.Key, page.On && group.On));
            }

            stack.Remove(group.Name);
            return folder;
        }

        /// <summary>Cache Admin-custom page titles so Label() can resolve without walking the tree.</summary>
        private void IndexLabels()
        {
            CustomLabels.Clear();
            Walk(Root, node =>
            {
                // Folders and unnamed pages keep the catalog default.
                if (node.IsFolder || node.Key.Length == 0 || node.Name.Length == 0)
                    return;
                CustomLabels[node.Key] = node.Name;
            });
        }

        /// <summary>If a well-known folder exists, make sure its companion pages sit inside it.</summary>
        private void EnsureCompanions(string folderName, params string[] keys)
        {
            MenuNode? folder = null;
            Walk(Root, node =>
            {
                // First folder with this display name is the companion host.
                if (folder == null &&
                    node.IsFolder &&
                    node.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                    folder = node;
            });
            // Admin removed the folder; do not recreate it.
            if (folder == null)
                return;
            foreach (var key in keys)
            {
                bool present = false;
                Walk(Root, node =>
                {
                    // This companion page already exists somewhere in the tree.
                    if (!node.IsFolder && node.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                        present = true;
                });
                // Companion may already sit at root or in another folder.
                if (!present)
                    folder.Children.Add(MenuNode.Page(key, folder.On));
            }
        }

        /// <summary>Keep the first occurrence of each page key; extra copies from old JSON are dropped.</summary>
        private static void DedupePages(List<MenuNode> nodes)
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            for (int i = nodes.Count - 1; i >= 0; i--)
            {
                var node = nodes[i];
                // Folders are unique by instance; recurse so nested pages are deduped too.
                if (node.IsFolder)
                {
                    DedupePages(node.Children);
                    continue;
                }

                // First copy of this key stays; later copies came from old JSON.
                if (node.Key.Length == 0 || seen.Add(node.Key))
                    continue;
                nodes.RemoveAt(i);
            }
        }
    }
}
