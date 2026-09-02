namespace CastRightCatchInvManagement
{
    /// <summary>Primary workspace window: sidebar, header, and nested pages.</summary>
    public partial class MainForm : Form
    {
        private readonly Workspace _workspace;
        private readonly NavSidebar _sidebar;

        public MainForm()
        {
            InitializeComponent();

            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            _workspace = Navigator.AttachMain(this, panelHost, _ => UpdateHeader());
            _sidebar = new NavSidebar(_workspace);
            _workspace.Sidebar = _sidebar;
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
            lblPageTitle.Text = UiStyle.PageTitle(page);
            lblPageSubtitle.Text = _workspace.CurrentPage == null
                ? "Choose a page from the sidebar"
                : UiStyle.PageSubtitle(page);
            _sidebar.RefreshState();
        }
    }
}
