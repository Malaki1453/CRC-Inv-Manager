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
                Text = "These details print on invoices. A data folder is required before the rest of the workspace will open.",
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

            var data = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutDataCard(data);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var productNumber = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutProductNumberCard(productNumber);
            var spacerPn = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var salesOrder = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutSalesOrderCard(salesOrder);
            var spacerSo = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var user = new CardPanel { Dock = DockStyle.Top, Height = 128 };
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

        private TextBox _soPattern = null!;
        private TextBox _soStart = null!;
        private Label _soPreview = null!;
        private TextBox _productPattern = null!;
        private TextBox _productStart = null!;
        private Label _productPreview = null!;

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
            if (_soPattern != null)
                _soPattern.Enabled = ready;
            if (_soStart != null)
                _soStart.Enabled = ready;
            if (_productPattern != null)
                _productPattern.Enabled = ready;
            if (_productStart != null)
                _productStart.Enabled = ready;

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
