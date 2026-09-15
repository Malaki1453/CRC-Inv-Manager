using System.Drawing.Drawing2D;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Admin page: User management (IT and admins) and Admin management (admins only).
    /// </summary>
    internal sealed class AdminSettings : Form, INavigationPage
    {
        private TabControl _tabs = null!;
        private TabPage? _adminTab;
        private DataGridView _grid = null!;
        private DataGridView _groupsGrid = null!;
        private MenuHintTree _menuTree = null!;
        private bool _menuLoading;
        private MenuLayout _menu = new();
        private TreeNode? _menuDrag;
        private TreeNode? _menuDropTarget;
        private MenuDropKind _menuDropKind;

        /// <summary>Where a dragged sidebar item will land relative to the hover row.</summary>
        private enum MenuDropKind
        {
            None,
            Before,
            After,
            Inside
        }
        private ListBox _admins = null!;
        private ListBox _it = null!;
        private CrcToggleSwitch _stayToggle = null!;
        private ComboBox _sessionDays = null!;
        private ComboBox _idleHours = null!;
        private CardPanel _bankCard = null!;
        private Panel _keysPanel = null!;
        private Button _keysToggle = null!;
        private TextBox _plaidId = null!;
        private TextBox _plaidSecret = null!;
        private ComboBox _plaidEnv = null!;
        private ComboBox _plaidSync = null!;
        private TextBox _smtpHost = null!;
        private TextBox _smtpPort = null!;
        private TextBox _smtpUser = null!;
        private TextBox _smtpPassword = null!;
        private Button _smtpShow = null!;
        private Button _smtpSave = null!;
        private Label _smtpPassHint = null!;
        private bool _smtpPasswordFresh;
        private bool _loadingMail;
        private bool _smtpDirty;
        private ListBox _vendorTypes = null!;
        private ListBox _vendorFilters = null!;
        private CheckedListBox _vendorFilterTypes = null!;
        private ComboBox _slotForwarder = null!;
        private ComboBox _slotLogistics = null!;
        private bool _loadingVendorLookup;

        /// <summary>Register this page and build User, Groups, Pages, and Admin tabs.</summary>
        public AdminSettings()
        {
            Navigator.Register(AppPage.Admin, this);
            BuildUi();
        }

        /// <summary>IT and admins. Reloads users, roles, and admin-only settings.</summary>
        public void HighlightCurrentPage()
        {
            // Regular users have no Admin page; send them to Settings.
            if (!AppState.IsAdmin && !AppState.IsIt)
            {
                Navigator.GoTo(AppPage.Settings);
                return;
            }

            ApplyTabs();
            LoadUsers();
            LoadGroups();
            LoadPages();
            LoadRoles();
            // Administrator-only settings and menu items.
            if (AppState.IsAdmin)
            {
                LoadBankFeed();
                LoadSession();
                LoadVendorTypes();
                // Leaving the tab would lose unsaved SMTP edits.
                if (_smtpDirty)
                    SaveMail();
                LoadMail();
            }
        }

        /// <summary>Create the tab control and load users, groups, pages, roles, and admin settings.</summary>
        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = Theme.BodyBold,
                Padding = new Point(16, 6),
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(140, 32),
                DrawMode = TabDrawMode.OwnerDrawFixed
            };
            _tabs.DrawItem += PaintAdminTab;

            var usersPage = new TabPage("User management")
            {
                BackColor = Theme.Cream,
                Padding = new Padding(0, 8, 0, 0)
            };
            usersPage.Controls.Add(BuildUsersTab());
            _tabs.TabPages.Add(usersPage);

            var groupsPage = new TabPage("Groups")
            {
                BackColor = Theme.Cream,
                Padding = new Padding(0, 8, 0, 0)
            };
            groupsPage.Controls.Add(BuildGroupsTab());
            _tabs.TabPages.Add(groupsPage);

            var pagesTab = new TabPage("Pages")
            {
                BackColor = Theme.Cream,
                Padding = new Padding(0, 8, 0, 0)
            };
            pagesTab.Controls.Add(BuildPagesTab());
            _tabs.TabPages.Add(pagesTab);

            _adminTab = new TabPage("Admin management")
            {
                BackColor = Theme.Cream,
                Padding = new Padding(0, 8, 0, 0)
            };
            _adminTab.Controls.Add(BuildAdminTab());
            _tabs.TabPages.Add(_adminTab);

            _tabs.SelectedIndexChanged += (_, _) =>
            {
                // Leaving the tab would lose unsaved SMTP edits.
                if (_smtpDirty)
                    SaveMail();
            };
            Controls.Add(_tabs);
            ApplyTabs();
            LoadUsers();
            LoadGroups();
            LoadPages();
            LoadRoles();
            LoadBankFeed();
            LoadSession();
            LoadMail();
        }

        /// <summary>Show Admin management only for administrators.</summary>
        private void ApplyTabs()
        {
            // UI not built yet.
            if (_adminTab == null)
                return;
            bool showAdmin = AppState.IsAdmin;
            // Restore Admin management after an IT-only session.
            if (showAdmin && !_tabs.TabPages.Contains(_adminTab))
                _tabs.TabPages.Add(_adminTab);
            // Hide Admin management so IT cannot change SMTP or Plaid.
            if (!showAdmin && _tabs.TabPages.Contains(_adminTab))
            {
                _tabs.SelectedIndex = 0;
                _tabs.TabPages.Remove(_adminTab);
            }
        }

        /// <summary>User grid plus administrator and IT role lists.</summary>
        private Control BuildUsersTab()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "Add users from the table and assign one or more groups. Allowed permissions from any group win. Locked means too many failed sign-ins — call the person, then Clear lock or Set password from the right-click menu."
            };

            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0, 8, 0, 0)
            };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            Theme.StyleGrid(_grid);
            _grid.AllowUserToOrderColumns = false;
            _grid.CellDoubleClick += (_, e) =>
            {
                // Header clicks are not a row to edit.
                if (e.RowIndex >= 0)
                    EditUser(RowUser(e.RowIndex));
            };
            _grid.CellMouseClick += (_, e) =>
            {
                // Context menu is only for an existing row.
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;
                ShowUserMenu(e.RowIndex);
            };
            var usersCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 8) };
            var tableBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Theme.Paper
            };
            var add = new Button
            {
                Text = "Add user",
                Size = new Size(120, 32),
                Location = new Point(8, 6)
            };
            Theme.StyleGoldButton(add);
            add.Click += (_, _) => EditUser(null);
            var addHost = new Panel
            {
                Dock = DockStyle.Right,
                Width = 136,
                BackColor = Theme.Paper
            };
            addHost.Controls.Add(add);
            tableBar.Controls.Add(addHost);
            usersCard.Controls.Add(_grid);
            usersCard.Controls.Add(tableBar);

            var roles = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            roles.Controls.Add(RoleCard("Administrators", true, out _admins), 0, 0);
            roles.Controls.Add(RoleCard("IT", false, out _it), 1, 0);

            split.Controls.Add(usersCard, 0, 0);
            split.Controls.Add(roles, 0, 1);

            host.Controls.Add(split);
            host.Controls.Add(intro);
            return host;
        }

        /// <summary>Access-group grid with add/edit/delete.</summary>
        private Control BuildGroupsTab()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 64,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "A user can be in several groups. Allowed permissions win over blocked ones. Admin cannot be changed or deleted: Settings and User management only, no tables. Only an administrator can change IT permissions. Other groups can be edited by IT and administrators."
            };

            _groupsGrid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            Theme.StyleGrid(_groupsGrid);
            _groupsGrid.AllowUserToOrderColumns = false;
            _groupsGrid.CellDoubleClick += (_, e) =>
            {
                // Header clicks are not a row to edit.
                if (e.RowIndex >= 0)
                    EditGroup(GroupName(e.RowIndex));
            };
            _groupsGrid.CellMouseClick += (_, e) =>
            {
                // Context menu is only for an existing row.
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;
                _groupsGrid.ClearSelection();
                _groupsGrid.Rows[e.RowIndex].Selected = true;
                string name = GroupName(e.RowIndex);
                var menu = new ContextMenuStrip();
                // Custom groups (and IT, for admins) can be opened.
                if (AccessGroups.CanEdit(name))
                    menu.Items.Add("Edit settings", null, (_, _) => EditGroup(name));
                // Built-in Admin/IT cannot be deleted.
                if (AccessGroups.CanDelete(name))
                    menu.Items.Add("Delete group", null, (_, _) => DeleteGroup(name));
                // Explain why the built-in group is locked.
                if (menu.Items.Count == 0)
                {
                    var locked = menu.Items.Add(AccessGroups.IsAdmin(name)
                        ? "Admin cannot be changed"
                        : "Only an administrator can change IT");
                    locked.Enabled = false;
                }

                menu.Show(_groupsGrid, _groupsGrid.PointToClient(Control.MousePosition));
            };

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            var tableBar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Theme.Paper
            };
            var add = new Button
            {
                Text = "Add group",
                Size = new Size(120, 32),
                Location = new Point(8, 6)
            };
            Theme.StyleGoldButton(add);
            add.Click += (_, _) => AddGroup();
            var addHost = new Panel
            {
                Dock = DockStyle.Right,
                Width = 136,
                BackColor = Theme.Paper
            };
            addHost.Controls.Add(add);
            tableBar.Controls.Add(addHost);
            card.Controls.Add(_groupsGrid);
            card.Controls.Add(tableBar);

            host.Controls.Add(card);
            host.Controls.Add(intro);
            return host;
        }

        /// <summary>Sidebar tree with drag-and-drop, rename, and delete.</summary>
        private Control BuildPagesTab()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 56,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "This tree is the sidebar. A tab icon is a dropdown; everything else is a page. Drag to move. Rename a folder or a page to change the sidebar tab. A gold line shows where it will land; a gold box means it will nest under that row."
            };

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(12) };
            var bar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Theme.Paper
            };
            var add = new Button { Text = "Add tab", Size = new Size(110, 32), Location = new Point(8, 6) };
            var rename = new Button { Text = "Rename", Size = new Size(90, 32), Location = new Point(136, 6) };
            var delete = new Button { Text = "Delete", Size = new Size(90, 32), Location = new Point(234, 6) };
            Theme.StyleGoldButton(add);
            Theme.StyleOutlineButton(rename);
            Theme.StyleOutlineButton(delete);
            add.Click += (_, _) => AddMenuFolder();
            rename.Click += (_, _) => RenameMenuNode();
            delete.Click += (_, _) => DeleteMenuNode();
            bar.Controls.Add(add);
            bar.Controls.Add(rename);
            bar.Controls.Add(delete);

            _menuTree = new MenuHintTree
            {
                Dock = DockStyle.Fill,
                AllowDrop = true,
                CheckBoxes = false,
                HideSelection = false,
                FullRowSelect = true,
                ShowLines = false,
                ShowPlusMinus = true,
                ShowRootLines = true,
                DrawMode = TreeViewDrawMode.OwnerDrawAll,
                Font = Theme.Body,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Cream,
                ForeColor = Theme.Navy,
                ItemHeight = 32,
                Indent = 22
            };
            _menuTree.AfterCheck += MenuTreeAfterCheck;
            _menuTree.ItemDrag += MenuTreeItemDrag;
            _menuTree.DragEnter += (_, e) =>
            {
                // Accept only sidebar nodes, not files.
                if (e.Data?.GetDataPresent(typeof(TreeNode)) == true)
                    e.Effect = DragDropEffects.Move;
            };
            _menuTree.DragOver += MenuTreeDragOver;
            _menuTree.DragLeave += (_, _) => ClearMenuDropHint();
            _menuTree.DragDrop += MenuTreeDragDrop;
            _menuTree.MouseDown += MenuTreeMouseDown;
            card.Controls.Add(_menuTree);
            card.Controls.Add(bar);

            host.Controls.Add(card);
            host.Controls.Add(intro);
            return host;
        }

        /// <summary>Reload the sidebar tree from MenuLayout.</summary>
        private void LoadPages()
        {
            // Pages tab not built yet.
            if (_menuTree == null)
                return;
            _menu = MenuLayout.Load();
            FillMenuTree();
        }

        /// <summary>Rebuild tree nodes from the in-memory menu without firing check events.</summary>
        private void FillMenuTree()
        {
            _menuLoading = true;
            _menuTree.BeginUpdate();
            _menuTree.Nodes.Clear();
            foreach (var node in _menu.Root)
                _menuTree.Nodes.Add(MakeTreeNode(node));
            _menuTree.EndUpdate();
            _menuTree.ExpandAll();
            _menuLoading = false;
        }

        /// <summary>Tree node tagged with the MenuNode, including children.</summary>
        private static TreeNode MakeTreeNode(MenuNode node)
        {
            var tree = new TreeNode(node.Title) { Tag = node, Checked = node.On };
            foreach (var child in node.Children)
                tree.Nodes.Add(MakeTreeNode(child));
            return tree;
        }

        /// <summary>MenuNode stored on the tree node, or null.</summary>
        private static MenuNode? NodeOf(TreeNode? tree) => tree?.Tag as MenuNode;

        /// <summary>Persist the sidebar layout and refresh open pages.</summary>
        private void SaveMenu()
        {
            _menu.Save();
            AppLock.NotifyChanged();
            Navigator.RefreshOpenPages();
        }

        /// <summary>Insert a new sidebar tab under the selection or at the root.</summary>
        private void AddMenuFolder()
        {
            string? name = PromptText("New tab", "TAB NAME");
            // Cancel or blank name leaves the tree unchanged.
            if (string.IsNullOrWhiteSpace(name))
                return;
            var folder = MenuNode.Folder(name.Trim());
            var selected = NodeOf(_menuTree.SelectedNode);
            // Nest the new tab inside the selected folder.
            if (selected is { IsFolder: true })
                selected.Children.Add(folder);
            // No selection: append a top-level tab.
            else if (selected == null)
                _menu.Root.Add(folder);
            // Opposite branch of the condition above.
            else
            {
                var loc = _menu.Locate(selected);
                int index = loc.Index < 0 ? loc.Siblings.Count : loc.Index + 1;
                loc.Siblings.Insert(Math.Clamp(index, 0, loc.Siblings.Count), folder);
            }

            SaveMenu();
            FillMenuTree();
        }

        /// <summary>Rename the selected folder or page after a prompt.</summary>
        private void RenameMenuNode()
        {
            var node = NodeOf(_menuTree.SelectedNode);
            // Need a selected tab to rename or delete.
            if (node == null)
            {
                MessageBox.Show("Select a tab to rename.", "Pages", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? name = PromptText("Rename tab", "TAB NAME", node.Title, "Save");
            // Cancel or blank name leaves the tree unchanged.
            if (string.IsNullOrWhiteSpace(name))
                return;
            node.Name = name.Trim();
            SaveMenu();
            FillMenuTree();
        }

        /// <summary>Remove the selected node and lift its children up one level.</summary>
        private void DeleteMenuNode()
        {
            var node = NodeOf(_menuTree.SelectedNode);
            // Need a selected tab to rename or delete.
            if (node == null)
                return;
            var loc = _menu.Locate(node);
            // Node is not in the model (stale tree).
            if (loc.Index < 0)
                return;
            loc.Siblings.RemoveAt(loc.Index);
            // Lift children so nested pages are not lost.
            if (node.IsFolder)
            {
                loc.Siblings.InsertRange(loc.Index, node.Children);
            }

            SaveMenu();
            FillMenuTree();
        }

        /// <summary>Toggle visibility of a sidebar item and save.</summary>
        private void MenuTreeAfterCheck(object? sender, TreeViewEventArgs e)
        {
            // Ignore checks fired while rebuilding the tree.
            if (_menuLoading || e.Node == null)
                return;
            var node = NodeOf(e.Node);
            // Need a selected tab to rename or delete.
            if (node == null)
                return;
            _menu.SetOn(node, e.Node.Checked);
            SaveMenu();
            FillMenuTree();
        }

        /// <summary>Start a move drag for a tree node.</summary>
        private void MenuTreeItemDrag(object? sender, ItemDragEventArgs e)
        {
            // Only tree nodes start a sidebar drag.
            if (e.Item is not TreeNode node)
                return;
            _menuDrag = node;
            _menuTree.DoDragDrop(node, DragDropEffects.Move);
        }

        /// <summary>Show the gold drop hint while dragging a sidebar item.</summary>
        private void MenuTreeDragOver(object? sender, DragEventArgs e)
        {
            // Reject non-node drags.
            if (e.Data?.GetDataPresent(typeof(TreeNode)) != true)
            {
                e.Effect = DragDropEffects.None;
                ClearMenuDropHint();
                return;
            }

            var point = _menuTree.PointToClient(new Point(e.X, e.Y));
            // Pointer is not a valid drop site.
            if (!TryMenuDrop(point, out var target, out var kind))
            {
                e.Effect = DragDropEffects.None;
                ClearMenuDropHint();
                return;
            }

            e.Effect = DragDropEffects.Move;
            SetMenuDropHint(target, kind);
        }

        /// <summary>Move the dragged item before, after, or inside the target.</summary>
        private void MenuTreeDragDrop(object? sender, DragEventArgs e)
        {
            var sourceNode = _menuDrag ?? e.Data?.GetData(typeof(TreeNode)) as TreeNode;
            var point = _menuTree.PointToClient(new Point(e.X, e.Y));
            bool hit = TryMenuDrop(point, out var targetTree, out var kind);
            ClearMenuDropHint();
            _menuDrag = null;
            var source = NodeOf(sourceNode);
            // Drop cancelled or left the tree.
            if (source == null || !hit)
                return;

            var target = NodeOf(targetTree);
            // Empty area means move to the end of the root.
            if (target == null || targetTree == null)
            {
                _menu.Move(source, _menu.Root, _menu.Root.Count);
                SaveMenu();
                FillMenuTree();
                return;
            }

            // Cannot nest a folder inside itself.
            if (source == target || _menu.IsDescendant(source, target))
                return;

            // Gold box: nest under that dropdown.
            if (kind == MenuDropKind.Inside && target.IsFolder)
            {
                // Sidebar only allows two folder levels.
                if (_menu.DepthOf(target) >= 2 && source.IsFolder)
                    return;
                _menu.Move(source, target.Children, target.Children.Count);
            }
            // Dropping onto a page wraps it in a new dropdown.
            else if (kind == MenuDropKind.Inside && !target.IsFolder && _menu.DepthOf(target) < 2)
            {
                var folder = _menu.WrapInFolder(target, target.Title);
                // Skip if wrap somehow created a cycle.
                if (!_menu.IsDescendant(source, folder))
                    _menu.Move(source, folder.Children, folder.Children.Count);
            }
            // Opposite branch of the condition above.
            else
            {
                var loc = _menu.Locate(target);
                // Node is not in the model (stale tree).
                if (loc.Index < 0)
                    return;
                int index = kind == MenuDropKind.Before ? loc.Index : loc.Index + 1;
                _menu.Move(source, loc.Siblings, index);
            }

            SaveMenu();
            FillMenuTree();
        }

        /// <summary>Decide before/after/inside from the pointer on a row.</summary>
        private bool TryMenuDrop(Point client, out TreeNode? target, out MenuDropKind kind)
        {
            target = _menuTree.GetNodeAt(client);
            kind = MenuDropKind.None;
            var source = _menuDrag;
            // Drag was cleared before hit-testing.
            if (source == null)
                return false;
            // Filter was deleted on another PC.
            if (target == null)
            {
                target = LastTreeNode(_menuTree.Nodes);
                kind = MenuDropKind.After;
                return target != source && !IsTreeDescendant(source, target);
            }

            // Cannot drop a node onto its descendant.
            if (target == source || IsTreeDescendant(source, target))
                return false;

            var bounds = NodeRow(target);
            int y = client.Y - bounds.Top;
            // Middle of the row means nest inside.
            if (y > bounds.Height / 4 && y < (bounds.Height * 3) / 4)
                kind = MenuDropKind.Inside;
            // Opposite branch of the condition above.
            else
                kind = y < bounds.Height / 2 ? MenuDropKind.Before : MenuDropKind.After;
            return true;
        }

        /// <summary>Invalidate the tree when the drop target changes.</summary>
        private void SetMenuDropHint(TreeNode? target, MenuDropKind kind)
        {
            // Avoid flicker when the hint has not changed.
            if (_menuDropTarget == target && _menuDropKind == kind)
                return;
            _menuDropTarget = target;
            _menuDropKind = kind;
            _menuTree.DropTarget = target;
            _menuTree.DropKind = kind;
            _menuTree.Invalidate();
        }

        /// <summary>Clear the gold line or box after drag leave or drop.</summary>
        private void ClearMenuDropHint()
        {
            // Already cleared.
            if (_menuDropKind == MenuDropKind.None && _menuDropTarget == null)
                return;
            _menuDropKind = MenuDropKind.None;
            _menuDropTarget = null;
            _menuTree.DropTarget = null;
            _menuTree.DropKind = MenuDropKind.None;
            _menuTree.Invalidate();
        }

        private sealed class MenuHintTree : TreeView
        {
            public TreeNode? DropTarget { get; set; }
            public MenuDropKind DropKind { get; set; }

            /// <summary>Owner-drawn sidebar tree with double buffering.</summary>
            public MenuHintTree()
            {
                Theme.EnableDoubleBuffer(this);
            }

            /// <summary>Paint chevron, checkbox, tab icon, and title for a row.</summary>
            protected override void OnDrawNode(DrawTreeNodeEventArgs e)
            {
                var node = e.Node;
                // Skip empty paint events.
                if (node == null || e.Bounds.Height <= 0)
                    return;
                var g = e.Graphics;
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var row = new Rectangle(0, e.Bounds.Y, ClientSize.Width, Math.Max(e.Bounds.Height, ItemHeight));
                bool selected = (e.State & TreeNodeStates.Selected) != 0;
                bool on = node.Checked;
                var menu = node.Tag as MenuNode;
                bool folder = menu?.IsFolder == true;

                using (var back = new SolidBrush(selected ? Theme.GridSelection : Theme.Cream))
                    g.FillRectangle(back, row);
                using (var line = new Pen(Theme.GridLine))
                    g.DrawLine(line, 12, row.Bottom - 1, row.Right - 12, row.Bottom - 1);

                var parts = LayoutNode(node, row, folder);
                // Folders get a chevron and tab icon; pages do not.
                if (folder)
                    DrawChevron(g, parts.Chevron, node.IsExpanded, on);
                DrawCheck(g, parts.Check, on);
                // Folders get a chevron and tab icon; pages do not.
                if (folder)
                    DrawTabIcon(g, parts.Icon, on);

                var textColor = on ? Theme.Navy : Theme.Muted;
                var font = folder ? Theme.BodyBold : Theme.Body;
                TextRenderer.DrawText(
                    g,
                    node.Text,
                    font,
                    parts.Text,
                    textColor,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPadding);
            }

            /// <summary>Toggle expand or checked without selecting when those glyphs are hit.</summary>
            protected override void OnMouseDown(MouseEventArgs e)
            {
                var node = GetNodeAt(e.Location);
                // Left-click on glyphs should not start a drag-select.
                if (node != null && e.Button == MouseButtons.Left)
                {
                    var parts = LayoutNode(node, NodeRow(node), node.Tag is MenuNode { IsFolder: true });
                    // Chevron toggles expand without changing selection.
                    if (parts.Chevron.Contains(e.Location) && node.Tag is MenuNode { IsFolder: true })
                    {
                        node.Toggle();
                        Invalidate();
                        return;
                    }

                    // Checkbox toggles sidebar visibility.
                    if (parts.Check.Contains(e.Location))
                    {
                        SelectedNode = node;
                        node.Checked = !node.Checked;
                        return;
                    }
                }

                base.OnMouseDown(e);
            }

            /// <summary>Draw the gold drop line or nest box after WM_PAINT.</summary>
            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                // Only paint the drop hint after WM_PAINT when dragging.
                if (m.Msg != 0x000F || DropKind == MenuDropKind.None)
                    return;
                using var g = Graphics.FromHwnd(Handle);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var row = DropTarget == null
                    ? new Rectangle(8, Math.Max(4, ClientSize.Height - 6), ClientSize.Width - 16, 0)
                    : NodeRow(DropTarget);
                // Gold box means the item will nest under this row.
                if (DropKind == MenuDropKind.Inside)
                {
                    using var fill = new SolidBrush(Color.FromArgb(70, Theme.Gold));
                    using var border = new Pen(Theme.Gold, 2);
                    var box = new Rectangle(2, row.Y, ClientSize.Width - 6, Math.Max(2, row.Height - 1));
                    g.FillRectangle(fill, box);
                    g.DrawRectangle(border, box);
                    return;
                }

                int y = DropKind == MenuDropKind.Before ? row.Y : row.Bottom - 1;
                int x = Math.Max(12, row.X + 18);
                using var gold = new Pen(Theme.Gold, 3);
                g.DrawLine(gold, x, y, ClientSize.Width - 10, y);
                using var dot = new SolidBrush(Theme.Gold);
                g.FillEllipse(dot, x - 5, y - 5, 10, 10);
            }

            /// <summary>Hit rectangles for chevron, check, icon, and text.</summary>
            private NodeParts LayoutNode(TreeNode node, Rectangle row, bool folder)
            {
                int x = 12 + node.Level * 22;
                int mid = row.Y + row.Height / 2;
                var chevron = new Rectangle(x, mid - 7, 14, 14);
                x += 18;
                var check = new Rectangle(x, mid - 8, 16, 16);
                x += 22;
                var icon = folder ? new Rectangle(x, mid - 9, 18, 18) : Rectangle.Empty;
                // Folders get a chevron and tab icon; pages do not.
                if (folder)
                    x += 24;
                var text = new Rectangle(x, row.Y, Math.Max(20, row.Right - x - 8), row.Height);
                return new NodeParts(chevron, check, icon, text);
            }

            private readonly record struct NodeParts(Rectangle Chevron, Rectangle Check, Rectangle Icon, Rectangle Text);

            /// <summary>Expand/collapse triangle for a folder row.</summary>
            private static void DrawChevron(Graphics g, Rectangle box, bool expanded, bool on)
            {
                using var brush = new SolidBrush(on ? Theme.Navy : Theme.Muted);
                int cx = box.X + box.Width / 2;
                int cy = box.Y + box.Height / 2;
                Point[] pts = expanded
                    ? new[] { new Point(cx - 5, cy - 2), new Point(cx + 5, cy - 2), new Point(cx, cy + 4) }
                    : new[] { new Point(cx - 2, cy - 5), new Point(cx + 4, cy), new Point(cx - 2, cy + 5) };
                g.FillPolygon(brush, pts);
            }

            /// <summary>Visibility checkbox with a gold mark when on.</summary>
            private static void DrawCheck(Graphics g, Rectangle box, bool on)
            {
                using var border = new Pen(on ? Theme.Navy : Theme.CreamDark, 1.5f);
                using var fill = new SolidBrush(on ? Theme.Paper : Theme.CreamDark);
                g.FillRectangle(fill, box);
                g.DrawRectangle(border, box);
                // Unchecked boxes stay empty so Off pages look muted.
                if (!on)
                    return;
                using var mark = new Pen(Theme.Gold, 2f) { StartCap = LineCap.Round, EndCap = LineCap.Round };
                g.DrawLines(mark, new[]
                {
                    new Point(box.X + 3, box.Y + 8),
                    new Point(box.X + 6, box.Y + 12),
                    new Point(box.X + 13, box.Y + 4)
                });
            }

            /// <summary>Folder tab glyph used for dropdowns.</summary>
            private static void DrawTabIcon(Graphics g, Rectangle box, bool on)
            {
                var body = new Rectangle(box.X, box.Y + 5, box.Width, box.Height - 5);
                var tab = new Rectangle(box.X, box.Y, 10, 8);
                using var fill = new SolidBrush(on ? Theme.Navy : Theme.Muted);
                using var gold = new SolidBrush(on ? Theme.Gold : Theme.CreamDark);
                using var path = Rounded(body, 2);
                g.FillPath(fill, path);
                g.FillRectangle(fill, tab);
                g.FillRectangle(gold, body.X, body.Bottom - 3, body.Width, 3);
            }

            /// <summary>Rounded rectangle path for the tab body.</summary>
            private static GraphicsPath Rounded(Rectangle box, int radius)
            {
                int d = radius * 2;
                var path = new GraphicsPath();
                path.AddArc(box.X, box.Y, d, d, 180, 90);
                path.AddArc(box.Right - d, box.Y, d, d, 270, 90);
                path.AddArc(box.Right - d, box.Bottom - d, d, d, 0, 90);
                path.AddArc(box.X, box.Bottom - d, d, d, 90, 90);
                path.CloseFigure();
                return path;
            }
        }

        /// <summary>Full-width row bounds for painting and hit tests.</summary>
        private static Rectangle NodeRow(TreeNode node)
        {
            var bounds = node.Bounds;
            return new Rectangle(0, bounds.Y, node.TreeView?.ClientSize.Width ?? bounds.Width, Math.Max(bounds.Height, 22));
        }

        /// <summary>Deepest last visible node, used when dropping at the bottom.</summary>
        private static TreeNode? LastTreeNode(TreeNodeCollection nodes)
        {
            // Empty tree has no last node.
            if (nodes.Count == 0)
                return null;
            var node = nodes[^1];
            while (node.IsExpanded && node.Nodes.Count > 0)
                node = node.Nodes[^1];
            return node;
        }

        /// <summary>True when node is nested under ancestor.</summary>
        private static bool IsTreeDescendant(TreeNode ancestor, TreeNode? node)
        {
            while (node != null)
            {
                // Walk parents until we know it is nested under ancestor.
                if (node.Parent == ancestor)
                    return true;
                node = node.Parent;
            }

            return false;
        }

        /// <summary>Select the hit node and show rename/delete on right-click.</summary>
        private void MenuTreeMouseDown(object? sender, MouseEventArgs e)
        {
            var hit = _menuTree.GetNodeAt(e.Location);
            // Mouse-down selects the row so right-click has a target.
            if (hit != null)
                _menuTree.SelectedNode = hit;
            // Rename/delete menu is only for a real node.
            if (e.Button != MouseButtons.Right || hit == null)
                return;
            var node = NodeOf(hit);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Rename", null, (_, _) => RenameMenuNode());
            menu.Items.Add("Delete", null, (_, _) => DeleteMenuNode());
            menu.Show(_menuTree, e.Location);
        }

        /// <summary>Stay signed in, vendor types, SMTP, and live bank feed cards.</summary>
        private Control BuildAdminTab()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream
            };
            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "Stay signed in, login email (SMTP), vendor types, and the live bank feed are administrator-only. They apply to everyone."
            };
            var session = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutSessionCard(session);
            var types = new CardPanel { Dock = DockStyle.Top, Height = 368 };
            LayoutVendorTypesCard(types);
            var mail = new CardPanel { Dock = DockStyle.Top, Height = 280 };
            LayoutMailCard(mail);
            _bankCard = new CardPanel { Dock = DockStyle.Top, Height = 210 };
            LayoutBankCard(_bankCard);
            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var spacerTypes = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var spacerMail = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            host.Controls.Add(_bankCard);
            host.Controls.Add(spacer);
            host.Controls.Add(mail);
            host.Controls.Add(spacerMail);
            host.Controls.Add(types);
            host.Controls.Add(spacerTypes);
            host.Controls.Add(session);
            host.Controls.Add(intro);
            return host;
        }

        /// <summary>Owner-draw tab headers with a gold underline on the selected tab.</summary>
        private static void PaintAdminTab(object? sender, DrawItemEventArgs e)
        {
            // Owner-draw can fire for a removed tab.
            if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var fill = new SolidBrush(selected ? Theme.Paper : Theme.Cream);
            e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                tabs.TabPages[e.Index].Text,
                Theme.BodyBold,
                e.Bounds,
                selected ? Theme.Navy : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            // Gold underline marks the active Admin tab.
            if (selected)
            {
                using var gold = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(gold, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3);
            }
        }

        /// <summary>Type list, named filters, and purchase-slot combos.</summary>
        private void LayoutVendorTypesCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Vendor types && filters",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            var hint = new Label
            {
                Text = "Types are the Edit Vendor dropdown. Filters group types for search fields — Logistics can include Trucking. The same filter can be used in more than one place.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(700, 32)
            };

            var lblTypes = new Label { Text = "TYPES", Location = new Point(24, 76) };
            var lblFilters = new Label { Text = "FILTERS", Location = new Point(248, 76) };
            var lblInFilter = new Label { Text = "TYPES IN THIS FILTER", Location = new Point(472, 76) };
            Theme.StyleFieldLabel(lblTypes);
            Theme.StyleFieldLabel(lblFilters);
            Theme.StyleFieldLabel(lblInFilter);

            _vendorTypes = new ListBox
            {
                Location = new Point(24, 94),
                Size = new Size(210, 130),
                Font = Theme.Body,
                BorderStyle = BorderStyle.FixedSingle
            };
            _vendorFilters = new ListBox
            {
                Location = new Point(248, 94),
                Size = new Size(210, 130),
                Font = Theme.Body,
                BorderStyle = BorderStyle.FixedSingle
            };
            _vendorFilterTypes = new CheckedListBox
            {
                Location = new Point(472, 94),
                Size = new Size(230, 130),
                Font = Theme.Body,
                BorderStyle = BorderStyle.FixedSingle,
                CheckOnClick = true
            };
            _vendorFilters.SelectedIndexChanged += (_, _) => FillFilterTypes();
            _vendorFilterTypes.ItemCheck += (_, _) =>
            {
                // Skip saves while the lists are being refilled.
                if (_loadingVendorLookup)
                    return;
                BeginInvoke(SaveFilterTypes);
            };

            var typeButtons = ActionRow(24, 232, 210, AddVendorType, RenameVendorType, DeleteVendorType);
            var filterButtons = ActionRow(248, 232, 210, AddVendorFilter, RenameVendorFilter, DeleteVendorFilter);

            var lblForwarder = new Label { Text = "NEW PURCHASE FORWARDER", Location = new Point(248, 276) };
            var lblLogistics = new Label { Text = "NEW PURCHASE LOGISTICS", Location = new Point(472, 276) };
            Theme.StyleFieldLabel(lblForwarder);
            Theme.StyleFieldLabel(lblLogistics);
            _slotForwarder = SlotCombo(248, 294);
            _slotLogistics = SlotCombo(472, 294);
            _slotForwarder.SelectedIndexChanged += (_, _) => SaveSlots();
            _slotLogistics.SelectedIndexChanged += (_, _) => SaveSlots();

            card.Controls.Add(heading);
            card.Controls.Add(hint);
            card.Controls.Add(lblTypes);
            card.Controls.Add(lblFilters);
            card.Controls.Add(lblInFilter);
            card.Controls.Add(_vendorTypes);
            card.Controls.Add(_vendorFilters);
            card.Controls.Add(_vendorFilterTypes);
            foreach (var button in typeButtons.Concat(filterButtons))
                card.Controls.Add(button);
            card.Controls.Add(lblForwarder);
            card.Controls.Add(lblLogistics);
            card.Controls.Add(_slotForwarder);
            card.Controls.Add(_slotLogistics);
            LoadVendorTypes();
        }

        /// <summary>Add/Rename/Delete buttons for types or filters.</summary>
        private static Button[] ActionRow(
            int x,
            int y,
            int width,
            Action add,
            Action rename,
            Action delete)
        {
            int gap = 6;
            int w = (width - gap * 2) / 3;
            var addBtn = new Button { Text = "Add", Size = new Size(w, 28), Location = new Point(x, y) };
            var renameBtn = new Button { Text = "Rename", Size = new Size(w, 28), Location = new Point(x + w + gap, y) };
            var deleteBtn = new Button { Text = "Delete", Size = new Size(w, 28), Location = new Point(x + (w + gap) * 2, y) };
            Theme.StyleGoldButton(addBtn);
            Theme.StyleOutlineButton(renameBtn);
            Theme.StyleOutlineButton(deleteBtn);
            addBtn.Font = Theme.Small;
            renameBtn.Font = Theme.Small;
            deleteBtn.Font = Theme.Small;
            addBtn.Click += (_, _) => add();
            renameBtn.Click += (_, _) => rename();
            deleteBtn.Click += (_, _) => delete();
            return new[] { addBtn, renameBtn, deleteBtn };
        }

        /// <summary>Drop-down of filters for a purchase form field.</summary>
        private static ComboBox SlotCombo(int x, int y)
        {
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(x, y),
                Size = new Size(210, 26)
            };
            Theme.StyleCombo(box);
            return box;
        }

        /// <summary>Reload types, filters, and slots, keeping the previous selection.</summary>
        private void LoadVendorTypes()
        {
            // Vendor card not built yet.
            if (_vendorTypes == null)
                return;
            _loadingVendorLookup = true;
            string? keepType = _vendorTypes.SelectedItem as string;
            string? keepFilter = SelectedFilter()?.Id;
            var catalog = VendorTypes.Catalog();

            _vendorTypes.Items.Clear();
            foreach (var name in catalog.Types)
                _vendorTypes.Items.Add(name);
            // Restore the previously selected type after reload.
            if (keepType != null)
            {
                for (int i = 0; i < _vendorTypes.Items.Count; i++)
                {
                    // Find the same type name ignoring case.
                    if (_vendorTypes.Items[i] is string item &&
                        item.Equals(keepType, StringComparison.OrdinalIgnoreCase))
                    {
                        _vendorTypes.SelectedIndex = i;
                        break;
                    }
                }
            }

            _vendorFilters.Items.Clear();
            foreach (var filter in catalog.Filters)
                _vendorFilters.Items.Add(filter);
            // Restore the previously selected filter after reload.
            if (keepFilter != null)
            {
                for (int i = 0; i < _vendorFilters.Items.Count; i++)
                {
                    // Find the same filter id ignoring case.
                    if (_vendorFilters.Items[i] is VendorTypeFilter filter &&
                        filter.Id.Equals(keepFilter, StringComparison.OrdinalIgnoreCase))
                    {
                        _vendorFilters.SelectedIndex = i;
                        break;
                    }
                }
            }

            // Always have a filter selected so types-in-filter can fill.
            if (_vendorFilters.SelectedIndex < 0 && _vendorFilters.Items.Count > 0)
                _vendorFilters.SelectedIndex = 0;

            FillFilterTypes();
            FillSlotCombo(_slotForwarder, VendorTypes.SlotPurchaseForwarder);
            FillSlotCombo(_slotLogistics, VendorTypes.SlotPurchaseLogistics);
            _loadingVendorLookup = false;
        }

        /// <summary>Filter currently selected in the list, or null.</summary>
        private VendorTypeFilter? SelectedFilter() =>
            _vendorFilters?.SelectedItem as VendorTypeFilter;

        /// <summary>Checkboxes for which types belong to the selected filter.</summary>
        private void FillFilterTypes()
        {
            // Filter checkbox list not built yet.
            if (_vendorFilterTypes == null)
                return;
            bool restore = _loadingVendorLookup;
            _loadingVendorLookup = true;
            var filter = SelectedFilter();
            _vendorFilterTypes.Items.Clear();
            foreach (var type in VendorTypes.Catalog().Types)
            {
                bool on = filter != null &&
                          filter.Types.Any(item => item.Equals(type, StringComparison.OrdinalIgnoreCase));
                _vendorFilterTypes.Items.Add(type, on);
            }

            _loadingVendorLookup = restore;
        }

        /// <summary>Fill a slot combo and select the assigned filter.</summary>
        private void FillSlotCombo(ComboBox box, string slot)
        {
            // Slot combo not built yet.
            if (box == null)
                return;
            var catalog = VendorTypes.Catalog();
            catalog.Slots.TryGetValue(slot, out var current);
            box.Items.Clear();
            foreach (var filter in catalog.Filters)
                box.Items.Add(filter);
            for (int i = 0; i < box.Items.Count; i++)
            {
                // Select the filter assigned to this purchase slot.
                if (box.Items[i] is VendorTypeFilter filter &&
                    filter.Id.Equals(current, StringComparison.OrdinalIgnoreCase))
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            // Preselect the first name so Enter adds without extra clicks.
            if (box.Items.Count > 0)
                box.SelectedIndex = 0;
        }

        /// <summary>Persist checked types for the selected filter.</summary>
        private void SaveFilterTypes()
        {
            // Skip saves while the lists are being refilled.
            if (_loadingVendorLookup)
                return;
            var filter = SelectedFilter();
            // No filter selected to save or rename.
            if (filter == null)
                return;
            var catalog = VendorTypes.Catalog();
            var target = catalog.Filters.FirstOrDefault(item =>
                item.Id.Equals(filter.Id, StringComparison.OrdinalIgnoreCase));
            // Filter was deleted on another PC.
            if (target == null)
                return;
            target.Types = _vendorFilterTypes.CheckedItems.Cast<string>().ToList();
            VendorTypes.SaveCatalog(catalog);
            filter.Types = target.Types.ToList();
        }

        /// <summary>Persist which filter Forwarder and Logistics use.</summary>
        private void SaveSlots()
        {
            // Skip saves while the lists are being refilled.
            if (_loadingVendorLookup)
                return;
            var catalog = VendorTypes.Catalog();
            // Store the Forwarder slot only when a filter is picked.
            if (_slotForwarder.SelectedItem is VendorTypeFilter forwarder)
                catalog.Slots[VendorTypes.SlotPurchaseForwarder] = forwarder.Id;
            // Store the Logistics slot only when a filter is picked.
            if (_slotLogistics.SelectedItem is VendorTypeFilter logistics)
                catalog.Slots[VendorTypes.SlotPurchaseLogistics] = logistics.Id;
            VendorTypes.SaveCatalog(catalog);
        }

        /// <summary>Prompt for a new Type name used on Edit Vendor.</summary>
        private void AddVendorType()
        {
            string? name = PromptText("New vendor type", "TYPE NAME");
            // Cancel or blank name leaves the tree unchanged.
            if (string.IsNullOrWhiteSpace(name))
                return;
            var catalog = VendorTypes.Catalog();
            // Type names must stay unique in the Edit Vendor dropdown.
            if (catalog.Types.Any(item => item.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("That type already exists.", "Vendor types", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            catalog.Types.Add(name.Trim());
            VendorTypes.SaveCatalog(catalog);
            LoadVendorTypes();
        }

        /// <summary>Rename the selected Type everywhere it is listed.</summary>
        private void RenameVendorType()
        {
            // Rename/delete needs a selected type.
            if (_vendorTypes.SelectedItem is not string current || current.Length == 0)
            {
                MessageBox.Show("Select a type to rename.", "Vendor types", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? name = PromptText("Rename vendor type", "TYPE NAME", current, "Save");
            // Cancel or unchanged name is a no-op.
            if (string.IsNullOrWhiteSpace(name) || name.Equals(current, StringComparison.OrdinalIgnoreCase))
                return;
            var catalog = VendorTypes.Catalog();
            // Type names must stay unique in the Edit Vendor dropdown.
            if (catalog.Types.Any(item => item.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("That type already exists.", "Vendor types", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            VendorTypes.RenameType(current, name.Trim());
            LoadVendorTypes();
        }

        /// <summary>Remove the selected Type from the catalog.</summary>
        private void DeleteVendorType()
        {
            // Rename/delete needs a selected type.
            if (_vendorTypes.SelectedItem is not string current || current.Length == 0)
            {
                MessageBox.Show("Select a type to delete.", "Vendor types", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var catalog = VendorTypes.Catalog();
            catalog.Types = catalog.Types.Where(item => !item.Equals(current, StringComparison.OrdinalIgnoreCase)).ToList();
            VendorTypes.SaveCatalog(catalog);
            LoadVendorTypes();
        }

        /// <summary>Create a named lookup filter and select it.</summary>
        private void AddVendorFilter()
        {
            string? name = PromptText("New lookup filter", "FILTER NAME");
            // Cancel or blank name leaves the tree unchanged.
            if (string.IsNullOrWhiteSpace(name))
                return;
            var catalog = VendorTypes.Catalog();
            catalog.Filters.Add(new VendorTypeFilter
            {
                Id = VendorTypes.NewFilterId(),
                Name = name.Trim()
            });
            VendorTypes.SaveCatalog(catalog);
            LoadVendorTypes();
            _vendorFilters.SelectedIndex = _vendorFilters.Items.Count - 1;
        }

        /// <summary>Rename the selected lookup filter.</summary>
        private void RenameVendorFilter()
        {
            var filter = SelectedFilter();
            // No filter selected to save or rename.
            if (filter == null)
            {
                MessageBox.Show("Select a filter to rename.", "Vendor types", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? name = PromptText("Rename lookup filter", "FILTER NAME", filter.Name, "Save");
            // Cancel or unchanged filter name is a no-op.
            if (string.IsNullOrWhiteSpace(name) || name.Equals(filter.Name, StringComparison.OrdinalIgnoreCase))
                return;
            var catalog = VendorTypes.Catalog();
            var target = catalog.Filters.FirstOrDefault(item =>
                item.Id.Equals(filter.Id, StringComparison.OrdinalIgnoreCase));
            // Filter was deleted on another PC.
            if (target == null)
                return;
            target.Name = name.Trim();
            VendorTypes.SaveCatalog(catalog);
            LoadVendorTypes();
        }

        /// <summary>Delete a filter that is not assigned to a form slot.</summary>
        private void DeleteVendorFilter()
        {
            var filter = SelectedFilter();
            // No filter selected to save or rename.
            if (filter == null)
            {
                MessageBox.Show("Select a filter to delete.", "Vendor types", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            var catalog = VendorTypes.Catalog();
            bool used = catalog.Slots.Values.Any(id =>
                id.Equals(filter.Id, StringComparison.OrdinalIgnoreCase));
            // Do not delete a filter still assigned to a purchase field.
            if (used)
            {
                MessageBox.Show(
                    "That filter is assigned to a field. Pick a different filter there first.",
                    "Vendor types",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Lookups need at least one filter to bind.
            if (catalog.Filters.Count <= 1)
            {
                MessageBox.Show("Keep at least one filter.", "Vendor types", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            catalog.Filters = catalog.Filters
                .Where(item => !item.Id.Equals(filter.Id, StringComparison.OrdinalIgnoreCase))
                .ToList();
            VendorTypes.SaveCatalog(catalog);
            LoadVendorTypes();
        }

        /// <summary>Stay signed in toggle, duration, and idle close.</summary>
        private void LayoutSessionCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Stay signed in",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            var hint = new Label
            {
                Text = "One setting for every user. When it is on, they can check Stay signed in on the sign-in screen.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(620, 28)
            };
            card.Controls.Add(heading);
            card.Controls.Add(hint);

            var lblOn = new Label { Text = "FOR EVERYONE" };
            Theme.StyleFieldLabel(lblOn);
            _stayToggle = new CrcToggleSwitch();
            _stayToggle.Location = new Point(24, 104);
            _stayToggle.Toggled += (_, _) => SaveSession();
            var onOff = new Label
            {
                Name = "lblStayOnOff",
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(78, 106)
            };
            lblOn.Location = new Point(24, 82);

            var lblDays = new Label { Text = "REMEMBER FOR" };
            Theme.StyleFieldLabel(lblDays);
            _sessionDays = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_sessionDays);
            foreach (int days in new[] { 7, 14, 30, 60, 90 })
                _sessionDays.Items.Add(new IntChoice(days, days + " days"));
            lblDays.Location = new Point(180, 82);
            _sessionDays.Location = new Point(180, 100);
            _sessionDays.Size = new Size(160, 26);
            _sessionDays.SelectionChangeCommitted += (_, _) => SaveSession();

            var lblIdle = new Label { Text = "CLOSE WHEN IDLE" };
            Theme.StyleFieldLabel(lblIdle);
            _idleHours = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_idleHours);
            foreach (int hours in new[] { 1, 2, 3, 5, 8, 12 })
                _idleHours.Items.Add(new IntChoice(hours, hours == 1 ? "After 1 hour" : "After " + hours + " hours"));
            lblIdle.Location = new Point(356, 82);
            _idleHours.Location = new Point(356, 100);
            _idleHours.Size = new Size(180, 26);
            _idleHours.SelectionChangeCommitted += (_, _) => SaveSession();

            card.Controls.Add(lblOn);
            card.Controls.Add(_stayToggle);
            card.Controls.Add(onOff);
            card.Controls.Add(lblDays);
            card.Controls.Add(_sessionDays);
            card.Controls.Add(lblIdle);
            card.Controls.Add(_idleHours);
        }

        /// <summary>Load stay-signed-in settings into the controls.</summary>
        private void LoadSession()
        {
            // Session card not built yet.
            if (_stayToggle == null)
                return;
            _stayToggle.SetOn(AppState.StaySignedInEnabled);
            SelectChoice(_sessionDays, AppState.StaySignedInDays, 30);
            SelectChoice(_idleHours, AppState.IdleCloseHours, 5);
            ApplySessionEnabled();
        }

        /// <summary>Write stay-signed-in policy and start or stop idle watch.</summary>
        private void SaveSession()
        {
            AppState.StaySignedInEnabled = _stayToggle.On;
            AppState.StaySignedInDays = _sessionDays.SelectedItem is IntChoice days ? days.Value : 30;
            AppState.IdleCloseHours = _idleHours.SelectedItem is IntChoice hours ? hours.Value : 5;
            ApplySessionEnabled();
            AppLock.SaveSettings();
            // Idle watch is meaningless when Stay signed in is off.
            if (!AppState.StaySignedInEnabled)
                IdleWatch.Stop();
            // This PC already remembered login, so restart idle timing.
            else if (AppState.StaySignedIn)
                IdleWatch.Start();
        }

        /// <summary>Enable duration/idle combos only when Stay signed in is on.</summary>
        private void ApplySessionEnabled()
        {
            bool on = _stayToggle.On;
            _sessionDays.Enabled = on;
            _idleHours.Enabled = on;
            // On/Off label is looked up by name from the card.
            if (Controls.Find("lblStayOnOff", true).FirstOrDefault() is Label label)
                label.Text = on ? "On" : "Off";
        }

        /// <summary>Select a matching IntChoice, or the fallback.</summary>
        private static void SelectChoice(ComboBox box, int value, int fallback)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                // Select the stored number of days or hours.
                if (box.Items[i] is IntChoice choice && choice.Value == value)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            for (int i = 0; i < box.Items.Count; i++)
            {
                // Stored value is not in the list; use the default.
                if (box.Items[i] is IntChoice choice && choice.Value == fallback)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            // Preselect the first name so Enter adds without extra clicks.
            if (box.Items.Count > 0)
                box.SelectedIndex = 0;
        }

        /// <summary>SMTP login email, password, host, and port fields.</summary>
        private void LayoutMailCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Login email (SMTP)",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            var hint = new Label
            {
                Text = "One mailbox for every user. Click Save after you change the login email or password. Show only works for a password you just typed.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(620, 28)
            };
            card.Controls.Add(heading);
            card.Controls.Add(hint);

            _smtpUser = new TextBox();
            _smtpPassword = new TextBox { UseSystemPasswordChar = true };
            _smtpHost = new TextBox();
            _smtpPort = new TextBox();
            Theme.StyleField(_smtpUser);
            Theme.StyleField(_smtpPassword);
            Theme.StyleField(_smtpHost);
            Theme.StyleField(_smtpPort);
            _smtpUser.PlaceholderText = "you@gmail.com";
            _smtpHost.PlaceholderText = Mailer.DefaultHost;
            _smtpPort.PlaceholderText = Mailer.DefaultPort.ToString();

            var lblEmail = new Label { Text = "LOGIN EMAIL" };
            var lblPass = new Label { Text = "PASSWORD" };
            var lblHost = new Label { Text = "SMTP HOST" };
            var lblPort = new Label { Text = "PORT" };
            Theme.StyleFieldLabel(lblEmail);
            Theme.StyleFieldLabel(lblPass);
            Theme.StyleFieldLabel(lblHost);
            Theme.StyleFieldLabel(lblPort);
            PlaceField(card, lblEmail, _smtpUser, 24, 76, 360);
            PlaceField(card, lblPass, _smtpPassword, 24, 130, 360);

            _smtpSave = new Button
            {
                Text = "Save",
                Size = new Size(110, 26),
                Location = new Point(396, 94)
            };
            Theme.StyleGoldButton(_smtpSave);
            _smtpSave.Click += (_, _) => SaveMail(announce: true);
            card.Controls.Add(_smtpSave);

            _smtpShow = new Button
            {
                Text = "Show",
                Size = new Size(84, 26),
                Location = new Point(396, 148)
            };
            Theme.StyleOutlineButton(_smtpShow);
            _smtpShow.Font = Theme.Small;
            new ToolTip { ShowAlways = true }.SetToolTip(
                _smtpShow,
                "Preview a password you just typed. The saved password cannot be shown.");
            _smtpShow.Click += (_, _) => ToggleSmtpPassword();
            card.Controls.Add(_smtpShow);

            PlaceField(card, lblHost, _smtpHost, 24, 184, 360);
            PlaceField(card, lblPort, _smtpPort, 404, 184, 80);

            _smtpPassHint = new Label
            {
                Text = "",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 236),
                Size = new Size(600, 24)
            };
            card.Controls.Add(_smtpPassHint);

            _smtpUser.TextChanged += (_, _) => MarkSmtpDirty();
            _smtpHost.TextChanged += (_, _) => MarkSmtpDirty();
            _smtpPort.TextChanged += (_, _) => MarkSmtpDirty();
            _smtpPassword.TextChanged += (_, _) =>
            {
                // Typing while LoadMail fills fields is not a user edit.
                if (_loadingMail)
                    return;
                _smtpPasswordFresh = _smtpPassword.Text.Length > 0;
                // Clearing the box hides a leftover Show preview.
                if (!_smtpPasswordFresh)
                    HideSmtpPassword();
                _smtpPassHint.Text = "";
                _smtpDirty = true;
            };
        }

        /// <summary>Remember unsaved SMTP edits so a tab change can save them.</summary>
        private void MarkSmtpDirty()
        {
            // Typing while LoadMail fills fields is not a user edit.
            if (_loadingMail)
                return;
            _smtpDirty = true;
            // Clear the saved-success hint once they edit again.
            if (_smtpPassHint != null && _smtpPassHint.ForeColor == Theme.Success)
                _smtpPassHint.Text = "";
        }

        /// <summary>Show a freshly typed password; saved passwords stay hidden.</summary>
        private void ToggleSmtpPassword()
        {
            // Saved passwords cannot be revealed; only a just-typed one can.
            if (!_smtpPasswordFresh || _smtpPassword.Text.Length == 0)
            {
                HideSmtpPassword();
                _smtpPassHint.ForeColor = Theme.Navy;
                _smtpPassHint.Text = "Show only works for a password you just typed. The saved password stays hidden â€” type it again to preview, then Save.";
                return;
            }

            bool hide = !_smtpPassword.UseSystemPasswordChar;
            _smtpPassword.UseSystemPasswordChar = hide;
            _smtpShow.Text = hide ? "Show" : "Hide";
            _smtpPassHint.ForeColor = Theme.Muted;
            _smtpPassHint.Text = hide
                ? ""
                : "This is the password you just typed. Click Save to keep it.";
        }

        /// <summary>Mask the password box and reset the Show button.</summary>
        private void HideSmtpPassword()
        {
            _smtpPassword.UseSystemPasswordChar = true;
            _smtpShow.Text = "Show";
        }

        /// <summary>Fill SMTP fields from the database without marking them dirty.</summary>
        private void LoadMail()
        {
            // Mail card not built yet.
            if (_smtpUser == null)
                return;
            var row = SqliteInventory.LoadAdminSmtp();
            SqliteInventory.ApplyAdminSmtp();
            _loadingMail = true;
            _smtpUser.Text = row.Email;
            _smtpPassword.Text = "";
            HideSmtpPassword();
            _smtpPasswordFresh = false;
            _smtpDirty = false;
            _smtpPassword.PlaceholderText = row.Password.Length == 0
                ? "Gmail app password"
                : "Saved â€” type a new password to change it";
            _smtpHost.Text = row.Host.Length > 0 ? row.Host : Mailer.ResolveHost();
            _smtpPort.Text = row.Port > 0 ? row.Port.ToString() : Mailer.DefaultPort.ToString();
            if (_smtpPassHint != null)
            {
                _smtpPassHint.ForeColor = row.Password.Length == 0 ? Theme.Muted : Theme.Success;
                _smtpPassHint.Text = row.Email.Length == 0 && row.Password.Length == 0
                    ? ""
                    : "In database: " +
                      (row.Email.Length > 0 ? row.Email : "(no email)") +
                      (row.Password.Length > 0 ? "  Â·  password saved" : "  Â·  no password");
            }
            _loadingMail = false;
        }

        /// <summary>Write SMTP settings; announce success or failure when requested.</summary>
        private void SaveMail(bool announce = false)
        {
            // Mail card not built yet.
            if (_smtpUser == null)
                return;
            // SMTP is administrator-only even if the tab was left open.
            if (!AppState.IsAdmin)
            {
                // Silent auto-save on tab change should not toast.
                if (announce)
                    ToastAlert.Error(FindForm() ?? this, "Only an administrator can save the login email.");
                return;
            }

            string email = _smtpUser.Text.Trim();
            string password = _smtpPassword.Text;
            string host = string.IsNullOrWhiteSpace(_smtpHost.Text)
                ? Mailer.DefaultHost
                : _smtpHost.Text.Trim();
            int port = Mailer.DefaultPort;
            // Invalid port falls back to 587.
            if (int.TryParse(_smtpPort.Text.Trim(), out int parsed) && parsed > 0)
                port = parsed;

            bool ok = SqliteInventory.SaveAdminSmtp(email, password, host, port, out string error);
            // Clear dirty so a later tab change does not rewrite the same values.
            if (ok)
                _smtpDirty = false;

            // Background save: skip the success/error UI.
            if (!announce)
                return;

            Control toastHost = FindForm() ?? this;
            // Clear dirty so a later tab change does not rewrite the same values.
            if (ok)
            {
                var row = SqliteInventory.LoadAdminSmtp();
                HideSmtpPassword();
                _loadingMail = true;
                _smtpUser.Text = row.Email;
                _smtpPassword.Text = "";
                _loadingMail = false;
                _smtpPasswordFresh = false;
                _smtpDirty = false;
                _smtpPassword.PlaceholderText = row.Password.Length == 0
                    ? "Gmail app password"
                    : "Saved â€” type a new password to change it";
                _smtpPassHint.ForeColor = Theme.Success;
                _smtpPassHint.Text = "In database: " +
                    (row.Email.Length > 0 ? row.Email : "(no email)") +
                    (row.Password.Length > 0 ? "  Â·  password saved" : "  Â·  no password");
                ToastAlert.Success(toastHost, "Saved to the database.");
            }
            // Opposite branch of the condition above.
            else
            {
                _smtpPassHint.ForeColor = Theme.Danger;
                _smtpPassHint.Text = string.IsNullOrWhiteSpace(error)
                    ? "Could not save to the database. Try Save again."
                    : error;
                ToastAlert.Error(toastHost, "Could not save login email.");
                MessageBox.Show(
                    this,
                    string.IsNullOrWhiteSpace(error)
                        ? "The login email could not be written to crc_inventory.db."
                        : error,
                    "Login email",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
            }
        }

        /// <summary>Position a caption and text box on a card.</summary>
        private static void PlaceField(Control parent, Label label, TextBox box, int x, int y, int width)
        {
            label.Location = new Point(x, y);
            box.Location = new Point(x, y + 18);
            box.Width = width;
            box.Height = 26;
            parent.Controls.Add(label);
            parent.Controls.Add(box);
        }

        private sealed class IntChoice
        {
            /// <summary>Combo item pairing an integer with display text.</summary>
            public IntChoice(int value, string label)
            {
                Value = value;
                Label = label;
            }

            public int Value { get; }
            public string Label { get; }
            /// <summary>Combo boxes show the label, not the numeric value.</summary>
            public override string ToString() => Label;
        }

        /// <summary>Plaid auto-sync, connect, and API key fields.</summary>
        private void LayoutBankCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Live bank feed",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            var hint = new Label
            {
                Text = "Only administrators can connect, sync, or change live-feed settings. People with Banking can still see imported transactions.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(620, 36)
            };
            card.Controls.Add(heading);
            card.Controls.Add(hint);

            var lblSync = new Label { Text = "AUTO-SYNC" };
            Theme.StyleFieldLabel(lblSync);
            _plaidSync = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_plaidSync);
            _plaidSync.Items.Add(new SyncChoice(0, "Off"));
            _plaidSync.Items.Add(new SyncChoice(1, "Every 1 hour"));
            _plaidSync.Items.Add(new SyncChoice(3, "Every 3 hours"));
            lblSync.Location = new Point(24, 86);
            _plaidSync.Location = new Point(24, 104);
            _plaidSync.Size = new Size(180, 26);
            _plaidSync.SelectionChangeCommitted += (_, _) => SaveBankFeedQuiet();
            card.Controls.Add(lblSync);
            card.Controls.Add(_plaidSync);

            var connect = new Button
            {
                Text = "Connect bank",
                Size = new Size(140, 34),
                Location = new Point(24, 148)
            };
            Theme.StyleGoldButton(connect);
            connect.Click += async (_, _) =>
            {
                // Connect bank needs keys before opening Plaid Link.
                if (!PlaidClient.IsConfigured)
                {
                    ShowKeys(true);
                    ToastAlert.Error(this, "Add API keys once, save, then Connect bank.");
                    return;
                }

                SaveBankFeedQuiet();
                await BankLive.ConnectAsync(this);
            };
            card.Controls.Add(connect);

            var sync = new Button
            {
                Text = "Sync now",
                Size = new Size(110, 34),
                Location = new Point(176, 148)
            };
            Theme.StyleOutlineButton(sync);
            sync.Click += async (_, _) => await BankLive.SyncAllAsync(this);
            card.Controls.Add(sync);

            _keysToggle = new Button
            {
                Text = "API keys",
                Size = new Size(110, 34),
                Location = new Point(298, 148)
            };
            Theme.StyleNavyButton(_keysToggle);
            _keysToggle.Click += (_, _) => ShowKeys(!_keysPanel.Visible);
            card.Controls.Add(_keysToggle);

            _keysPanel = new Panel
            {
                Location = new Point(12, 196),
                Size = new Size(640, 210),
                BackColor = Theme.Paper,
                Visible = false
            };

            var lblEnv = new Label { Text = "ENVIRONMENT" };
            Theme.StyleFieldLabel(lblEnv);
            _plaidEnv = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_plaidEnv);
            _plaidEnv.Items.AddRange(new object[] { "sandbox", "development", "production" });
            lblEnv.Location = new Point(12, 8);
            _plaidEnv.Location = new Point(12, 26);
            _plaidEnv.Size = new Size(160, 26);

            var lblId = new Label { Text = "CLIENT ID" };
            Theme.StyleFieldLabel(lblId);
            _plaidId = new TextBox();
            Theme.StyleField(_plaidId);
            lblId.Location = new Point(188, 8);
            _plaidId.Location = new Point(188, 26);
            _plaidId.Size = new Size(420, 26);

            var lblSecret = new Label { Text = "SECRET" };
            Theme.StyleFieldLabel(lblSecret);
            _plaidSecret = new TextBox { UseSystemPasswordChar = true };
            Theme.StyleField(_plaidSecret);
            lblSecret.Location = new Point(12, 64);
            _plaidSecret.Location = new Point(12, 82);
            _plaidSecret.Size = new Size(596, 26);

            var save = new Button
            {
                Text = "Save keys",
                Size = new Size(120, 34),
                Location = new Point(12, 118)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) => SaveBankFeed();

            var how = new Label
            {
                Text = "One-time: dashboard.plaid.com â†’ Team Settings â†’ Keys. Sandbox test login is user_good / pass_good. Use Development or Production after Plaid approves a live app.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(12, 158),
                Size = new Size(600, 44)
            };

            _keysPanel.Controls.Add(lblEnv);
            _keysPanel.Controls.Add(_plaidEnv);
            _keysPanel.Controls.Add(lblId);
            _keysPanel.Controls.Add(_plaidId);
            _keysPanel.Controls.Add(lblSecret);
            _keysPanel.Controls.Add(_plaidSecret);
            _keysPanel.Controls.Add(save);
            _keysPanel.Controls.Add(how);
            card.Controls.Add(_keysPanel);
        }

        /// <summary>Expand or collapse the API keys panel.</summary>
        private void ShowKeys(bool show)
        {
            _keysPanel.Visible = show;
            _keysToggle.Text = show ? "Hide keys" : "API keys";
            _bankCard.Height = show ? 430 : 210;
        }

        private sealed class SyncChoice
        {
            /// <summary>Combo item for auto-sync hours.</summary>
            public SyncChoice(int hours, string label)
            {
                Hours = hours;
                Label = label;
            }

            public int Hours { get; }
            public string Label { get; }
            /// <summary>Combo boxes show the label, not the numeric value.</summary>
            public override string ToString() => Label;
        }

        /// <summary>Persist Plaid keys and sync interval without a toast.</summary>
        private void SaveBankFeedQuiet()
        {
            AppState.PlaidClientId = _plaidId.Text.Trim();
            AppState.PlaidSecret = _plaidSecret.Text.Trim();
            AppState.PlaidEnv = _plaidEnv.SelectedItem?.ToString() ?? "sandbox";
            AppState.PlaidSyncHours = _plaidSync.SelectedItem is SyncChoice choice ? choice.Hours : 1;
            AppLock.SaveSettings();
            BankLiveWatch.Start();
        }

        /// <summary>Fill Plaid fields and open keys when they are still empty.</summary>
        private void LoadBankFeed()
        {
            _plaidId.Text = AppState.PlaidClientId;
            _plaidSecret.Text = AppState.PlaidSecret;
            string env = string.IsNullOrWhiteSpace(AppState.PlaidEnv) ? "sandbox" : AppState.PlaidEnv;
            int index = _plaidEnv.Items.IndexOf(env);
            _plaidEnv.SelectedIndex = index >= 0 ? index : 0;
            int hours = AppState.PlaidSyncHours;
            _plaidSync.SelectedIndex = hours == 3 ? 2 : hours == 0 ? 0 : 1;
            ShowKeys(!PlaidClient.IsConfigured);
        }

        /// <summary>Save Plaid settings and toast success.</summary>
        private void SaveBankFeed()
        {
            SaveBankFeedQuiet();
            ToastAlert.Success(this, "Live bank feed settings were saved.");
        }

        /// <summary>Fill the user grid including lock status and table-access summary.</summary>
        private void LoadUsers()
        {
            // Users tab not built yet.
            if (_grid == null)
                return;
            _grid.Columns.Clear();
            _grid.Columns.Add("Username", "Username");
            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Email", "Email");
            _grid.Columns.Add("Group", "Groups");
            _grid.Columns.Add("Status", "Status");
            _grid.Columns.Add("Admin", "Admin");
            _grid.Columns.Add("IT", "IT");
            _grid.Columns.Add("Access", "Table access");
            foreach (var account in Accounts.List())
            {
                int row = _grid.Rows.Add(
                    account.Username,
                    account.DisplayName,
                    account.Email,
                    SqliteInventory.GetAccessGroup(account.Username),
                    account.LoginLocked ? "Locked" : "",
                    account.IsAdmin ? "Yes" : "",
                    account.IsIt ? "Yes" : "",
                    TableAccess.UserSummary(account.Username));
                // Highlight locked accounts so IT can call them.
                if (account.LoginLocked)
                    _grid.Rows[row].DefaultCellStyle.BackColor = Theme.DangerFill;
            }
        }

        /// <summary>Fill the group grid with type, member count, and settings summary.</summary>
        private void LoadGroups()
        {
            // Groups tab not built yet.
            if (_groupsGrid == null)
                return;
            _groupsGrid.Columns.Clear();
            _groupsGrid.Columns.Add("Name", "Group");
            _groupsGrid.Columns.Add("Kind", "Type");
            _groupsGrid.Columns.Add("Members", "Users");
            _groupsGrid.Columns.Add("Access", "Settings");
            foreach (var (name, json) in SqliteInventory.ListAccessGroups())
            {
                string kind = AccessGroups.IsAdmin(name)
                    ? "Built-in Â· locked"
                    : AccessGroups.IsIt(name)
                        ? "Built-in"
                        : "Custom";
                string access = AccessGroups.IsAdmin(name)
                    ? "Settings and user management Â· no tables"
                    : TableAccess.Summary(json);
                _groupsGrid.Rows.Add(
                    name,
                    kind,
                    SqliteInventory.CountGroupMembers(name).ToString(),
                    access);
            }
        }

        /// <summary>Group name in column 0 of the groups grid.</summary>
        private string GroupName(int row) =>
            _groupsGrid.Rows[row].Cells[0].Value?.ToString()?.Trim() ?? "";

        /// <summary>Create a custom group and open its data-access dialog.</summary>
        private void AddGroup()
        {
            string? name = PromptText("New group", "GROUP NAME");
            // Cancel or blank name leaves the tree unchanged.
            if (string.IsNullOrWhiteSpace(name))
                return;
            // Admin and IT already exist and cannot be duplicated.
            if (AccessGroups.IsBuiltIn(name))
            {
                MessageBox.Show("Admin and IT are built-in groups.", "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Group names must be unique.
            if (SqliteInventory.ListAccessGroups().Any(g =>
                    g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("That group already exists.", "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Database rejected the new group.
            if (!SqliteInventory.SaveAccessGroup(name, "", out string error))
            {
                MessageBox.Show(error, "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UserAccessForm.ShowGroup(this, name);
            LoadGroups();
            LoadUsers();
        }

        /// <summary>Open data access for a group that the current user may edit.</summary>
        private void EditGroup(string name)
        {
            // No selected group row.
            if (name.Length == 0)
                return;
            // Admin is locked; IT is administrator-only.
            if (!AccessGroups.CanEdit(name))
            {
                MessageBox.Show(
                    AccessGroups.IsAdmin(name)
                        ? "The Admin group cannot be changed. It has Settings and User management, and cannot see tables."
                        : "Only an administrator can change the IT group.",
                    "Groups",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            // Reload after a successful group-access save.
            if (UserAccessForm.ShowGroup(this, name))
            {
                LoadGroups();
                LoadUsers();
            }
        }

        /// <summary>Delete a custom group after confirm.</summary>
        private void DeleteGroup(string name)
        {
            // No selected group row.
            if (name.Length == 0)
                return;
            // Built-in groups cannot be removed.
            if (!AccessGroups.CanDelete(name))
            {
                MessageBox.Show(
                    "The " + name + " group cannot be deleted.",
                    "Groups",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }
            var ask = MessageBox.Show(
                "Delete group \"" + name + "\"? Users in it keep their accounts but lose the group.",
                "Delete group",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            // Accidental delete would drop users from the group.
            if (ask != DialogResult.Yes)
                return;
            // Members or a built-in name can block delete.
            if (!SqliteInventory.DeleteAccessGroup(name, out string error))
            {
                MessageBox.Show(error, "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadGroups();
            LoadUsers();
        }

        /// <summary>Small text prompt; null if they cancel.</summary>
        private string? PromptText(string title, string caption, string? value = null, string okText = "Create")
        {
            using var form = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(360, 140),
                BackColor = Theme.Cream,
                Font = Theme.Body
            };
            var label = new Label { Text = caption, Location = new Point(24, 16), AutoSize = true };
            Theme.StyleFieldLabel(label);
            var box = new TextBox
            {
                Location = new Point(24, 36),
                Size = new Size(312, 26),
                Text = value ?? ""
            };
            Theme.StyleField(box);
            var ok = new Button
            {
                Text = okText,
                DialogResult = DialogResult.OK,
                Size = new Size(90, 32),
                Location = new Point(150, 84)
            };
            Theme.StyleGoldButton(ok);
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 32),
                Location = new Point(246, 84)
            };
            Theme.StyleOutlineButton(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.Controls.Add(label);
            form.Controls.Add(box);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            return form.ShowDialog(this) == DialogResult.OK ? box.Text.Trim() : null;
        }

        /// <summary>Username in column 0 of the users grid.</summary>
        private string RowUser(int row) =>
            _grid.Rows[row].Cells[0].Value?.ToString()?.Trim() ?? "";

        /// <summary>Right-click menu: edit, access, password, lock, delete.</summary>
        private void ShowUserMenu(int row)
        {
            _grid.ClearSelection();
            _grid.Rows[row].Selected = true;
            string user = RowUser(row);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Edit user", null, (_, _) => EditUser(user));
            // Administrator-only settings and menu items.
            if (AppState.IsAdmin)
                menu.Items.Add("Data access", null, (_, _) =>
                {
                    if (UserAccessForm.ShowFor(this, user))
                        LoadUsers();
                });
            // You change your own password; IT resets everyone else.
            if (user.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                menu.Items.Add("Change my password", null, (_, _) =>
                {
                    using var change = new ChangePasswordForm(user, requireCurrent: true);
                    change.ShowDialog(this);
                });
            // Opposite branch of the condition above.
            else
                menu.Items.Add("Reset password", null, (_, _) => ResetPassword(user));
            // Locked accounts get Clear lock and Set password.
            if (Accounts.List().Any(a =>
                    a.Username.Equals(user, StringComparison.OrdinalIgnoreCase) && a.LoginLocked))
            {
                menu.Items.Add("Clear lock", null, (_, _) => ClearLoginLock(user));
                menu.Items.Add("Set password", null, (_, _) => SetCustomPassword(user));
            }

            menu.Items.Add("Delete user", null, (_, _) => DeleteUser(user));
            menu.Show(_grid, _grid.PointToClient(Control.MousePosition));
        }

        /// <summary>Add or edit a user, then reload lists.</summary>
        private void EditUser(string? username)
        {
            using var form = new ItUserEditForm(username);
            // Reload after a successful add/edit.
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                LoadUsers();
                LoadRoles();
            }
        }

        /// <summary>Generate a temporary password and email it when SMTP is set.</summary>
        private void ResetPassword(string username)
        {
            var confirm = MessageBox.Show(
                "Generate a new password for this user? We will email it if SMTP is set. You can also copy the details to send yourself. They will have to change it at next sign-in.",
                "Reset password",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            // Accidental reset/delete must not change the account.
            if (confirm != DialogResult.Yes)
                return;

            string temp = Accounts.GenerateTemporaryPassword();
            // Do not email a password we failed to store.
            if (!Accounts.SetPassword(username, temp, out string error, mustChange: true))
            {
                MessageBox.Show(error, "Reset password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string email = Accounts.List().FirstOrDefault(a =>
                a.Username.Equals(username, StringComparison.OrdinalIgnoreCase))?.Email ?? "";
            ItUserEditForm.SendLoginEmail(this, email, username, temp);
            LoadUsers();
        }

        /// <summary>Unlock a locked account after IT confirms the person.</summary>
        private void ClearLoginLock(string username)
        {
            // Non-IT callers or missing users are rejected.
            if (!Accounts.UnlockLogin(username, out string error))
            {
                MessageBox.Show(error, "Clear lock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ToastAlert.Success(this, username + " can sign in with their existing password.");
            LoadUsers();
        }

        /// <summary>Set a phone-dictated password that must change at sign-in.</summary>
        private void SetCustomPassword(string username)
        {
            // Cancel leaves the lock and old password.
            if (!PromptCustomPassword(username, out string password))
                return;
            // Policy failures keep the previous password.
            if (!Accounts.SetPassword(username, password, out string error, mustChange: true))
            {
                MessageBox.Show(error, "Set password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ToastAlert.Success(this, "Tell " + username + " the new password. They must change it at sign-in.");
            LoadUsers();
        }

        /// <summary>Ask for a new password twice; false if they cancel.</summary>
        private bool PromptCustomPassword(string username, out string password)
        {
            password = "";
            using var form = new Form
            {
                Text = "Set password",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(400, 210),
                BackColor = Theme.Cream,
                Font = Theme.Body
            };
            var hint = new Label
            {
                Text = "Give " + username + " this password on the phone (8+ characters, a capital, a number, and a symbol). They must change it at next sign-in. The lock is cleared.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 12),
                Size = new Size(350, 40)
            };
            var lbl = new Label { Text = "NEW PASSWORD", Location = new Point(24, 56), AutoSize = true };
            Theme.StyleFieldLabel(lbl);
            var box = new TextBox
            {
                Location = new Point(24, 74),
                Size = new Size(350, 26),
                UseSystemPasswordChar = true
            };
            Theme.StyleField(box);
            var lbl2 = new Label { Text = "CONFIRM", Location = new Point(24, 108), AutoSize = true };
            Theme.StyleFieldLabel(lbl2);
            var confirm = new TextBox
            {
                Location = new Point(24, 126),
                Size = new Size(350, 26),
                UseSystemPasswordChar = true
            };
            Theme.StyleField(confirm);
            var ok = new Button
            {
                Text = "Save",
                Size = new Size(90, 32),
                Location = new Point(190, 164)
            };
            Theme.StyleGoldButton(ok);
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 32),
                Location = new Point(286, 164)
            };
            Theme.StyleOutlineButton(cancel);
            string chosen = "";
            ok.Click += (_, _) =>
            {
                // Empty or mismatched passwords must not save.
                if (box.Text.Length == 0 || box.Text != confirm.Text)
                {
                    MessageBox.Show(
                        box.Text.Length == 0 ? "Enter a password." : "The passwords do not match.",
                        "Set password",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                    return;
                }

                chosen = box.Text;
                form.DialogResult = DialogResult.OK;
            };
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.Controls.Add(hint);
            form.Controls.Add(lbl);
            form.Controls.Add(box);
            form.Controls.Add(lbl2);
            form.Controls.Add(confirm);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            // Cancel returns no password.
            if (form.ShowDialog(this) != DialogResult.OK)
                return false;
            password = chosen;
            return password.Length > 0;
        }

        /// <summary>Delete a user after confirm, blocking last IT/admin and self.</summary>
        private void DeleteUser(string username)
        {
            var confirm = MessageBox.Show(
                "Delete user \"" + username + "\"? They will no longer be able to sign in.",
                "Delete user",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            // Accidental reset/delete must not change the account.
            if (confirm != DialogResult.Yes)
                return;

            // Last IT/admin or self-delete is blocked.
            if (!Accounts.DeleteUser(username, out string error))
            {
                MessageBox.Show(error, "Delete user", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadUsers();
            LoadRoles();
        }

        /// <summary>Administrator or IT list with Add and Remove.</summary>
        private CardPanel RoleCard(string title, bool admin, out ListBox list)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(admin ? 0 : 8, 0, admin ? 8 : 0, 0) };
            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 14)
            };
            list = new ListBox
            {
                Location = new Point(20, 44),
                Size = new Size(280, 120),
                Font = Theme.Body,
                BorderStyle = BorderStyle.FixedSingle
            };
            var add = new Button { Text = "Add", Size = new Size(80, 32) };
            var remove = new Button { Text = "Remove", Size = new Size(90, 32) };
            Theme.StyleGoldButton(add);
            Theme.StyleOutlineButton(remove);
            bool isAdmin = admin;
            var box = list;
            add.Click += (_, _) => AddRole(isAdmin);
            remove.Click += (_, _) => RemoveRole(isAdmin, box);
            card.Controls.Add(heading);
            card.Controls.Add(list);
            card.Controls.Add(add);
            card.Controls.Add(remove);
            card.Resize += (_, _) =>
            {
                box.Width = Math.Max(160, card.Width - 44);
                box.Height = Math.Max(60, card.Height - 110);
                add.Location = new Point(20, card.Height - 48);
                remove.Location = new Point(110, card.Height - 48);
            };
            return card;
        }

        /// <summary>Fill administrator and IT list boxes.</summary>
        private void LoadRoles()
        {
            // Role lists not built yet.
            if (_admins == null)
                return;
            FillRoles(_admins, Accounts.ReadAdmins());
            FillRoles(_it, Accounts.ReadIt());
        }

        /// <summary>Replace list-box items with sorted usernames.</summary>
        private static void FillRoles(ListBox box, List<string> names)
        {
            box.Items.Clear();
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                box.Items.Add(name);
        }

        /// <summary>Grant administrator or IT to a picked user.</summary>
        private void AddRole(bool admin)
        {
            var existing = new HashSet<string>(
                admin ? Accounts.ReadAdmins() : Accounts.ReadIt(),
                StringComparer.OrdinalIgnoreCase);
            var choices = Accounts.List()
                .Select(a => a.Username)
                .Where(name => !existing.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            // Every user already has this role.
            if (choices.Length == 0)
            {
                MessageBox.Show(
                    "Every user already has this access. Add a user first.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string? picked = PickUser(choices, admin ? "Add administrator" : "Add IT user");
            // Cancel on the picker leaves roles unchanged.
            if (string.IsNullOrWhiteSpace(picked))
                return;

            // Keep admins.json and the Admin group in sync.
            if (admin)
            {
                Accounts.AddAdmin(picked);
                SqliteInventory.AddAccessGroup(picked, AccessGroups.Admin);
            }
            // Opposite branch of the condition above.
            else
            {
                Accounts.AddIt(picked);
                SqliteInventory.AddAccessGroup(picked, AccessGroups.IT);
            }

            ApplyGroupIfCurrent(picked);
            LoadUsers();
            LoadRoles();
            LoadGroups();
        }

        /// <summary>Revoke administrator or IT from the selected name.</summary>
        private void RemoveRole(bool admin, ListBox box)
        {
            // Remove needs a selected name.
            if (box.SelectedItem is not string username)
                return;

            // Last remaining admin/IT is blocked.
            if (!(admin ? Accounts.RemoveAdmin(username, out string error) : Accounts.RemoveIt(username, out error)))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Keep admins.json and the Admin group in sync.
            if (admin)
                SqliteInventory.RemoveAccessGroup(username, AccessGroups.Admin);
            // Opposite branch of the condition above.
            else
                SqliteInventory.RemoveAccessGroup(username, AccessGroups.IT);

            if (username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                // Keep admins.json and the Admin group in sync.
                if (admin)
                    AppState.IsAdmin = Accounts.IsAdmin(username);
                // Opposite branch of the condition above.
                else
                    AppState.IsIt = Accounts.IsIt(username);
                TableAccess.Apply(username);
                AppLock.NotifyChanged();
            }

            LoadUsers();
            LoadRoles();
            LoadGroups();
        }

        /// <summary>Reload rights immediately when the signed-in user changed roles.</summary>
        private static void ApplyGroupIfCurrent(string username)
        {
            // Other users pick up the role at next sign-in.
            if (!username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                return;
            AppState.IsAdmin = Accounts.IsAdmin(username);
            AppState.IsIt = Accounts.IsIt(username);
            TableAccess.Apply(username);
            AppLock.NotifyChanged();
        }

        /// <summary>Modal combo to pick a username, or null if they cancel.</summary>
        private string? PickUser(string[] choices, string title)
        {
            using var form = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(360, 140),
                BackColor = Theme.Cream,
                Font = Theme.Body
            };
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(24, 28),
                Width = 312
            };
            Theme.StyleCombo(box);
            box.Items.AddRange(choices);
            // Preselect the first name so Enter adds without extra clicks.
            if (box.Items.Count > 0)
                box.SelectedIndex = 0;
            var ok = new Button
            {
                Text = "Add",
                DialogResult = DialogResult.OK,
                Size = new Size(90, 32),
                Location = new Point(150, 84)
            };
            Theme.StyleGoldButton(ok);
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 32),
                Location = new Point(246, 84)
            };
            Theme.StyleOutlineButton(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.Controls.Add(box);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            return form.ShowDialog(this) == DialogResult.OK ? box.SelectedItem as string : null;
        }
    }
}
