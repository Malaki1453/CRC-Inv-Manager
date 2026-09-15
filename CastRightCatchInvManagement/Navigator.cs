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
                // Closed extras and empty hosts have nothing to reload.
                if (!workspace.IsAlive || workspace.CurrentPage == null)
                    continue;

                // Ask the nested page to reload when this workspace still hosts a live instance.
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

        /// <summary>Register the primary window as the first workspace and install history keys.</summary>
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

        /// <summary>Foreground workspace, or the main window when none is focused.</summary>
        internal static Workspace? ActiveWorkspace => _active ?? _main;

        /// <summary>Go back in the active workspace's page history.</summary>
        public static void GoBack() => GoBack(_active ?? _main);

        /// <summary>Go forward in the active workspace's page history.</summary>
        public static void GoForward() => GoForward(_active ?? _main);

        /// <summary>Pop the back stack and show that page in this workspace without stealing another window.</summary>
        internal static void GoBack(Workspace? workspace)
        {
            workspace ??= _active ?? _main;
            // App can close while a mouse-side-button message is still queued.
            if (workspace == null || !workspace.IsAlive)
                return;

            var page = workspace.TakeBack();
            // Already at the start of this window's history.
            if (page == null)
                return;

            MoveHistory(workspace, page.Value);
        }

        /// <summary>Pop the forward stack and show that page in this workspace.</summary>
        internal static void GoForward(Workspace? workspace)
        {
            workspace ??= _active ?? _main;
            // App can close while a mouse-side-button message is still queued.
            if (workspace == null || !workspace.IsAlive)
                return;

            var page = workspace.TakeForward();
            // Already at the end of this window's history.
            if (page == null)
                return;

            MoveHistory(workspace, page.Value);
        }

        /// <summary>Navigate without pushing another history entry (back/forward already moved the stacks).</summary>
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

        /// <summary>Install the mouse-side-button / Alt+Left filter once per process.</summary>
        private static void EnsureHistoryFilter()
        {
            // Only one filter; attaching extras must not add a second.
            if (_historyFilterAdded)
                return;
            Application.AddMessageFilter(new HistoryInputFilter());
            _historyFilterAdded = true;
        }

        /// <summary>Remember a page form created outside the factory map.</summary>
        public static void Register(AppPage page, Form form)
        {
            PrepareAsPage(form);
            _instances[page] = form;
        }

        /// <summary>True when this page's form exists and has not been disposed.</summary>
        public static bool IsRegistered(AppPage page)
        {
            return _instances.TryGetValue(page, out var form) && form != null && !form.IsDisposed;
        }

        /// <summary>True when any live workspace is showing this page.</summary>
        public static bool IsOpen(AppPage page)
        {
            foreach (var workspace in AllWorkspaces())
            {
                // Closed extras and empty hosts are not showing a page.
                if (workspace.IsAlive && workspace.CurrentPage == page)
                    return true;
            }

            return false;
        }

        /// <summary>Get or create the page form and parent it to main if it is still unhosted.</summary>
        public static T Ensure<T>(AppPage page) where T : Form
        {
            var target = GetInstance(page);

            // Callers that touch child controls need a parent before CreateControl.
            if (target.Parent == null && _main != null && _main.IsAlive)
            {
                PrepareAsPage(target);
                _main.Host.Controls.Add(target);
            }

            // CreateControl so child lookups do not throw before the handle exists.
            if (!target.IsHandleCreated)
                target.CreateControl();

            return (T)target;
        }

        /// <summary>Show a page in the active workspace, focusing another window if it is already open.</summary>
        public static void GoTo(AppPage page)
        {
            GoTo(page, _active ?? _main);
        }

        /// <summary>Show a page in a workspace after access checks and renamed-page aliases.</summary>
        internal static void GoTo(AppPage page, Workspace? workspace, bool reuseOpenWindow = true)
        {
            workspace ??= _active ?? _main;
            // MainForm.AttachMain has not run yet.
            if (workspace == null || !workspace.IsAlive)
                throw new InvalidOperationException("Navigator host has not been set.");

            // AddSale was renamed; keep old links working.
            if (page == AppPage.AddSale)
                page = AppPage.SalesOrder;
            // IT Users / Access live on the Admin page now.
            if (page == AppPage.ItUsers || page == AppPage.ItAccess)
                page = AppPage.Admin;
            // Non-staff cannot open Admin; Settings is the fallback chrome page.
            if (page == AppPage.Admin && !AppState.IsAdmin && !AppState.IsIt)
                page = AppPage.Settings;
            // Denied tables bounce to Home with a warning instead of showing a blank page.
            if (!TableAccess.CanPage(page))
            {
                MessageBox.Show(
                    "You do not have access to that table.",
                    "Access",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                page = AppPage.Dashboard;
            }

            // Clicking a page that is already in another window brings that window forward.
            if (reuseOpenWindow && TryFocusOther(page, workspace))
                return;

            ShowIn(workspace, page);
        }

        /// <summary>Open a page in a new extra window, or focus the window that already shows it.</summary>
        public static void OpenDetached(AppPage page) => OpenDetached(page, null);

        /// <summary>Open in a new extra window offset from the source, unless that page is already shown.</summary>
        internal static void OpenDetached(AppPage page, Workspace? from)
        {
            // OpenDetached cannot create extras before AttachMain.
            if (_main == null || !_main.IsAlive)
                throw new InvalidOperationException("Navigator host has not been set.");

            // Denied tables cannot open a second window.
            if (!TableAccess.CanPage(page))
            {
                MessageBox.Show(
                    "You do not have access to that table.",
                    "Access",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // One page, one window: never duplicate the nested form.
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

        /// <summary>Bring the workspace that is showing this page to the front.</summary>
        internal static bool TryFocus(AppPage page)
        {
            var workspace = WorkspaceShowing(page);
            // No live window is showing this page.
            if (workspace == null)
                return false;
            FocusWorkspace(workspace);
            return true;
        }

        /// <summary>Pages currently visible in any live workspace, for the open-windows bar.</summary>
        internal static IReadOnlyList<AppPage> ListOpenPages()
        {
            var pages = new List<AppPage>();
            foreach (var workspace in AllWorkspaces())
            {
                // Skip closed extras and empty hosts so the open-windows bar stays accurate.
                if (workspace.IsAlive && workspace.CurrentPage is AppPage page)
                    pages.Add(page);
            }

            return pages;
        }

        /// <summary>Mark this window as the one that receives GoTo and history keys.</summary>
        internal static void Activate(Workspace? workspace)
        {
            // Ignore activate from a window that already closed.
            if (workspace == null || !workspace.IsAlive)
                return;

            _active = workspace;
            // Remember the last extra so closing main can promote it.
            if (!workspace.IsMain)
                _lastExtra = workspace;
            // Keep Navigator.CurrentPage in sync with the focused window.
            if (workspace.CurrentPage is AppPage page)
                CurrentPage = page;
        }

        /// <summary>Wire activate/close so extras promote or return pages to main.</summary>
        private static void BindWindow(Workspace workspace)
        {
            workspace.Window.Activated += (_, _) => Activate(workspace);
            workspace.Window.FormClosing += (_, e) =>
            {
                // Caller already cancelled (unsaved prompt).
                if (e.Cancel)
                    return;
                // Closing main may promote an extra so the process stays up.
                if (workspace.IsMain)
                    TryPromote(workspace);
                // Extras return nested pages to main instead of disposing them.
                else
                    CloseExtra(workspace);
            };
            workspace.Window.FormClosed += (_, _) => OnWindowClosed(workspace);
        }

        /// <summary>Build a detached PageWindow with its own sidebar and history bar.</summary>
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

        /// <summary>When the main window closes, make the last extra the new main so the process stays up.</summary>
        private static bool TryPromote(Workspace retiring)
        {
            var successor = PickSuccessor(retiring);
            // No extras left; the process will exit after this window closes.
            if (successor == null)
                return false;

            foreach (var extra in _extras)
            {
                // Clear Owner so extras are not disposed with the retiring main window.
                if (extra.IsAlive)
                    extra.Window.Owner = null;
            }

            SalvagePages(retiring, successor);

            successor.IsMain = true;
            retiring.IsMain = false;
            _extras.Remove(successor);
            _main = successor;
            // Successor is now main; it is no longer an extra to remember.
            if (_lastExtra == successor)
                _lastExtra = null;

            // A minimized extra would look like the app vanished after main closed.
            if (successor.Window.WindowState == FormWindowState.Minimized)
                successor.Window.WindowState = FormWindowState.Normal;
            successor.Window.ShowInTaskbar = true;
            successor.Window.BringToFront();
            successor.Window.Activate();
            Activate(successor);
            return true;
        }

        /// <summary>Prefer the last focused extra; otherwise the first remaining extra.</summary>
        private static Workspace? PickSuccessor(Workspace retiring)
        {
            // Prefer the extra the user last focused so that window becomes the new main.
            if (_lastExtra != null &&
                _lastExtra.IsAlive &&
                _lastExtra != retiring &&
                _extras.Contains(_lastExtra))
                return _lastExtra;

            foreach (var extra in _extras)
            {
                // Any remaining extra can keep the process alive.
                if (extra.IsAlive && extra != retiring)
                    return extra;
            }

            return null;
        }

        /// <summary>Move nested page forms from a closing window into the successor without disposing them.</summary>
        private static void SalvagePages(Workspace from, Workspace to)
        {
            // Both windows must still exist to reparent nested forms.
            if (!from.IsAlive || !to.IsAlive)
                return;

            Form? keepVisible = null;
            // Keep the successor's current page in front after the move.
            if (to.CurrentPage is AppPage keep &&
                _instances.TryGetValue(keep, out var shown) &&
                shown.Parent == to.Host)
                keepVisible = shown;

            var forms = new List<Form>();
            foreach (Control control in from.Host.Controls)
            {
                // Only nested page forms move; chrome controls stay on the closing window.
                if (control is Form form && !form.IsDisposed)
                    forms.Add(form);
            }

            foreach (var form in forms)
            {
                from.Host.Controls.Remove(form);
                PrepareAsPage(form);
                // Skip forms already parented to the successor.
                if (!to.Host.Controls.Contains(form))
                    to.Host.Controls.Add(form);
            }

            // Restore the page the successor was already showing after the reparent.
            if (keepVisible != null && !keepVisible.IsDisposed)
            {
                keepVisible.Visible = true;
                keepVisible.BringToFront();
                return;
            }

            // Successor was empty; show the page the closing window had.
            if (to.CurrentPage == null && from.CurrentPage != null)
                ShowIn(to, from.CurrentPage.Value);
        }

        /// <summary>Drop a closed workspace and exit the process when no windows remain.</summary>
        private static void OnWindowClosed(Workspace workspace)
        {
            // Clear _main only when this closed window was still the registered main.
            if (workspace.IsMain && _main == workspace)
                _main = null;

            _extras.Remove(workspace);

            // Drop a stale last-extra pointer so promotion does not pick a disposed window.
            if (_lastExtra == workspace)
                _lastExtra = null;

            // Move GoTo/history targeting to main when the focused extra closes.
            if (_active == workspace)
                Activate(_main);

            RefreshAllChrome();

            // Last window closed: end the ApplicationContext message loop.
            if (!HasAliveWindow())
                Application.ExitThread();
        }

        /// <summary>True while any workspace window is still open.</summary>
        private static bool HasAliveWindow()
        {
            foreach (var workspace in AllWorkspaces())
            {
                // Any remaining window keeps the message loop running.
                if (workspace.IsAlive)
                    return true;
            }

            return false;
        }

        /// <summary>Host a page in dest, stealing it from another window if needed.</summary>
        private static void ShowIn(Workspace dest, AppPage page, bool activate = true, bool recordHistory = true)
        {
            // Do not host a page into a window that already closed.
            if (!dest.IsAlive)
                return;

            var form = GetInstance(page);
            var previous = OwnerOf(form);

            // Already showing this page here; just refresh and focus.
            if (dest.CurrentPage == page && form.Parent == dest.Host && form.Visible)
            {
                // Recoveries skip Activate so the stealing window stays in front.
                if (activate)
                {
                    Activate(dest);
                    PageChanged?.Invoke(page);
                }
                Highlight(form);
                dest.RefreshChrome();
                return;
            }

            // Back/forward already adjusted the stacks; do not push a duplicate.
            if (recordHistory && !_movingHistory && dest.CurrentPage is AppPage from && from != page)
                dest.PushHistory(from);

            bool stoleCurrent = previous != null &&
                                previous != dest &&
                                previous.CurrentPage == page;

            HidePages(dest.Host);

            // Nested forms can have only one parent; steal from the other host.
            if (form.Parent != null && form.Parent != dest.Host)
                form.Parent.Controls.Remove(form);

            PrepareAsPage(form);
            form.Visible = true;
            // Add only once; re-adding would reorder and flicker.
            if (!dest.Host.Controls.Contains(form))
                dest.Host.Controls.Add(form);
            form.BringToFront();

            dest.CurrentPage = page;
            Highlight(form);

            // The other window lost its visible page; show a leftover or empty hint.
            if (stoleCurrent && previous != null && previous.IsAlive)
                Recover(previous);

            RefreshAllChrome();

            // Mark dest as the GoTo/history target after the page is visible.
            if (activate)
            {
                Activate(dest);
                PageChanged?.Invoke(page);
            }
        }

        /// <summary>After a page is stolen, show another hosted form or a fallback/empty hint.</summary>
        private static void Recover(Workspace workspace)
        {
            foreach (Control control in workspace.Host.Controls)
            {
                // Look for a leftover nested page form to show instead of the stolen one.
                if (control is Form form && !form.IsDisposed)
                {
                    var page = PageOf(form);
                    // Controls other than registered page forms stay hidden.
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

            // Main must always show a page; extras may sit empty.
            if (workspace.IsMain)
            {
                ShowIn(workspace, PickFallback(workspace), activate: false, recordHistory: false);
                return;
            }

            workspace.CurrentPage = null;
            ShowEmpty(workspace);
            workspace.RefreshChrome();
        }

        /// <summary>Home, then Settings, then Reports — skip pages already open in another window.</summary>
        private static AppPage PickFallback(Workspace dest)
        {
            foreach (var page in new[] { AppPage.Dashboard, AppPage.Settings, AppPage.Reports })
            {
                // Do not steal a page another window is already showing.
                if (!IsShownElsewhere(page, dest))
                    return page;
            }

            return AppPage.Settings;
        }

        /// <summary>True when another live workspace is already showing this page.</summary>
        internal static bool IsShownElsewhere(AppPage page, Workspace? dest)
        {
            foreach (var workspace in AllWorkspaces())
            {
                // Match any other live window already displaying this page.
                if (workspace != dest && workspace.IsAlive && workspace.CurrentPage == page)
                    return true;
            }

            return false;
        }

        /// <summary>The live workspace whose CurrentPage is this page, if any.</summary>
        private static Workspace? WorkspaceShowing(AppPage page)
        {
            foreach (var workspace in AllWorkspaces())
            {
                // First live workspace showing this page wins.
                if (workspace.IsAlive && workspace.CurrentPage == page)
                    return workspace;
            }

            return null;
        }

        /// <summary>Focus another window that already shows the page; false if it is only here or nowhere.</summary>
        private static bool TryFocusOther(AppPage page, Workspace? from)
        {
            var existing = WorkspaceShowing(page);
            // Focus only a different live window; same-window or missing is a miss.
            if (existing == null || existing == from || !existing.IsAlive)
                return false;
            FocusWorkspace(existing);
            return true;
        }

        /// <summary>Restore, show, and activate a workspace window.</summary>
        private static void FocusWorkspace(Workspace workspace)
        {
            // Closed windows cannot be restored or activated.
            if (!workspace.IsAlive)
                return;
            // A minimized window would stay in the taskbar instead of coming forward.
            if (workspace.Window.WindowState == FormWindowState.Minimized)
                workspace.Window.WindowState = FormWindowState.Normal;
            workspace.Window.Show();
            workspace.Window.BringToFront();
            workspace.Window.Activate();
            Activate(workspace);
        }

        /// <summary>Refresh titles and sidebar marks on every live window.</summary>
        private static void RefreshAllChrome()
        {
            foreach (var workspace in AllWorkspaces())
            {
                // Skip disposed extras so title/sidebar refresh cannot throw.
                if (workspace.IsAlive)
                    workspace.RefreshChrome();
            }
        }

        /// <summary>Return this extra's page forms to main, then show one if main is empty.</summary>
        private static void CloseExtra(Workspace extra)
        {
            _extras.Remove(extra);
            // Closing the focused extra moves GoTo/history back to main.
            if (_active == extra)
                Activate(_main);

            // Main already gone (process shutting down); forms will dispose with the extra.
            if (_main == null || !_main.IsAlive)
                return;

            var forms = new List<Form>();
            foreach (Control control in extra.Host.Controls)
            {
                // Collect nested page forms so they can be reparented to main.
                if (control is Form form && !form.IsDisposed)
                    forms.Add(form);
            }

            foreach (var form in forms)
            {
                extra.Host.Controls.Remove(form);
                PrepareAsPage(form);
                // Skip forms already sitting on main.
                if (!_main.Host.Controls.Contains(form))
                    _main.Host.Controls.Add(form);
            }

            // Main still showing a page keeps it; extras just donate their forms.
            if (_main.CurrentPage == null)
            {
                AppPage? restore = extra.CurrentPage;
                // CurrentPage can be null on an empty extra that still hosts leftover forms.
                if (restore == null && forms.Count > 0)
                    restore = PageOf(forms[0]);

                ShowIn(_main, restore ?? PickFallback(_main));
            }
        }

        /// <summary>Reuse the cached page form, or construct it from the factory map.</summary>
        private static Form GetInstance(AppPage page)
        {
            // Create a new instance only when the cache is empty or the form was disposed.
            if (!_instances.TryGetValue(page, out var target) || target == null || target.IsDisposed)
            {
                // Unknown pages have no form factory and cannot be hosted.
                if (!_factories.ContainsKey(page))
                    throw new InvalidOperationException($"No form registered for {page}");

                target = _factories[page]();
                PrepareAsPage(target);
                _instances[page] = target;
            }

            return target;
        }

        /// <summary>Workspace whose host currently parents this nested form.</summary>
        private static Workspace? OwnerOf(Form form)
        {
            foreach (var workspace in AllWorkspaces())
            {
                // The host that currently parents this nested form owns it.
                if (workspace.IsAlive && form.Parent == workspace.Host)
                    return workspace;
            }

            return null;
        }

        /// <summary>AppPage for a nested form, or null if it is not in the instance map.</summary>
        private static AppPage? PageOf(Form? form)
        {
            // No form means no page mapping.
            if (form == null)
                return null;

            foreach (var pair in _instances)
            {
                // Match the cached instance to its AppPage key.
                if (pair.Value == form)
                    return pair.Key;
            }

            return null;
        }

        /// <summary>Main first, then extras (including ones that may already be disposed).</summary>
        private static IEnumerable<Workspace> AllWorkspaces()
        {
            // Main is listed first so fallbacks prefer it.
            if (_main != null)
                yield return _main;
            foreach (var extra in _extras)
                yield return extra;
        }

        /// <summary>Hide every nested control so only the incoming page is visible.</summary>
        private static void HidePages(Panel host)
        {
            foreach (Control control in host.Controls)
                control.Visible = false;
        }

        /// <summary>Show a centered hint when an extra window has no page left.</summary>
        private static void ShowEmpty(Workspace workspace)
        {
            HidePages(workspace.Host);

            Panel? hint = null;
            foreach (Control control in workspace.Host.Controls)
            {
                // Reuse the existing empty-state panel if this extra already has one.
                if (control.Name == "EmptyHint")
                    hint = (Panel)control;
            }

            // Build the empty-state panel once so extras do not stack labels.
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

        /// <summary>Ask a nested page to reload for the current folder and Current/Old view.</summary>
        private static void Highlight(Form form)
        {
            // Pages that implement INavigationPage reload for the current folder/view.
            if (form is INavigationPage navPage)
                navPage.HighlightCurrentPage();
        }

        /// <summary>Embed a Form as a fill child of a workspace host.</summary>
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
        /// <summary>Bind this window and its nested-page host.</summary>
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

        /// <summary>Record the page we are leaving; consecutive duplicates and long stacks are trimmed.</summary>
        public void PushHistory(AppPage page)
        {
            // Same page twice in a row is a no-op (sidebar re-click).
            if (_back.Count > 0 && _back[^1] == page)
                return;
            _back.Add(page);
            // Cap so long sessions cannot grow unbounded.
            if (_back.Count > 40)
                _back.RemoveAt(0);
            _forward.Clear();
        }

        /// <summary>Pop back and push the current page onto forward.</summary>
        public AppPage? TakeBack()
        {
            // Nothing to pop when this window has no back history.
            if (_back.Count == 0)
                return null;
            var page = _back[^1];
            _back.RemoveAt(_back.Count - 1);
            // Empty host has nothing to put on the forward stack.
            if (CurrentPage is AppPage here)
                _forward.Add(here);
            return page;
        }

        /// <summary>Pop forward and push the current page onto back.</summary>
        public AppPage? TakeForward()
        {
            // Nothing to pop when this window has no forward history.
            if (_forward.Count == 0)
                return null;
            var page = _forward[^1];
            _forward.RemoveAt(_forward.Count - 1);
            // Empty host has nothing to put on the back stack.
            if (CurrentPage is AppPage here)
                _back.Add(here);
            return page;
        }

        /// <summary>Update this window's title/subtitle and sidebar selection marks.</summary>
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

        /// <summary>Consume back/forward hardware and Alt+Left/Right when a workspace is in front.</summary>
        public bool PreFilterMessage(ref Message m)
        {
            // Do not steal history from a modal dialog (sign-in, details, etc.).
            if (HasModal())
                return false;

            // Mouse side buttons (including non-client) map to back/forward.
            if (m.Msg is WmXButtonDown or WmNcXButtonDown)
            {
                int button = (int)((m.WParam.ToInt64() >> 16) & 0xFFFF);
                // XButton1 is hardware back.
                if (button == XButton1)
                    return TryBack();
                // XButton2 is hardware forward.
                if (button == XButton2)
                    return TryForward();
                return false;
            }

            // Keyboard/browser app commands also request back/forward.
            if (m.Msg == WmAppCommand)
            {
                int command = (int)((m.LParam.ToInt64() >> 16) & 0x0FFF);
                // Browser-back app command.
                if (command == AppCommandBrowserBack)
                    return TryBack();
                // Browser-forward app command.
                if (command == AppCommandBrowserForward)
                    return TryForward();
                return false;
            }

            // Alt+Left/Right and dedicated browser keys navigate history.
            if (m.Msg is WmKeyDown or WmSysKeyDown)
            {
                var key = (Keys)(m.WParam.ToInt64() & 0xFFFF);
                bool alt = (Control.ModifierKeys & Keys.Alt) != 0;
                // BrowserBack key or Alt+Left is back.
                if (key == Keys.BrowserBack || (alt && key == Keys.Left))
                    return TryBack();
                // BrowserForward key or Alt+Right is forward.
                if (key == Keys.BrowserForward || (alt && key == Keys.Right))
                    return TryForward();
            }

            return false;
        }

        /// <summary>Navigate back only when a workspace is focused and has history.</summary>
        private static bool TryBack()
        {
            var workspace = Navigator.ActiveWorkspace;
            // Do not consume the key when a dialog is in front or this window has no back stack.
            if (!WorkspaceIsForeground() || workspace == null || !workspace.CanGoBack)
                return false;
            Navigator.GoBack(workspace);
            return true;
        }

        /// <summary>Navigate forward only when a workspace is focused and has history.</summary>
        private static bool TryForward()
        {
            var workspace = Navigator.ActiveWorkspace;
            // Do not consume the key when a dialog is in front or this window has no forward stack.
            if (!WorkspaceIsForeground() || workspace == null || !workspace.CanGoForward)
                return false;
            Navigator.GoForward(workspace);
            return true;
        }

        /// <summary>Ignore history keys while a non-workspace dialog is the active form.</summary>
        private static bool WorkspaceIsForeground()
        {
            var form = Form.ActiveForm;
            return form is MainForm or PageWindow;
        }

        /// <summary>True when any modal dialog is open so side buttons do not navigate under it.</summary>
        private static bool HasModal()
        {
            foreach (Form form in Application.OpenForms)
            {
                // A visible modal must keep side buttons; navigating under it would hide the dialog.
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

        /// <summary>Build header, host, footer, and open-windows bar for a detached workspace.</summary>
        public PageWindow()
        {
            Text = "Cast Right Catch — Inventory";
            StartPosition = FormStartPosition.CenterParent;
            MinimumSize = new Size(900, 600);
            Size = new Size(1100, 720);
            BackColor = Theme.Cream;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            // Use the packaged seal icon when the asset pack is present.
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

        /// <summary>Place Back/Forward next to the title once the workspace exists.</summary>
        public void BindHistory(Workspace workspace)
        {
            _history = new NavHistoryBar(workspace);
            _history.Dock = DockStyle.Left;
            _header.Controls.Add(_history);
        }

        /// <summary>Title, subtitle, and open-windows chips for the page shown in this extra.</summary>
        public void SetChrome(AppPage? page)
        {
            // Extra can sit empty after its page was stolen.
            if (page == null)
            {
                Text = "Cast Right Catch — Inventory";
                _title.Text = "Cast Right Catch";
                _subtitle.Text = "Choose a page from the sidebar";
            }
            // Named page: title bar and header show that page.
            else
            {
                Text = UiStyle.PageTitle(page.Value) + "  ·  Cast Right Catch";
                _title.Text = UiStyle.PageTitle(page.Value);
                _subtitle.Text = UiStyle.PageSubtitle(page.Value);
            }

            RebuildOpenBar(page);
            _history?.Sync();
        }

        /// <summary>Chips for every open workspace; hidden when this is the only window.</summary>
        private void RebuildOpenBar(AppPage? current)
        {
            var open = Navigator.ListOpenPages();
            _openBar.Visible = open.Count > 1;
            _openBar.Controls.Clear();
            // A single window does not need the open-windows chip bar.
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
                // Gold marks the page in this window; outline jumps to another.
                if (here)
                    Theme.StyleGoldButton(btn);
                // Other windows stay outlined so a click focuses them instead of duplicating.
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
