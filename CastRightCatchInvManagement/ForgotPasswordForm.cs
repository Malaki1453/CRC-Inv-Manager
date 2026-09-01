namespace CastRightCatchInvManagement
{
    internal sealed class ForgotPasswordForm : Form
    {
        private readonly TextBox _user;
        private readonly ComboBox[] _questions = new ComboBox[3];
        private readonly TextBox[] _answers = new TextBox[3];
        private readonly TextBox _next;
        private readonly TextBox _confirm;
        private readonly Button _load;
        private readonly Panel _rest;

        public ForgotPasswordForm()
        {
            Text = "Forgot password";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 560);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var hint = new Label
            {
                Text = "Enter your username, then answer your security questions to set a new password.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 16),
                Size = new Size(370, 36)
            };
            Controls.Add(hint);

            _user = Field("USERNAME", 24, 56, 250);
            _load = new Button
            {
                Text = "Load questions",
                Size = new Size(110, 28),
                Location = new Point(284, 72)
            };
            Theme.StyleNavyButton(_load);
            _load.Click += (_, _) => LoadQuestions();
            Controls.Add(_load);

            _rest = new Panel
            {
                Location = new Point(0, 110),
                Size = new Size(420, 440),
                Visible = false,
                BackColor = Theme.Cream
            };
            Controls.Add(_rest);

            int y = 0;
            for (int i = 0; i < 3; i++)
            {
                _questions[i] = QuestionBox("QUESTION " + (i + 1), 24, y, 370);
                y += 54;
                _answers[i] = FieldOn(_rest, "ANSWER " + (i + 1), 24, y, 370);
                y += 54;
            }

            _next = FieldOn(_rest, "NEW PASSWORD", 24, y, 370);
            _next.UseSystemPasswordChar = true;
            y += 54;
            _confirm = FieldOn(_rest, "CONFIRM PASSWORD", 24, y, 370);
            _confirm.UseSystemPasswordChar = true;

            var save = new Button
            {
                Text = "Save password",
                Size = new Size(140, 34),
                Location = new Point(154, 380)
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
                Location = new Point(304, 380)
            };
            Theme.StyleOutlineButton(cancel);
            _rest.Controls.Add(save);
            _rest.Controls.Add(cancel);
            CancelButton = cancel;
        }

        private void LoadQuestions()
        {
            string user = _user.Text.Trim();
            if (user.Length == 0)
            {
                MessageBox.Show("Enter your username.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }

            if (!Accounts.TryGetSecurityQuestions(user, out string q1, out string q2, out string q3))
            {
                MessageBox.Show(
                    "No security questions are set for that user. Ask IT to reset the password.",
                    Text,
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            _questions[0].Text = q1;
            _questions[1].Text = q2;
            _questions[2].Text = q3;
            _rest.Visible = true;
        }

        private bool Save()
        {
            string user = _user.Text.Trim();
            if (!Accounts.VerifySecurityAnswers(user, _answers[0].Text, _answers[1].Text, _answers[2].Text))
            {
                MessageBox.Show("Those answers are not right.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (_next.Text != _confirm.Text)
            {
                MessageBox.Show("The passwords do not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            if (!Accounts.SetPassword(user, _next.Text, out string error, mustChange: false))
            {
                MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            MessageBox.Show("Password saved. Sign in with the new password.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return true;
        }

        private ComboBox QuestionBox(string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption, Location = new Point(x, y), AutoSize = true };
            Theme.StyleFieldLabel(label);
            var value = new ComboBox
            {
                Location = new Point(x, y + 16),
                Size = new Size(width, 26),
                Enabled = false,
                DropDownStyle = ComboBoxStyle.DropDown
            };
            Theme.StyleCombo(value);
            _rest.Controls.Add(label);
            _rest.Controls.Add(value);
            return value;
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

        private static TextBox FieldOn(Control parent, string caption, int x, int y, int width)
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
