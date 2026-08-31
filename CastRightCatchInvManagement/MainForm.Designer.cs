namespace CastRightCatchInvManagement
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
                components.Dispose();
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            panelHost = new Panel();
            panelHeader = new Panel();
            lblPageTitle = new Label();
            lblPageSubtitle = new Label();
            panelFooter = new Panel();
            lblFooter = new Label();
            SuspendLayout();
            panelHeader.SuspendLayout();
            panelFooter.SuspendLayout();

            panelHost.Dock = DockStyle.Fill;
            panelHost.Name = "panelHost";
            panelHost.BackColor = Theme.Cream;

            panelHeader.Dock = DockStyle.Top;
            panelHeader.Height = 78;
            panelHeader.Name = "panelHeader";
            panelHeader.BackColor = Theme.Paper;
            panelHeader.Padding = new Padding(0);
            headerGold = new Panel();
            headerGold.Dock = DockStyle.Bottom;
            headerGold.Height = 3;
            headerGold.BackColor = Theme.Gold;
            headerGold.Name = "headerGold";

            lblPageTitle.AutoSize = false;
            lblPageTitle.Dock = DockStyle.Top;
            lblPageTitle.Height = 42;
            lblPageTitle.Font = Theme.PageTitle;
            lblPageTitle.ForeColor = Theme.Navy;
            lblPageTitle.Text = "Command Center";
            lblPageTitle.TextAlign = ContentAlignment.BottomLeft;
            lblPageTitle.Padding = new Padding(28, 0, 28, 0);
            lblPageTitle.Name = "lblPageTitle";

            lblPageSubtitle.AutoSize = false;
            lblPageSubtitle.Dock = DockStyle.Fill;
            lblPageSubtitle.Font = Theme.Small;
            lblPageSubtitle.ForeColor = Theme.Muted;
            lblPageSubtitle.Text = "";
            lblPageSubtitle.TextAlign = ContentAlignment.TopLeft;
            lblPageSubtitle.Padding = new Padding(30, 4, 28, 0);
            lblPageSubtitle.Name = "lblPageSubtitle";

            panelHeader.Controls.Add(lblPageSubtitle);
            panelHeader.Controls.Add(lblPageTitle);
            panelHeader.Controls.Add(headerGold);

            panelFooter.Dock = DockStyle.Bottom;
            panelFooter.Height = 36;
            panelFooter.Name = "panelFooter";
            panelFooter.BackColor = Theme.NavyDark;
            footerGold = new Panel();
            footerGold.Dock = DockStyle.Top;
            footerGold.Height = 2;
            footerGold.BackColor = Theme.Gold;
            footerGold.Name = "footerGold";

            lblFooter.Dock = DockStyle.Fill;
            lblFooter.Font = Theme.Small;
            lblFooter.ForeColor = Theme.CreamDark;
            lblFooter.TextAlign = ContentAlignment.MiddleCenter;
            lblFooter.Name = "lblFooter";
            lblFooter.Text = "(253) 540-2631    ·    jwatts@castrightcatch.com    ·    PO Box 1064  ·  Orting, WA 98360";
            panelFooter.Controls.Add(lblFooter);
            panelFooter.Controls.Add(footerGold);

            AutoScaleDimensions = new SizeF(7F, 15F);
            AutoScaleMode = AutoScaleMode.Font;
            BackColor = Theme.Cream;
            ClientSize = new Size(1280, 760);
            MinimumSize = new Size(1180, 680);
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Cast Right Catch — Inventory";
            Controls.Add(panelHost);
            Controls.Add(panelHeader);
            Controls.Add(panelFooter);

            panelHeader.ResumeLayout(false);
            panelFooter.ResumeLayout(false);
            ResumeLayout(false);
        }

        #endregion

        public Panel panelHost;
        private Panel panelHeader;
        private Label lblPageTitle;
        private Label lblPageSubtitle;
        private Panel panelFooter;
        private Label lblFooter;
        private Panel headerGold;
        private Panel footerGold;
    }
}
