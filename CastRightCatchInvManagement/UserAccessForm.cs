namespace CastRightCatchInvManagement
{
    /// <summary>Administrator dialog: which tables this user may open.</summary>
    internal sealed class UserAccessForm : Form
    {
        private readonly string _username;
        private readonly Dictionary<string, CheckBox> _tables = new(StringComparer.OrdinalIgnoreCase);

        public static bool ShowFor(IWin32Window? owner, string username)
        {
            using var form = new UserAccessForm(username);
            var result = owner == null ? form.ShowDialog() : form.ShowDialog(owner);
            return result == DialogResult.OK;
        }

        private UserAccessForm(string username)
        {
            _username = username.Trim();
            Text = "Table access  ·  " + _username;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 470);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var intro = new Label
            {
                Text = "Unchecked tables are hidden in the sidebar for this user. Administrators and IT still see every table. Stay signed in is chosen on the sign-in screen, not here.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 16),
                Size = new Size(370, 56)
            };
            Controls.Add(intro);

            var denied = TableAccess.ParseDenied(SqliteInventory.GetTableAccess(_username));
            int y = 80;
            foreach (var (key, label) in TableAccess.All)
            {
                var box = new CheckBox
                {
                    Text = label,
                    Checked = !denied.Contains(key),
                    Location = new Point(28, y),
                    AutoSize = true,
                    Font = Theme.Body,
                    ForeColor = Theme.Navy
                };
                _tables[key] = box;
                Controls.Add(box);
                y += 28;
            }

            var save = new Button
            {
                Text = "Save",
                Size = new Size(100, 34),
                Location = new Point(204, 418)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                Save();
                DialogResult = DialogResult.OK;
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 34),
                Location = new Point(314, 418)
            };
            Theme.StyleOutlineButton(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(save);
            Controls.Add(cancel);
        }

        private void Save()
        {
            var denied = _tables
                .Where(pair => !pair.Value.Checked)
                .Select(pair => pair.Key)
                .ToList();
            SqliteInventory.SetTableAccess(_username, TableAccess.ToJson(denied));

            if (_username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                TableAccess.Apply(_username);
                AppLock.NotifyChanged();
            }
        }
    }
}
