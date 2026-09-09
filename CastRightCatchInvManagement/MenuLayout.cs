using System.Text.Json;
using System.Text.Json.Serialization;

namespace CastRightCatchInvManagement
{
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
            IsFolder ? (string.IsNullOrWhiteSpace(Name) ? "Folder" : Name) : MenuLayout.Label(Key);

        public static MenuNode Folder(string name, bool on = true) =>
            new() { Kind = "folder", Name = (name ?? "").Trim(), On = on, Children = new() };

        public static MenuNode Page(string key, bool on = true) =>
            new() { Kind = "page", Key = key, On = on, Children = new() };
    }

    internal sealed class MenuPageItem
    {
        public string Key { get; set; } = "";
        public string Nested { get; set; } = "";
        public bool On { get; set; } = true;
        public bool IsNested => !string.IsNullOrWhiteSpace(Nested);
    }

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

        public List<MenuNode> Root { get; set; } = new();
        public List<MenuGroup> Groups { get; set; } = new();
        public Dictionary<string, bool> Standalone { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static readonly (string Key, string Label, AppPage Page)[] Catalog =
        {
            ("PurchaseSales", "Purchases", AppPage.PurchaseSales),
            ("AddPurchase", "New Purchase", AppPage.AddPurchase),
            ("Sales", "Sales", AppPage.Sales),
            ("AddSale", "Sales Form", AppPage.AddSale),
            ("SalesOrder", "Create Sales Order", AppPage.SalesOrder),
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

        public static Action? OpenAction(AppPage page) => page switch
        {
            AppPage.AddPurchase => AddPurchase.OpenNew,
            AppPage.AddSale => AddSale.OpenNew,
            _ => null
        };

        public static string Label(string key) =>
            Catalog.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Label
            is { Length: > 0 } label
                ? label
                : key;

        public static bool TryPage(string key, out AppPage page)
        {
            var hit = Catalog.FirstOrDefault(item => item.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
            if (string.IsNullOrEmpty(hit.Key))
            {
                page = AppPage.Dashboard;
                return false;
            }

            page = hit.Page;
            return true;
        }

        public static MenuLayout Load()
        {
            var map = SqliteInventory.ReadPublicSettings();
            if (map.TryGetValue(SettingKey, out var json) && !string.IsNullOrWhiteSpace(json))
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<MenuLayout>(json, JsonOptions);
                    if (loaded != null)
                    {
                        loaded.Normalize();
                        return loaded;
                    }
                }
                catch
                {
                    // fall through to seed
                }
            }

            var seed = Seed();
            seed.Save(bump: false);
            return seed;
        }

        public static MenuLayout Seed()
        {
            var layout = new MenuLayout
            {
                Root =
                {
                    FolderWith("Purchases", "PurchaseSales", "AddPurchase"),
                    FolderWith("Sales", "Sales", "AddSale", "SalesOrder"),
                    FolderWith("Invoices", "Invoicing", "InvoicePdf")
                }
            };
            layout.Normalize();
            return layout;
        }

        private static MenuNode FolderWith(string name, params string[] keys)
        {
            var folder = MenuNode.Folder(name);
            foreach (var key in keys)
                folder.Children.Add(MenuNode.Page(key));
            return folder;
        }

        public void Save(bool bump = true)
        {
            Normalize();
            Groups = new List<MenuGroup>();
            Standalone = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            SqliteInventory.WriteSettings(new Dictionary<string, string>
            {
                [SettingKey] = JsonSerializer.Serialize(this, JsonOptions)
            });
            if (bump)
                Revision++;
        }

        public HashSet<string> AssignedKeys()
        {
            var keys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            Walk(Root, node =>
            {
                if (!node.IsFolder && node.Key.Length > 0)
                    keys.Add(node.Key);
            });
            return keys;
        }

        public static void Walk(IEnumerable<MenuNode> nodes, Action<MenuNode> visit)
        {
            foreach (var node in nodes)
            {
                visit(node);
                if (node.Children.Count > 0)
                    Walk(node.Children, visit);
            }
        }

        public (List<MenuNode> Siblings, int Index) Locate(MenuNode target)
        {
            var found = Locate(Root, target);
            return found ?? (Root, -1);
        }

        private static (List<MenuNode> Siblings, int Index)? Locate(List<MenuNode> siblings, MenuNode target)
        {
            int index = siblings.IndexOf(target);
            if (index >= 0)
                return (siblings, index);
            foreach (var node in siblings)
            {
                var nested = Locate(node.Children, target);
                if (nested != null)
                    return nested;
            }

            return null;
        }

        public bool IsDescendant(MenuNode ancestor, MenuNode node)
        {
            foreach (var child in ancestor.Children)
            {
                if (child == node || IsDescendant(child, node))
                    return true;
            }

            return false;
        }

        public int DepthOf(MenuNode node)
        {
            int depth = 0;
            var current = node;
            while (true)
            {
                var parent = ParentOf(current);
                if (parent == null)
                    return depth;
                depth++;
                current = parent;
            }
        }

        public MenuNode? ParentOf(MenuNode node)
        {
            MenuNode? found = null;
            Walk(Root, candidate =>
            {
                if (found == null && candidate.Children.Contains(node))
                    found = candidate;
            });
            return found;
        }

        public void SetOn(MenuNode node, bool on)
        {
            node.On = on;
            if (node.IsFolder)
            {
                foreach (var child in node.Children)
                    SetOn(child, on);
            }
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

        public void Move(MenuNode source, List<MenuNode> dest, int index)
        {
            var from = Locate(source);
            if (from.Index < 0)
                return;
            from.Siblings.RemoveAt(from.Index);
            if (ReferenceEquals(from.Siblings, dest) && from.Index < index)
                index--;
            index = Math.Clamp(index, 0, dest.Count);
            dest.Insert(index, source);
        }

        public MenuNode WrapInFolder(MenuNode target, string folderName)
        {
            var loc = Locate(target);
            if (loc.Index < 0)
                return target;
            var folder = MenuNode.Folder(folderName, target.On);
            loc.Siblings[loc.Index] = folder;
            folder.Children.Add(target);
            return folder;
        }

        private void Normalize()
        {
            Root ??= new List<MenuNode>();
            Groups ??= new List<MenuGroup>();
            Standalone ??= new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            if (Root.Count == 0 && Groups.Count > 0)
                MigrateFromGroups();

            Walk(Root, node =>
            {
                node.Children ??= new List<MenuNode>();
                node.Kind = node.IsFolder || node.Children.Count > 0 ? "folder" : "page";
                if (node.IsFolder)
                    node.Name = (node.Name ?? "").Trim();
            });

            EnsureCompanions("Purchases", "PurchaseSales", "AddPurchase");
            EnsureCompanions("Sales", "Sales", "AddSale", "SalesOrder");
            EnsureCompanions("Invoices", "Invoicing", "InvoicePdf");

            var used = AssignedKeys();
            foreach (var item in Catalog)
            {
                if (used.Contains(item.Key))
                    continue;
                bool on = !Standalone.TryGetValue(item.Key, out bool stored) || stored;
                Root.Add(MenuNode.Page(item.Key, on));
            }
        }

        private void MigrateFromGroups()
        {
            var nested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (var group in Groups)
            {
                foreach (var page in group.Pages)
                {
                    if (page.IsNested)
                        nested.Add(page.Nested.Trim());
                }
            }

            foreach (var group in Groups)
            {
                if (nested.Contains(group.Name))
                    continue;
                Root.Add(FromGroup(group, new HashSet<string>(StringComparer.OrdinalIgnoreCase)));
            }
        }

        private MenuNode FromGroup(MenuGroup group, HashSet<string> stack)
        {
            var folder = MenuNode.Folder(group.Name, group.On);
            if (!stack.Add(group.Name))
                return folder;
            foreach (var page in group.Pages)
            {
                if (page.IsNested)
                {
                    var nested = Groups.FirstOrDefault(item =>
                        item.Name.Equals(page.Nested, StringComparison.OrdinalIgnoreCase));
                    if (nested != null)
                        folder.Children.Add(FromGroup(nested, stack));
                    continue;
                }

                if (TryPage(page.Key, out _))
                    folder.Children.Add(MenuNode.Page(page.Key, page.On && group.On));
            }

            stack.Remove(group.Name);
            return folder;
        }

        private void EnsureCompanions(string folderName, params string[] keys)
        {
            MenuNode? folder = null;
            Walk(Root, node =>
            {
                if (folder == null &&
                    node.IsFolder &&
                    node.Name.Equals(folderName, StringComparison.OrdinalIgnoreCase))
                    folder = node;
            });
            if (folder == null)
                return;
            foreach (var key in keys)
            {
                bool present = false;
                Walk(Root, node =>
                {
                    if (!node.IsFolder && node.Key.Equals(key, StringComparison.OrdinalIgnoreCase))
                        present = true;
                });
                if (!present)
                    folder.Children.Add(MenuNode.Page(key, folder.On));
            }
        }
    }
}
