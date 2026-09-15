namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Shared folder, company info, numbering, and the signed-in account.
    /// Company info, numbering, and term roll-over are administrator-only.
    /// </summary>
    public partial class Settings : Form, INavigationPage
    {
        /// <summary>Register this page, build cards, and load company and account fields.</summary>
        public Settings()
        {
            InitializeComponent();
            Navigator.Register(AppPage.Settings, this);
            BuildUi();
            LoadCompanyInfo();
            ApplyLockState();
        }

        /// <summary>Shown or refreshed: enable or disable admin-only fields for the signed-in user.</summary>
        public void HighlightCurrentPage()
        {
            ApplyLockState();
        }

        /// <summary>Company, data folder, numbering, and account cards.</summary>
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
                Text = DataLink.UseInventoryServer
                    ? "These details print on invoices. Every computer talks to the same inventory server so they share the database and these settings."
                    : "These details print on invoices. Point every computer at the same shared data folder so they share the database and these settings.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 48, 0)
            };
            introRow.Controls.Add(intro);
            introRow.Controls.Add(help);
            introRow.Resize += (_, _) => help.Location = new Point(Math.Max(8, introRow.Width - 40), 4);

            var company = new CardPanel { Dock = DockStyle.Top, Height = 280 };
            LayoutCompanyCard(company);

            var data = new CardPanel { Dock = DockStyle.Top, Height = 214 };
            LayoutDataCard(data);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var productNumber = new CardPanel { Dock = DockStyle.Top, Height = 220 };
            LayoutProductNumberCard(productNumber);
            var spacerPn = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var salesOrder = new CardPanel { Dock = DockStyle.Top, Height = 220 };
            LayoutSalesOrderCard(salesOrder);
            var spacerSo = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var user = new CardPanel { Dock = DockStyle.Top, Height = 390 };
            LayoutUserCard(user);
            var spacerUser = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };

            Controls.Add(user);
            Controls.Add(spacerUser);
            Controls.Add(productNumber);
            Controls.Add(spacerPn);
            Controls.Add(salesOrder);
            Controls.Add(spacerSo);
            Controls.Add(data);
            Controls.Add(spacer);
            Controls.Add(company);
            Controls.Add(introRow);
        }

        /// <summary>Business name, address, phone, email, and payment terms. Administrator-only to edit.</summary>
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

            var hint = new Label
            {
                Text = "Only an administrator can change company information. These details print on invoices and sales orders.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                MaximumSize = new Size(640, 0),
                AutoSize = true
            };
            card.Controls.Add(hint);

            Theme.StyleFieldLabel(lblBusinessName);
            Theme.StyleField(txtBusinessName);
            Theme.StyleFieldLabel(lblAddress);
            Theme.StyleField(txtAddress);
            Theme.StyleFieldLabel(lblPhone);
            Theme.StyleField(txtPhone);
            Theme.StyleFieldLabel(lblEmail);
            Theme.StyleField(txtEmail);
            Theme.StyleFieldLabel(lblPaymentTerms);
            Theme.StyleField(txtPaymentTerms);

            PlaceField(card, lblBusinessName, txtBusinessName, 24, 68, 640);
            PlaceField(card, lblAddress, txtAddress, 24, 116, 640);
            PlaceField(card, lblPhone, txtPhone, 24, 164, 240);
            PlaceField(card, lblEmail, txtEmail, 284, 164, 380);
            PlaceField(card, lblPaymentTerms, txtPaymentTerms, 24, 212, 640);

            txtBusinessName.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtAddress.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtEmail.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;
            txtPaymentTerms.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            BindInvoiceField(txtBusinessName, v => AppState.BusinessName = v, adminOnly: true);
            BindInvoiceField(txtAddress, v => AppState.Address = v, adminOnly: true);
            BindInvoiceField(txtPhone, v => AppState.Phone = v, adminOnly: true);
            BindInvoiceField(txtEmail, v => AppState.CompanyEmail = v, adminOnly: true);
            BindInvoiceField(txtPaymentTerms, v => AppState.PaymentTerms = v, adminOnly: true);
        }

        /// <summary>Shared data folder path and Roll to Next Term.</summary>
        private void LayoutDataCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = DataLink.IsRemote ? "Inventory server && term" : "Data folder && term",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            var note = new Label
            {
                Text = DataLink.IsRemote
                    ? "This PC is connected to the inventory server. Database files stay on the host. This PC only remembers the server IP and certificate pin."
                    : "Use one shared folder on every computer (a network drive, or a folder this PC shares). Live work is crc_inventory.db. Finished previous-term rows go into old_inventory.db. This PC only remembers the folder path.",
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
        private CheckBox _reuseSo = null!;
        private TextBox _productPattern = null!;
        private TextBox _productStart = null!;
        private Label _productPreview = null!;
        private CheckBox _reuseProduct = null!;
        private bool _syncingReuse;
        private TextBox _userEmail = null!;

        /// <summary>Purchase PO # pattern (CRC#### / CRCyy-####) and start number. Administrator-only.</summary>
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
                Text = "Same as sales orders. CRCyy-#### and start 10001 gives CRC26-10001 in 2026. Leave blank to keep CRC26-10001, CRC26-10002, …",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                MaximumSize = new Size(640, 0),
                AutoSize = true
            };
            card.Controls.Add(hint);

            var lblPattern = new Label { Text = "PATTERN" };
            Theme.StyleFieldLabel(lblPattern);
            _productPattern = new TextBox
            {
                Text = AppState.ProductNumberPattern,
                PlaceholderText = "CRCyy-####"
            };
            Theme.StyleField(_productPattern);
            PlaceField(card, lblPattern, _productPattern, 24, 82, 280);

            var lblStart = new Label { Text = "START" };
            Theme.StyleFieldLabel(lblStart);
            _productStart = new TextBox
            {
                Text = AppState.ProductNumberStart,
                PlaceholderText = "1"
            };
            Theme.StyleField(_productStart);
            PlaceField(card, lblStart, _productStart, 324, 82, 140);

            _productPreview = new Label
            {
                AutoSize = true,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                Location = new Point(24, 140)
            };
            card.Controls.Add(_productPreview);

            _reuseProduct = MakeReuseCheckbox(card);

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

        /// <summary>Show the next purchase PO that the current pattern and start would produce.</summary>
        private void UpdateProductNumberPreview()
        {
            // Product numbering card not built yet.
            if (_productPreview == null)
                return;

            string savedPattern = AppState.ProductNumberPattern;
            string savedStart = AppState.ProductNumberStart;
            AppState.ProductNumberPattern = _productPattern.Text.Trim();
            AppState.ProductNumberStart = _productStart.Text.Trim();
            // Temporarily apply the typed pattern so Preview uses it.
            try
            {
                _productPreview.Text = "Next product number:  " + DataFiles.PreviewProductNumber();
            }
            // Restore saved numbering so leaving the box without save does not persist.
            finally
            {
                AppState.ProductNumberPattern = savedPattern;
                AppState.ProductNumberStart = savedStart;
            }
        }

        /// <summary>Sales order # pattern and start number. Administrator-only.</summary>
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
                Text = "Use # for the running number. yy or yyyy is the year, mm the month, and dd the day. CRCyy-#### starts at CRC26-0001 in 2026. Add a start of 1000 to get CRC26-1000. Leave the pattern blank to keep 10001, 10002, …",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                MaximumSize = new Size(640, 0),
                AutoSize = true
            };
            card.Controls.Add(hint);

            var lblPattern = new Label { Text = "PATTERN" };
            Theme.StyleFieldLabel(lblPattern);
            _soPattern = new TextBox
            {
                Text = AppState.SalesOrderPattern,
                PlaceholderText = "CRCyy-####"
            };
            Theme.StyleField(_soPattern);
            PlaceField(card, lblPattern, _soPattern, 24, 82, 280);

            var lblStart = new Label { Text = "START" };
            Theme.StyleFieldLabel(lblStart);
            _soStart = new TextBox
            {
                Text = AppState.SalesOrderStart,
                PlaceholderText = "1"
            };
            Theme.StyleField(_soStart);
            PlaceField(card, lblStart, _soStart, 324, 82, 140);

            _soPreview = new Label
            {
                AutoSize = true,
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                Location = new Point(24, 140)
            };
            card.Controls.Add(_soPreview);

            _reuseSo = MakeReuseCheckbox(card);

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

        /// <summary>Show the next SO # that the current pattern and start would produce.</summary>
        private void UpdateSalesOrderPreview()
        {
            // Sales-order numbering card not built yet.
            if (_soPreview == null)
                return;

            string savedPattern = AppState.SalesOrderPattern;
            string savedStart = AppState.SalesOrderStart;
            AppState.SalesOrderPattern = _soPattern.Text.Trim();
            AppState.SalesOrderStart = _soStart.Text.Trim();
            // Temporarily apply the typed pattern so Preview uses it.
            try
            {
                _soPreview.Text = "Next sales order:  " + DataFiles.PreviewSalesOrderNumber();
            }
            // Restore saved numbering so leaving the box without save does not persist.
            finally
            {
                AppState.SalesOrderPattern = savedPattern;
                AppState.SalesOrderStart = savedStart;
            }
        }

        /// <summary>Shared reuse-missing-numbers checkbox for a numbering card.</summary>
        private CheckBox MakeReuseCheckbox(Control card)
        {
            var box = new CheckBox
            {
                Text = "Reuse missing numbers  (if CRC10 is deleted, the next number can be CRC10 again)",
                AutoSize = true,
                Font = Theme.Small,
                ForeColor = Theme.Navy,
                Location = new Point(24, 168)
            };
            card.Controls.Add(box);
            box.CheckedChanged += (_, _) => SaveReuseMissing(box.Checked);
            _syncingReuse = true;
            // Load the flag without firing SaveReuseMissing.
            try
            {
                box.Checked = AppState.ReuseMissingNumbers;
            }
            finally
            {
                // Allow later clicks to persist the shared flag.
                _syncingReuse = false;
            }
            return box;
        }

        /// <summary>Persist the shared “reuse missing numbers” flag and refresh both numbering previews.</summary>
        private void SaveReuseMissing(bool value)
        {
            // Ignore CheckedChanged while LoadCompanyInfo sets the box.
            if (_syncingReuse)
                return;

            _syncingReuse = true;
            // Guard against re-entrancy while updating both numbering checkboxes.
            try
            {
                AppState.ReuseMissingNumbers = value;
                // Keep both numbering cards on the same shared flag.
                if (_reuseProduct != null)
                    _reuseProduct.Checked = value;
                // Keep the sales-order checkbox in sync with product numbering.
                if (_reuseSo != null)
                    _reuseSo.Checked = value;
                AppLock.SaveSettings();
                UpdateProductNumberPreview();
                UpdateSalesOrderPreview();
            }
            finally
            {
                // Allow later clicks to persist the shared flag.
                _syncingReuse = false;
            }
        }

        private Button _signOut = null!;
        private TextBox _accountUser = null!;
        private TextBox _accountName = null!;
        private TextBox _accountCurrent = null!;
        private TextBox _accountNew = null!;
        private TextBox _accountConfirm = null!;
        private Button _accountSave = null!;

        /// <summary>Signed-in user’s username, name, email, password, and sign out.</summary>
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
            _signOut.Click += (_, _) => Accounts.LogOutAndRestart();
            card.Controls.Add(_signOut);

            var hint = new Label
            {
                Text = "Username, name, email, and password are yours to change. Email is also the sales-rep address on invoices. Call IT support to reset a forgotten password.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 280),
                Size = new Size(620, 36)
            };
            card.Controls.Add(hint);
        }

        /// <summary>Save this user’s username, name, email, and optional password change.</summary>
        private void SaveOwnAccount()
        {
            // Account save needs a signed-in user.
            if (!AppState.SignedIn)
                return;

            string newUser = _accountUser.Text.Trim();
            // Duplicate or blank usernames are rejected.
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

            // Blank new password means keep the current one.
            if (_accountNew.Text.Length > 0 || _accountConfirm.Text.Length > 0)
            {
                // Mismatched passwords must not replace the hash.
                if (_accountNew.Text != _accountConfirm.Text)
                {
                    MessageBox.Show("The new passwords do not match.", "Account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                // Wrong current password or policy failure keeps the old hash.
                if (!Accounts.ChangeOwnPassword(AppState.CurrentUsername, _accountCurrent.Text, _accountNew.Text, out error))
                {
                    MessageBox.Show(error, "Account", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                _accountCurrent.Text = "";
                _accountNew.Text = "";
                _accountConfirm.Text = "";
                // Refresh this PC’s session token after a password change.
                if (AppState.StaySignedIn)
                {
                    var self = Accounts.List().FirstOrDefault(a =>
                        a.Username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase));
                    // The renamed account row is used to rewrite the local session.
                    if (self != null)
                        Accounts.RememberSignIn(self);
                }
            }

            ToastAlert.Success(this, "Account saved.");
            ApplyLockState();
        }

        /// <summary>Position a caption and text box on a card.</summary>
        private static void PlaceField(Control parent, Label label, TextBox box, int x, int y, int width)
        {
            label.Location = new Point(x, y);
            box.Location = new Point(x, y + 18);
            box.Width = width;
            box.Height = 26;
            parent.Controls.Add(label);
            parent.Controls.Add(box);
        }

        /// <summary>Save a company field on Leave when the user may edit it.</summary>
        private static void BindInvoiceField(TextBox box, Action<string> set, bool adminOnly = false)
        {
            box.Leave += (_, _) =>
            {
                // Non-admins can view company fields but not persist edits.
                if (adminOnly && !AppState.IsAdmin)
                    return;
                set(box.Text.Trim());
                AppLock.SaveSettings();
            };
        }

        /// <summary>Copy AppState into every Settings field (company, numbering, account, SMTP, folder).</summary>
        private void LoadCompanyInfo()
        {
            txtBusinessName.Text = AppState.BusinessName;
            txtAddress.Text = AppState.Address;
            txtPhone.Text = AppState.Phone;
            txtEmail.Text = AppState.CompanyEmail;
            txtPaymentTerms.Text = AppState.PaymentTerms;

            txtFolderPath.Text = DataLink.IsRemote
                ? AppState.ServerHost + ":" + AppState.ServerPort
                : AppLock.HasFolder()
                    ? AppState.InventoryFolder
                    : "No folder selected — click Change Folder";

            // Numbering controls exist after BuildUi.
            if (_soPattern != null)
                _soPattern.Text = AppState.SalesOrderPattern;
            // Start-number box is optional until the card is built.
            if (_soStart != null)
                _soStart.Text = AppState.SalesOrderStart;
            UpdateSalesOrderPreview();

            // Product pattern box is optional until the card is built.
            if (_productPattern != null)
                _productPattern.Text = AppState.ProductNumberPattern;
            // Product start box is optional until the card is built.
            if (_productStart != null)
                _productStart.Text = AppState.ProductNumberStart;
            _syncingReuse = true;
            // Guard against re-entrancy while setting both reuse checkboxes from AppState.
            try
            {
                // Keep both numbering cards on the same shared flag.
                if (_reuseProduct != null)
                    _reuseProduct.Checked = AppState.ReuseMissingNumbers;
                // Keep the sales-order checkbox in sync with product numbering.
                if (_reuseSo != null)
                    _reuseSo.Checked = AppState.ReuseMissingNumbers;
            }
            finally
            {
                // Allow later clicks to persist the shared flag.
                _syncingReuse = false;
            }
            UpdateProductNumberPreview();

            // Account card may not be built yet during first load.
            if (_userEmail != null)
                _userEmail.Text = AppState.UserEmail;
            // Fill username when the account card exists.
            if (_accountUser != null)
                _accountUser.Text = AppState.CurrentUsername;
            // Fill display name when the account card exists.
            if (_accountName != null)
                _accountName.Text = AppState.CurrentDisplayName;

            // Signed-in caption is looked up by name from the card.
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

        /// <summary>
        /// Company info, numbering, and Roll to Next Term need an administrator.
        /// Account fields need a signed-in user. Folder can always be changed.
        /// </summary>
        private void ApplyLockState()
        {
            bool ready = AppLock.HasFolder();
            bool admin = ready && AppState.IsAdmin;

            txtBusinessName.Enabled = admin;
            txtAddress.Enabled = admin;
            txtPhone.Enabled = admin;
            txtEmail.Enabled = admin;
            txtPaymentTerms.Enabled = admin;
            // Numbering controls exist after BuildUi.
            if (_soPattern != null)
                _soPattern.Enabled = admin;
            // Start-number box is optional until the card is built.
            if (_soStart != null)
                _soStart.Enabled = admin;
            // Product pattern box is optional until the card is built.
            if (_productPattern != null)
                _productPattern.Enabled = admin;
            // Product start box is optional until the card is built.
            if (_productStart != null)
                _productStart.Enabled = admin;
            // Keep both numbering cards on the same shared flag.
            if (_reuseProduct != null)
                _reuseProduct.Enabled = admin;
            // Keep the sales-order checkbox in sync with product numbering.
            if (_reuseSo != null)
                _reuseSo.Enabled = admin;
            // Account card may not be built yet during first load.
            if (_userEmail != null)
                _userEmail.Enabled = ready && AppState.SignedIn;
            // Fill username when the account card exists.
            if (_accountUser != null)
                _accountUser.Enabled = ready && AppState.SignedIn;
            // Fill display name when the account card exists.
            if (_accountName != null)
                _accountName.Enabled = ready && AppState.SignedIn;
            // Password boxes exist after the account card is built.
            if (_accountCurrent != null)
                _accountCurrent.Enabled = ready && AppState.SignedIn;
            // New-password box is disabled until signed in.
            if (_accountNew != null)
                _accountNew.Enabled = ready && AppState.SignedIn;
            // Confirm box follows the same signed-in lock.
            if (_accountConfirm != null)
                _accountConfirm.Enabled = ready && AppState.SignedIn;
            // Save is disabled until a user is signed in.
            if (_accountSave != null)
                _accountSave.Enabled = ready && AppState.SignedIn;

            txtFolderPath.ReadOnly = true;
            txtFolderPath.BackColor = ready ? Theme.Paper : Theme.DangerFill;
            txtFolderPath.ForeColor = ready ? Theme.Ink : Theme.Danger;

            // Highlight the missing folder so they click Change Folder.
            if (!ready)
                txtFolderPath.Text = "No folder selected — click Change Folder";

            btnChangeFolder.Enabled = true;
            btnRollToNextTerm.Enabled = admin;

            // Signed-in caption is looked up by name from the card.
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

        /// <summary>Point this PC at a different shared data folder and reload settings from that database.</summary>
        private void btnChangeFolder_Click(object sender, EventArgs e)
        {
            // Server clients change host on the sign-in screen, not here.
            if (DataLink.IsRemote)
            {
                MessageBox.Show(
                    "The server IP is set on the sign-in screen. Sign out and connect to a different address if you need to.",
                    "Inventory server",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the shared folder for the inventory database (same folder on every computer)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };

            // Cancel keeps the current folder.
            if (dialog.ShowDialog() != DialogResult.OK)
                return;

            // The picker can return a path that was deleted.
            if (!Directory.Exists(dialog.SelectedPath))
                return;

            AppLock.SaveFolder(dialog.SelectedPath);
            txtFolderPath.Text = dialog.SelectedPath;

            ApplyLockState();
            DataFiles.EnsureFilesExistOrAsk();
            AppLock.LoadSharedSettings();
            LoadCompanyInfo();
        }

        /// <summary>
        /// Administrator only: move completed process rows into Old Inventory and start a new term.
        /// Unfinished rows stay in the live database.
        /// </summary>
        private void btnRollToNextTerm_Click(object sender, EventArgs e)
        {
            // Roll-over needs a live database.
            if (!AppLock.HasFolder())
            {
                MessageBox.Show(
                    "Select a data folder first.",
                    "No Folder",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return;
            }

            // Only administrators can archive completed process rows.
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
                "This starts a new term. Completed purchases, sales, invoices, banking, debits, and credits move into old_inventory.db. Unfinished rows stay in the live database with no date until they are completed. Leftover CSV files move into 'old data'. Customers, vendors, and inventory stay current.\n\nContinue?",
                "Roll to Next Term",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);

            // Roll-over archives completed rows; require an explicit yes.
            if (confirm != DialogResult.Yes)
                return;

            // Archive completed rows; show success only if the move finishes.
            try
            {
                DataFiles.RollToNextTerm();
                MessageBox.Show(
                    "A new term was started. Completed work is in old_inventory.db. Unfinished rows stayed in the live database.",
                    "Term Rolled",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            }
            // A failed roll-over must not look like it succeeded.
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, "Roll Failed", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }
    }
}
