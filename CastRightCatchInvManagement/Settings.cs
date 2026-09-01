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

            var introRow = new Panel
            {
                Dock = DockStyle.Top,
                Height = 44,
                BackColor = Theme.Cream
            };
            var help = new Button
            {
                Text = "",
                Size = new Size(36, 36),
                Anchor = AnchorStyles.Top | AnchorStyles.Right,
                TabStop = false,
                AccessibleName = "Controls"
            };
            Theme.StyleGoldButton(help);
            help.Paint += (_, e) => ControlsGlyph.Paint(e.Graphics, help.ClientRectangle, Theme.NavyDark);
            new ToolTip { ShowAlways = true }.SetToolTip(help, "Controls");
            help.Click += (_, _) => Navigator.GoTo(AppPage.Help);
            var intro = new Label
            {
                Text = "These details print on invoices. Point every computer at the same shared data folder so they share the database and these settings.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 48, 0)
            };
            introRow.Controls.Add(intro);
            introRow.Controls.Add(help);
            introRow.Resize += (_, _) => help.Location = new Point(Math.Max(8, introRow.Width - 40), 4);

            var company = new CardPanel { Dock = DockStyle.Top, Height = 300 };
            LayoutCompanyCard(company);

            var data = new CardPanel { Dock = DockStyle.Top, Height = 214 };
            LayoutDataCard(data);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var productNumber = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutProductNumberCard(productNumber);
            var spacerPn = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var salesOrder = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutSalesOrderCard(salesOrder);
            var spacerSo = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var user = new CardPanel { Dock = DockStyle.Top, Height = 390 };
            LayoutUserCard(user);
            var spacerUser = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var mail = new CardPanel { Dock = DockStyle.Top, Height = 220 };
            LayoutMailCard(mail);
            var spacerMail = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };

            Controls.Add(user);
            Controls.Add(spacerUser);
            Controls.Add(mail);
            Controls.Add(spacerMail);
            Controls.Add(productNumber);
            Controls.Add(spacerPn);
            Controls.Add(salesOrder);
            Controls.Add(spacerSo);
            Controls.Add(data);
            Controls.Add(spacer);
            Controls.Add(company);
            Controls.Add(introRow);
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

            var note = new Label
            {
                Text = "Use one shared folder on every computer (a network drive, or a folder this PC shares). Inventory, settings, and PDFs live in crc_inventory.db. This PC only remembers the folder path.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(640, 36)
            };
            card.Controls.Add(note);

            Theme.StyleFieldLabel(lblFolder);
            Theme.StyleField(txtFolderPath);
            Theme.StyleNavyButton(btnChangeFolder);
            Theme.StyleOutlineButton(btnRollToNextTerm);

            PlaceField(card, lblFolder, txtFolderPath, 24, 82, 460);
            txtFolderPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            btnChangeFolder.Size = new Size(150, 34);
            btnChangeFolder.Location = new Point(500, 100);
            btnChangeFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            btnRollToNextTerm.Size = new Size(150, 34);
            btnRollToNextTerm.Location = new Point(500, 140);
            btnRollToNextTerm.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            card.Controls.Add(btnChangeFolder);
            card.Controls.Add(btnRollToNextTerm);

            card.Resize += (_, _) =>
            {
                note.Width = Math.Max(200, card.Width - 48);
                txtFolderPath.Width = Math.Max(200, card.Width - 210);
                btnChangeFolder.Left = card.Width - 174;
                btnRollToNextTerm.Left = card.Width - 174;
            };
        }

        private TextBox _soPattern = null!;
        private TextBox _soStart = null!;
        private Label _soPreview = null!;
        private TextBox _productPattern = null!;
        private TextBox _productStart = null!;
        private Label _productPreview = null!;
        private TextBox _userEmail = null!;

        private void LayoutProductNumberCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Product numbers",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            var hint = new Label
            {
                Text = "Same as sales orders. CRC#### starts at CRC0001. CRC26-#### and start 10001 gives CRC26-10001. Leave blank to keep CRC26-10001, CRC26-10002, …",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                AutoSize = true
            };
            card.Controls.Add(hint);

            var lblPattern = new Label { Text = "PATTERN" };
            Theme.StyleFieldLabel(lblPattern);
            _productPattern = new TextBox
            {
                Text = AppState.ProductNumberPattern,
                PlaceholderText = "CRC####"
            };
            Theme.StyleField(_productPattern);
            PlaceField(card, lblPattern, _productPattern, 24, 64, 280);

            var lblStart = new Label { Text = "START" };
            Theme.StyleFieldLabel(lblStart);
            _productStart = new TextBox
            {
                Text = AppState.ProductNumberStart,
                PlaceholderText = "1"
            };
            Theme.StyleField(_productStart);
            PlaceField(card, lblStart, _productStart, 324, 64, 140);

            _productPreview = new Label
            {
                AutoSize = true,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                Location = new Point(24, 122)
            };
            card.Controls.Add(_productPreview);

            void SaveAndPreview()
            {
                AppState.ProductNumberPattern = _productPattern.Text.Trim();
                AppState.ProductNumberStart = _productStart.Text.Trim();
                AppLock.SaveSettings();
                UpdateProductNumberPreview();
            }

            _productPattern.Leave += (_, _) => SaveAndPreview();
            _productStart.Leave += (_, _) => SaveAndPreview();
            _productPattern.TextChanged += (_, _) => UpdateProductNumberPreview();
            _productStart.TextChanged += (_, _) => UpdateProductNumberPreview();
            UpdateProductNumberPreview();
        }

        private void UpdateProductNumberPreview()
        {
            if (_productPreview == null)
                return;

            string savedPattern = AppState.ProductNumberPattern;
            string savedStart = AppState.ProductNumberStart;
            AppState.ProductNumberPattern = _productPattern.Text.Trim();
            AppState.ProductNumberStart = _productStart.Text.Trim();
            try
            {
                _productPreview.Text = "Next product number:  " + DataFiles.PreviewProductNumber();
            }
            finally
            {
                AppState.ProductNumberPattern = savedPattern;
                AppState.ProductNumberStart = savedStart;
            }
        }

        private void LayoutSalesOrderCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Sales order numbers",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            var hint = new Label
            {
                Text = "Use # for digits. CRC#### starts at CRC0001. Add a start of 1000 to get CRC1000. Leave the pattern blank to keep 10001, 10002, …",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                AutoSize = true
            };
            card.Controls.Add(hint);

            var lblPattern = new Label { Text = "PATTERN" };
            Theme.StyleFieldLabel(lblPattern);
            _soPattern = new TextBox
            {
                Text = AppState.SalesOrderPattern,
                PlaceholderText = "CRC####"
            };
            Theme.StyleField(_soPattern);
            PlaceField(card, lblPattern, _soPattern, 24, 64, 280);

            var lblStart = new Label { Text = "START" };
            Theme.StyleFieldLabel(lblStart);
            _soStart = new TextBox
            {
                Text = AppState.SalesOrderStart,
                PlaceholderText = "1"
            };
            Theme.StyleField(_soStart);
            PlaceField(card, lblStart, _soStart, 324, 64, 140);

            _soPreview = new Label
            {
                AutoSize = true,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                Location = new Point(24, 122)
            };
            card.Controls.Add(_soPreview);

            void SaveAndPreview()
            {
                AppState.SalesOrderPattern = _soPattern.Text.Trim();
                AppState.SalesOrderStart = _soStart.Text.Trim();
                AppLock.SaveSettings();
                UpdateSalesOrderPreview();
            }

            _soPattern.Leave += (_, _) => SaveAndPreview();
            _soStart.Leave += (_, _) => SaveAndPreview();
            _soPattern.TextChanged += (_, _) => UpdateSalesOrderPreview();
            _soStart.TextChanged += (_, _) => UpdateSalesOrderPreview();
            UpdateSalesOrderPreview();
        }

        private void UpdateSalesOrderPreview()
        {
            if (_soPreview == null)
                return;

            string savedPattern = AppState.SalesOrderPattern;
            string savedStart = AppState.SalesOrderStart;
            AppState.SalesOrderPattern = _soPattern.Text.Trim();
            AppState.SalesOrderStart = _soStart.Text.Trim();
            try
            {
                _soPreview.Text = "Next sales order:  " + DataFiles.PreviewSalesOrderNumber();
            }
            finally
            {
                AppState.SalesOrderPattern = savedPattern;
                AppState.SalesOrderStart = savedStart;
            }
        }

        private Button _signOut = null!;
        private TextBox _accountUser = null!;
        private TextBox _accountName = null!;
        private TextBox _accountCurrent = null!;
        private TextBox _accountNew = null!;
        private TextBox _accountConfirm = null!;
        private Button _accountSave = null!;
        private TextBox _smtpHost = null!;
        private TextBox _smtpPort = null!;
        private TextBox _smtpUser = null!;
        private TextBox _smtpPassword = null!;

        private void LayoutUserCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Your account",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            var who = new Label
            {
                Name = "lblSignedIn",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 40),
                AutoSize = true
            };
            card.Controls.Add(who);

            _accountUser = new TextBox();
            _accountName = new TextBox();
            _userEmail = new TextBox { Name = "txtUserEmail" };
            _accountCurrent = new TextBox { UseSystemPasswordChar = true };
            _accountNew = new TextBox { UseSystemPasswordChar = true };
            _accountConfirm = new TextBox { UseSystemPasswordChar = true };
            Theme.StyleField(_accountUser);
            Theme.StyleField(_accountName);
            Theme.StyleField(_userEmail);
            Theme.StyleField(_accountCurrent);
            Theme.StyleField(_accountNew);
            Theme.StyleField(_accountConfirm);
            _userEmail.PlaceholderText = "you@example.com";
            _accountNew.PlaceholderText = "Leave blank to keep";

            var lblUser = new Label { Text = "USERNAME" };
            var lblName = new Label { Text = "NAME" };
            var lblEmail = new Label { Text = "EMAIL" };
            var lblCurrent = new Label { Text = "CURRENT PASSWORD" };
            var lblNew = new Label { Text = "NEW PASSWORD" };
            var lblConfirm = new Label { Text = "CONFIRM PASSWORD" };
            Theme.StyleFieldLabel(lblUser);
            Theme.StyleFieldLabel(lblName);
            Theme.StyleFieldLabel(lblEmail);
            Theme.StyleFieldLabel(lblCurrent);
            Theme.StyleFieldLabel(lblNew);
            Theme.StyleFieldLabel(lblConfirm);
            PlaceField(card, lblUser, _accountUser, 24, 62, 300);
            PlaceField(card, lblName, _accountName, 344, 62, 300);
            PlaceField(card, lblEmail, _userEmail, 24, 116, 620);
            PlaceField(card, lblCurrent, _accountCurrent, 24, 170, 200);
            PlaceField(card, lblNew, _accountNew, 244, 170, 200);
            PlaceField(card, lblConfirm, _accountConfirm, 464, 170, 200);

            _accountSave = new Button
            {
                Text = "Save account",
                Size = new Size(140, 34),
                Location = new Point(24, 236)
            };
            Theme.StyleGoldButton(_accountSave);
            _accountSave.Click += (_, _) => SaveOwnAccount();
            card.Controls.Add(_accountSave);

            _signOut = new Button
            {
                Text = "Sign out",
                Size = new Size(110, 34),
                Location = new Point(176, 236)
            };
            Theme.StyleOutlineButton(_signOut);
            _signOut.Click += (_, _) =>
            {
                AppState.SignOut();
                Application.Restart();
            };
            card.Controls.Add(_signOut);

            var questions = new Button
            {
                Text = "Security questions",
                Size = new Size(160, 34),
                Location = new Point(296, 236)
            };
            Theme.StyleNavyButton(questions);
            questions.Click += (_, _) =>
            {
                if (!AppState.SignedIn)
                    return;
                using var form = new SecurityQuestionsForm(AppState.CurrentUsername);
                form.ShowDialog(this);
            };
            card.Controls.Add(questions);

            var hint = new Label
            {
                Text = "Username, name, email, and password are yours to change. Email is also the sales-rep address on invoices.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 280),
                Size = new Size(620, 36)
            };
            card.Controls.Add(hint);
        }

        private void LayoutMailCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Login email (SMTP)",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);
            var hint = new Label
            {
                Text = "Used when IT adds a user. The message includes their username and temporary password.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 40),
                Size = new Size(620, 28)
            };
            card.Controls.Add(hint);

            _smtpHost = new TextBox();
            _smtpPort = new TextBox();
            _smtpUser = new TextBox();
            _smtpPassword = new TextBox { UseSystemPasswordChar = true };
            Theme.StyleField(_smtpHost);
            Theme.StyleField(_smtpPort);
            Theme.StyleField(_smtpUser);
            Theme.StyleField(_smtpPassword);
            _smtpHost.PlaceholderText = "smtp.office365.com";
            _smtpPort.PlaceholderText = "587";
            var lblHost = new Label { Text = "SMTP HOST" };
            var lblPort = new Label { Text = "PORT" };
            var lblSmtpUser = new Label { Text = "SMTP USERNAME" };
            var lblSmtpPass = new Label { Text = "SMTP PASSWORD" };
            Theme.StyleFieldLabel(lblHost);
            Theme.StyleFieldLabel(lblPort);
            Theme.StyleFieldLabel(lblSmtpUser);
            Theme.StyleFieldLabel(lblSmtpPass);
            PlaceField(card, lblHost, _smtpHost, 24, 72, 360);
            PlaceField(card, lblPort, _smtpPort, 404, 72, 80);
            PlaceField(card, lblSmtpUser, _smtpUser, 24, 126, 300);
            PlaceField(card, lblSmtpPass, _smtpPassword, 344, 126, 280);
            BindInvoiceField(_smtpHost, v => AppState.SmtpHost = v);
            BindInvoiceField(_smtpUser, v => AppState.SmtpUser = v);
            _smtpPort.Leave += (_, _) =>
            {
                if (int.TryParse(_smtpPort.Text.Trim(), out int port) && port > 0)
                    AppState.SmtpPort = port;
                AppLock.SaveSettings();
            };
            _smtpPassword.Leave += (_, _) =>
            {
                AppState.SmtpPassword = _smtpPassword.Text;
                AppLock.SaveSettings();
            };
        }

        private void SaveOwnAccount()
        {
            if (!AppState.SignedIn)
                return;

            string newUser = _accountUser.Text.Trim();
            if (!Accounts.RenameUser(AppState.CurrentUsername, newUser, out string error))
            {
                MessageBox.Show(error, "Account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string name = _accountName.Text.Trim();
            string email = _userEmail.Text.Trim();
            SqliteInventory.UpdateAccount(AppState.CurrentUsername, name, email);
            AppState.CurrentDisplayName = name.Length > 0 ? name : AppState.CurrentUsername;
            AppState.UserEmail = email;
            AppLock.SaveSettings();

            if (_accountNew.Text.Length > 0 || _accountConfirm.Text.Length > 0)
            {
                if (_accountNew.Text != _accountConfirm.Text)
                {
                    MessageBox.Show("The new passwords do not match.", "Account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (!Accounts.ChangeOwnPassword(AppState.CurrentUsername, _accountCurrent.Text, _accountNew.Text, out error))
                {
                    MessageBox.Show(error, "Account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _accountCurrent.Text = "";
                _accountNew.Text = "";
                _accountConfirm.Text = "";
            }

            ToastAlert.Success(this, "Account saved.");
            ApplyLockState();
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

            if (_soPattern != null)
                _soPattern.Text = AppState.SalesOrderPattern;
            if (_soStart != null)
                _soStart.Text = AppState.SalesOrderStart;
            UpdateSalesOrderPreview();

            if (_productPattern != null)
                _productPattern.Text = AppState.ProductNumberPattern;
            if (_productStart != null)
                _productStart.Text = AppState.ProductNumberStart;
            UpdateProductNumberPreview();

            if (_userEmail != null)
                _userEmail.Text = AppState.UserEmail;
            if (_accountUser != null)
                _accountUser.Text = AppState.CurrentUsername;
            if (_accountName != null)
                _accountName.Text = AppState.CurrentDisplayName;
            if (_smtpHost != null)
                _smtpHost.Text = AppState.SmtpHost;
            if (_smtpPort != null)
                _smtpPort.Text = AppState.SmtpPort > 0 ? AppState.SmtpPort.ToString() : "587";
            if (_smtpUser != null)
                _smtpUser.Text = AppState.SmtpUser;
            if (_smtpPassword != null)
                _smtpPassword.Text = AppState.SmtpPassword;

            if (Controls.Find("lblSignedIn", true).FirstOrDefault() is Label who)
            {
                string name = AppState.CurrentDisplayName.Length > 0
                    ? AppState.CurrentDisplayName
                    : AppState.CurrentUsername;
                who.Text = AppState.SignedIn
                    ? name + (AppState.IsIt ? "  ·  IT" : AppState.IsAdmin ? "  ·  Administrator" : "  ·  User")
                    : "Not signed in";
            }
        }

        private void ApplyLockState()
        {
            bool ready = AppLock.HasFolder();
            bool admin = ready && AppState.IsAdmin;

            txtBusinessName.Enabled = admin;
            txtAddress.Enabled = admin;
            txtPhone.Enabled = admin;
            txtEmail.Enabled = admin;
            txtEIN.Enabled = admin;
            txtPaymentTerms.Enabled = admin;
            if (_soPattern != null)
                _soPattern.Enabled = admin;
            if (_soStart != null)
                _soStart.Enabled = admin;
            if (_productPattern != null)
                _productPattern.Enabled = admin;
            if (_productStart != null)
                _productStart.Enabled = admin;
            if (_userEmail != null)
                _userEmail.Enabled = ready && AppState.SignedIn;
            if (_accountUser != null)
                _accountUser.Enabled = ready && AppState.SignedIn;
            if (_accountName != null)
                _accountName.Enabled = ready && AppState.SignedIn;
            if (_accountCurrent != null)
                _accountCurrent.Enabled = ready && AppState.SignedIn;
            if (_accountNew != null)
                _accountNew.Enabled = ready && AppState.SignedIn;
            if (_accountConfirm != null)
                _accountConfirm.Enabled = ready && AppState.SignedIn;
            if (_accountSave != null)
                _accountSave.Enabled = ready && AppState.SignedIn;
            if (_smtpHost != null)
                _smtpHost.Enabled = admin;
            if (_smtpPort != null)
                _smtpPort.Enabled = admin;
            if (_smtpUser != null)
                _smtpUser.Enabled = admin;
            if (_smtpPassword != null)
                _smtpPassword.Enabled = admin;

            txtFolderPath.ReadOnly = true;
            txtFolderPath.BackColor = ready ? Theme.Paper : Theme.DangerFill;
            txtFolderPath.ForeColor = ready ? Theme.Ink : Theme.Danger;

            if (!ready)
                txtFolderPath.Text = "No folder selected — click Change Folder";

            btnChangeFolder.Enabled = true;
            btnRollToNextTerm.Enabled = admin;

            if (Controls.Find("lblSignedIn", true).FirstOrDefault() is Label who)
            {
                string name = AppState.CurrentDisplayName.Length > 0
                    ? AppState.CurrentDisplayName
                    : AppState.CurrentUsername;
                who.Text = AppState.SignedIn
                    ? name + (AppState.IsIt ? "  ·  IT" : AppState.IsAdmin ? "  ·  Administrator" : "  ·  User")
                    : "Not signed in";
            }
        }

        private void btnChangeFolder_Click(object sender, EventArgs e)
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the shared folder for the inventory database (same folder on every computer)",
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
            AppLock.LoadSharedSettings();
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

            if (!AppState.IsAdmin)
            {
                MessageBox.Show(
                    "Only an administrator can roll to the next term.",
                    "Administrator",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            var confirm = MessageBox.Show(
                "This starts a new term. Purchases, sales, invoices, and other term data stay in the database under the previous term. Leftover CSV files move into 'old data'. Customers, vendors, and item codes stay current.\n\nContinue?",
                "Roll to Next Term",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            if (confirm != DialogResult.Yes)
                return;

            try
            {
                DataFiles.RollToNextTerm();
                MessageBox.Show(
                    "A new term was started. Earlier rows stay in the database.",
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
