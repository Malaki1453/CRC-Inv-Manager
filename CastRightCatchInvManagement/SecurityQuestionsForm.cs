namespace CastRightCatchInvManagement
{
    internal sealed class SecurityQuestionsForm : Form
    {
        private readonly string _username;
        private readonly ComboBox[] _questions = new ComboBox[3];
        private readonly TextBox[] _answers = new TextBox[3];

        public SecurityQuestionsForm(string username)
        {
            _username = username;
            Text = "Security questions";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(420, 400);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var hint = new Label
            {
                Text = "Pick three different questions. Answers are stored hashed, not as plain text.",
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Location = new Point(24, 16),
                Size = new Size(370, 32)
            };
            Controls.Add(hint);

            Accounts.TryGetSecurityQuestions(username, out string q1, out string q2, out string q3);
            string[] existing = { q1, q2, q3 };
            int y = 52;
            for (int i = 0; i < 3; i++)
            {
                _questions[i] = QuestionBox("QUESTION " + (i + 1), 24, y, 370, existing[i], i);
                y += 54;
                _answers[i] = Field("ANSWER " + (i + 1), 24, y, 370);
                y += 54;
            }

            var save = new Button
            {
                Text = "Save",
                Size = new Size(110, 34),
                Location = new Point(184, ClientSize.Height - 52)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                if (!Accounts.SetSecurityQuestions(
                        _username,
                        _questions[0].Text, _answers[0].Text,
                        _questions[1].Text, _answers[1].Text,
                        _questions[2].Text, _answers[2].Text,
                        out string error))
                {
                    MessageBox.Show(error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                DialogResult = DialogResult.OK;
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 34),
                Location = new Point(304, ClientSize.Height - 52)
            };
            Theme.StyleOutlineButton(cancel);
            AcceptButton = save;
            CancelButton = cancel;
            Controls.Add(save);
            Controls.Add(cancel);
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
                box.SelectedIndex = index >= 0 ? index : slot;
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
