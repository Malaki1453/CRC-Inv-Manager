namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Sign-in, server IP connect, first-IT create (local folder only), and forgot-password.
    /// </summary>
    internal sealed class SignInForm : Form
    {
        private readonly TextBox _folder;
        private readonly Button _changeFolder;
        private readonly TextBox _host;
        private readonly TextBox _port;
        private readonly Button _connect;
        private readonly CardPanel _serverCard;
        private readonly CardPanel _folderCard;
        private readonly LinkLabel _switchMode;
        private readonly Label _status;
        private readonly Panel _setupPanel;
        private readonly Panel _signInPanel;
        private readonly TextBox _setupUser;
        private readonly TextBox _setupPassword;
        private readonly TextBox _setupConfirm;
        private readonly TextBox _user;
        private readonly TextBox _password;
        private readonly CheckBox _staySignedIn;

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
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

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
            var serverLabel = new Label { Text = "SERVER IP" };
            Theme.StyleFieldLabel(serverLabel);
            serverLabel.Location = new Point(20, 14);
            _host = new TextBox { Location = new Point(20, 34), Size = new Size(250, 26) };
            Theme.StyleField(_host);
            var portLabel = new Label { Text = "PORT" };
            Theme.StyleFieldLabel(portLabel);
            portLabel.Location = new Point(280, 14);
            _port = new TextBox { Location = new Point(280, 34), Size = new Size(70, 26), Text = DataLink.DefaultPort.ToString() };
            Theme.StyleField(_port);
            _connect = new Button
            {
                Text = "Connect",
                Size = new Size(90, 28),
                Location = new Point(308, 72)
            };
            Theme.StyleNavyButton(_connect);
            _connect.Click += (_, _) => ConnectServer();
            _serverCard.Controls.Add(serverLabel);
            _serverCard.Controls.Add(_host);
            _serverCard.Controls.Add(portLabel);
            _serverCard.Controls.Add(_port);
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

            _switchMode = new LinkLabel
            {
                Text = "Use a local folder instead",
                Font = Theme.Small,
                LinkColor = Theme.Navy,
                ActiveLinkColor = Theme.Gold,
                Location = new Point(20, 78),
                AutoSize = true
            };
            _switchMode.Click += (_, _) => ToggleLocalFolder();
            _switchMode.Visible = DataLink.UseInventoryServer;
            _serverCard.Controls.Add(_switchMode);

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
                using var form = new ForgotPasswordForm();
                form.ShowDialog(this);
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
                _host.Text = AppState.ServerHost;
                _port.Text = AppState.ServerPort > 0 ? AppState.ServerPort.ToString() : DataLink.DefaultPort.ToString();
                if (!DataLink.UseInventoryServer ||
                    (!AppState.UseServer &&
                     !string.IsNullOrWhiteSpace(AppState.InventoryFolder) &&
                     Directory.Exists(AppState.InventoryFolder) &&
                     !DataLink.IsRemote))
                {
                    ShowLocalFolderUi();
                }

                RefreshMode();
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
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;
            if (!Directory.Exists(dialog.SelectedPath))
                return;

            AppLock.SaveFolder(dialog.SelectedPath);
            DataFiles.EnsureFilesExistOrAsk();
            AppLock.LoadSharedSettings();
            Accounts.EnsureFile();
            RefreshMode();
        }

        private void ToggleLocalFolder()
        {
            if (_folderCard.Visible)
            {
                ShowServerUi();
                RefreshMode();
                return;
            }

            DataLink.Disconnect();
            AppState.UseServer = false;
            ShowLocalFolderUi();
            RefreshMode();
        }

        private void ShowServerUi()
        {
            if (!DataLink.UseInventoryServer)
                return;
            _folderCard.Visible = false;
            _serverCard.Visible = true;
            _switchMode.Text = "Use a local folder instead";
            _switchMode.Location = new Point(20, 78);
            _switchMode.Parent = _serverCard;
        }

        private void ShowLocalFolderUi()
        {
            _serverCard.Visible = false;
            _folderCard.Visible = true;
            _switchMode.Text = "Connect to the inventory server";
            _switchMode.Location = new Point(20, 66);
            _switchMode.Parent = _folderCard;
            _switchMode.Visible = DataLink.UseInventoryServer;
        }

        private void ConnectServer()
        {
            DataLink.ParseEndpoint(_host.Text, out string host, out int parsedFromHost);
            if (host.Length == 0)
            {
                ShowError("Enter the server IP address.");
                return;
            }

            int port = parsedFromHost;
            if (int.TryParse(_port.Text.Trim(), out int typed) && typed > 0 && typed <= 65535)
                port = typed;

            string? pin = AppState.ServerFingerprint;
            try
            {
                DataLink.Connect(host, port, string.IsNullOrWhiteSpace(pin) ? null : pin);
            }
            catch (Exception ex) when (ex.Message.Contains("fingerprint", StringComparison.OrdinalIgnoreCase))
            {
                var retry = MessageBox.Show(
                    this,
                    "This server’s certificate is different from last time. Trust it and connect?",
                    "Inventory server",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning);
                if (retry != DialogResult.Yes)
                {
                    ShowError("Not connected.");
                    return;
                }

                try
                {
                    DataLink.Connect(host, port, fingerprint: null);
                }
                catch (Exception retryEx)
                {
                    ShowError(retryEx.Message);
                    return;
                }
            }
            catch (Exception ex)
            {
                ShowError(ex.Message);
                return;
            }

            AppLock.SaveServer(host, port, DataLink.Fingerprint);
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

            if (serverUi)
            {
                _connect.Text = DataLink.IsRemote ? "Connected" : "Connect";
                if (!DataLink.IsRemote)
                {
                    _setupPanel.Visible = false;
                    _signInPanel.Visible = false;
                    _status.ForeColor = Theme.Muted;
                    _status.Text = "Enter the inventory server IP and connect. Later this can be filled in for you.";
                    AcceptButton = _connect;
                    _host.Focus();
                    return;
                }

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
            if (first)
            {
                AcceptButton = _setupPanel.Controls.OfType<Button>().FirstOrDefault();
                _setupUser.Focus();
            }
            else
            {
                AcceptButton = _signInPanel.Controls.OfType<Button>().FirstOrDefault();
                _staySignedIn.Visible = AppState.StaySignedInEnabled;
                _staySignedIn.Text =
                    $"Stay signed in on this PC  ({AppState.StaySignedInDays} days; close after {AppState.IdleCloseHours} hours idle)";
                _user.Focus();
            }
        }

        /// <summary>Create the first IT user (always IT), require security questions, then enter the app.</summary>
        private void CreateAdmin()
        {
            if (_setupPassword.Text != _setupConfirm.Text)
            {
                ShowError("The passwords do not match.");
                return;
            }

            if (!Accounts.CreateAdmin(_setupUser.Text, _setupPassword.Text, _setupUser.Text, out string error))
            {
                ShowError(error);
                return;
            }

            if (!Accounts.TrySignIn(_setupUser.Text, _setupPassword.Text, out var account, out error) ||
                account == null)
            {
                ShowError(error.Length > 0 ? error : "The IT account was created. Sign in.");
                RefreshMode();
                return;
            }

            Accounts.Apply(account);
            if (!EnsureSecurityQuestions(account.Username))
                return;
            FinishLogin(account);
        }

        /// <summary>
        /// Check username/password. May force a password change and security questions on first login.
        /// </summary>
        private void SignIn()
        {
            bool stay = _staySignedIn.Visible && _staySignedIn.Checked;
            if (!Accounts.TrySignIn(_user.Text, _password.Text, out var account, out string error, stay) ||
                account == null)
            {
                ShowError(error);
                return;
            }

            Accounts.Apply(account);
            if (account.MustChangePassword)
            {
                using var change = new ChangePasswordForm(account.Username, requireCurrent: false);
                if (change.ShowDialog(this) != DialogResult.OK)
                {
                    AppState.SignOut();
                    ShowError("You must choose a new password before using the app.");
                    return;
                }

                account.MustChangePassword = false;
            }

            if (!EnsureSecurityQuestions(account.Username))
                return;

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
            if (stay)
                Accounts.RememberSignIn(account);
            else
                Accounts.ForgetThisPc();
            if (DataLink.IsRemote)
                AppLock.LoadSharedSettings();
            DialogResult = DialogResult.OK;
            Close();
        }

        private bool EnsureSecurityQuestions(string username)
        {
            if (Accounts.HasSecurityQuestions(username))
                return true;

            using var form = new SecurityQuestionsForm(username);
            if (form.ShowDialog(this) == DialogResult.OK)
                return true;

            AppState.SignOut();
            ShowError("Set security questions before using the app.");
            return false;
        }

        private void ShowError(string message)
        {
            _status.ForeColor = Theme.Danger;
            _status.Text = message;
        }

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
