namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Hosts each <see cref="AppPage"/> as a nested form. Extra windows are more workspaces
    /// that can steal a page from the main window.
    /// </summary>
    public static class Navigator
    {
        private static readonly Dictionary<AppPage, Form> _instances = new();
        private static readonly List<Workspace> _extras = new();

        private static readonly Dictionary<AppPage, Func<Form>> _factories = new()
        {
            [AppPage.Dashboard]     = () => new Dashboard(),
            [AppPage.PurchaseSales] = () => new PurchaseSales(),
            [AppPage.AddPurchase]   = () => new AddPurchase(),
            [AppPage.Sales]         = () => new Sales(),
            [AppPage.SalesOrder]    = () => new SalesOrder(),
            [AppPage.Customers]     = () => new Customers(),
            [AppPage.Vendors]       = () => new Vendors(),
            [AppPage.ItemCodes]     = () => new ItemCodes(),
            [AppPage.Invoicing]     = () => new Invoicing(),
            [AppPage.InvoicePdf]    = () => new InvoicePdf(),
            [AppPage.Debits]        = () => new Debits(),
            [AppPage.Credits]       = () => new Credits(),
            [AppPage.Banking]       = () => new Banking(),
            [AppPage.Reports]       = () => new Reports(),
            [AppPage.PendingChanges] = () => new PendingChanges(),
            [AppPage.Settings]      = () => new Settings(),
            [AppPage.Help]          = () => new Help(),
            [AppPage.ItUsers]       = () => new ItUsersForm(),
            [AppPage.ItAccess]      = () => new ItAccessForm(),
            [AppPage.Admin]         = () => new AdminSettings()
        };

        public static event Action<AppPage>? PageChanged;
        public static AppPage CurrentPage { get; private set; } = AppPage.Dashboard;

        /// <summary>Reload every visible page after data or the Current/Old toggle changes.</summary>
        public static void RefreshOpenPages()
        {
            foreach (var workspace in AllWorkspaces())
            {
                if (!workspace.IsAlive || workspace.CurrentPage == null)
                    continue;

                if (_instances.TryGetValue(workspace.CurrentPage.Value, out var form) &&
                    form != null && !form.IsDisposed)
                    Highlight(form);

                workspace.RefreshChrome();
            }
        }

        private static Workspace? _main;
        private static Workspace? _active;
        private static Workspace? _lastExtra;
        private static bool _movingHistory;
        private static bool _historyFilterAdded;

        internal static Workspace AttachMain(Form window, Panel host, Action<AppPage?> updateChrome)
        {
            _main = new Workspace(window, host, isMain: true)
            {
                UpdateChrome = updateChrome
            };
            BindWindow(_main);
            _active = _main;
            EnsureHistoryFilter();
            return _main;
        }

        internal static Workspace? ActiveWorkspace => _active ?? _main;

        public static void GoBack() => GoBack(_active ?? _main);

        public static void GoForward() => GoForward(_active ?? _main);

        internal static void GoBack(Workspace? workspace)
        {
            workspace ??= _active ?? _main;
            if (workspace == null || !workspace.IsAlive)
                return;

            var page = workspace.TakeBack();
            if (page == null)
                return;

            MoveHistory(workspace, page.Value);
        }

        internal static void GoForward(Workspace? workspace)
        {
            workspace ??= _active ?? _main;
            if (workspace == null || !workspace.IsAlive)
                return;

            var page = workspace.TakeForward();
            if (page == null)
                return;

            MoveHistory(workspace, page.Value);
        }

        private static void MoveHistory(Workspace workspace, AppPage page)
        {
            _movingHistory = true;
            try
            {
                GoTo(page, workspace, reuseOpenWindow: false);
            }
            finally
            {
                _movingHistory = false;
            }
        }

        private static void EnsureHistoryFilter()
        {
            if (_historyFilterAdded)
                return;
            Application.AddMessageFilter(new HistoryInputFilter());
            _historyFilterAdded = true;
        }

        public static void Register(AppPage page, Form form)
        {
            PrepareAsPage(form);
            _instances[page] = form;
        }

        public static bool IsRegistered(AppPage page)
        {
            return _instances.TryGetValue(page, out var form) && form != null && !form.IsDisposed;
        }

        public static bool IsOpen(AppPage page)
        {
            foreach (var workspace in AllWorkspaces())
            {
                if (workspace.IsAlive && workspace.CurrentPage == page)
                    return true;
            }

            return false;
        }

        public static T Ensure<T>(AppPage page) where T : Form
        {
            var target = GetInstance(page);

            if (target.Parent == null && _main != null && _main.IsAlive)
            {
                PrepareAsPage(target);
                _main.Host.Controls.Add(target);
            }

            if (!target.IsHandleCreated)
                target.CreateControl();

            return (T)target;
        }

        public static void GoTo(AppPage page)
        {
            GoTo(page, _active ?? _main);
        }

        internal static void GoTo(AppPage page, Workspace? workspace, bool reuseOpenWindow = true)
        {
            workspace ??= _active ?? _main;
            if (workspace == null || !workspace.IsAlive)
                throw new InvalidOperationException("Navigator host has not been set.");

            if (page == AppPage.AddSale)
                page = AppPage.SalesOrder;
            if (page == AppPage.ItUsers || page == AppPage.ItAccess)
                page = AppPage.Admin;
            if (page == AppPage.Admin && !AppState.IsAdmin && !AppState.IsIt)
                page = AppPage.Settings;
            if (!TableAccess.CanPage(page))
            {
                MessageBox.Show(
                    "You do not have access to that table.",
                    "Access",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                page = AppPage.Dashboard;
            }

            if (reuseOpenWindow && TryFocusOther(page, workspace))
                return;

            ShowIn(workspace, page);
        }

        public static void OpenDetached(AppPage page) => OpenDetached(page, null);

        internal static void OpenDetached(AppPage page, Workspace? from)
        {
            if (_main == null || !_main.IsAlive)
                throw new InvalidOperationException("Navigator host has not been set.");

            if (!TableAccess.CanPage(page))
            {
                MessageBox.Show(
                    "You do not have access to that table.",
                    "Access",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            if (TryFocusOther(page, from))
                return;

            var extra = CreateExtra();
            ShowIn(extra, page);

            var owner = from is { IsAlive: true } ? from.Window : _main.Window;
            extra.Window.StartPosition = FormStartPosition.Manual;
            int offset = 36 * _extras.Count;
            extra.Window.Location = new Point(owner.Left + offset, owner.Top + offset);
            extra.Window.Size = owner.Size;
            extra.Window.ShowInTaskbar = true;
            extra.Window.Show();
            FocusWorkspace(extra);
        }

        internal static bool TryFocus(AppPage page)
        {
            var workspace = WorkspaceShowing(page);
            if (workspace == null)
                return false;
            FocusWorkspace(workspace);
            return true;
        }

        internal static IReadOnlyList<AppPage> ListOpenPages()
        {
            var pages = new List<AppPage>();
            foreach (var workspace in AllWorkspaces())
            {
                if (workspace.IsAlive && workspace.CurrentPage is AppPage page)
                    pages.Add(page);
            }

            return pages;
        }

        internal static void Activate(Workspace? workspace)
        {
            if (workspace == null || !workspace.IsAlive)
                return;

            _active = workspace;
            if (!workspace.IsMain)
                _lastExtra = workspace;
            if (workspace.CurrentPage is AppPage page)
                CurrentPage = page;
        }

        private static void BindWindow(Workspace workspace)
        {
            workspace.Window.Activated += (_, _) => Activate(workspace);
            workspace.Window.FormClosing += (_, e) =>
            {
                if (e.Cancel)
                    return;
                if (workspace.IsMain)
                    TryPromote(workspace);
                else
                    CloseExtra(workspace);
            };
            workspace.Window.FormClosed += (_, _) => OnWindowClosed(workspace);
        }

        private static Workspace CreateExtra()
        {
            var window = new PageWindow();
            var extra = new Workspace(window, window.Host, isMain: false)
            {
                UpdateChrome = window.SetChrome
            };
            var sidebar = new NavSidebar(extra);
            extra.Sidebar = sidebar;
            window.BindHistory(extra);
            window.Controls.Add(sidebar);

            BindWindow(extra);
            _extras.Add(extra);
            return extra;
        }

        private static bool TryPromote(Workspace retiring)
        {
            var successor = PickSuccessor(retiring);
            if (successor == null)
                return false;

            foreach (var extra in _extras)
            {
                if (extra.IsAlive)
                    extra.Window.Owner = null;
            }

            SalvagePages(retiring, successor);

            successor.IsMain = true;
            retiring.IsMain = false;
            _extras.Remove(successor);
            _main = successor;
            if (_lastExtra == successor)
                _lastExtra = null;

            if (successor.Window.WindowState == FormWindowState.Minimized)
                successor.Window.WindowState = FormWindowState.Normal;
            successor.Window.ShowInTaskbar = true;
            successor.Window.BringToFront();
            successor.Window.Activate();
            Activate(successor);
            return true;
        }

        private static Workspace? PickSuccessor(Workspace retiring)
        {
            if (_lastExtra != null &&
                _lastExtra.IsAlive &&
                _lastExtra != retiring &&
                _extras.Contains(_lastExtra))
                return _lastExtra;

            foreach (var extra in _extras)
            {
                if (extra.IsAlive && extra != retiring)
                    return extra;
            }

            return null;
        }

        private static void SalvagePages(Workspace from, Workspace to)
        {
            if (!from.IsAlive || !to.IsAlive)
                return;

            Form? keepVisible = null;
            if (to.CurrentPage is AppPage keep &&
                _instances.TryGetValue(keep, out var shown) &&
                shown.Parent == to.Host)
                keepVisible = shown;

            var forms = new List<Form>();
            foreach (Control control in from.Host.Controls)
            {
                if (control is Form form && !form.IsDisposed)
                    forms.Add(form);
            }

            foreach (var form in forms)
            {
                from.Host.Controls.Remove(form);
                PrepareAsPage(form);
                if (!to.Host.Controls.Contains(form))
                    to.Host.Controls.Add(form);
            }

            if (keepVisible != null && !keepVisible.IsDisposed)
            {
                keepVisible.Visible = true;
                keepVisible.BringToFront();
                return;
            }

            if (to.CurrentPage == null && from.CurrentPage != null)
                ShowIn(to, from.CurrentPage.Value);
        }

        private static void OnWindowClosed(Workspace workspace)
        {
            if (workspace.IsMain && _main == workspace)
                _main = null;

            _extras.Remove(workspace);

            if (_lastExtra == workspace)
                _lastExtra = null;

            if (_active == workspace)
                Activate(_main);

            RefreshAllChrome();

            if (!HasAliveWindow())
                Application.ExitThread();
        }

        private static bool HasAliveWindow()
        {
            foreach (var workspace in AllWorkspaces())
            {
                if (workspace.IsAlive)
                    return true;
            }

            return false;
        }

        private static void ShowIn(Workspace dest, AppPage page, bool activate = true, bool recordHistory = true)
        {
            if (!dest.IsAlive)
                return;

            var form = GetInstance(page);
            var previous = OwnerOf(form);

            if (dest.CurrentPage == page && form.Parent == dest.Host && form.Visible)
            {
                if (activate)
                {
                    Activate(dest);
                    PageChanged?.Invoke(page);
                }
                Highlight(form);
                dest.RefreshChrome();
                return;
            }

            if (recordHistory && !_movingHistory && dest.CurrentPage is AppPage from && from != page)
                dest.PushHistory(from);

            bool stoleCurrent = previous != null &&
                                previous != dest &&
                                previous.CurrentPage == page;

            HidePages(dest.Host);

            if (form.Parent != null && form.Parent != dest.Host)
                form.Parent.Controls.Remove(form);

            PrepareAsPage(form);
            form.Visible = true;
            if (!dest.Host.Controls.Contains(form))
                dest.Host.Controls.Add(form);
            form.BringToFront();

            dest.CurrentPage = page;
            Highlight(form);

            if (stoleCurrent && previous != null && previous.IsAlive)
                Recover(previous);

            RefreshAllChrome();

            if (activate)
            {
                Activate(dest);
                PageChanged?.Invoke(page);
            }
        }

        private static void Recover(Workspace workspace)
        {
            foreach (Control control in workspace.Host.Controls)
            {
                if (control is Form form && !form.IsDisposed)
                {
                    var page = PageOf(form);
                    if (page == null)
                        continue;

                    HidePages(workspace.Host);
                    form.Visible = true;
                    form.BringToFront();
                    workspace.CurrentPage = page;
                    Highlight(form);
                    workspace.RefreshChrome();
                    return;
                }
            }

            if (workspace.IsMain)
            {
                ShowIn(workspace, PickFallback(workspace), activate: false, recordHistory: false);
                return;
            }

            workspace.CurrentPage = null;
            ShowEmpty(workspace);
            workspace.RefreshChrome();
        }

        private static AppPage PickFallback(Workspace dest)
        {
            foreach (var page in new[] { AppPage.Dashboard, AppPage.Settings, AppPage.Reports })
            {
                if (!IsShownElsewhere(page, dest))
                    return page;
            }

            return AppPage.Settings;
        }

        internal static bool IsShownElsewhere(AppPage page, Workspace? dest)
        {
            foreach (var workspace in AllWorkspaces())
            {
                if (workspace != dest && workspace.IsAlive && workspace.CurrentPage == page)
                    return true;
            }

            return false;
        }

        private static Workspace? WorkspaceShowing(AppPage page)
        {
            foreach (var workspace in AllWorkspaces())
            {
                if (workspace.IsAlive && workspace.CurrentPage == page)
                    return workspace;
            }

            return null;
        }

        private static bool TryFocusOther(AppPage page, Workspace? from)
        {
            var existing = WorkspaceShowing(page);
            if (existing == null || existing == from || !existing.IsAlive)
                return false;
            FocusWorkspace(existing);
            return true;
        }

        private static void FocusWorkspace(Workspace workspace)
        {
            if (!workspace.IsAlive)
                return;
            if (workspace.Window.WindowState == FormWindowState.Minimized)
                workspace.Window.WindowState = FormWindowState.Normal;
            workspace.Window.Show();
            workspace.Window.BringToFront();
            workspace.Window.Activate();
            Activate(workspace);
        }

        private static void RefreshAllChrome()
        {
            foreach (var workspace in AllWorkspaces())
            {
                if (workspace.IsAlive)
                    workspace.RefreshChrome();
            }
        }

        private static void CloseExtra(Workspace extra)
        {
            _extras.Remove(extra);
            if (_active == extra)
                Activate(_main);

            if (_main == null || !_main.IsAlive)
                return;

            var forms = new List<Form>();
            foreach (Control control in extra.Host.Controls)
            {
                if (control is Form form && !form.IsDisposed)
                    forms.Add(form);
            }

            foreach (var form in forms)
            {
                extra.Host.Controls.Remove(form);
                PrepareAsPage(form);
                if (!_main.Host.Controls.Contains(form))
                    _main.Host.Controls.Add(form);
            }

            if (_main.CurrentPage == null)
            {
                AppPage? restore = extra.CurrentPage;
                if (restore == null && forms.Count > 0)
                    restore = PageOf(forms[0]);

                ShowIn(_main, restore ?? PickFallback(_main));
            }
        }

        private static Form GetInstance(AppPage page)
        {
            if (!_instances.TryGetValue(page, out var target) || target == null || target.IsDisposed)
            {
                if (!_factories.ContainsKey(page))
                    throw new InvalidOperationException($"No form registered for {page}");

                target = _factories[page]();
                PrepareAsPage(target);
                _instances[page] = target;
            }

            return target;
        }

        private static Workspace? OwnerOf(Form form)
        {
            foreach (var workspace in AllWorkspaces())
            {
                if (workspace.IsAlive && form.Parent == workspace.Host)
                    return workspace;
            }

            return null;
        }

        private static AppPage? PageOf(Form? form)
        {
            if (form == null)
                return null;

            foreach (var pair in _instances)
            {
                if (pair.Value == form)
                    return pair.Key;
            }

            return null;
        }

        private static IEnumerable<Workspace> AllWorkspaces()
        {
            if (_main != null)
                yield return _main;
            foreach (var extra in _extras)
                yield return extra;
        }

        private static void HidePages(Panel host)
        {
            foreach (Control control in host.Controls)
                control.Visible = false;
        }

        private static void ShowEmpty(Workspace workspace)
        {
            HidePages(workspace.Host);

            Panel? hint = null;
            foreach (Control control in workspace.Host.Controls)
            {
                if (control.Name == "EmptyHint")
                    hint = (Panel)control;
            }

            if (hint == null)
            {
                hint = new Panel
                {
                    Name = "EmptyHint",
                    Dock = DockStyle.Fill,
                    BackColor = Theme.Cream
                };
                hint.Controls.Add(new Label
                {
                    Text = "Choose a page from the sidebar.",
                    Dock = DockStyle.Fill,
                    TextAlign = ContentAlignment.MiddleCenter,
                    ForeColor = Theme.Muted,
                    Font = Theme.Body,
                    BackColor = Color.Transparent
                });
                workspace.Host.Controls.Add(hint);
            }

            hint.Visible = true;
            hint.BringToFront();
        }

        private static void Highlight(Form form)
        {
            if (form is INavigationPage navPage)
                navPage.HighlightCurrentPage();
        }

        private static void PrepareAsPage(Form form)
        {
            form.TopLevel = false;
            form.FormBorderStyle = FormBorderStyle.None;
            form.Dock = DockStyle.Fill;
            form.Visible = false;
        }
    }

    /// <summary>One window plus its nested page host and sidebar.</summary>
    internal sealed class Workspace
    {
        public Workspace(Form window, Panel host, bool isMain)
        {
            Window = window;
            Host = host;
            IsMain = isMain;
        }

        public Form Window { get; }
        public Panel Host { get; }
        public bool IsMain { get; set; }
        public AppPage? CurrentPage { get; set; }
        public NavSidebar? Sidebar { get; set; }
        public Action<AppPage?>? UpdateChrome { get; set; }

        private readonly List<AppPage> _back = new();
        private readonly List<AppPage> _forward = new();

        public bool CanGoBack => _back.Count > 0;
        public bool CanGoForward => _forward.Count > 0;

        public bool IsAlive =>
            Window != null && !Window.IsDisposed &&
            Host != null && !Host.IsDisposed;

        public void PushHistory(AppPage page)
        {
            if (_back.Count > 0 && _back[^1] == page)
                return;
            _back.Add(page);
            if (_back.Count > 40)
                _back.RemoveAt(0);
            _forward.Clear();
        }

        public AppPage? TakeBack()
        {
            if (_back.Count == 0)
                return null;
            var page = _back[^1];
            _back.RemoveAt(_back.Count - 1);
            if (CurrentPage is AppPage here)
                _forward.Add(here);
            return page;
        }

        public AppPage? TakeForward()
        {
            if (_forward.Count == 0)
                return null;
            var page = _forward[^1];
            _forward.RemoveAt(_forward.Count - 1);
            if (CurrentPage is AppPage here)
                _back.Add(here);
            return page;
        }

        public void RefreshChrome()
        {
            UpdateChrome?.Invoke(CurrentPage);
            Sidebar?.RefreshState();
        }
    }

    /// <summary>Mouse side buttons, Alt+Left/Right, and browser back/forward keys.</summary>
    internal sealed class HistoryInputFilter : IMessageFilter
    {
        private const int WmKeyDown = 0x0100;
        private const int WmSysKeyDown = 0x0104;
        private const int WmXButtonDown = 0x020B;
        private const int WmNcXButtonDown = 0x00AB;
        private const int WmAppCommand = 0x0319;
        private const int XButton1 = 0x0001;
        private const int XButton2 = 0x0002;
        private const int AppCommandBrowserBack = 1;
        private const int AppCommandBrowserForward = 2;

        public bool PreFilterMessage(ref Message m)
        {
            if (HasModal())
                return false;

            if (m.Msg is WmXButtonDown or WmNcXButtonDown)
            {
                int button = (int)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                if (button == XButton1)
                    return TryBack();
                if (button == XButton2)
                    return TryForward();
                return false;
            }

            if (m.Msg == WmAppCommand)
            {
                int command = (int)((m.LParam.ToInt64() >> 16) & 0x0FFF);
                if (command == AppCommandBrowserBack)
                    return TryBack();
                if (command == AppCommandBrowserForward)
                    return TryForward();
                return false;
            }

            if (m.Msg is WmKeyDown or WmSysKeyDown)
            {
                var key = (Keys)(m.WParam.ToInt64() & 0xFFFF);
                bool alt = (Control.ModifierKeys & Keys.Alt) != 0;
                if (key == Keys.BrowserBack || (alt && key == Keys.Left))
                    return TryBack();
                if (key == Keys.BrowserForward || (alt && key == Keys.Right))
                    return TryForward();
            }

            return false;
        }

        private static bool TryBack()
        {
            var workspace = Navigator.ActiveWorkspace;
            if (!WorkspaceIsForeground() || workspace == null || !workspace.CanGoBack)
                return false;
            Navigator.GoBack(workspace);
            return true;
        }

        private static bool TryForward()
        {
            var workspace = Navigator.ActiveWorkspace;
            if (!WorkspaceIsForeground() || workspace == null || !workspace.CanGoForward)
                return false;
            Navigator.GoForward(workspace);
            return true;
        }

        private static bool WorkspaceIsForeground()
        {
            var form = Form.ActiveForm;
            return form is MainForm or PageWindow;
        }

        private static bool HasModal()
        {
            foreach (Form form in Application.OpenForms)
            {
                if (form.Visible && form.Modal)
                    return true;
            }

            return false;
        }
    }

    /// <summary>Extra detached workspace window with its own sidebar.</summary>
    internal sealed class PageWindow : Form
    {
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly FlowLayoutPanel _openBar;
        private readonly Panel _header;
        private NavHistoryBar? _history;

        public Panel Host { get; }

        public PageWindow()
        {
            Text = "Cast Right Catch — Inventory";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 600);
            Size = new Size(1100, 720);
            BackColor = Theme.Cream;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;
            WindowChrome.Apply(this);

            Host = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Cream,
                Name = "panelHost"
            };

            _openBar = new FlowLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 36,
                BackColor = Theme.Paper,
                Padding = new Padding(22, 4, 16, 4),
                WrapContents = false,
                AutoScroll = true,
                Visible = false
            };

            _header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 78,
                BackColor = Theme.Paper
            };
            var headerGold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };
            _title = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Top,
                Height = 42,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                Text = "Cast Right Catch",
                TextAlign = ContentAlignment.BottomLeft,
                Padding = new Padding(12, 0, 28, 0)
            };
            _subtitle = new Label
            {
                AutoSize = false,
                Dock = DockStyle.Fill,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Text = "",
                TextAlign = ContentAlignment.TopLeft,
                Padding = new Padding(12, 4, 28, 0)
            };
            var titles = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Paper
            };
            titles.Controls.Add(_subtitle);
            titles.Controls.Add(_title);
            _header.Controls.Add(titles);
            _header.Controls.Add(headerGold);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 36,
                BackColor = Theme.NavyDark
            };
            var footerGold = new Panel
            {
                Dock = DockStyle.Top,
                Height = 2,
                BackColor = Theme.Gold
            };
            var footerLabel = new Label
            {
                Dock = DockStyle.Fill,
                Font = Theme.Small,
                ForeColor = Theme.CreamDark,
                TextAlign = ContentAlignment.MiddleCenter,
                Text = "(253) 540-2631    ·    jwatts@castrightcatch.com    ·    PO Box 1064  ·  Orting, WA 98360"
            };
            footer.Controls.Add(footerLabel);
            footer.Controls.Add(footerGold);

            Controls.Add(Host);
            Controls.Add(_openBar);
            Controls.Add(_header);
            Controls.Add(footer);
        }

        public void BindHistory(Workspace workspace)
        {
            _history = new NavHistoryBar(workspace);
            _history.Dock = DockStyle.Left;
            _header.Controls.Add(_history);
        }

        public void SetChrome(AppPage? page)
        {
            if (page == null)
            {
                Text = "Cast Right Catch — Inventory";
                _title.Text = "Cast Right Catch";
                _subtitle.Text = "Choose a page from the sidebar";
            }
            else
            {
                Text = UiStyle.PageTitle(page.Value) + "  ·  Cast Right Catch";
                _title.Text = UiStyle.PageTitle(page.Value);
                _subtitle.Text = UiStyle.PageSubtitle(page.Value);
            }

            RebuildOpenBar(page);
            _history?.Sync();
        }

        private void RebuildOpenBar(AppPage? current)
        {
            var open = Navigator.ListOpenPages();
            _openBar.Visible = open.Count > 1;
            _openBar.Controls.Clear();
            if (open.Count <= 1)
                return;

            _openBar.Controls.Add(new Label
            {
                Text = "OPEN WINDOWS",
                Font = Theme.Caption,
                ForeColor = Theme.Muted,
                AutoSize = true,
                Margin = new Padding(0, 8, 12, 0)
            });

            foreach (var page in open)
            {
                bool here = page == current;
                var btn = new Button
                {
                    Text = UiStyle.PageTitle(page),
                    AutoSize = true,
                    Height = 24,
                    MinimumSize = new Size(0, 24),
                    Margin = new Padding(0, 2, 6, 2),
                    Padding = new Padding(10, 0, 10, 0),
                    Font = Theme.Small,
                    TabStop = false
                };
                if (here)
                    Theme.StyleGoldButton(btn);
                else
                    Theme.StyleOutlineButton(btn);
                btn.Font = Theme.Small;
                var target = page;
                btn.Click += (_, _) => Navigator.TryFocus(target);
                _openBar.Controls.Add(btn);
            }
        }
    }
}
