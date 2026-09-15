namespace CastRightCatchInvManagement
{
    /// <summary>Primary workspace window: sidebar, header, and nested pages.</summary>
    public partial class MainForm : Form
    {
        private readonly Workspace _workspace;
        private readonly NavSidebar _sidebar;
        private readonly NavHistoryBar _history;

        /// <summary>Attach the sidebar and open Home, or Settings if no data folder is set.</summary>
        public MainForm()
        {
            InitializeComponent();

            // Use the packaged seal icon when the asset pack is present.
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;
            WindowChrome.Apply(this);

            _workspace = Navigator.AttachMain(this, panelHost, _ => UpdateHeader());
            _sidebar = new NavSidebar(_workspace);
            _workspace.Sidebar = _sidebar;
            _history = new NavHistoryBar(_workspace);
            _history.Dock = DockStyle.Left;
            panelHeader.Controls.Add(_history);
            Controls.Add(_sidebar);

            AppLock.Changed += UpdateHeader;

            // Shared folder exists: start on Home.
            if (AppLock.HasFolder())
                Navigator.GoTo(AppPage.Dashboard);
            // No folder yet: Settings is the only page that can pick one.
            else
                Navigator.GoTo(AppPage.Settings);

            UpdateHeader();
        }

        /// <summary>Unsubscribe from shared-settings changes so a closed window is not refreshed.</summary>
        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            AppLock.Changed -= UpdateHeader;
            base.OnFormClosed(e);
        }

        /// <summary>Sync title, subtitle, and history buttons to the page shown in this window.</summary>
        private void UpdateHeader()
        {
            // Closed while a folder-change callback was still queued.
            if (IsDisposed)
                return;

            // AppLock.Changed can fire from a background load.
            if (InvokeRequired)
            {
                BeginInvoke(UpdateHeader);
                return;
            }

            var page = _workspace.CurrentPage ?? Navigator.CurrentPage;
            bool home = page == AppPage.Dashboard;
            panelHeader.Visible = !home;
            panelFooter.Visible = !home;
            lblPageTitle.Text = home ? "" : UiStyle.PageTitle(page);
            // Home uses its own hero; empty host after a steal needs a prompt.
            lblPageSubtitle.Text = home
                ? ""
                : _workspace.CurrentPage == null
                    ? "Choose a page from the sidebar"
                    : UiStyle.PageSubtitle(page);
            _sidebar.RefreshState();
            _history.Sync();
        }
    }
}
