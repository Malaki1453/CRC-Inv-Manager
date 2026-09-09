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

        public AdminSettings()
        {
            Navigator.Register(AppPage.Admin, this);
            BuildUi();
        }

        /// <summary>IT and admins. Reloads users, roles, and admin-only settings.</summary>
        public void HighlightCurrentPage()
        {
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
            if (AppState.IsAdmin)
            {
                LoadBankFeed();
                LoadSession();
                if (_smtpDirty)
                    SaveMail();
                LoadMail();
            }
        }

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

        private void ApplyTabs()
        {
            if (_adminTab == null)
                return;
            bool showAdmin = AppState.IsAdmin;
            if (showAdmin && !_tabs.TabPages.Contains(_adminTab))
                _tabs.TabPages.Add(_adminTab);
            if (!showAdmin && _tabs.TabPages.Contains(_adminTab))
            {
                _tabs.SelectedIndex = 0;
                _tabs.TabPages.Remove(_adminTab);
            }
        }

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
                if (e.RowIndex >= 0)
                    EditUser(RowUser(e.RowIndex));
            };
            _grid.CellMouseClick += (_, e) =>
            {
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
                if (e.RowIndex >= 0)
                    EditGroup(GroupName(e.RowIndex));
            };
            _groupsGrid.CellMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;
                _groupsGrid.ClearSelection();
                _groupsGrid.Rows[e.RowIndex].Selected = true;
                string name = GroupName(e.RowIndex);
                var menu = new ContextMenuStrip();
                if (AccessGroups.CanEdit(name))
                    menu.Items.Add("Edit settings", null, (_, _) => EditGroup(name));
                if (AccessGroups.CanDelete(name))
                    menu.Items.Add("Delete group", null, (_, _) => DeleteGroup(name));
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

        private Control BuildPagesTab()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };
            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 56,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "This tree is the sidebar. A tab icon is a dropdown; everything else is a page. Drag to move. A gold line shows where it will land; a gold box means it will nest under that row."
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
            rename.Click += (_, _) => RenameMenuFolder();
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

        private void LoadPages()
        {
            if (_menuTree == null)
                return;
            _menu = MenuLayout.Load();
            FillMenuTree();
        }

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

        private static TreeNode MakeTreeNode(MenuNode node)
        {
            var tree = new TreeNode(node.Title) { Tag = node, Checked = node.On };
            foreach (var child in node.Children)
                tree.Nodes.Add(MakeTreeNode(child));
            return tree;
        }

        private static MenuNode? NodeOf(TreeNode? tree) => tree?.Tag as MenuNode;

        private void SaveMenu()
        {
            _menu.Save();
            AppLock.NotifyChanged();
        }

        private void AddMenuFolder()
        {
            string? name = PromptText("New tab", "TAB NAME");
            if (string.IsNullOrWhiteSpace(name))
                return;
            var folder = MenuNode.Folder(name.Trim());
            var selected = NodeOf(_menuTree.SelectedNode);
            if (selected is { IsFolder: true })
                selected.Children.Add(folder);
            else if (selected == null)
                _menu.Root.Add(folder);
            else
            {
                var loc = _menu.Locate(selected);
                int index = loc.Index < 0 ? loc.Siblings.Count : loc.Index + 1;
                loc.Siblings.Insert(Math.Clamp(index, 0, loc.Siblings.Count), folder);
            }

            SaveMenu();
            FillMenuTree();
        }

        private void RenameMenuFolder()
        {
            var node = NodeOf(_menuTree.SelectedNode);
            if (node is not { IsFolder: true })
            {
                MessageBox.Show("Select a tab to rename.", "Pages", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }

            string? name = PromptText("Rename tab", "TAB NAME");
            if (string.IsNullOrWhiteSpace(name))
                return;
            node.Name = name.Trim();
            SaveMenu();
            FillMenuTree();
        }

        private void DeleteMenuNode()
        {
            var node = NodeOf(_menuTree.SelectedNode);
            if (node == null)
                return;
            var loc = _menu.Locate(node);
            if (loc.Index < 0)
                return;
            loc.Siblings.RemoveAt(loc.Index);
            if (node.IsFolder)
            {
                loc.Siblings.InsertRange(loc.Index, node.Children);
            }

            SaveMenu();
            FillMenuTree();
        }

        private void MenuTreeAfterCheck(object? sender, TreeViewEventArgs e)
        {
            if (_menuLoading || e.Node == null)
                return;
            var node = NodeOf(e.Node);
            if (node == null)
                return;
            _menu.SetOn(node, e.Node.Checked);
            SaveMenu();
            FillMenuTree();
        }

        private void MenuTreeItemDrag(object? sender, ItemDragEventArgs e)
        {
            if (e.Item is not TreeNode node)
                return;
            _menuDrag = node;
            _menuTree.DoDragDrop(node, DragDropEffects.Move);
        }

        private void MenuTreeDragOver(object? sender, DragEventArgs e)
        {
            if (e.Data?.GetDataPresent(typeof(TreeNode)) != true)
            {
                e.Effect = DragDropEffects.None;
                ClearMenuDropHint();
                return;
            }

            var point = _menuTree.PointToClient(new Point(e.X, e.Y));
            if (!TryMenuDrop(point, out var target, out var kind))
            {
                e.Effect = DragDropEffects.None;
                ClearMenuDropHint();
                return;
            }

            e.Effect = DragDropEffects.Move;
            SetMenuDropHint(target, kind);
        }

        private void MenuTreeDragDrop(object? sender, DragEventArgs e)
        {
            var sourceNode = _menuDrag ?? e.Data?.GetData(typeof(TreeNode)) as TreeNode;
            var point = _menuTree.PointToClient(new Point(e.X, e.Y));
            bool hit = TryMenuDrop(point, out var targetTree, out var kind);
            ClearMenuDropHint();
            _menuDrag = null;
            var source = NodeOf(sourceNode);
            if (source == null || !hit)
                return;

            var target = NodeOf(targetTree);
            if (target == null || targetTree == null)
            {
                _menu.Move(source, _menu.Root, _menu.Root.Count);
                SaveMenu();
                FillMenuTree();
                return;
            }

            if (source == target || _menu.IsDescendant(source, target))
                return;

            if (kind == MenuDropKind.Inside && target.IsFolder)
            {
                if (_menu.DepthOf(target) >= 2 && source.IsFolder)
                    return;
                _menu.Move(source, target.Children, target.Children.Count);
            }
            else if (kind == MenuDropKind.Inside && !target.IsFolder && _menu.DepthOf(target) < 2)
            {
                var folder = _menu.WrapInFolder(target, target.Title);
                if (!_menu.IsDescendant(source, folder))
                    _menu.Move(source, folder.Children, folder.Children.Count);
            }
            else
            {
                var loc = _menu.Locate(target);
                if (loc.Index < 0)
                    return;
                int index = kind == MenuDropKind.Before ? loc.Index : loc.Index + 1;
                _menu.Move(source, loc.Siblings, index);
            }

            SaveMenu();
            FillMenuTree();
        }

        private bool TryMenuDrop(Point client, out TreeNode? target, out MenuDropKind kind)
        {
            target = _menuTree.GetNodeAt(client);
            kind = MenuDropKind.None;
            var source = _menuDrag;
            if (source == null)
                return false;
            if (target == null)
            {
                target = LastTreeNode(_menuTree.Nodes);
                kind = MenuDropKind.After;
                return target != source && !IsTreeDescendant(source, target);
            }

            if (target == source || IsTreeDescendant(source, target))
                return false;

            var bounds = NodeRow(target);
            int y = client.Y - bounds.Top;
            if (y > bounds.Height / 4 && y < (bounds.Height * 3) / 4)
                kind = MenuDropKind.Inside;
            else
                kind = y < bounds.Height / 2 ? MenuDropKind.Before : MenuDropKind.After;
            return true;
        }

        private void SetMenuDropHint(TreeNode? target, MenuDropKind kind)
        {
            if (_menuDropTarget == target && _menuDropKind == kind)
                return;
            _menuDropTarget = target;
            _menuDropKind = kind;
            _menuTree.DropTarget = target;
            _menuTree.DropKind = kind;
            _menuTree.Invalidate();
        }

        private void ClearMenuDropHint()
        {
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

            public MenuHintTree()
            {
                Theme.EnableDoubleBuffer(this);
            }

            protected override void OnDrawNode(DrawTreeNodeEventArgs e)
            {
                var node = e.Node;
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
                if (folder)
                    DrawChevron(g, parts.Chevron, node.IsExpanded, on);
                DrawCheck(g, parts.Check, on);
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

            protected override void OnMouseDown(MouseEventArgs e)
            {
                var node = GetNodeAt(e.Location);
                if (node != null && e.Button == MouseButtons.Left)
                {
                    var parts = LayoutNode(node, NodeRow(node), node.Tag is MenuNode { IsFolder: true });
                    if (parts.Chevron.Contains(e.Location) && node.Tag is MenuNode { IsFolder: true })
                    {
                        node.Toggle();
                        Invalidate();
                        return;
                    }

                    if (parts.Check.Contains(e.Location))
                    {
                        SelectedNode = node;
                        node.Checked = !node.Checked;
                        return;
                    }
                }

                base.OnMouseDown(e);
            }

            protected override void WndProc(ref Message m)
            {
                base.WndProc(ref m);
                if (m.Msg != 0x000F || DropKind == MenuDropKind.None)
                    return;
                using var g = Graphics.FromHwnd(Handle);
                g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                var row = DropTarget == null
                    ? new Rectangle(8, Math.Max(4, ClientSize.Height - 6), ClientSize.Width - 16, 0)
                    : NodeRow(DropTarget);
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

            private NodeParts LayoutNode(TreeNode node, Rectangle row, bool folder)
            {
                int x = 12 + node.Level * 22;
                int mid = row.Y + row.Height / 2;
                var chevron = new Rectangle(x, mid - 7, 14, 14);
                x += 18;
                var check = new Rectangle(x, mid - 8, 16, 16);
                x += 22;
                var icon = folder ? new Rectangle(x, mid - 9, 18, 18) : Rectangle.Empty;
                if (folder)
                    x += 24;
                var text = new Rectangle(x, row.Y, Math.Max(20, row.Right - x - 8), row.Height);
                return new NodeParts(chevron, check, icon, text);
            }

            private readonly record struct NodeParts(Rectangle Chevron, Rectangle Check, Rectangle Icon, Rectangle Text);

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

            private static void DrawCheck(Graphics g, Rectangle box, bool on)
            {
                using var border = new Pen(on ? Theme.Navy : Theme.CreamDark, 1.5f);
                using var fill = new SolidBrush(on ? Theme.Paper : Theme.CreamDark);
                g.FillRectangle(fill, box);
                g.DrawRectangle(border, box);
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

        private static Rectangle NodeRow(TreeNode node)
        {
            var bounds = node.Bounds;
            return new Rectangle(0, bounds.Y, node.TreeView?.ClientSize.Width ?? bounds.Width, Math.Max(bounds.Height, 22));
        }

        private static TreeNode? LastTreeNode(TreeNodeCollection nodes)
        {
            if (nodes.Count == 0)
                return null;
            var node = nodes[^1];
            while (node.IsExpanded && node.Nodes.Count > 0)
                node = node.Nodes[^1];
            return node;
        }

        private static bool IsTreeDescendant(TreeNode ancestor, TreeNode? node)
        {
            while (node != null)
            {
                if (node.Parent == ancestor)
                    return true;
                node = node.Parent;
            }

            return false;
        }

        private void MenuTreeMouseDown(object? sender, MouseEventArgs e)
        {
            var hit = _menuTree.GetNodeAt(e.Location);
            if (hit != null)
                _menuTree.SelectedNode = hit;
            if (e.Button != MouseButtons.Right || hit == null)
                return;
            var node = NodeOf(hit);
            var menu = new ContextMenuStrip();
            if (node is { IsFolder: true })
                menu.Items.Add("Rename", null, (_, _) => RenameMenuFolder());
            menu.Items.Add("Delete", null, (_, _) => DeleteMenuNode());
            menu.Show(_menuTree, e.Location);
        }

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
                Text = "Stay signed in, login email (SMTP), and the live bank feed are administrator-only. They apply to everyone."
            };
            var session = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutSessionCard(session);
            var mail = new CardPanel { Dock = DockStyle.Top, Height = 280 };
            LayoutMailCard(mail);
            _bankCard = new CardPanel { Dock = DockStyle.Top, Height = 210 };
            LayoutBankCard(_bankCard);
            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var spacerMail = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            host.Controls.Add(_bankCard);
            host.Controls.Add(spacer);
            host.Controls.Add(mail);
            host.Controls.Add(spacerMail);
            host.Controls.Add(session);
            host.Controls.Add(intro);
            return host;
        }

        private static void PaintAdminTab(object? sender, DrawItemEventArgs e)
        {
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
            if (selected)
            {
                using var gold = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(gold, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3);
            }
        }

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

        private void LoadSession()
        {
            if (_stayToggle == null)
                return;
            _stayToggle.SetOn(AppState.StaySignedInEnabled);
            SelectChoice(_sessionDays, AppState.StaySignedInDays, 30);
            SelectChoice(_idleHours, AppState.IdleCloseHours, 5);
            ApplySessionEnabled();
        }

        private void SaveSession()
        {
            AppState.StaySignedInEnabled = _stayToggle.On;
            AppState.StaySignedInDays = _sessionDays.SelectedItem is IntChoice days ? days.Value : 30;
            AppState.IdleCloseHours = _idleHours.SelectedItem is IntChoice hours ? hours.Value : 5;
            ApplySessionEnabled();
            AppLock.SaveSettings();
            if (!AppState.StaySignedInEnabled)
                IdleWatch.Stop();
            else if (AppState.StaySignedIn)
                IdleWatch.Start();
        }

        private void ApplySessionEnabled()
        {
            bool on = _stayToggle.On;
            _sessionDays.Enabled = on;
            _idleHours.Enabled = on;
            if (Controls.Find("lblStayOnOff", true).FirstOrDefault() is Label label)
                label.Text = on ? "On" : "Off";
        }

        private static void SelectChoice(ComboBox box, int value, int fallback)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is IntChoice choice && choice.Value == value)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is IntChoice choice && choice.Value == fallback)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            if (box.Items.Count > 0)
                box.SelectedIndex = 0;
        }

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
                if (_loadingMail)
                    return;
                _smtpPasswordFresh = _smtpPassword.Text.Length > 0;
                if (!_smtpPasswordFresh)
                    HideSmtpPassword();
                _smtpPassHint.Text = "";
                _smtpDirty = true;
            };
        }

        private void MarkSmtpDirty()
        {
            if (_loadingMail)
                return;
            _smtpDirty = true;
            if (_smtpPassHint != null && _smtpPassHint.ForeColor == Theme.Success)
                _smtpPassHint.Text = "";
        }

        private void ToggleSmtpPassword()
        {
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

        private void HideSmtpPassword()
        {
            _smtpPassword.UseSystemPasswordChar = true;
            _smtpShow.Text = "Show";
        }

        private void LoadMail()
        {
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

        private void SaveMail(bool announce = false)
        {
            if (_smtpUser == null)
                return;
            if (!AppState.IsAdmin)
            {
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
            if (int.TryParse(_smtpPort.Text.Trim(), out int parsed) && parsed > 0)
                port = parsed;

            bool ok = SqliteInventory.SaveAdminSmtp(email, password, host, port, out string error);
            if (ok)
                _smtpDirty = false;

            if (!announce)
                return;

            Control toastHost = FindForm() ?? this;
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
            public IntChoice(int value, string label)
            {
                Value = value;
                Label = label;
            }

            public int Value { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }

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

        private void ShowKeys(bool show)
        {
            _keysPanel.Visible = show;
            _keysToggle.Text = show ? "Hide keys" : "API keys";
            _bankCard.Height = show ? 430 : 210;
        }

        private sealed class SyncChoice
        {
            public SyncChoice(int hours, string label)
            {
                Hours = hours;
                Label = label;
            }

            public int Hours { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }

        private void SaveBankFeedQuiet()
        {
            AppState.PlaidClientId = _plaidId.Text.Trim();
            AppState.PlaidSecret = _plaidSecret.Text.Trim();
            AppState.PlaidEnv = _plaidEnv.SelectedItem?.ToString() ?? "sandbox";
            AppState.PlaidSyncHours = _plaidSync.SelectedItem is SyncChoice choice ? choice.Hours : 1;
            AppLock.SaveSettings();
            BankLiveWatch.Start();
        }

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

        private void SaveBankFeed()
        {
            SaveBankFeedQuiet();
            ToastAlert.Success(this, "Live bank feed settings were saved.");
        }

        private void LoadUsers()
        {
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
                if (account.LoginLocked)
                    _grid.Rows[row].DefaultCellStyle.BackColor = Theme.DangerFill;
            }
        }

        private void LoadGroups()
        {
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

        private string GroupName(int row) =>
            _groupsGrid.Rows[row].Cells[0].Value?.ToString()?.Trim() ?? "";

        private void AddGroup()
        {
            string? name = PromptText("New group", "GROUP NAME");
            if (string.IsNullOrWhiteSpace(name))
                return;
            if (AccessGroups.IsBuiltIn(name))
            {
                MessageBox.Show("Admin and IT are built-in groups.", "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (SqliteInventory.ListAccessGroups().Any(g =>
                    g.Name.Equals(name, StringComparison.OrdinalIgnoreCase)))
            {
                MessageBox.Show("That group already exists.", "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!SqliteInventory.SaveAccessGroup(name, "", out string error))
            {
                MessageBox.Show(error, "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            UserAccessForm.ShowGroup(this, name);
            LoadGroups();
            LoadUsers();
        }

        private void EditGroup(string name)
        {
            if (name.Length == 0)
                return;
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

            if (UserAccessForm.ShowGroup(this, name))
            {
                LoadGroups();
                LoadUsers();
            }
        }

        private void DeleteGroup(string name)
        {
            if (name.Length == 0)
                return;
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
            if (ask != DialogResult.Yes)
                return;
            if (!SqliteInventory.DeleteAccessGroup(name, out string error))
            {
                MessageBox.Show(error, "Groups", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadGroups();
            LoadUsers();
        }

        private string? PromptText(string title, string caption)
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
            var box = new TextBox { Location = new Point(24, 36), Size = new Size(312, 26) };
            Theme.StyleField(box);
            var ok = new Button
            {
                Text = "Create",
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

        private string RowUser(int row) =>
            _grid.Rows[row].Cells[0].Value?.ToString()?.Trim() ?? "";

        private void ShowUserMenu(int row)
        {
            _grid.ClearSelection();
            _grid.Rows[row].Selected = true;
            string user = RowUser(row);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Edit user", null, (_, _) => EditUser(user));
            if (AppState.IsAdmin)
                menu.Items.Add("Data access", null, (_, _) =>
                {
                    if (UserAccessForm.ShowFor(this, user))
                        LoadUsers();
                });
            if (user.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                menu.Items.Add("Change my password", null, (_, _) =>
                {
                    using var change = new ChangePasswordForm(user, requireCurrent: true);
                    change.ShowDialog(this);
                });
            else
                menu.Items.Add("Reset password", null, (_, _) => ResetPassword(user));
            if (Accounts.List().Any(a =>
                    a.Username.Equals(user, StringComparison.OrdinalIgnoreCase) && a.LoginLocked))
            {
                menu.Items.Add("Clear lock", null, (_, _) => ClearLoginLock(user));
                menu.Items.Add("Set password", null, (_, _) => SetCustomPassword(user));
            }

            menu.Items.Add("Delete user", null, (_, _) => DeleteUser(user));
            menu.Show(_grid, _grid.PointToClient(Control.MousePosition));
        }

        private void EditUser(string? username)
        {
            using var form = new ItUserEditForm(username);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                LoadUsers();
                LoadRoles();
            }
        }

        private void ResetPassword(string username)
        {
            var confirm = MessageBox.Show(
                "Generate a new password for this user? We will email it if SMTP is set. You can also copy the details to send yourself. They will have to change it at next sign-in.",
                "Reset password",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            string temp = Accounts.GenerateTemporaryPassword();
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

        private void ClearLoginLock(string username)
        {
            if (!Accounts.UnlockLogin(username, out string error))
            {
                MessageBox.Show(error, "Clear lock", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ToastAlert.Success(this, username + " can sign in with their existing password.");
            LoadUsers();
        }

        private void SetCustomPassword(string username)
        {
            if (!PromptCustomPassword(username, out string password))
                return;
            if (!Accounts.SetPassword(username, password, out string error, mustChange: true))
            {
                MessageBox.Show(error, "Set password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            ToastAlert.Success(this, "Tell " + username + " the new password. They must change it at sign-in.");
            LoadUsers();
        }

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
            if (form.ShowDialog(this) != DialogResult.OK)
                return false;
            password = chosen;
            return password.Length > 0;
        }

        private void DeleteUser(string username)
        {
            var confirm = MessageBox.Show(
                "Delete user \"" + username + "\"? They will no longer be able to sign in.",
                "Delete user",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            if (!Accounts.DeleteUser(username, out string error))
            {
                MessageBox.Show(error, "Delete user", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadUsers();
            LoadRoles();
        }

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

        private void LoadRoles()
        {
            if (_admins == null)
                return;
            FillRoles(_admins, Accounts.ReadAdmins());
            FillRoles(_it, Accounts.ReadIt());
        }

        private static void FillRoles(ListBox box, List<string> names)
        {
            box.Items.Clear();
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                box.Items.Add(name);
        }

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
            if (string.IsNullOrWhiteSpace(picked))
                return;

            if (admin)
            {
                Accounts.AddAdmin(picked);
                SqliteInventory.AddAccessGroup(picked, AccessGroups.Admin);
            }
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

        private void RemoveRole(bool admin, ListBox box)
        {
            if (box.SelectedItem is not string username)
                return;

            if (!(admin ? Accounts.RemoveAdmin(username, out string error) : Accounts.RemoveIt(username, out error)))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (admin)
                SqliteInventory.RemoveAccessGroup(username, AccessGroups.Admin);
            else
                SqliteInventory.RemoveAccessGroup(username, AccessGroups.IT);

            if (username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                if (admin)
                    AppState.IsAdmin = Accounts.IsAdmin(username);
                else
                    AppState.IsIt = Accounts.IsIt(username);
                TableAccess.Apply(username);
                AppLock.NotifyChanged();
            }

            LoadUsers();
            LoadRoles();
            LoadGroups();
        }

        private static void ApplyGroupIfCurrent(string username)
        {
            if (!username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                return;
            AppState.IsAdmin = Accounts.IsAdmin(username);
            AppState.IsIt = Accounts.IsIt(username);
            TableAccess.Apply(username);
            AppLock.NotifyChanged();
        }

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
