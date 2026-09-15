namespace CastRightCatchInvManagement
{
    /// <summary>IT: add users, reset passwords, and email temporary logins.</summary>
    internal sealed class ItUsersForm : Form, INavigationPage
    {
        private DataGridView _grid = null!;

        /// <summary>Register this page and build the user grid (now redirected to Admin).</summary>
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

        /// <summary>Lay out add-user, IT/admins, and the user grid.</summary>
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
                // Header clicks are not a user to edit.
                if (e.RowIndex < 0)
                    return;
                EditUser(RowUser(e.RowIndex));
            };
            _grid.CellMouseClick += (_, e) =>
            {
                // Context menu is only for an existing user row.
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                string user = RowUser(e.RowIndex);
                var menu = new ContextMenuStrip();
                menu.Items.Add("Edit user", null, (_, _) => EditUser(user));
                // Table access is administrator-only.
                if (AppState.IsAdmin)
                    menu.Items.Add("Table access", null, (_, _) =>
                    {
                        // Reload so group/override changes show immediately.
                        if (UserAccessForm.ShowFor(FindForm(), user))
                            LoadUsers();
                    });
                // You change your own password; IT resets everyone else.
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

        /// <summary>Username in column 0 of this grid row.</summary>
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
            // Refresh after a successful add/edit so the grid matches the database.
            if (form.ShowDialog(FindForm()) == DialogResult.OK)
                LoadUsers();
        }

        /// <summary>Generate a temporary password, hash it, and email it if SMTP is set.</summary>
        private void ResetPassword(string username)
        {
            var confirm = MessageBox.Show(
                "Generate a new password for this user? We will email it if SMTP is set. You can also copy the details to send yourself. They will have to change it at next sign-in.",
                "Reset password",
                MessageBoxButtons.YesNo,
                MessageBoxIcon.Question);
            // Accidental reset would email a new password they did not ask for.
            if (confirm != DialogResult.Yes)
                return;

            string temp = Accounts.GenerateTemporaryPassword();
            // Policy or missing-user failures must not email a password we did not store.
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
            // Accidental delete would lock that person out.
            if (confirm != DialogResult.Yes)
                return;

            // Last IT/admin or self-delete is rejected with a message.
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

        /// <summary>Register this page and build the admin/IT lists (now redirected to Admin).</summary>
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

        /// <summary>Lay out administrator and IT role lists.</summary>
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

        /// <summary>One role list with Add/Remove for administrators or IT.</summary>
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

        /// <summary>Reload administrator and IT names from admins.json.</summary>
        private void LoadLists()
        {
            Fill(_admins, Accounts.ReadAdmins());
            Fill(_it, Accounts.ReadIt());
        }

        /// <summary>Replace list-box items with sorted usernames.</summary>
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
            // Nothing to pick means every account already has this role.
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
            // Cancel on the picker leaves roles unchanged.
            if (string.IsNullOrWhiteSpace(picked))
                return;

            // Keep admins.json and the access-group membership in sync.
            if (admin)
            {
                Accounts.AddAdmin(picked);
                SqliteInventory.AddAccessGroup(picked, AccessGroups.Admin);
            }
            else
            {
                Accounts.AddIt(picked);
                SqliteInventory.AddAccessGroup(picked, AccessGroups.IT);
            }

            // Live-refresh sidebar rights when granting a role to yourself.
            if (picked.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                AppState.IsAdmin = Accounts.IsAdmin(picked);
                AppState.IsIt = Accounts.IsIt(picked);
                TableAccess.Apply(picked);
                AppLock.NotifyChanged();
            }

            LoadLists();
        }

        /// <summary>Revoke administrator or IT from the selected name. Blocks removing the last of either role.</summary>
        private void RemoveRole(bool admin, ListBox box)
        {
            // Remove needs a selected name.
            if (box.SelectedItem is not string username)
                return;

            // Last remaining admin/IT is blocked inside RemoveAdmin/RemoveIt.
            if (!(admin ? Accounts.RemoveAdmin(username, out string error) : Accounts.RemoveIt(username, out error)))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            // Drop the matching group so table access follows the role change.
            if (admin)
                SqliteInventory.RemoveAccessGroup(username, AccessGroups.Admin);
            else
                SqliteInventory.RemoveAccessGroup(username, AccessGroups.IT);

            // Live-refresh if you removed a role from yourself.
            if (username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                // Re-read so a remaining role is not cleared by mistake.
                if (admin)
                    AppState.IsAdmin = Accounts.IsAdmin(username);
                else
                    AppState.IsIt = Accounts.IsIt(username);
                TableAccess.Apply(username);
                AppLock.NotifyChanged();
            }

            LoadLists();
        }

        /// <summary>Modal combo to pick a username, or null if they cancel.</summary>
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
            // Preselect the first name so Enter adds without extra clicks.
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
        private readonly CheckedListBox _groups;
        private readonly TextBox _password;
        private readonly TextBox _confirm;

        /// <summary>Add a user, or edit name/email/groups for an existing login.</summary>
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
            ClientSize = new Size(400, add ? 470 : 430);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            // Match other CRC dialogs when the brand icon is present.
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            int y = 20;
            _user = Field("USERNAME", 24, y, 350);
            y += 54;
            _name = Field("NAME", 24, y, 350);
            y += 54;
            _email = Field("EMAIL", 24, y, 350);
            y += 54;
            var groupLabel = new Label { Text = "GROUPS", Location = new Point(24, y), AutoSize = true };
            Theme.StyleFieldLabel(groupLabel);
            _groups = new CheckedListBox
            {
                Location = new Point(24, y + 16),
                Size = new Size(350, 96),
                CheckOnClick = true,
                BorderStyle = BorderStyle.FixedSingle,
                BackColor = Theme.Paper,
                ForeColor = Theme.Navy
            };
            foreach (var (name, _) in SqliteInventory.ListAccessGroups())
                _groups.Items.Add(name);
            Controls.Add(groupLabel);
            Controls.Add(_groups);
            _password = new TextBox { Visible = false };
            _confirm = new TextBox { Visible = false };

            var hint = new Label
            {
                Text = add
                    ? "A random password is created (capital, number, and symbol). We email it when SMTP is set. Check every group this user should have. Allowed permissions from any group win."
                    : "Save to update name, email, username, or groups. Allowed permissions from any group win over blocked ones. Use Reset password on the user list for a new temporary password.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, y + 120),
                Size = new Size(350, 52)
            };
            Controls.Add(hint);

            // Prefill when editing so IT changes only what they need.
            if (!add)
            {
                var account = Accounts.List().FirstOrDefault(a =>
                    a.Username.Equals(_username, StringComparison.OrdinalIgnoreCase));
                _user.Text = _username;
                // Stale usernames still allow a rename even if the list row vanished.
                if (account != null)
                {
                    _name.Text = account.DisplayName;
                    _email.Text = account.Email;
                }

                var assigned = SqliteInventory.GetAccessGroups(_username!);
                foreach (var group in assigned)
                {
                    int index = _groups.Items.IndexOf(group);
                    // Groups renamed since assignment still need to show as checked.
                    if (index < 0)
                        index = _groups.Items.Add(group);
                    _groups.SetItemChecked(index, true);
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
                // Stay open on validation errors so they can fix the form.
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
            // Block non-admins from assigning or removing Admin.
            if (!CanAssignSelectedGroup(_username))
                return false;

            string user = _user.Text.Trim();
            string error;
            // New user: create login, assign groups, then email the temp password.
            if (_username == null)
            {
                string email = _email.Text.Trim();
                // Optional email must still look like an address if they typed one.
                if (email.Length > 0 && !email.Contains('@'))
                {
                    MessageBox.Show("That email does not look right.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                string password = Accounts.GenerateTemporaryPassword();
                if (!Accounts.CreateUser(user, password, _name.Text, email, out error))
                {
                    MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }

                // Do not email a login if group assignment failed after insert.
                if (!ApplySelectedGroup(user))
                    return false;
                SendLoginEmail(this, email, user, password);
                return true;
            }

            if (!Accounts.RenameUser(_username, user, out error))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            SqliteInventory.UpdateAccount(user, _name.Text.Trim(), _email.Text.Trim());
            if (!ApplySelectedGroup(user))
                return false;
            // Keep the signed-in session matching a self-edit or rename.
            if (user.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase) ||
                _username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                AppState.CurrentUsername = user;
                AppState.CurrentDisplayName = _name.Text.Trim();
                AppState.UserEmail = _email.Text.Trim();
                TableAccess.Apply(user);
                AppLock.NotifyChanged();
            }

            return true;
        }

        /// <summary>Checked group names, skipping blank items.</summary>
        private List<string> SelectedGroups()
        {
            var selected = new List<string>();
            foreach (var item in _groups.CheckedItems)
            {
                string name = item?.ToString()?.Trim() ?? "";
                // Ignore empty checked items from a stale list box.
                if (name.Length > 0)
                    selected.Add(name);
            }

            return selected;
        }

        /// <summary>False when a non-admin tries to add or remove the Admin group.</summary>
        private bool CanAssignSelectedGroup(string? username)
        {
            var selected = SelectedGroups();
            var previous = string.IsNullOrWhiteSpace(username)
                ? new List<string>()
                : SqliteInventory.GetAccessGroups(username);
            bool addingAdmin = selected.Any(AccessGroups.IsAdmin) && !previous.Any(AccessGroups.IsAdmin);
            bool removingAdmin = previous.Any(AccessGroups.IsAdmin) && !selected.Any(AccessGroups.IsAdmin);
            // Only administrators may change Admin membership.
            if ((addingAdmin || removingAdmin) && !AppState.IsAdmin)
            {
                MessageBox.Show(
                    addingAdmin
                        ? "Only an administrator can assign the Admin group."
                        : "Only an administrator can move someone out of the Admin group.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        /// <summary>Write checked groups and keep admin/IT role lists in sync.</summary>
        private bool ApplySelectedGroup(string username)
        {
            if (!CanAssignSelectedGroup(username))
                return false;
            var selected = SelectedGroups();
            SqliteInventory.SetAccessGroups(username, selected);
            // Admin group also grants administrator in admins.json.
            if (selected.Any(AccessGroups.IsAdmin))
                Accounts.AddAdmin(username);
            // IT group also grants IT in admins.json.
            if (selected.Any(AccessGroups.IsIt))
                Accounts.AddIt(username);
            return true;
        }

        internal static void SendLoginEmail(IWin32Window? owner, string email, string username, string password)
        {
            bool sent;
            string error;
            // No mailbox: skip the spinner and go straight to the copy dialog.
            if (string.IsNullOrWhiteSpace(email) || !email.Contains('@'))
            {
                sent = Mailer.TrySendNewUserDetails(email, username, password, out error);
            }
            else
            {
                (sent, error) = WaitForm.Run(owner, "Sending login email…", () =>
                {
                    bool ok = Mailer.TrySendNewUserDetails(email, username, password, out string err);
                    return (ok, err);
                });
            }

            // Success toast when we have a control host; otherwise a simple message box.
            if (sent)
            {
                if (owner is Control host)
                    ToastAlert.Success(host, "Login email sent to " + email + ".");
                else
                    MessageBox.Show(
                        owner,
                        "Login email sent to " + email + ".",
                        "Users",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Information);
                return;
            }

            using var form = new LoginShareForm(email, username, password, sent, error);
            form.ShowDialog(owner);
        }

        /// <summary>Caption plus styled text box on this dialog.</summary>
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

    /// <summary>Shows the new login so IT can copy it when email is not available.</summary>
    internal sealed class LoginShareForm : Form
    {
        /// <summary>Show the temporary login so IT can copy it when email is missing or failed.</summary>
        public LoginShareForm(
            string email,
            string username,
            string password,
            bool emailed,
            string error)
        {
            Text = "User login";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            MinimumSize = new Size(520, 460);
            ClientSize = new Size(560, 500);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            // Match other CRC dialogs when the brand icon is present.
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            string body = Mailer.NewUserBody(username, password);
            string status;
            // Tell IT whether they still need to copy the password by hand.
            if (emailed)
                status = "A login email was sent to " + email + ". You can also copy the details below.";
            else if (string.IsNullOrWhiteSpace(email))
                status = "No email was sent. Copy the details below and send them yourself.";
            else
                status = "The email did not send" +
                         (string.IsNullOrWhiteSpace(error) ? "" : ":\r\n\r\n" + error.Trim()) +
                         "\r\n\r\nCopy the details below and send them yourself.";

            var heading = new Label
            {
                Text = "User saved",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(20, 12, 20, 0)
            };
            var note = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                WordWrap = true,
                Text = status,
                Dock = DockStyle.Top,
                Height = 110,
                Font = Theme.Small,
                ForeColor = emailed ? Theme.Success : Theme.Ink,
                BorderStyle = BorderStyle.None,
                BackColor = Theme.Cream,
                Margin = new Padding(0),
                TabStop = false
            };
            var notePad = new Panel
            {
                Dock = DockStyle.Top,
                Height = 118,
                Padding = new Padding(20, 4, 20, 8),
                BackColor = Theme.Cream
            };
            notePad.Controls.Add(note);

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Theme.Cream
            };
            var copy = new Button
            {
                Text = "Copy text",
                Size = new Size(120, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Left
            };
            Theme.StyleGoldButton(copy);
            copy.Click += (_, _) =>
            {
                try
                {
                    Clipboard.SetText(body);
                    copy.Text = "Copied";
                }
                catch
                {
                    // Clipboard can be locked by another app; offer manual copy.
                    MessageBox.Show(
                        this,
                        "Could not copy. Select the text and press Ctrl+C.",
                        Text,
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Warning);
                }
            };
            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Size = new Size(100, 34),
                Anchor = AnchorStyles.Bottom | AnchorStyles.Right
            };
            Theme.StyleNavyButton(close);
            footer.Controls.Add(copy);
            footer.Controls.Add(close);
            footer.Resize += (_, _) =>
            {
                copy.Location = new Point(20, 10);
                close.Location = new Point(Math.Max(150, footer.Width - 120), 10);
            };
            copy.Location = new Point(20, 10);
            close.Location = new Point(440, 10);
            AcceptButton = close;
            CancelButton = close;

            var box = new TextBox
            {
                Dock = DockStyle.Fill,
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Text = body,
                WordWrap = true
            };
            Theme.StyleField(box);
            box.BackColor = Theme.Paper;
            var boxPad = new Panel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 0, 20, 8),
                BackColor = Theme.Cream
            };
            boxPad.Controls.Add(box);

            Controls.Add(boxPad);
            Controls.Add(notePad);
            Controls.Add(heading);
            Controls.Add(footer);
        }
    }
}
