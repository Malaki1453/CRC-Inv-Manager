using CrcInventory.Protocol;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Sign-in, server IP connect, first-IT create (local folder only), and forgot-password.
    /// </summary>
    internal sealed class SignInForm : Form
    {
        private readonly TextBox _folder;
        private readonly Button _changeFolder;
        private readonly Button _connect;
        private readonly CardPanel _serverCard;
        private readonly CardPanel _folderCard;

        private readonly Label _status;
        private readonly Panel _setupPanel;
        private readonly Panel _signInPanel;
        private readonly TextBox _setupUser;
        private readonly TextBox _setupPassword;
        private readonly TextBox _setupConfirm;
        private readonly TextBox _user;
        private readonly TextBox _password;
        private readonly CheckBox _staySignedIn;

        /// <summary>Build the branded login window; Shown picks server vs folder vs first-IT setup.</summary>
        public SignInForm()
        {
            Text = "Cast Right Catch Inventory";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            MinimizeBox = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(460, 700);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            // Use the packaged seal icon when the asset pack is present.
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;
            WindowChrome.Apply(this);

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 132,
                BackColor = Theme.NavyDark
            };
            var gold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };
            // Header still reads without the seal if the PNG is missing.
            if (BrandAssets.Seal != null)
            {
                header.Controls.Add(new PictureBox
                {
                    Image = BrandAssets.Seal,
                    SizeMode = PictureBoxSizeMode.Zoom,
                    Location = new Point(24, 22),
                    Size = new Size(64, 64),
                    BackColor = Color.Transparent
                });
            }

            header.Controls.Add(new Label
            {
                Text = "CAST RIGHT",
                Font = Theme.BrandTitle,
                ForeColor = Theme.Cream,
                AutoSize = true,
                Location = new Point(100, 28),
                BackColor = Color.Transparent
            });
            header.Controls.Add(new Label
            {
                Text = "Catch Co.",
                Font = Theme.BrandItalic,
                ForeColor = Theme.GoldLight,
                AutoSize = true,
                Location = new Point(100, 52),
                BackColor = Color.Transparent
            });
            header.Controls.Add(new Label
            {
                Text = "INVENTORY MANAGER",
                Font = Theme.Caption,
                ForeColor = Color.FromArgb(170, Theme.CreamDark),
                AutoSize = true,
                Location = new Point(100, 84),
                BackColor = Color.Transparent
            });
            header.Controls.Add(gold);

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(28, 20, 28, 20),
                BackColor = Theme.Cream
            };

            _serverCard = new CardPanel { Dock = DockStyle.Top, Height = 118, Visible = false };
            var serverLabel = new Label { Text = "INVENTORY SERVER" };
            Theme.StyleFieldLabel(serverLabel);
            serverLabel.Location = new Point(20, 14);
            var hostHint = new Label
            {
                Text = InventoryHost.DnsName + ":" + InventoryHost.Port,
                Font = Theme.Body,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 36)
            };
            _connect = new Button
            {
                Text = "Retry",
                Size = new Size(90, 28),
                Location = new Point(308, 72)
            };
            Theme.StyleNavyButton(_connect);
            _connect.Click += (_, _) => ConnectToCompanyServer();
            _serverCard.Controls.Add(serverLabel);
            _serverCard.Controls.Add(hostHint);
            _serverCard.Controls.Add(_connect);

            _folderCard = new CardPanel { Dock = DockStyle.Top, Height = 92 };
            var folderLabel = new Label { Text = "DATA FOLDER" };
            Theme.StyleFieldLabel(folderLabel);
            folderLabel.Location = new Point(20, 14);
            _folder = new TextBox { ReadOnly = true, Location = new Point(20, 34), Size = new Size(278, 26) };
            Theme.StyleField(_folder);
            _changeFolder = new Button
            {
                Text = "Change",
                Size = new Size(90, 28),
                Location = new Point(308, 33)
            };
            Theme.StyleNavyButton(_changeFolder);
            _changeFolder.Click += (_, _) => ChooseFolder();
            _folderCard.Controls.Add(folderLabel);
            _folderCard.Controls.Add(_folder);
            _folderCard.Controls.Add(_changeFolder);

            _setupPanel = new CardPanel { Dock = DockStyle.Top, Height = 280, Visible = false };
            var setupTitle = new Label
            {
                Text = "Create the first IT user",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 14)
            };
            var setupHint = new Label
            {
                Text = "No IT user is listed yet. This account becomes IT so you can add people later. Password is stored with Argon2id, never as plain text.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(20, 40),
                Size = new Size(360, 36)
            };
            _setupUser = AddBox(_setupPanel, "USERNAME", 20, 82, 360);
            _setupPassword = AddBox(_setupPanel, "PASSWORD", 20, 136, 360);
            _setupPassword.UseSystemPasswordChar = true;
            _setupConfirm = AddBox(_setupPanel, "CONFIRM PASSWORD", 20, 190, 360);
            _setupConfirm.UseSystemPasswordChar = true;
            var create = new Button
            {
                Text = "Create IT user",
                Size = new Size(150, 34),
                Location = new Point(20, 236)
            };
            Theme.StyleGoldButton(create);
            create.Click += (_, _) => CreateAdmin();
            AcceptButton = create;
            _setupPanel.Controls.Add(setupTitle);
            _setupPanel.Controls.Add(setupHint);
            _setupPanel.Controls.Add(create);

            _signInPanel = new CardPanel { Dock = DockStyle.Top, Height = 268, Visible = false };
            var signTitle = new Label
            {
                Text = "Sign in",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 14)
            };
            _user = AddBox(_signInPanel, "USERNAME", 20, 46, 360);
            _password = AddBox(_signInPanel, "PASSWORD", 20, 100, 360);
            _password.UseSystemPasswordChar = true;
            _staySignedIn = new CheckBox
            {
                Text = "Stay signed in on this PC",
                Font = Theme.Small,
                ForeColor = Theme.Navy,
                Location = new Point(20, 156),
                Size = new Size(360, 22),
                AutoSize = false
            };
            var signIn = new Button
            {
                Text = "Sign in",
                Size = new Size(120, 34),
                Location = new Point(20, 188)
            };
            Theme.StyleGoldButton(signIn);
            signIn.Click += (_, _) => SignIn();
            var forgot = new Button
            {
                Text = "Forgot password",
                Size = new Size(140, 34),
                Location = new Point(150, 188)
            };
            Theme.StyleOutlineButton(forgot);
            forgot.Click += (_, _) =>
            {
                MessageBox.Show(
                    "Call IT support to reset your password. They will unlock your account or give you a new password.",
                    "Forgot password",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
            };
            _signInPanel.Controls.Add(signTitle);
            _signInPanel.Controls.Add(_staySignedIn);
            _signInPanel.Controls.Add(signIn);
            _signInPanel.Controls.Add(forgot);

            _status = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = Theme.Small,
                ForeColor = Theme.Danger,
                Padding = new Padding(4, 8, 4, 0)
            };

            var spacer = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Cream };
            var spacer2 = new Panel { Dock = DockStyle.Top, Height = 12, BackColor = Theme.Cream };

            body.Controls.Add(_status);
            body.Controls.Add(_signInPanel);
            body.Controls.Add(spacer2);
            body.Controls.Add(_setupPanel);
            body.Controls.Add(spacer);
            body.Controls.Add(_folderCard);
            body.Controls.Add(_serverCard);

            Controls.Add(body);
            Controls.Add(header);

            Shown += (_, _) =>
            {
                // Local SQLite build: folder picker. Hosted build: always inventory.castrightcatch.com.
                if (!DataLink.UseInventoryServer)
                {
                    ShowLocalFolderUi();
                    RefreshMode();
                    return;
                }

                ShowServerUi();
                RefreshMode();
                ConnectToCompanyServer();
            };
        }

        /// <summary>Pick the shared data folder, create the databases if needed, then show sign-in or first-IT setup.</summary>
        private void ChooseFolder()
        {
            using var dialog = new FolderBrowserDialog
            {
                Description = "Select the shared folder for the inventory database (same folder on every computer)",
                UseDescriptionForTitle = true,
                ShowNewFolderButton = true
            };
            // User cancelled the folder picker.
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            // Path can disappear between OK and use (network share dropped).
            if (!Directory.Exists(dialog.SelectedPath))
                return;

            AppLock.SaveFolder(dialog.SelectedPath);
            DataFiles.EnsureFilesExistOrAsk();
            AppLock.LoadSharedSettings();
            Accounts.EnsureFile();
            RefreshMode();
        }

        /// <summary>Show the company hostname card; hide the folder picker.</summary>
        private void ShowServerUi()
        {
            // UseInventoryServer=false is local SQLite; never show the server card.
            if (!DataLink.UseInventoryServer)
                return;
            _folderCard.Visible = false;
            _serverCard.Visible = true;
        }

        /// <summary>Show the shared-folder picker (local SQLite builds only).</summary>
        private void ShowLocalFolderUi()
        {
            _serverCard.Visible = false;
            _folderCard.Visible = true;
        }

        /// <summary>Resolve the company DNS name and connect. No IP is typed.</summary>
        private void ConnectToCompanyServer()
        {
            ConnectServer(InventoryHost.DnsName, InventoryHost.Port);
        }

        /// <summary>Connect to the inventory server, prompting if the certificate fingerprint changed.</summary>
        private void ConnectServer(string host, int port)
        {
            // A blank host cannot resolve.
            if (string.IsNullOrWhiteSpace(host))
            {
                ShowError("The inventory server name is not set.");
                return;
            }

            _status.ForeColor = Theme.Muted;
            _status.Text = "Connecting to " + host + "…";
            Application.DoEvents();

            string? pin = AppState.ServerFingerprint;
            try
            {
                DataLink.Connect(host, port, string.IsNullOrWhiteSpace(pin) ? null : pin);
            }
            // Certificate changed or self-signed rejected: ask before trusting a new fingerprint (possible MITM).
            catch (Exception ex) when (
                ex.Message.Contains("fingerprint", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("rejected", StringComparison.OrdinalIgnoreCase) ||
                ex.Message.Contains("invalid according to the validation procedure", StringComparison.OrdinalIgnoreCase))
            {
                var retry = MessageBox.Show(
                    this,
                    "This server’s certificate is different from last time. Trust it and connect?",
                    "Inventory server",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                // User declined to trust a new certificate (possible MITM).
                if (retry != DialogResult.Yes)
                {
                    ShowError("Not connected.");
                    return;
                }

                try
                {
                    DataLink.Connect(host, port, fingerprint: null);
                }
                // Second connect still failed; stay on the sign-in screen.
                catch (Exception retryEx)
                {
                    ShowError(retryEx.Message);
                    return;
                }
            }
            // Host/port/TLS errors stay on this screen instead of crashing startup.
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }

            try
            {
                AppLock.SaveServer(host, port, DataLink.Fingerprint);
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }

            RefreshMode();
        }

        /// <summary>
        /// No connection → prompt for IP (or a local folder).
        /// Server with no IT user → message to bootstrap on the host PC.
        /// Otherwise → username/password sign-in.
        /// </summary>
        private void RefreshMode()
        {
            bool serverUi = _serverCard.Visible;
            bool ready = AppLock.HasFolder();
            _folder.Text = !string.IsNullOrWhiteSpace(AppState.InventoryFolder)
                ? AppState.InventoryFolder
                : "No folder selected — click Change";
            _folder.BackColor = Directory.Exists(AppState.InventoryFolder ?? "") ? Theme.Paper : Theme.DangerFill;
            _folder.ForeColor = Directory.Exists(AppState.InventoryFolder ?? "") ? Theme.Ink : Theme.Danger;

            // Server card is showing: connect first, then sign in over TLS (not local SQLite).
            if (serverUi)
            {
                _connect.Text = DataLink.IsRemote ? "Connected" : "Connect";
                // Still need a live TLS session before accounts can be listed.
                if (!DataLink.IsRemote)
                {
                    _setupPanel.Visible = false;
                    _signInPanel.Visible = false;
                    _status.ForeColor = Theme.Muted;
                    if (string.IsNullOrWhiteSpace(_status.Text) || _status.Text.StartsWith("Enter the", StringComparison.Ordinal))
                        _status.Text = "Connecting to " + InventoryHost.DnsName + "…";
                    AcceptButton = _connect;
                    return;
                }

                // First IT user is created on the server host, not from a client.
                if (!Accounts.HasItUser())
                {
                    _setupPanel.Visible = false;
                    _signInPanel.Visible = false;
                    _status.ForeColor = Theme.Muted;
                    _status.Text = "The first IT user must be created on the server PC (CrcInventoryServer --bootstrap).";
                    AcceptButton = null;
                    return;
                }

                _setupPanel.Visible = false;
                _signInPanel.Visible = true;
                _status.Text = "";
                AcceptButton = _signInPanel.Controls.OfType<Button>().FirstOrDefault();
                _staySignedIn.Visible = AppState.StaySignedInEnabled;
                _staySignedIn.Text =
                    $"Stay signed in on this PC  ({AppState.StaySignedInDays} days; close after {AppState.IdleCloseHours} hours idle)";
                _user.Focus();
                return;
            }

            // Local folder mode cannot list users until a path exists.
            if (!ready)
            {
                _setupPanel.Visible = false;
                _signInPanel.Visible = false;
                _status.ForeColor = Theme.Muted;
                _status.Text = "Choose the shared data folder first. Users and IT access live there.";
                AcceptButton = null;
                return;
            }

            Accounts.EnsureFile();
            bool first = !Accounts.HasItUser();
            _setupPanel.Visible = first;
            _signInPanel.Visible = !first;
            _status.Text = "";
            // Empty local accounts file: this PC bootstraps the first IT user.
            if (first)
            {
                AcceptButton = _setupPanel.Controls.OfType<Button>().FirstOrDefault();
                _setupUser.Focus();
            }
            // IT already exists: show username/password (forgot password is IT-only, no security questions).
            else
            {
                AcceptButton = _signInPanel.Controls.OfType<Button>().FirstOrDefault();
                _staySignedIn.Visible = AppState.StaySignedInEnabled;
                _staySignedIn.Text =
                    $"Stay signed in on this PC  ({AppState.StaySignedInDays} days; close after {AppState.IdleCloseHours} hours idle)";
                _user.Focus();
            }
        }

        /// <summary>Create the first IT user (always IT), then enter the app.</summary>
        private void CreateAdmin()
        {
            // Confirm field is a typed copy, not a second stored secret.
            if (_setupPassword.Text != _setupConfirm.Text)
            {
                ShowError("The passwords do not match.");
                return;
            }

            // Policy or duplicate-username failures stay on the setup card.
            if (!Accounts.CreateAdmin(_setupUser.Text, _setupPassword.Text, _setupUser.Text, out string error))
            {
                ShowError(error);
                return;
            }

            // Account exists but sign-in failed (rare: hash write lag or validation).
            if (!Accounts.TrySignIn(_setupUser.Text, _setupPassword.Text, out var account, out error) ||
                account == null)
            {
                ShowError(error.Length > 0 ? error : "The IT account was created. Sign in.");
                RefreshMode();
                return;
            }

            Accounts.Apply(account);
            FinishLogin(account);
        }

        /// <summary>
        /// Check username/password. May force a password change on first login.
        /// </summary>
        private void SignIn()
        {
            bool stay = _staySignedIn.Visible && _staySignedIn.Checked;
            // Wrong password, lock, or missing user must not open the workspace.
            if (!Accounts.TrySignIn(_user.Text, _password.Text, out var account, out string error, stay) ||
                account == null)
            {
                ShowError(error);
                return;
            }

            Accounts.Apply(account);
            // IT-issued temp passwords must be replaced before any workspace page opens.
            if (account.MustChangePassword)
            {
                using var change = new ChangePasswordForm(
                    account.Username,
                    requireCurrent: false,
                    knownCurrent: _password.Text);
                // Skipping the change leaves them signed out; reset is IT-only, no security questions.
                if (change.ShowDialog(this) != DialogResult.OK)
                {
                    AppState.SignOut();
                    ShowError("You must choose a new password before using the app.");
                    return;
                }

                account.MustChangePassword = false;
            }

            FinishLogin(account);
        }

        /// <summary>Enter the app. Stay signed in uses the shared Admin policy and this screen’s checkbox.</summary>
        private void FinishLogin(AppAccount account)
        {
            bool stay = AppState.StaySignedInEnabled &&
                        _signInPanel.Visible &&
                        _staySignedIn.Visible &&
                        _staySignedIn.Checked;
            account.StaySignedIn = stay;
            Accounts.Apply(account);
            // Remembered sessions follow Admin policy; otherwise drop any old cookie on this PC.
            if (stay)
                Accounts.RememberSignIn(account);
            // Unchecked Stay signed in must not leave a resume token on this PC.
            else
                Accounts.ForgetThisPc();
            // Remote clients load company settings only after they have a token.
            if (DataLink.IsRemote)
                AppLock.LoadSharedSettings();
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>Show a red status line under the sign-in card.</summary>
        private void ShowError(string message)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = message;
        }

        /// <summary>Caption plus text box added to a parent card.</summary>
        private static TextBox AddBox(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption, Location = new Point(x, y), AutoSize = true };
            Theme.StyleFieldLabel(label);
            var box = new TextBox
            {
                Location = new Point(x, y + 16),
                Size = new Size(width, 26)
            };
            Theme.StyleField(box);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }
    }
}
