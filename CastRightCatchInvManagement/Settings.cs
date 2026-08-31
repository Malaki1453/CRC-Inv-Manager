namespace CastRightCatchInvManagement
{
    public partial class Settings : Form, INavigationPage
    {
        public Settings()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Settings, this);
            BuildUi();
            LoadCompanyInfo();
            ApplyLockState();
        }

        public void HighlightCurrentPage()
        {
            ApplyLockState();
        }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            AutoScroll = true;
            Padding = new Padding(28, 16, 28, 24);

            lblTitle.Visible = false;

            var intro = new Label
            {
                Text = "These details print on invoices. A data folder is required before the rest of the workspace will open.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 36
            };

            var company = new CardPanel { Dock = DockStyle.Top, Height = 300 };
            LayoutCompanyCard(company);

            var data = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutDataCard(data);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var user = new CardPanel { Dock = DockStyle.Top, Height = 128 };
            LayoutUserCard(user);
            var spacerUser = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };

            Controls.Add(user);
            Controls.Add(spacerUser);
            Controls.Add(data);
            Controls.Add(spacer);
            Controls.Add(company);
            Controls.Add(intro);
        }

        private void LayoutCompanyCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Information for invoices",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            Theme.StyleFieldLabel(lblBusinessName);
            Theme.StyleField(txtBusinessName);
            Theme.StyleFieldLabel(lblAddress);
            Theme.StyleField(txtAddress);
            Theme.StyleFieldLabel(lblPhone);
            Theme.StyleField(txtPhone);
            Theme.StyleFieldLabel(lblEmail);
            Theme.StyleField(txtEmail);
            Theme.StyleFieldLabel(lblEIN);
            Theme.StyleField(txtEIN);
            Theme.StyleFieldLabel(lblPaymentTerms);
            Theme.StyleField(txtPaymentTerms);

            PlaceField(card, lblBusinessName, txtBusinessName, 24, 48, 640);
            PlaceField(card, lblAddress, txtAddress, 24, 96, 640);
            PlaceField(card, lblPhone, txtPhone, 24, 144, 240);
            PlaceField(card, lblEmail, txtEmail, 284, 144, 380);
            PlaceField(card, lblEIN, txtEIN, 24, 192, 240);
            PlaceField(card, lblPaymentTerms, txtPaymentTerms, 284, 192, 380);

            txtBusinessName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtAddress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtEmail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtPaymentTerms.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            BindInvoiceField(txtBusinessName, v => AppState.BusinessName = v);
            BindInvoiceField(txtAddress, v => AppState.Address = v);
            BindInvoiceField(txtPhone, v => AppState.Phone = v);
            BindInvoiceField(txtEmail, v => AppState.CompanyEmail = v);
            BindInvoiceField(txtEIN, v => AppState.Ein = v);
            BindInvoiceField(txtPaymentTerms, v => AppState.PaymentTerms = v);
        }

        private void LayoutDataCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Data folder && term",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            Theme.StyleFieldLabel(lblFolder);
            Theme.StyleField(txtFolderPath);
            Theme.StyleNavyButton(btnChangeFolder);
            Theme.StyleOutlineButton(btnRollToNextTerm);

            PlaceField(card, lblFolder, txtFolderPath, 24, 50, 460);
            txtFolderPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            btnChangeFolder.Size = new Size(150, 34);
            btnChangeFolder.Location = new Point(500, 68);
            btnChangeFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            btnRollToNextTerm.Size = new Size(150, 34);
            btnRollToNextTerm.Location = new Point(500, 108);
            btnRollToNextTerm.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            card.Controls.Add(btnChangeFolder);
            card.Controls.Add(btnRollToNextTerm);

            card.Resize += (_, _) =>
            {
                txtFolderPath.Width = Math.Max(200, card.Width - 210);
                btnChangeFolder.Left = card.Width - 174;
                btnRollToNextTerm.Left = card.Width - 174;
            };
        }

        private void LayoutUserCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "User settings",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            var lbl = new Label { Text = "EMAIL" };
            Theme.StyleFieldLabel(lbl);

            var box = new TextBox
            {
                Name = "txtUserEmail",
                Text = AppState.UserEmail
            };
            Theme.StyleField(box);
            box.PlaceholderText = "you@example.com";
            box.TextChanged += (_, _) =>
            {
                AppState.UserEmail = box.Text;
                AppLock.SaveSettings();
            };

            PlaceField(card, lbl, box, 24, 48, 640);
            box.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            card.Resize += (_, _) =>
            {
                box.Width = Math.Max(200, card.Width - 48);
            };
        }

        private static void PlaceField(Control parent, Label label, TextBox box, int x, int y, int width)
        {
            label.Location = new Point(x, y);
            box.Location = new Point(x, y + 18);
            box.Width = width;
            box.Height = 26;
            parent.Controls.Add(label);
            parent.Controls.Add(box);
        }

        private static void BindInvoiceField(TextBox box, Action<string> set)
        {
            box.Leave += (_, _) =>
            {
                set(box.Text.Trim());
                AppLock.SaveSettings();
            };
        }

        private void LoadCompanyInfo()
        {
            txtBusinessName.Text = AppState.BusinessName;
            txtAddress.Text = AppState.Address;
            txtPhone.Text = AppState.Phone;
            txtEmail.Text = AppState.CompanyEmail;
            txtEIN.Text = AppState.Ein;
            txtPaymentTerms.Text = AppState.PaymentTerms;

            txtFolderPath.Text = AppLock.HasFolder()
                ? AppState.InventoryFolder
                : "No folder selected — click Change Folder";
        }

        private void ApplyLockState()
        {
            bool ready = AppLock.HasFolder();

            txtBusinessName.Enabled = ready;
            txtAddress.Enabled = ready;
            txtPhone.Enabled = ready;
            txtEmail.Enabled = ready;
            txtEIN.Enabled = ready;
            txtPaymentTerms.Enabled = ready;

            txtFolderPath.ReadOnly = true;
            txtFolderPath.BackColor = ready ? Theme.Paper : Theme.DangerFill;
            txtFolderPath.ForeColor = ready ? Theme.Ink : Theme.Danger;

            if (!ready)
                txtFolderPath.Text = "No folder selected — click Change Folder";

            btnChangeFolder.Enabled = true;
            btnRollToNextTerm.Enabled = ready;
        }

        private void btnChangeFolder_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the folder that contains your inventory CSV files",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            if (!Directory.Exists(dialog.SelectedPath))
                return;

            AppLock.SaveFolder(dialog.SelectedPath);
            txtFolderPath.Text = dialog.SelectedPath;

            ApplyLockState();
            DataFiles.EnsureFilesExistOrAsk();
            LoadCompanyInfo();
        }

        private void btnRollToNextTerm_Click(object sender, EventArgs e)
        {
            if (!AppLock.HasFolder())
            {
                MessageBox.Show(
                    "Select a data folder first.",
                    "No Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "This will archive the current CSV files into the 'old data' folder and start a new blank term.\n\nContinue?",
                "Roll to Next Term",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                DataFiles.RollToNextTerm();
                MessageBox.Show(
                    "Current files were archived and a new term was started.",
                    "Term Rolled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Roll Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
