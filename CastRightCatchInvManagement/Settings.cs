using CrcInventory.Protocol;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Shared folder and the signed-in account.
    /// </summary>
    public partial class Settings : Form, INavigationPage
    {
        /// <summary>Register this page, build cards, and load folder and account fields.</summary>
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

        /// <summary>Data folder and account cards.</summary>
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
                    ? "Your account and this PC’s connection. Company information and document numbers are on Admin → Admin management."
                    : "Your account and this PC’s data folder. Company information and document numbers are on Admin → Admin management.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Fill,
                Padding = new Padding(0, 8, 48, 0)
            };
            introRow.Controls.Add(intro);
            introRow.Controls.Add(help);
            introRow.Resize += (_, _) => help.Location = new Point(Math.Max(8, introRow.Width - 40), 4);

            var data = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutDataCard(data);

            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            var user = new CardPanel { Dock = DockStyle.Top, Height = 390 };
            LayoutUserCard(user);
            var spacerUser = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };

            Controls.Add(user);
            Controls.Add(spacerUser);
            Controls.Add(data);
            Controls.Add(spacer);
            Controls.Add(introRow);
        }

        /// <summary>Shared data folder path, or the inventory server hostname on hosted builds.</summary>
        private void LayoutDataCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = DataLink.UseInventoryServer ? "Inventory server" : "Data folder",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            card.Controls.Add(heading);

            var note = new Label
            {
                Text = DataLink.UseInventoryServer
                    ? "This PC connects to " + InventoryHost.DnsName + ". Database files stay on the host."
                    : "Use one shared folder on every computer (a network drive, or a folder this PC shares). Live work is crc_inventory.db. This PC only remembers the folder path.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(640, 36)
            };
            card.Controls.Add(note);

            Theme.StyleFieldLabel(lblFolder);
            lblFolder.Text = DataLink.UseInventoryServer ? "INVENTORY SERVER" : "DATA FOLDER PATH";
            Theme.StyleField(txtFolderPath);
            Theme.StyleNavyButton(btnChangeFolder);

            PlaceField(card, lblFolder, txtFolderPath, 24, 82, 460);
            txtFolderPath.Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right;

            btnChangeFolder.Size = new Size(150, 34);
            btnChangeFolder.Location = new Point(500, 100);
            btnChangeFolder.Anchor = AnchorStyles.Top | AnchorStyles.Right;

            if (!DataLink.UseInventoryServer)
                card.Controls.Add(btnChangeFolder);

            card.Resize += (_, _) =>
            {
                note.Width = Math.Max(200, card.Width - 48);
                txtFolderPath.Width = Math.Max(200, card.Width - 210);
                btnChangeFolder.Left = card.Width - 174;
            };
        }

        private TextBox _userEmail = null!;

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

        /// <summary>Copy AppState into folder and account fields.</summary>
        private void LoadCompanyInfo()
        {
            txtFolderPath.Text = DataLink.UseInventoryServer
                ? InventoryHost.DnsName + ":" + InventoryHost.Port
                : AppLock.HasFolder()
                    ? AppState.InventoryFolder
                    : "No folder selected — click Change Folder";

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
        /// Account fields need a signed-in user. Folder can always be changed.
        /// </summary>
        private void ApplyLockState()
        {
            bool ready = AppLock.HasFolder();

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

            if (!ready)
                txtFolderPath.Text = DataLink.UseInventoryServer
                    ? "Not connected to the inventory server"
                    : "No folder selected — click Change Folder";

            btnChangeFolder.Enabled = !DataLink.UseInventoryServer;
            btnChangeFolder.Visible = !DataLink.UseInventoryServer;

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
            // Hosted builds have no folder; the hostname is fixed.
            if (DataLink.UseInventoryServer)
                return;

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
    }
}
