namespace CastRightCatchInvManagement
{
    /// <summary>Command Center home overview.</summary>
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
            DataFiles.DataChanged += LoadSummaryNumbers;
            BuildUi();
            LoadSummaryNumbers();
        }

        /// <summary>Called when this page is shown or data changes. Refreshes the four stat cards.</summary>
        public void HighlightCurrentPage()
        {
            LoadSummaryNumbers();
        }

        /// <summary>Hero image, four metric cards, and the welcome copy.</summary>
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

        /// <summary>
        /// Fill the four cards from sales and invoices in the current database view
        /// (live only, or archive + live when Old is on).
        /// </summary>
        private void LoadSummaryNumbers()
        {
            if (IsDisposed || _cardRevenue == null)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(LoadSummaryNumbers);
                return;
            }

            var summary = DataFiles.GetDashboardSummary();
            string scope = summary.ViewingOld ? "All inventory" : "This term";

            _cardRevenue.Value = summary.Revenue.ToString("C");
            _cardRevenue.Hint = scope;

            _cardOutstanding.Value = summary.Outstanding.ToString("C");
            _cardOutstanding.Hint = "Unpaid invoices";

            _cardLateFees.Value = summary.LateFees.ToString("C");
            _cardLateFees.Hint = summary.OverdueInvoices == 0
                ? "None overdue"
                : summary.OverdueInvoices == 1
                    ? "1 overdue invoice"
                    : $"{summary.OverdueInvoices} overdue invoices";

            _cardDeals.Value = summary.Deals.ToString("N0");
            _cardDeals.Hint = summary.ViewingOld ? "Logged (all inventory)" : "Logged this term";
        }
    }
}
