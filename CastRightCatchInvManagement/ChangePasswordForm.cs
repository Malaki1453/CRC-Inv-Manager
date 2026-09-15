namespace CastRightCatchInvManagement
{
    /// <summary>Forced or voluntary password change. New passwords need 8+ characters, a capital, a number, and a symbol.</summary>
    internal sealed class ChangePasswordForm : Form
    {
        private readonly string _username;
        private readonly bool _requireCurrent;
        private readonly TextBox _current;
        private readonly TextBox _next;
        private readonly TextBox _confirm;

        /// <summary>
        /// requireCurrent is true for a signed-in change; false for first-login (no close box so it cannot be skipped).
        /// </summary>
        public ChangePasswordForm(string username, bool requireCurrent)
        {
            _username = username;
            _requireCurrent = requireCurrent;
            Text = "Choose a new password";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = requireCurrent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, requireCurrent ? 280 : 240);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            // Use the packaged seal icon when the asset pack is present.
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var hint = new Label
            {
                Text = requireCurrent
                    ? "Enter your current password, then choose a new one (8+ characters, a capital, a number, and a symbol)."
                    : "Choose a new password (8+ characters, a capital, a number, and a symbol).",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 16),
                Size = new Size(370, 36)
            };
            Controls.Add(hint);

            int y = 58;
            // Voluntary change must prove the current password; first-login has none yet.
            if (requireCurrent)
            {
                _current = Field("CURRENT PASSWORD", 24, y, 370);
                _current.UseSystemPasswordChar = true;
                y += 54;
            }
            // IT-issued temp password: no current password and no security questions.
            else
            {
                _current = new TextBox { Visible = false };
            }

            _next = Field("NEW PASSWORD", 24, y, 370);
            _next.UseSystemPasswordChar = true;
            y += 54;
            _confirm = Field("CONFIRM PASSWORD", 24, y, 370);
            _confirm.UseSystemPasswordChar = true;

            var save = new Button
            {
                Text = "Save password",
                Size = new Size(140, 34),
                Location = new Point(154, ClientSize.Height - 52)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                // Leave the dialog open when validation or persist fails.
                if (Save())
                    DialogResult = DialogResult.OK;
            };
            var cancel = new Button
            {
                Text = requireCurrent ? "Cancel" : "Cancel sign in",
                DialogResult = DialogResult.Cancel,
                Size = new Size(120, 34),
                Location = new Point(300, ClientSize.Height - 52)
            };
            Theme.StyleOutlineButton(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(save);
            Controls.Add(cancel);
        }

        /// <summary>Validate match and complexity, then persist via Accounts.</summary>
        private bool Save()
        {
            // Confirm field is a typed copy, not a second stored secret.
            if (_next.Text != _confirm.Text)
            {
                MessageBox.Show("The passwords do not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            bool ok = _requireCurrent
                ? Accounts.ChangeOwnPassword(_username, _current.Text, _next.Text, out string error)
                : Accounts.SetPassword(_username, _next.Text, out error, mustChange: false);
            // Keep the dialog open so the user can fix a weak or wrong password.
            if (!ok)
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            return true;
        }

        /// <summary>Caption plus password box at a fixed location on this dialog.</summary>
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
