namespace CastRightCatchInvManagement
{
    /// <summary>Home splash: a full-area brand image with no controls.</summary>
    public partial class Dashboard : Form, INavigationPage
    {
        public Dashboard()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Dashboard, this);
            BuildUi();
        }

        public void HighlightCurrentPage() { }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(0);
            BackColor = Theme.NavyDark;

            var image = BrandAssets.HomeHero ?? BrandAssets.Hero;
            var banner = new CoverBanner
            {
                Dock = DockStyle.Fill,
                AlignY = 0.35f,
                Image = image
            };
            Controls.Add(banner);
        }
    }
}
