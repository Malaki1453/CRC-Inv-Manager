namespace CastRightCatchInvManagement
{
    /// <summary>Home splash: a full-area brand image with no controls.</summary>
    public partial class Dashboard : Form, INavigationPage
    {
        /// <summary>Register this splash as the home page and paint the brand image.</summary>
        public Dashboard()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Dashboard, this);
            BuildUi();
        }

        /// <summary>Home has no live data; the brand image is already in place.</summary>
        public void HighlightCurrentPage() { }

        /// <summary>Fill the page with the home hero so the splash has no extra chrome.</summary>
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
