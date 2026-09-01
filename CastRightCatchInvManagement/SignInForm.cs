namespace CastRightCatchInvManagement
{
    internal sealed class SignInForm : Form
    {
        private readonly TextBox _folder;
        private readonly Button _changeFolder;
        private readonly Label _status;
        private readonly Panel _setupPanel;
        private readonly Panel _signInPanel;
        private readonly TextBox _setupUser;
        private readonly TextBox _setupPassword;
        private readonly TextBox _setupConfirm;
        private readonly TextBox _user;
        private readonly TextBox _password;

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
            ClientSize = new Size(460, 640);
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

            var folderCard = new CardPanel { Dock = DockStyle.Top, Height = 92 };
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
            folderCard.Controls.Add(folderLabel);
            folderCard.Controls.Add(_folder);
            folderCard.Controls.Add(_changeFolder);

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

            _signInPanel = new CardPanel { Dock = DockStyle.Top, Height = 230, Visible = false };
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
            var signIn = new Button
            {
                Text = "Sign in",
                Size = new Size(120, 34),
                Location = new Point(20, 160)
            };
            Theme.StyleGoldButton(signIn);
            signIn.Click += (_, _) => SignIn();
            var forgot = new Button
            {
                Text = "Forgot password",
                Size = new Size(140, 34),
                Location = new Point(150, 160)
            };
            Theme.StyleOutlineButton(forgot);
            forgot.Click += (_, _) =>
            {
                using var form = new ForgotPasswordForm();
                form.ShowDialog(this);
            };
            _signInPanel.Controls.Add(signTitle);
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
            body.Controls.Add(folderCard);

            Controls.Add(body);
            Controls.Add(header);

            Shown += (_, _) => RefreshMode();
        }

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

        private void RefreshMode()
        {
            bool ready = AppLock.HasFolder();
            _folder.Text = ready
                ? AppState.InventoryFolder
                : "No folder selected — click Change";
            _folder.BackColor = ready ? Theme.Paper : Theme.DangerFill;
            _folder.ForeColor = ready ? Theme.Ink : Theme.Danger;

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
                _user.Focus();
            }
        }

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
            DialogResult = DialogResult.OK;
            Close();
        }

        private void SignIn()
        {
            if (!Accounts.TrySignIn(_user.Text, _password.Text, out var account, out string error) ||
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
            }
            else if (!EnsureSecurityQuestions(account.Username))
            {
                return;
            }

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
