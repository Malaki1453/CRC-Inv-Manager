namespace CastRightCatchInvManagement
{
    /// <summary>Add, edit, or remove bank accounts used when reading statement files.</summary>
    internal sealed class BankAccountsForm : Form
    {
        private readonly DataGridView _grid = new();

        public static void ShowList(IWin32Window? owner)
        {
            using var form = new BankAccountsForm();
            if (owner != null)
                form.ShowDialog(owner);
            else
                form.ShowDialog();
        }

        private BankAccountsForm()
        {
            Text = "Bank accounts";
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = false;
            ShowInTaskbar = false;
            ClientSize = new Size(640, 420);
            MinimumSize = new Size(520, 320);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Theme.Cream,
                Padding = new Padding(16, 10, 16, 10)
            };
            var add = new Button { Text = "Add account", Size = new Size(130, 34), Location = new Point(16, 10) };
            Theme.StyleGoldButton(add);
            add.Click += (_, _) => Edit(null);
            var close = new Button
            {
                Text = "Close",
                DialogResult = DialogResult.OK,
                Size = new Size(90, 34)
            };
            Theme.StyleOutlineButton(close);
            footer.Controls.Add(add);
            footer.Controls.Add(close);
            footer.Resize += (_, _) => close.Location = new Point(Math.Max(160, footer.Width - 106), 10);

            var intro = new Label
            {
                Dock = DockStyle.Top,
                Height = 40,
                Padding = new Padding(16, 10, 16, 0),
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                Text = "Accounts are labels for imported statements. Download OFX, QFX, or CSV from the bank, then use Read file on Banking."
            };

            _grid.Dock = DockStyle.Fill;
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.AllowUserToDeleteRows = false;
            _grid.MultiSelect = false;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            Theme.StyleGrid(_grid);
            _grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex >= 0)
                    Edit(RowId(e.RowIndex));
            };
            _grid.CellMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0)
                    return;
                _grid.ClearSelection();
                _grid.Rows[e.RowIndex].Selected = true;
                long id = RowId(e.RowIndex);
                var menu = new ContextMenuStrip();
                menu.Items.Add("Edit", null, (_, _) => Edit(id));
                menu.Items.Add("Delete", null, (_, _) => Delete(id));
                menu.Show(_grid, _grid.PointToClient(Cursor.Position));
            };

            Controls.Add(_grid);
            Controls.Add(intro);
            Controls.Add(footer);
            CancelButton = close;
            LoadRows();
        }

        /// <summary>Ask which account to import into. Null if they cancel or have none.</summary>
        public static (long Id, string Name)? PickAccount(IWin32Window owner)
        {
            var accounts = SqliteInventory.ListBankAccounts();
            if (accounts.Count == 0)
            {
                var ask = MessageBox.Show(
                    owner,
                    "Add a bank account first, then read a statement file into it.",
                    "Bank accounts",
                    MessageBoxButtons.OKCancel,
                    MessageBoxIcon.Information);
                if (ask != DialogResult.OK)
                    return null;
                ShowList(owner);
                accounts = SqliteInventory.ListBankAccounts();
                if (accounts.Count == 0)
                    return null;
            }

            if (accounts.Count == 1)
                return (accounts[0].Id, accounts[0].Name);

            using var form = new Form
            {
                Text = "Choose account",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(360, 160),
                BackColor = Theme.Cream,
                Font = Theme.Body
            };
            var box = new ComboBox
            {
                DropDownStyle = ComboBoxStyle.DropDownList,
                Location = new Point(24, 40),
                Width = 310
            };
            Theme.StyleCombo(box);
            foreach (var account in accounts)
                box.Items.Add(new Choice(account.Id, account.Name, account.Bank, account.Last4));
            box.SelectedIndex = 0;
            var ok = new Button
            {
                Text = "Read file",
                Size = new Size(100, 32),
                Location = new Point(154, 100)
            };
            Theme.StyleGoldButton(ok);
            ok.Click += (_, _) => form.DialogResult = DialogResult.OK;
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(80, 32),
                Location = new Point(264, 100)
            };
            Theme.StyleOutlineButton(cancel);
            form.Controls.Add(new Label
            {
                Text = "Which account is this statement for?",
                Location = new Point(24, 16),
                AutoSize = true
            });
            form.Controls.Add(box);
            form.Controls.Add(ok);
            form.Controls.Add(cancel);
            form.AcceptButton = ok;
            form.CancelButton = cancel;
            if (form.ShowDialog(owner) != DialogResult.OK || box.SelectedItem is not Choice choice)
                return null;
            return (choice.Id, choice.Name);
        }

        private void LoadRows()
        {
            _grid.Columns.Clear();
            _grid.Columns.Add("Id", "Id");
            _grid.Columns["Id"]!.Visible = false;
            _grid.Columns.Add("Name", "Name");
            _grid.Columns.Add("Bank", "Bank");
            _grid.Columns.Add("Last4", "Last 4");
            _grid.Columns.Add("Notes", "Notes");
            foreach (var account in SqliteInventory.ListBankAccounts())
                _grid.Rows.Add(account.Id, account.Name, account.Bank, account.Last4, account.Notes);
        }

        private long RowId(int row)
        {
            return long.TryParse(_grid.Rows[row].Cells[0].Value?.ToString(), out long id) ? id : 0;
        }

        private void Edit(long? id)
        {
            var existing = id == null
                ? default((long Id, string Name, string Bank, string Last4, string Notes))
                : SqliteInventory.ListBankAccounts().FirstOrDefault(a => a.Id == id.Value);

            using var form = new Form
            {
                Text = id == null ? "Add account" : "Edit account",
                StartPosition = FormStartPosition.CenterParent,
                FormBorderStyle = FormBorderStyle.FixedDialog,
                MinimizeBox = false,
                MaximizeBox = false,
                ClientSize = new Size(400, 280),
                BackColor = Theme.Cream,
                Font = Theme.Body
            };
            var name = Field(form, "NAME", 24, 20, 350);
            var bank = Field(form, "BANK", 24, 74, 350);
            var last4 = Field(form, "LAST 4", 24, 128, 120);
            var notes = Field(form, "NOTES", 160, 128, 214);
            if (id != null)
            {
                name.Text = existing.Name;
                bank.Text = existing.Bank;
                last4.Text = existing.Last4;
                notes.Text = existing.Notes;
            }

            var save = new Button { Text = "Save", Size = new Size(100, 34), Location = new Point(184, 220) };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                if (name.Text.Trim().Length == 0)
                {
                    MessageBox.Show("Enter an account name.", form.Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }

                if (id == null)
                    SqliteInventory.InsertBankAccount(name.Text.Trim(), bank.Text.Trim(), last4.Text.Trim(), notes.Text.Trim());
                else
                    SqliteInventory.UpdateBankAccount(id.Value, name.Text.Trim(), bank.Text.Trim(), last4.Text.Trim(), notes.Text.Trim());
                form.DialogResult = DialogResult.OK;
            };
            var cancel = new Button
            {
                Text = "Cancel",
                DialogResult = DialogResult.Cancel,
                Size = new Size(90, 34),
                Location = new Point(294, 220)
            };
            Theme.StyleOutlineButton(cancel);
            form.Controls.Add(save);
            form.Controls.Add(cancel);
            form.AcceptButton = save;
            form.CancelButton = cancel;
            if (form.ShowDialog(this) == DialogResult.OK)
                LoadRows();
        }

        private void Delete(long id)
        {
            if (MessageBox.Show(
                    "Delete this bank account? Imported transactions stay in Banking.",
                    "Bank accounts",
                    MessageBoxButtons.YesNo,
                    MessageBoxIcon.Warning) != DialogResult.Yes)
                return;

            SqliteInventory.DeleteBankAccount(id);
            LoadRows();
        }

        private static TextBox Field(Form form, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption, Location = new Point(x, y), AutoSize = true };
            Theme.StyleFieldLabel(label);
            var box = new TextBox
            {
                Location = new Point(x, y + 16),
                Size = new Size(width, 26)
            };
            Theme.StyleField(box);
            form.Controls.Add(label);
            form.Controls.Add(box);
            return box;
        }

        private sealed class Choice
        {
            public Choice(long id, string name, string bank, string last4)
            {
                Id = id;
                Name = name;
                Label = last4.Length > 0
                    ? $"{name}  ·  {bank} ****{last4}"
                    : bank.Length > 0 ? $"{name}  ·  {bank}" : name;
            }

            public long Id { get; }
            public string Name { get; }
            public string Label { get; }
            public override string ToString() => Label;
        }
    }
}
