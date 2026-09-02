namespace CastRightCatchInvManagement
{
    /// <summary>IT: add users, reset passwords, and email temporary logins.</summary>
    internal sealed class ItUsersForm : Form, INavigationPage
    {
        private DataGridView _grid = null!;

        public ItUsersForm()
        {
            Navigator.Register(AppPage.ItUsers, this);
            BuildUi();
        }

        /// <summary>User management now lives on Admin. This page just opens that tab.</summary>
        public void HighlightCurrentPage()
        {
            Navigator.GoTo(AppPage.Admin);
        }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = Theme.Cream
            };
            var add = new Button { Text = "Add user", Size = new Size(120, 34), Location = new Point(0, 8) };
            Theme.StyleGoldButton(add);
            add.Click += (_, _) => EditUser(null);
            var access = new Button { Text = "IT && admins", Size = new Size(130, 34) };
            Theme.StyleNavyButton(access);
            access.Click += (_, _) => Navigator.GoTo(AppPage.ItAccess);
            footer.Controls.Add(add);
            footer.Controls.Add(access);
            footer.Resize += (_, _) => access.Location = new Point(Math.Max(140, footer.Width - 150), 8);

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "IT can add users and reset passwords. New users get an email with their login and must change the password at first sign-in."
            };

            _grid = new DataGridView
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AllowUserToDeleteRows = false,
                MultiSelect = false,
                SelectionMode = DataGridViewSelectionMode.FullRowSelect
            };
            Theme.StyleGrid(_grid);
            _grid.AllowUserToOrderColumns = false;
            _grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex < 0)
                    return;
                EditUser(RowUser(e.RowIndex));
            };
            _grid.CellMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                string user = RowUser(e.RowIndex);
                var menu = new ContextMenuStrip();
                menu.Items.Add("Edit user", null, (_, _) => EditUser(user));
                if (AppState.IsAdmin)
                    menu.Items.Add("Table access", null, (_, _) =>
                    {
                        if (UserAccessForm.ShowFor(FindForm(), user))
                            LoadUsers();
                    });
                if (user.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                    menu.Items.Add("Change my password", null, (_, _) =>
                    {
                        using var change = new ChangePasswordForm(user, requireCurrent: true);
                        change.ShowDialog(FindForm());
                    });
                else
                    menu.Items.Add("Reset password", null, (_, _) => ResetPassword(user));
                menu.Items.Add("Delete user", null, (_, _) => DeleteUser(user));
                menu.Show(_grid, _grid.PointToClient(Control.MousePosition));
            };

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            card.Controls.Add(_grid);

            Controls.Add(card);
            Controls.Add(intro);
            Controls.Add(footer);
            LoadUsers();
        }

        private string RowUser(int row)
        {
            return _grid.Rows[row].Cells[0].Value?.ToString()?.Trim() ?? "";
        }

        /// <summary>Fill the grid with usernames, names, emails, and admin/IT flags.</summary>
        private void LoadUsers()
        {
            _grid.Columns.Clear();
            _grid.Columns.Add("Username", "Username");
            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Email", "Email");
            _grid.Columns.Add("Admin", "Admin");
            _grid.Columns.Add("IT", "IT");
            foreach (var account in Accounts.List())
            {
                _grid.Rows.Add(
                    account.Username,
                    account.DisplayName,
                    account.Email,
                    account.IsAdmin ? "Yes" : "",
                    account.IsIt ? "Yes" : "");
            }
        }

        /// <summary>Open the add/edit user dialog. Null username means add a new person.</summary>
        private void EditUser(string? username)
        {
            using var form = new ItUserEditForm(username);
            if (form.ShowDialog(FindForm()) == DialogResult.OK)
                LoadUsers();
        }

        /// <summary>Generate a 6-character temporary password, hash it, and email it if SMTP is set.</summary>
        private void ResetPassword(string username)
        {
            var confirm = MessageBox.Show(
                "Generate a new 6-character password and email it to this user? They will have to change it at next sign-in.",
                "Reset password",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            if (confirm != DialogResult.Yes)
                return;

            string temp = Accounts.GenerateTemporaryPassword();
            if (!Accounts.SetPassword(username, temp, out string error, mustChange: true))
            {
                MessageBox.Show(error, "Reset password", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            string email = Accounts.List().FirstOrDefault(a =>
                a.Username.Equals(username, StringComparison.OrdinalIgnoreCase))?.Email ?? "";
            ItUserEditForm.SendLoginEmail(FindForm(), email, username, temp);
            LoadUsers();
        }

        /// <summary>Remove a user after confirm. Cannot delete yourself, the last IT, or the last admin.</summary>
        private void DeleteUser(string username)
        {
            var confirm = MessageBox.Show(
                "Delete user \"" + username + "\"? They will no longer be able to sign in.",
                "Delete user",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Warning);
            if (confirm != DialogResult.Yes)
                return;

            if (!Accounts.DeleteUser(username, out string error))
            {
                MessageBox.Show(error, "Delete user", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            LoadUsers();
        }
    }

    /// <summary>IT: who is an administrator or IT user. Writes admins.json and the database.</summary>
    internal sealed class ItAccessForm : Form, INavigationPage
    {
        private ListBox _admins = null!;
        private ListBox _it = null!;

        public ItAccessForm()
        {
            Navigator.Register(AppPage.ItAccess, this);
            BuildUi();
        }

        /// <summary>Role lists now live on Admin. This page just opens that tab.</summary>
        public void HighlightCurrentPage()
        {
            Navigator.GoTo(AppPage.Admin);
        }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = Theme.Cream
            };
            var users = new Button { Text = "Users", Size = new Size(110, 34), Location = new Point(0, 8) };
            Theme.StyleNavyButton(users);
            users.Click += (_, _) => Navigator.GoTo(AppPage.ItUsers);
            footer.Controls.Add(users);

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 44,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "Administrators can change company settings. IT can manage users. Names are stored in admins.json in the shared folder."
            };

            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            split.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50));
            split.Controls.Add(RoleCard("Administrators", true, out _admins), 0, 0);
            split.Controls.Add(RoleCard("IT", false, out _it), 1, 0);

            Controls.Add(split);
            Controls.Add(intro);
            Controls.Add(footer);
            LoadLists();
        }

        private CardPanel RoleCard(string title, bool admin, out ListBox list)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(0, 0, admin ? 8 : 0, 0) };
            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 14)
            };
            list = new ListBox
            {
                Location = new Point(20, 48),
                Size = new Size(280, 280),
                Font = Theme.Body,
                BorderStyle = BorderStyle.FixedSingle
            };
            var add = new Button { Text = "Add", Size = new Size(80, 32) };
            var remove = new Button { Text = "Remove", Size = new Size(90, 32) };
            Theme.StyleGoldButton(add);
            Theme.StyleOutlineButton(remove);
            bool isAdmin = admin;
            var box = list;
            add.Click += (_, _) => AddRole(isAdmin);
            remove.Click += (_, _) => RemoveRole(isAdmin, box);
            card.Controls.Add(heading);
            card.Controls.Add(list);
            card.Controls.Add(add);
            card.Controls.Add(remove);
            card.Resize += (_, _) =>
            {
                box.Width = Math.Max(160, card.Width - 44);
                box.Height = Math.Max(80, card.Height - 120);
                add.Location = new Point(20, card.Height - 52);
                remove.Location = new Point(110, card.Height - 52);
            };
            return card;
        }

        private void LoadLists()
        {
            Fill(_admins, Accounts.ReadAdmins());
            Fill(_it, Accounts.ReadIt());
        }

        private static void Fill(ListBox box, List<string> names)
        {
            box.Items.Clear();
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                box.Items.Add(name);
        }

        /// <summary>Grant administrator (<paramref name="admin"/> true) or IT (false) to a picked user.</summary>
        private void AddRole(bool admin)
        {
            var accounts = Accounts.List();
            var existing = new HashSet<string>(
                admin ? Accounts.ReadAdmins() : Accounts.ReadIt(),
                StringComparer.OrdinalIgnoreCase);
            var choices = accounts
                .Select(a => a.Username)
                .Where(name => !existing.Contains(name))
                .OrderBy(name => name, StringComparer.OrdinalIgnoreCase)
                .ToArray();
            if (choices.Length == 0)
            {
                MessageBox.Show(
                    "Every user already has this access. Add a user first.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string? picked = PickUser(choices, admin ? "Add administrator" : "Add IT user");
            if (string.IsNullOrWhiteSpace(picked))
                return;

            if (admin)
                Accounts.AddAdmin(picked);
            else
                Accounts.AddIt(picked);
            LoadLists();
        }

        /// <summary>Revoke administrator or IT from the selected name. Blocks removing the last of either role.</summary>
        private void RemoveRole(bool admin, ListBox box)
        {
            if (box.SelectedItem is not string username)
                return;

            if (!(admin ? Accounts.RemoveAdmin(username, out string error) : Accounts.RemoveIt(username, out error)))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                if (admin)
                    AppState.IsAdmin = Accounts.IsAdmin(username);
                else
                    AppState.IsIt = Accounts.IsIt(username);
                AppLock.NotifyChanged();
            }

            LoadLists();
        }

        private string? PickUser(string[] choices, string title)
        {
            using var form = new Form
            {
                Text = title,
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(360, 140),
                BackColor = Theme.Cream,
                Font = Theme.Body
            };
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(24, 28),
                Width = 312
            };
            Theme.StyleCombo(box);
            box.Items.AddRange(choices);
            if (box.Items.Count > 0)
                box.SelectedIndex = 0;
            var ok = new Button
            {
                Text = "Add",
                DialogResult = DialogResult.OK,
                Size = new Size(90, 32),
                Location = new Point(150, 84)
            };
            Theme.StyleGoldButton(ok);
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 32),
                Location = new Point(246, 84)
            };
            Theme.StyleOutlineButton(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            form.Controls.Add(box);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            return form.ShowDialog(this) == DialogResult.OK ? box.SelectedItem as string : null;
        }
    }

    /// <summary>IT self-edit: username, name, email, and password.</summary>
    internal sealed class ItUserEditForm : Form
    {
        private readonly string? _username;
        private readonly bool _passwordOnly;
        private readonly TextBox _user;
        private readonly TextBox _name;
        private readonly TextBox _email;
        private readonly TextBox _password;
        private readonly TextBox _confirm;

        public ItUserEditForm(string? username, bool passwordOnly = false)
        {
            _username = string.IsNullOrWhiteSpace(username) ? null : username.Trim();
            _passwordOnly = passwordOnly;
            bool add = _username == null;
            Text = add ? "Add user" : "Edit user";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(400, add ? 300 : 280);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            int y = 20;
            _user = Field("USERNAME", 24, y, 350);
            y += 54;
            _name = Field("NAME", 24, y, 350);
            y += 54;
            _email = Field("EMAIL", 24, y, 350);
            _password = new TextBox { Visible = false };
            _confirm = new TextBox { Visible = false };

            var hint = new Label
            {
                Text = add
                    ? "A random 6-character password will be emailed. They must change it at first sign-in."
                    : "Save to update name, email, or username. Use Reset password on the user list for a new temporary password.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, y + 54),
                Size = new Size(350, 48)
            };
            Controls.Add(hint);

            if (!add)
            {
                var account = Accounts.List().FirstOrDefault(a =>
                    a.Username.Equals(_username, StringComparison.OrdinalIgnoreCase));
                _user.Text = _username;
                if (account != null)
                {
                    _name.Text = account.DisplayName;
                    _email.Text = account.Email;
                }
            }

            var save = new Button
            {
                Text = add ? "Add" : "Save",
                Size = new Size(110, 34),
                Location = new Point(170, ClientSize.Height - 52)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                if (Save())
                    DialogResult = DialogResult.OK;
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 34),
                Location = new Point(288, ClientSize.Height - 52)
            };
            Theme.StyleOutlineButton(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(save);
            Controls.Add(cancel);
        }

        /// <summary>Create or update this user. New users get a temp password emailed when possible.</summary>
        private bool Save()
        {
            string user = _user.Text.Trim();
            string error;
            if (_username == null)
            {
                if (_email.Text.Trim().Length == 0 || !_email.Text.Contains('@'))
                {
                    MessageBox.Show("Enter an email so we can send their login.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                string password = Accounts.GenerateTemporaryPassword();
                if (!Accounts.CreateUser(user, password, _name.Text, _email.Text, out error))
                {
                    MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                SendLoginEmail(this, _email.Text.Trim(), user, password);
                return true;
            }

            if (!Accounts.RenameUser(_username, user, out error))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            SqliteInventory.UpdateAccount(user, _name.Text.Trim(), _email.Text.Trim());
            if (user.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase) ||
                _username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                AppState.CurrentUsername = user;
                AppState.CurrentDisplayName = _name.Text.Trim();
                AppState.UserEmail = _email.Text.Trim();
                AppLock.NotifyChanged();
            }

            return true;
        }

        internal static void SendLoginEmail(IWin32Window? owner, string email, string username, string password)
        {
            if (Mailer.TrySendNewUserDetails(email, username, password, out string error))
            {
                MessageBox.Show(
                    owner,
                    "A login email was sent to " + email + ".",
                    "Users",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            MessageBox.Show(
                owner,
                "The user was saved, but the email did not send.\n\n" +
                error + "\n\nGive them these details:\nUsername: " + username +
                "\nTemporary password: " + password,
                "Users",
                MessageBoxButtons.OK,
                MessageBoxIcon.Warning);
        }

        private TextBox Field(string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption, Location = new Point(x, y), AutoSize = true };
            Theme.StyleFieldLabel(label);
            var box = new TextBox
            {
                Location = new Point(x, y + 16),
                Size = new Size(width, 26)
            };
            Theme.StyleField(box);
            Controls.Add(label);
            Controls.Add(box);
            return box;
        }
    }
}
