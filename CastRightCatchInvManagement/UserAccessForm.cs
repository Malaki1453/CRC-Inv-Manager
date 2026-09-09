namespace CastRightCatchInvManagement
{
    /// <summary>Administrator dialog: table visibility, write mode, hidden columns, and blocked companies.</summary>
    internal sealed class UserAccessForm : Form
    {
        private readonly Action<string> _save;
        private readonly string? _groupBaseline;
        private readonly Action? _afterReset;
        public bool Applied { get; private set; }
        private readonly Dictionary<string, CheckBox> _visible = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, ComboBox> _write = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, TextBox> _hiddenCols = new(StringComparer.OrdinalIgnoreCase);
        private TextBox _blocked = null!;

        public static bool ShowFor(IWin32Window? owner, string username)
        {
            username = (username ?? "").Trim();
            var groups = SqliteInventory.GetAccessGroups(username);
            string baseline = SqliteInventory.GetGroupAccessMerged(username);
            string effective = SqliteInventory.GetEffectiveTableAccess(username);
            var formTables = TableAccess.All
                .Where(item => item.Key != TableAccess.Reports)
                .Select(item => item.Key);

            using var form = new UserAccessForm(
                "Data access  ·  " + username,
                effective,
                json =>
                {
                    string stored = groups.Count == 0
                        ? json
                        : DataAccess.Diff(baseline, json, formTables);
                    SqliteInventory.SetTableAccess(username, stored);
                    ApplyIfCurrent(username);
                },
                groups.Count > 0 ? baseline : null,
                groups.Count > 0
                    ? () =>
                    {
                        SqliteInventory.SetTableAccess(username, "");
                        ApplyIfCurrent(username);
                    }
                    : null);
            var result = owner == null ? form.ShowDialog() : form.ShowDialog(owner);
            return result == DialogResult.OK || form.Applied;
        }

        private static void ApplyIfCurrent(string username)
        {
            if (!username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                return;
            TableAccess.Apply(username);
            AppLock.NotifyChanged();
        }

        public static bool ShowGroup(IWin32Window? owner, string groupName)
        {
            groupName = (groupName ?? "").Trim();
            if (groupName.Length == 0)
                return false;
            if (!AccessGroups.CanEdit(groupName))
            {
                MessageBox.Show(
                    owner,
                    AccessGroups.IsAdmin(groupName)
                        ? "The Admin group cannot be changed. It has Settings and User management, and cannot see tables."
                        : "Only an administrator can change the IT group.",
                    "Groups",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return false;
            }

            using var form = new UserAccessForm(
                "Group  ·  " + groupName,
                SqliteInventory.GetGroupAccess(groupName),
                json =>
                {
                    SqliteInventory.SaveAccessGroup(groupName, json, out _);
                    string current = AppState.CurrentUsername;
                    if (SqliteInventory.GetAccessGroups(current)
                            .Any(name => name.Equals(groupName, StringComparison.OrdinalIgnoreCase)))
                    {
                        TableAccess.Apply(current);
                        AppLock.NotifyChanged();
                    }
                });
            var result = owner == null ? form.ShowDialog() : form.ShowDialog(owner);
            return result == DialogResult.OK;
        }

        private UserAccessForm(
            string title,
            string json,
            Action<string> onSave,
            string? groupBaseline = null,
            Action? afterReset = null)
        {
            _save = onSave;
            _groupBaseline = groupBaseline;
            _afterReset = afterReset;
            Text = title;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = false;
            MaximizeBox = true;
            ShowInTaskbar = false;
            ClientSize = new Size(720, 640);
            MinimumSize = new Size(640, 520);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Theme.Cream
            };
            var save = new Button { Text = "Save", Size = new Size(100, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                Save();
                Applied = true;
                DialogResult = DialogResult.OK;
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 34),
                Anchor = AnchorStyles.Right | AnchorStyles.Top
            };
            Theme.StyleOutlineButton(cancel);
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            if (_groupBaseline != null)
            {
                var reset = new Button
                {
                    Text = "Reset to groups",
                    Size = new Size(150, 34),
                    Location = new Point(16, 10),
                    Anchor = AnchorStyles.Left | AnchorStyles.Top
                };
                Theme.StyleOutlineButton(reset);
                reset.Click += (_, _) =>
                {
                    LoadJson(_groupBaseline);
                    _afterReset?.Invoke();
                    Applied = true;
                };
                footer.Controls.Add(reset);
            }

            footer.Resize += (_, _) =>
            {
                save.Location = new Point(footer.Width - 210, 10);
                cancel.Location = new Point(footer.Width - 100, 10);
            };

            var scroll = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(24, 16, 24, 16)
            };

            var intro = new Label
            {
                Text = _groupBaseline != null
                    ? "Unchecked tables are hidden. Write mode is View only, Confirm first, or Add / edit / delete. Anything you change here overrides this user's groups for that item. Reset to groups clears those overrides so group settings apply again."
                    : "Unchecked tables are hidden in the sidebar. Write mode is View only, Confirm first (Review tab), or Add / edit / delete. Hidden columns (for example Routing Number, Account Number) and blocked companies apply even when the table is visible.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 8),
                Size = new Size(650, 60)
            };
            scroll.Controls.Add(intro);

            int y = 76;
            foreach (var (key, label) in TableAccess.All)
            {
                if (key == TableAccess.Reports)
                    continue;

                var heading = new Label
                {
                    Text = label,
                    Font = Theme.BodyBold,
                    ForeColor = Theme.Navy,
                    Location = new Point(24, y),
                    AutoSize = true
                };
                scroll.Controls.Add(heading);
                y += 22;

                var visible = new CheckBox
                {
                    Text = "Visible",
                    Location = new Point(28, y),
                    AutoSize = true
                };
                _visible[key] = visible;
                scroll.Controls.Add(visible);

                var write = new ComboBox
                {
                    DropDownStyle = ComboBoxStyle.DropDownList,
                    Location = new Point(130, y - 2),
                    Size = new Size(180, 26)
                };
                Theme.StyleCombo(write);
                write.Items.AddRange(new object[]
                {
                    DataAccess.ModeLabel(DataWriteMode.View),
                    DataAccess.ModeLabel(DataWriteMode.Confirm),
                    DataAccess.ModeLabel(DataWriteMode.Auto)
                });
                _write[key] = write;
                scroll.Controls.Add(write);

                var hidden = new TextBox
                {
                    Location = new Point(324, y - 2),
                    Size = new Size(340, 26),
                    PlaceholderText = "Hidden columns, comma-separated"
                };
                Theme.StyleField(hidden);
                _hiddenCols[key] = hidden;
                scroll.Controls.Add(hidden);

                visible.CheckedChanged += (_, _) => write.Enabled = visible.Checked;
                y += 36;
            }

            var blockLabel = new Label
            {
                Text = "Blocked customers, vendors, and products (one code or name per line). Matching parties, items, and their trades stay hidden.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, y),
                Size = new Size(640, 36)
            };
            scroll.Controls.Add(blockLabel);
            y += 40;
            _blocked = new TextBox
            {
                Location = new Point(24, y),
                Size = new Size(640, 120),
                Multiline = true,
                ScrollBars = ScrollBars.Vertical
            };
            Theme.StyleField(_blocked);
            scroll.Controls.Add(_blocked);
            scroll.AutoScrollMinSize = new Size(0, y + 140);
            LoadJson(json);

            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(scroll);
            Controls.Add(footer);
        }

        private void LoadJson(string? json)
        {
            var denied = TableAccess.ParseDenied(json ?? "");
            var policies = new Dictionary<string, DataTablePolicy>(StringComparer.OrdinalIgnoreCase);
            var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DataAccess.Parse(json ?? "", policies, blocked);

            foreach (var (key, visible) in _visible)
            {
                visible.Checked = !denied.Contains(key);
                policies.TryGetValue(key, out var policy);
                policy ??= new DataTablePolicy();
                if (_write.TryGetValue(key, out var write))
                {
                    write.SelectedItem = DataAccess.ModeLabel(policy.Write);
                    if (write.SelectedIndex < 0)
                        write.SelectedIndex = 2;
                    write.Enabled = visible.Checked;
                }

                if (_hiddenCols.TryGetValue(key, out var hidden))
                    hidden.Text = string.Join(", ", policy.HideColumns);
            }

            _blocked.Text = string.Join(Environment.NewLine, blocked);
        }

        private void Save()
        {
            var denied = _visible
                .Where(pair => !pair.Value.Checked)
                .Select(pair => pair.Key)
                .ToList();
            var policies = new Dictionary<string, DataTablePolicy>(StringComparer.OrdinalIgnoreCase);
            foreach (var (key, _) in TableAccess.All)
            {
                if (!_write.TryGetValue(key, out var write))
                    continue;
                policies[key] = new DataTablePolicy
                {
                    Write = DataAccess.ParseMode(write.Text),
                    HideColumns = (_hiddenCols[key].Text ?? "")
                        .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                        .ToList()
                };
            }

            var blocked = (_blocked.Text ?? "")
                .Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

            _save(DataAccess.BuildJson(denied, policies, blocked));
        }
    }
}
