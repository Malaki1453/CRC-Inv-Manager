namespace CastRightCatchInvManagement
{
    /// <summary>Primary workspace window: sidebar, header, and nested pages.</summary>
    public partial class MainForm : Form
    {
        private readonly Workspace _workspace;
        private readonly NavSidebar _sidebar;
        private readonly NavHistoryBar _history;

        public MainForm()
        {
            InitializeComponent();

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

            if (AppLock.HasFolder())
                Navigator.GoTo(AppPage.Dashboard);
            else
                Navigator.GoTo(AppPage.Settings);

            UpdateHeader();
        }

        protected override void OnFormClosed(FormClosedEventArgs e)
        {
            AppLock.Changed -= UpdateHeader;
            base.OnFormClosed(e);
        }

        private void UpdateHeader()
        {
            if (IsDisposed)
                return;

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
