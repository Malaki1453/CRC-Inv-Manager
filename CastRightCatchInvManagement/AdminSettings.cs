namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Admin page: User management (IT and admins) and Admin management (admins only).
    /// </summary>
    internal sealed class AdminSettings : Form, INavigationPage
    {
        private TabControl _tabs = null!;
        private TabPage? _adminTab;
        private DataGridView _grid = null!;
        private ListBox _admins = null!;
        private ListBox _it = null!;
        private CrcToggleSwitch _stayToggle = null!;
        private ComboBox _sessionDays = null!;
        private ComboBox _idleHours = null!;
        private CardPanel _bankCard = null!;
        private Panel _keysPanel = null!;
        private Button _keysToggle = null!;
        private TextBox _plaidId = null!;
        private TextBox _plaidSecret = null!;
        private ComboBox _plaidEnv = null!;
        private ComboBox _plaidSync = null!;

        public AdminSettings()
        {
            Navigator.Register(AppPage.Admin, this);
            BuildUi();
        }

        /// <summary>IT and admins. Reloads users, roles, and admin-only settings.</summary>
        public void HighlightCurrentPage()
        {
            if (!AppState.IsAdmin && !AppState.IsIt)
            {
                Navigator.GoTo(AppPage.Settings);
                return;
            }

            ApplyTabs();
            LoadUsers();
            LoadRoles();
            if (AppState.IsAdmin)
            {
                LoadBankFeed();
                LoadSession();
            }
        }

        private void BuildUi()
        {
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);

            _tabs = new TabControl
            {
                Dock = DockStyle.Fill,
                Font = Theme.BodyBold,
                Padding = new Point(16, 6),
                SizeMode = TabSizeMode.Fixed,
                ItemSize = new Size(160, 32),
                DrawMode = TabDrawMode.OwnerDrawFixed
            };
            _tabs.DrawItem += PaintAdminTab;

            var usersPage = new TabPage("User management")
            {
                BackColor = Theme.Cream,
                Padding = new Padding(0, 8, 0, 0)
            };
            usersPage.Controls.Add(BuildUsersTab());
            _tabs.TabPages.Add(usersPage);

            _adminTab = new TabPage("Admin management")
            {
                BackColor = Theme.Cream,
                Padding = new Padding(0, 8, 0, 0)
            };
            _adminTab.Controls.Add(BuildAdminTab());
            _tabs.TabPages.Add(_adminTab);

            Controls.Add(_tabs);
            ApplyTabs();
            LoadUsers();
            LoadRoles();
            LoadBankFeed();
            LoadSession();
        }

        private void ApplyTabs()
        {
            if (_adminTab == null)
                return;
            bool showAdmin = AppState.IsAdmin;
            if (showAdmin && !_tabs.TabPages.Contains(_adminTab))
                _tabs.TabPages.Add(_adminTab);
            if (!showAdmin && _tabs.TabPages.Contains(_adminTab))
            {
                _tabs.SelectedIndex = 0;
                _tabs.TabPages.Remove(_adminTab);
            }
        }

        private Control BuildUsersTab()
        {
            var host = new Panel { Dock = DockStyle.Fill, BackColor = Theme.Cream };

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 48,
                BackColor = Theme.Cream
            };
            var add = new Button { Text = "Add user", Size = new Size(120, 34), Location = new Point(0, 8) };
            Theme.StyleGoldButton(add);
            add.Click += (_, _) => EditUser(null);
            footer.Controls.Add(add);

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "Add users, reset passwords, and assign IT or administrator. Table access is on the right-click menu."
            };

            var split = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 1,
                RowCount = 2,
                Padding = new Padding(0, 8, 0, 0)
            };
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 58f));
            split.RowStyles.Add(new RowStyle(SizeType.Percent, 42f));

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
                if (e.RowIndex >= 0)
                    EditUser(RowUser(e.RowIndex));
            };
            _grid.CellMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;
                ShowUserMenu(e.RowIndex);
            };
            var usersCard = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1), Margin = new Padding(0, 0, 0, 8) };
            usersCard.Controls.Add(_grid);

            var roles = new TableLayoutPanel
            {
                Dock = DockStyle.Fill,
                ColumnCount = 2,
                RowCount = 1
            };
            roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            roles.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, 50f));
            roles.Controls.Add(RoleCard("Administrators", true, out _admins), 0, 0);
            roles.Controls.Add(RoleCard("IT", false, out _it), 1, 0);

            split.Controls.Add(usersCard, 0, 0);
            split.Controls.Add(roles, 0, 1);

            host.Controls.Add(split);
            host.Controls.Add(intro);
            host.Controls.Add(footer);
            return host;
        }

        private Control BuildAdminTab()
        {
            var host = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream
            };
            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Text = "Stay signed in and the live bank feed are administrator-only. They apply to everyone."
            };
            var session = new CardPanel { Dock = DockStyle.Top, Height = 168 };
            LayoutSessionCard(session);
            _bankCard = new CardPanel { Dock = DockStyle.Top, Height = 210 };
            LayoutBankCard(_bankCard);
            var spacer = new Panel { Dock = DockStyle.Top, Height = 16, BackColor = Theme.Cream };
            host.Controls.Add(_bankCard);
            host.Controls.Add(spacer);
            host.Controls.Add(session);
            host.Controls.Add(intro);
            return host;
        }

        private static void PaintAdminTab(object? sender, DrawItemEventArgs e)
        {
            if (sender is not TabControl tabs || e.Index < 0 || e.Index >= tabs.TabCount)
                return;

            bool selected = (e.State & DrawItemState.Selected) == DrawItemState.Selected;
            using var fill = new SolidBrush(selected ? Theme.Paper : Theme.Cream);
            e.Graphics.FillRectangle(fill, e.Bounds);
            TextRenderer.DrawText(
                e.Graphics,
                tabs.TabPages[e.Index].Text,
                Theme.BodyBold,
                e.Bounds,
                selected ? Theme.Navy : Theme.Muted,
                TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter);
            if (selected)
            {
                using var gold = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(gold, e.Bounds.X, e.Bounds.Bottom - 3, e.Bounds.Width, 3);
            }
        }

        private void LayoutSessionCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Stay signed in",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            var hint = new Label
            {
                Text = "One setting for every user. When it is on, they can check Stay signed in on the sign-in screen.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(620, 28)
            };
            card.Controls.Add(heading);
            card.Controls.Add(hint);

            var lblOn = new Label { Text = "FOR EVERYONE" };
            Theme.StyleFieldLabel(lblOn);
            _stayToggle = new CrcToggleSwitch();
            _stayToggle.Location = new Point(24, 104);
            _stayToggle.Toggled += (_, _) => SaveSession();
            var onOff = new Label
            {
                Name = "lblStayOnOff",
                Font = Theme.BodyBold,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(78, 106)
            };
            lblOn.Location = new Point(24, 82);

            var lblDays = new Label { Text = "REMEMBER FOR" };
            Theme.StyleFieldLabel(lblDays);
            _sessionDays = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_sessionDays);
            foreach (int days in new[] { 7, 14, 30, 60, 90 })
                _sessionDays.Items.Add(new IntChoice(days, days + " days"));
            lblDays.Location = new Point(180, 82);
            _sessionDays.Location = new Point(180, 100);
            _sessionDays.Size = new Size(160, 26);
            _sessionDays.SelectionChangeCommitted += (_, _) => SaveSession();

            var lblIdle = new Label { Text = "CLOSE WHEN IDLE" };
            Theme.StyleFieldLabel(lblIdle);
            _idleHours = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_idleHours);
            foreach (int hours in new[] { 1, 2, 3, 5, 8, 12 })
                _idleHours.Items.Add(new IntChoice(hours, hours == 1 ? "After 1 hour" : "After " + hours + " hours"));
            lblIdle.Location = new Point(356, 82);
            _idleHours.Location = new Point(356, 100);
            _idleHours.Size = new Size(180, 26);
            _idleHours.SelectionChangeCommitted += (_, _) => SaveSession();

            card.Controls.Add(lblOn);
            card.Controls.Add(_stayToggle);
            card.Controls.Add(onOff);
            card.Controls.Add(lblDays);
            card.Controls.Add(_sessionDays);
            card.Controls.Add(lblIdle);
            card.Controls.Add(_idleHours);
        }

        private void LoadSession()
        {
            if (_stayToggle == null)
                return;
            _stayToggle.SetOn(AppState.StaySignedInEnabled);
            SelectChoice(_sessionDays, AppState.StaySignedInDays, 30);
            SelectChoice(_idleHours, AppState.IdleCloseHours, 5);
            ApplySessionEnabled();
        }

        private void SaveSession()
        {
            AppState.StaySignedInEnabled = _stayToggle.On;
            AppState.StaySignedInDays = _sessionDays.SelectedItem is IntChoice days ? days.Value : 30;
            AppState.IdleCloseHours = _idleHours.SelectedItem is IntChoice hours ? hours.Value : 5;
            ApplySessionEnabled();
            AppLock.SaveSettings();
            if (!AppState.StaySignedInEnabled)
                IdleWatch.Stop();
            else if (AppState.StaySignedIn)
                IdleWatch.Start();
        }

        private void ApplySessionEnabled()
        {
            bool on = _stayToggle.On;
            _sessionDays.Enabled = on;
            _idleHours.Enabled = on;
            if (Controls.Find("lblStayOnOff", true).FirstOrDefault() is Label label)
                label.Text = on ? "On" : "Off";
        }

        private static void SelectChoice(ComboBox box, int value, int fallback)
        {
            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is IntChoice choice && choice.Value == value)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            for (int i = 0; i < box.Items.Count; i++)
            {
                if (box.Items[i] is IntChoice choice && choice.Value == fallback)
                {
                    box.SelectedIndex = i;
                    return;
                }
            }

            if (box.Items.Count > 0)
                box.SelectedIndex = 0;
        }

        private sealed class IntChoice
        {
            public IntChoice(int value, string label)
            {
                Value = value;
                Label = label;
            }

            public int Value { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }

        private void LayoutBankCard(CardPanel card)
        {
            var heading = new Label
            {
                Text = "Live bank feed",
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                Location = new Point(24, 14),
                AutoSize = true
            };
            var hint = new Label
            {
                Text = "Only administrators can connect, sync, or change live-feed settings. People with Banking can still see imported transactions.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 42),
                Size = new Size(620, 36)
            };
            card.Controls.Add(heading);
            card.Controls.Add(hint);

            var lblSync = new Label { Text = "AUTO-SYNC" };
            Theme.StyleFieldLabel(lblSync);
            _plaidSync = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_plaidSync);
            _plaidSync.Items.Add(new SyncChoice(0, "Off"));
            _plaidSync.Items.Add(new SyncChoice(1, "Every 1 hour"));
            _plaidSync.Items.Add(new SyncChoice(3, "Every 3 hours"));
            lblSync.Location = new Point(24, 86);
            _plaidSync.Location = new Point(24, 104);
            _plaidSync.Size = new Size(180, 26);
            _plaidSync.SelectionChangeCommitted += (_, _) => SaveBankFeedQuiet();
            card.Controls.Add(lblSync);
            card.Controls.Add(_plaidSync);

            var connect = new Button
            {
                Text = "Connect bank",
                Size = new Size(140, 34),
                Location = new Point(24, 148)
            };
            Theme.StyleGoldButton(connect);
            connect.Click += async (_, _) =>
            {
                if (!PlaidClient.IsConfigured)
                {
                    ShowKeys(true);
                    ToastAlert.Error(this, "Add API keys once, save, then Connect bank.");
                    return;
                }

                SaveBankFeedQuiet();
                await BankLive.ConnectAsync(this);
            };
            card.Controls.Add(connect);

            var sync = new Button
            {
                Text = "Sync now",
                Size = new Size(110, 34),
                Location = new Point(176, 148)
            };
            Theme.StyleOutlineButton(sync);
            sync.Click += async (_, _) => await BankLive.SyncAllAsync(this);
            card.Controls.Add(sync);

            _keysToggle = new Button
            {
                Text = "API keys",
                Size = new Size(110, 34),
                Location = new Point(298, 148)
            };
            Theme.StyleNavyButton(_keysToggle);
            _keysToggle.Click += (_, _) => ShowKeys(!_keysPanel.Visible);
            card.Controls.Add(_keysToggle);

            _keysPanel = new Panel
            {
                Location = new Point(12, 196),
                Size = new Size(640, 210),
                BackColor = Theme.Paper,
                Visible = false
            };

            var lblEnv = new Label { Text = "ENVIRONMENT" };
            Theme.StyleFieldLabel(lblEnv);
            _plaidEnv = new ComboBox { DropDownStyle = ComboBoxStyle.DropDownList };
            Theme.StyleCombo(_plaidEnv);
            _plaidEnv.Items.AddRange(new object[] { "sandbox", "development", "production" });
            lblEnv.Location = new Point(12, 8);
            _plaidEnv.Location = new Point(12, 26);
            _plaidEnv.Size = new Size(160, 26);

            var lblId = new Label { Text = "CLIENT ID" };
            Theme.StyleFieldLabel(lblId);
            _plaidId = new TextBox();
            Theme.StyleField(_plaidId);
            lblId.Location = new Point(188, 8);
            _plaidId.Location = new Point(188, 26);
            _plaidId.Size = new Size(420, 26);

            var lblSecret = new Label { Text = "SECRET" };
            Theme.StyleFieldLabel(lblSecret);
            _plaidSecret = new TextBox { UseSystemPasswordChar = true };
            Theme.StyleField(_plaidSecret);
            lblSecret.Location = new Point(12, 64);
            _plaidSecret.Location = new Point(12, 82);
            _plaidSecret.Size = new Size(596, 26);

            var save = new Button
            {
                Text = "Save keys",
                Size = new Size(120, 34),
                Location = new Point(12, 118)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) => SaveBankFeed();

            var how = new Label
            {
                Text = "One-time: dashboard.plaid.com → Team Settings → Keys. Sandbox test login is user_good / pass_good. Use Development or Production after Plaid approves a live app.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(12, 158),
                Size = new Size(600, 44)
            };

            _keysPanel.Controls.Add(lblEnv);
            _keysPanel.Controls.Add(_plaidEnv);
            _keysPanel.Controls.Add(lblId);
            _keysPanel.Controls.Add(_plaidId);
            _keysPanel.Controls.Add(lblSecret);
            _keysPanel.Controls.Add(_plaidSecret);
            _keysPanel.Controls.Add(save);
            _keysPanel.Controls.Add(how);
            card.Controls.Add(_keysPanel);
        }

        private void ShowKeys(bool show)
        {
            _keysPanel.Visible = show;
            _keysToggle.Text = show ? "Hide keys" : "API keys";
            _bankCard.Height = show ? 430 : 210;
        }

        private sealed class SyncChoice
        {
            public SyncChoice(int hours, string label)
            {
                Hours = hours;
                Label = label;
            }

            public int Hours { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }

        private void SaveBankFeedQuiet()
        {
            AppState.PlaidClientId = _plaidId.Text.Trim();
            AppState.PlaidSecret = _plaidSecret.Text.Trim();
            AppState.PlaidEnv = _plaidEnv.SelectedItem?.ToString() ?? "sandbox";
            AppState.PlaidSyncHours = _plaidSync.SelectedItem is SyncChoice choice ? choice.Hours : 1;
            AppLock.SaveSettings();
            BankLiveWatch.Start();
        }

        private void LoadBankFeed()
        {
            _plaidId.Text = AppState.PlaidClientId;
            _plaidSecret.Text = AppState.PlaidSecret;
            string env = string.IsNullOrWhiteSpace(AppState.PlaidEnv) ? "sandbox" : AppState.PlaidEnv;
            int index = _plaidEnv.Items.IndexOf(env);
            _plaidEnv.SelectedIndex = index >= 0 ? index : 0;
            int hours = AppState.PlaidSyncHours;
            _plaidSync.SelectedIndex = hours == 3 ? 2 : hours == 0 ? 0 : 1;
            ShowKeys(!PlaidClient.IsConfigured);
        }

        private void SaveBankFeed()
        {
            SaveBankFeedQuiet();
            ToastAlert.Success(this, "Live bank feed settings were saved.");
        }

        private void LoadUsers()
        {
            if (_grid == null)
                return;
            _grid.Columns.Clear();
            _grid.Columns.Add("Username", "Username");
            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Email", "Email");
            _grid.Columns.Add("Admin", "Admin");
            _grid.Columns.Add("IT", "IT");
            _grid.Columns.Add("Access", "Table access");
            foreach (var account in Accounts.List())
            {
                _grid.Rows.Add(
                    account.Username,
                    account.DisplayName,
                    account.Email,
                    account.IsAdmin ? "Yes" : "",
                    account.IsIt ? "Yes" : "",
                    TableAccess.Summary(SqliteInventory.GetTableAccess(account.Username)));
            }
        }

        private string RowUser(int row) =>
            _grid.Rows[row].Cells[0].Value?.ToString()?.Trim() ?? "";

        private void ShowUserMenu(int row)
        {
            _grid.ClearSelection();
            _grid.Rows[row].Selected = true;
            string user = RowUser(row);
            var menu = new ContextMenuStrip();
            menu.Items.Add("Edit user", null, (_, _) => EditUser(user));
            if (AppState.IsAdmin)
                menu.Items.Add("Table access", null, (_, _) =>
                {
                    if (UserAccessForm.ShowFor(this, user))
                        LoadUsers();
                });
            if (user.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                menu.Items.Add("Change my password", null, (_, _) =>
                {
                    using var change = new ChangePasswordForm(user, requireCurrent: true);
                    change.ShowDialog(this);
                });
            else
                menu.Items.Add("Reset password", null, (_, _) => ResetPassword(user));
            menu.Items.Add("Delete user", null, (_, _) => DeleteUser(user));
            menu.Show(_grid, _grid.PointToClient(Control.MousePosition));
        }

        private void EditUser(string? username)
        {
            using var form = new ItUserEditForm(username);
            if (form.ShowDialog(this) == DialogResult.OK)
            {
                LoadUsers();
                LoadRoles();
            }
        }

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
            ItUserEditForm.SendLoginEmail(this, email, username, temp);
            LoadUsers();
        }

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
            LoadRoles();
        }

        private CardPanel RoleCard(string title, bool admin, out ListBox list)
        {
            var card = new CardPanel { Dock = DockStyle.Fill, Margin = new Padding(admin ? 0 : 8, 0, admin ? 8 : 0, 0) };
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
                Location = new Point(20, 44),
                Size = new Size(280, 120),
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
                box.Height = Math.Max(60, card.Height - 110);
                add.Location = new Point(20, card.Height - 48);
                remove.Location = new Point(110, card.Height - 48);
            };
            return card;
        }

        private void LoadRoles()
        {
            if (_admins == null)
                return;
            FillRoles(_admins, Accounts.ReadAdmins());
            FillRoles(_it, Accounts.ReadIt());
        }

        private static void FillRoles(ListBox box, List<string> names)
        {
            box.Items.Clear();
            foreach (var name in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
                box.Items.Add(name);
        }

        private void AddRole(bool admin)
        {
            var existing = new HashSet<string>(
                admin ? Accounts.ReadAdmins() : Accounts.ReadIt(),
                StringComparer.OrdinalIgnoreCase);
            var choices = Accounts.List()
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
            LoadUsers();
            LoadRoles();
        }

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

            LoadUsers();
            LoadRoles();
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
}
