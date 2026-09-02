namespace CastRightCatchInvManagement
{
    /// <summary>Forced or voluntary password change. New passwords must be at least 8 characters.</summary>
    internal sealed class ChangePasswordForm : Form
    {
        private readonly string _username;
        private readonly bool _requireCurrent;
        private readonly bool _requireQuestions;
        private readonly TextBox _current;
        private readonly TextBox _next;
        private readonly TextBox _confirm;
        private readonly ComboBox[] _questions = new ComboBox[3];
        private readonly TextBox[] _answers = new TextBox[3];

        public ChangePasswordForm(string username, bool requireCurrent)
        {
            _username = username;
            _requireCurrent = requireCurrent;
            _requireQuestions = !requireCurrent || !Accounts.HasSecurityQuestions(username);
            Text = "Choose a new password";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            ControlBox = requireCurrent;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, _requireQuestions ? 560 : requireCurrent ? 280 : 240);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var hint = new Label
            {
                Text = requireCurrent
                    ? "Enter your current password, then choose a new one (at least 8 characters)."
                    : "Choose a new password (at least 8 characters) and three security questions.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 16),
                Size = new Size(370, 36)
            };
            Controls.Add(hint);

            int y = 58;
            if (requireCurrent)
            {
                _current = Field("CURRENT PASSWORD", 24, y, 370);
                _current.UseSystemPasswordChar = true;
                y += 54;
            }
            else
            {
                _current = new TextBox { Visible = false };
            }

            _next = Field("NEW PASSWORD", 24, y, 370);
            _next.UseSystemPasswordChar = true;
            y += 54;
            _confirm = Field("CONFIRM PASSWORD", 24, y, 370);
            _confirm.UseSystemPasswordChar = true;
            y += 58;

            if (_requireQuestions)
            {
                string q1 = "", q2 = "", q3 = "";
                Accounts.TryGetSecurityQuestions(username, out q1, out q2, out q3);
                string[] existing = { q1, q2, q3 };
                for (int i = 0; i < 3; i++)
                {
                    _questions[i] = QuestionBox("QUESTION " + (i + 1), 24, y, 370, existing[i], i);
                    y += 54;
                    _answers[i] = Field("ANSWER " + (i + 1), 24, y, 370);
                    y += 54;
                }
            }

            var save = new Button
            {
                Text = "Save password",
                Size = new Size(140, 34),
                Location = new Point(154, ClientSize.Height - 52)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
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

        private bool Save()
        {
            if (_next.Text != _confirm.Text)
            {
                MessageBox.Show("The passwords do not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            bool ok = _requireCurrent
                ? Accounts.ChangeOwnPassword(_username, _current.Text, _next.Text, out string error)
                : Accounts.SetPassword(_username, _next.Text, out error, mustChange: false);
            if (!ok)
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_requireQuestions)
            {
                if (!Accounts.SetSecurityQuestions(
                        _username,
                        _questions[0].Text, _answers[0].Text,
                        _questions[1].Text, _answers[1].Text,
                        _questions[2].Text, _answers[2].Text,
                        out error))
                {
                    MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }

            return true;
        }

        private ComboBox QuestionBox(string caption, int x, int y, int width, string selected, int slot)
        {
            var label = new Label { Text = caption, Location = new Point(x, y), AutoSize = true };
            Theme.StyleFieldLabel(label);
            var box = new ComboBox
            {
                Location = new Point(x, y + 16),
                Size = new Size(width, 26),
                DropDownStyle = ComboBoxStyle.DropDownList
            };
            Theme.StyleCombo(box);
            box.Items.AddRange(Accounts.SecurityQuestionBank);
            if (selected.Length > 0)
            {
                int index = box.Items.IndexOf(selected);
                box.SelectedIndex = index >= 0 ? index : 0;
            }
            else if (box.Items.Count > 0)
                box.SelectedIndex = Math.Min(box.Items.Count - 1, slot);
            Controls.Add(label);
            Controls.Add(box);
            return box;
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
