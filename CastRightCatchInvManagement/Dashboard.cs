namespace CastRightCatchInvManagement
{
    public partial class Dashboard : Form, INavigationPage
    {
        private StatCard _cardRevenue = null!;
        private StatCard _cardOutstanding = null!;
        private StatCard _cardLateFees = null!;
        private StatCard _cardDeals = null!;

        public Dashboard()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Dashboard, this);
            BuildUi();
            LoadSummaryNumbers();
        }

        public void HighlightCurrentPage()
        {
            LoadSummaryNumbers();
        }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(0);

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(28, 20, 28, 24)
            };
            Theme.EnableDoubleBuffer(scroll);

            var hero = new CoverBanner
            {
                Height = 360,
                Dock = DockStyle.Top,
                AlignY = 0.28f,
                Image = BrandAssets.Hero ?? BrandAssets.HomeHero,
                Overlay = BrandAssets.Wordmark
            };

            var stats = new TableLayoutPanel
            {
                Dock = DockStyle.Top,
                Height = 132,
                BackColor = Theme.Cream,
                ColumnCount = 4,
                RowCount = 1,
                Padding = new Padding(0, 18, 0, 8)
            };
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));
            stats.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 25f));

            _cardRevenue = new StatCard("Total Revenue", "$0.00", "This term");
            _cardOutstanding = new StatCard("Outstanding", "$0.00", "Unpaid invoices");
            _cardLateFees = new StatCard("Late Fees", "$0.00", "Accrued");
            _cardDeals = new StatCard("Deals", "0", "Logged this term");

            stats.Controls.Add(_cardRevenue, 0, 0);
            stats.Controls.Add(_cardOutstanding, 1, 0);
            stats.Controls.Add(_cardLateFees, 2, 0);
            stats.Controls.Add(_cardDeals, 3, 0);
            foreach (Control card in stats.Controls)
            {
                card.Dock = DockStyle.Fill;
                card.Margin = new Padding(0, 0, 12, 0);
            }
            _cardDeals.Margin = new Padding(0);

            var welcome = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = 168,
                Margin = new Padding(0)
            };

            var welcomeTitle = new Label
            {
                Text = "Welcome aboard",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(28, 22)
            };
            var welcomeBody = new Label
            {
                Text = "Track purchases, customers, and invoices from one place. Open Settings to choose a data folder and unlock the rest of the workspace.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Location = new Point(28, 56),
                Size = new Size(520, 70)
            };

            if (BrandAssets.BoatLogo != null)
            {
                var boat = new PictureBox
                {
                    Image = BrandAssets.BoatLogo,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Anchor = AnchorStyles.Top | AnchorStyles.Right,
                    Size = new Size(220, 130),
                    Location = new Point(welcome.Width - 250, 16),
                    BackColor = Color.Transparent
                };
                welcome.Resize += (_, _) =>
                {
                    boat.Location = new Point(Math.Max(360, welcome.Width - 250), 16);
                    welcomeBody.Width = Math.Max(280, welcome.Width - 300);
                };
                welcome.Controls.Add(boat);
            }

            welcome.Controls.Add(welcomeTitle);
            welcome.Controls.Add(welcomeBody);

            var body = new Panel
            {
                Dock = DockStyle.Top,
                Height = 320,
                BackColor = Theme.Cream,
                Padding = new Padding(0, 12, 0, 0)
            };
            welcome.Dock = DockStyle.Top;
            stats.Dock = DockStyle.Top;

            body.Controls.Add(welcome);
            body.Controls.Add(stats);

            scroll.Controls.Add(body);
            Controls.Add(scroll);
            Controls.Add(hero);
        }

        private void LoadSummaryNumbers()
        {
            if (_cardRevenue == null)
                return;

            _cardRevenue.Value = "$0.00";
            _cardOutstanding.Value = "$0.00";
            _cardLateFees.Value = "$0.00";
            _cardDeals.Value = "0";
        }
    }
}
