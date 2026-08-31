namespace CastRightCatchInvManagement
{
    public partial class Reports : Form, INavigationPage
    {
        public Reports()
        {
            InitializeComponent();
            BuildUi();
        }

        public void HighlightCurrentPage() { }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);

            var intro = new Label
            {
                Text = "Each report will use the current term’s data.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 28
            };

            var grid = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 3,
                RowCount = 2,
                Padding = new Padding(0, 12, 0, 0)
            };
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.33f));
            grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 33.34f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50f));

            var reports = new (string Title, string Hint)[]
            {
                ("Aging Report", "Outstanding invoices by age"),
                ("Commission Tracker", "Commissions by deal and period"),
                ("Monthly P&&L", "Profit and loss for the term"),
                ("Supplier Performance", "Vendor volume, cost, and margin"),
                ("Customer Risk Report", "Credit, terms, and late balances"),
                ("Profit Per Species", "Margin rolled up by species")
            };

            for (int i = 0; i < reports.Length; i++)
            {
                var card = BuildReportCard(reports[i].Title, reports[i].Hint);
                grid.Controls.Add(card, i % 3, i / 3);
            }

            Controls.Add(grid);
            Controls.Add(intro);
        }

        private static CardPanel BuildReportCard(string title, string hint)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(0, 0, 12, 12)
            };

            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(24, 22)
            };
            var body = new Label
            {
                Text = hint,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                AutoSize = false,
                Location = new Point(24, 56),
                Size = new Size(220, 48)
            };
            card.Controls.Add(heading);
            card.Controls.Add(body);
            card.Resize += (_, _) => body.Width = Math.Max(120, card.Width - 48);
            return card;
        }
    }
}
