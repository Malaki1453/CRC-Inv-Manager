namespace CastRightCatchInvManagement
{
    internal sealed class PartyEditForm : Form
    {
        private readonly bool _vendor;
        private readonly string _originalCode;
        private readonly TextBox _code;
        private readonly TextBox _name;
        private readonly TextBox _company;
        private readonly TextBox _phone;
        private readonly TextBox _balance;
        private readonly TextBox _notes;
        private readonly TextBox _contact;
        private readonly TextBox _address;
        private readonly TextBox _email;
        private readonly TextBox _terms;
        private readonly TextBox _extra;
        private readonly Label _subtitle;

        public static void OpenCustomerNew() => ShowEdit(false, null);

        public static void OpenCustomerEdit(Dictionary<string, string> record) => ShowEdit(false, record);

        public static void OpenVendorNew() => ShowEdit(true, null);

        public static void OpenVendorEdit(Dictionary<string, string> record) => ShowEdit(true, record);

        private static void ShowEdit(bool vendor, Dictionary<string, string>? record)
        {
            using var form = new PartyEditForm(vendor, record);
            form.ShowDialog();
        }

        private PartyEditForm(bool vendor, Dictionary<string, string>? record)
        {
            _vendor = vendor;
            _originalCode = record == null ? "" : DataFiles.GetRecord(record, "Code").Trim();
            bool editing = _originalCode.Length > 0;
            string kind = vendor ? "Vendor" : "Customer";

            Text = editing ? "Edit " + kind : "Add " + kind;
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(680, vendor ? 580 : 720);
            MinimumSize = new Size(560, vendor ? 480 : 580);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Theme.Paper,
                Padding = new Padding(24, 8, 24, 0)
            };
            var gold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };
            var title = new Label
            {
                Text = Text,
                Dock = DockStyle.Top,
                Height = 36,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            _subtitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft
            };
            header.Controls.Add(_subtitle);
            header.Controls.Add(title);
            header.Controls.Add(gold);

            var save = new Button
            {
                Text = editing ? "Save" : "Add " + kind,
                Size = new Size(editing ? 110 : 140, 34)
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                if (SaveRecord())
                    DialogResult = DialogResult.OK;
            };
            AcceptButton = save;

            var cancel = new Button
            {
                Text = "Cancel",
                Size = new Size(96, 34),
                DialogResult = DialogResult.Cancel
            };
            Theme.StyleOutlineButton(cancel);
            CancelButton = cancel;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 60,
                BackColor = Theme.Paper,
                Padding = new Padding(20, 12, 20, 12)
            };
            footer.Paint += (_, e) =>
            {
                using var line = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(line, 0, 0, footer.Width, 2);
            };
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            footer.Resize += (_, _) =>
            {
                save.Location = new Point(Math.Max(140, footer.Width - save.Width - 128), 13);
                cancel.Location = new Point(Math.Max(20, footer.Width - 116), 13);
            };

            var body = new Panel
            {
                Dock = DockStyle.Fill,
                AutoScroll = true,
                BackColor = Theme.Cream,
                Padding = new Padding(20, 16, 8, 16)
            };

            var identity = Section("Identity", 150, 2, out var identityGrid);
            _code = PutField(identityGrid, 0, 0, "CODE");
            _name = PutField(identityGrid, 1, 0, "NAME");
            _company = PutField(identityGrid, 0, 1, "COMPANY");
            _phone = PutField(identityGrid, 1, 1, "PHONE");
            _code.PlaceholderText = vendor ? "V-1001" : "C-1001";
            _name.PlaceholderText = vendor ? "Vendor name" : "Customer name";
            _company.PlaceholderText = "Company";
            _phone.PlaceholderText = "(253) 000-0000";

            _contact = new TextBox { Visible = false };
            _email = new TextBox { Visible = false };
            _address = new TextBox { Visible = false };

            CardPanel? contact = null;
            if (!vendor)
            {
                contact = Section("Contact", 196, 2, out var contactGrid);
                _contact = PutField(contactGrid, 0, 0, "CONTACT NAME");
                _email = PutField(contactGrid, 1, 0, "EMAIL");
                _address = PutField(contactGrid, 0, 1, "ADDRESS", colSpan: 2, multiline: true);
                _contact.PlaceholderText = "Who we talk to";
                _email.PlaceholderText = "name@company.com";
                contactGrid.RowStyles.Clear();
                contactGrid.RowStyles.Add(new RowStyle(SizeType.Absolute, 58));
                contactGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            }

            var account = Section("Account", vendor ? 160 : 108, 3, out var accountGrid);
            _terms = PutField(accountGrid, 0, 0, "TERMS");
            _extra = PutField(accountGrid, 1, 0, vendor ? "TYPE" : "CREDIT LIMIT");
            if (vendor)
            {
                _contact = PutField(accountGrid, 2, 0, "AMOUNT");
                _balance = PutField(accountGrid, 0, 1, "CURRENT BALANCE");
                _contact.PlaceholderText = "0.00";
                accountGrid.RowStyles.Clear();
                accountGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                accountGrid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
            }
            else
            {
                _balance = PutField(accountGrid, 2, 0, "CURRENT BALANCE");
            }
            _terms.PlaceholderText = "NET 15";
            _extra.PlaceholderText = vendor ? "Processor" : "0.00";
            _balance.PlaceholderText = "0.00";

            var notes = Section("Description", 180, 1, out var notesGrid);
            _notes = PutField(notesGrid, 0, 0, "NOTES", multiline: true);
            _notes.PlaceholderText = vendor
                ? "Notes about this vendor"
                : "Notes about this customer";

            if (record != null)
            {
                _code.Text = DataFiles.GetRecord(record, "Code");
                _name.Text = DataFiles.GetRecord(record, "Name");
                _company.Text = First(record, "Company", "Name");
                _phone.Text = DataFiles.GetRecord(record, "Phone");
                _balance.Text = DataFiles.GetRecord(record, "Current Balance");
                _terms.Text = DataFiles.GetRecord(record, "Terms");
                _notes.Text = First(record, "Description", "Notes");
                if (vendor)
                {
                    _extra.Text = DataFiles.GetRecord(record, "Type");
                    _contact.Text = DataFiles.GetRecord(record, "Amount");
                }
                else
                {
                    _extra.Text = DataFiles.GetRecord(record, "Credit Limit");
                    _contact.Text = DataFiles.GetRecord(record, "Contact Name");
                    _email.Text = DataFiles.GetRecord(record, "Email");
                    _address.Text = DataFiles.GetRecord(record, "Address");
                }
            }

            UpdateSubtitle();
            _name.TextChanged += (_, _) => UpdateSubtitle();
            _code.TextChanged += (_, _) => UpdateSubtitle();
            _company.TextChanged += (_, _) => UpdateSubtitle();

            var spacerNotes = Spacer();
            var spacerAccount = Spacer();
            var spacerMid = Spacer();

            body.Controls.Add(notes);
            body.Controls.Add(spacerNotes);
            body.Controls.Add(account);
            body.Controls.Add(spacerAccount);
            if (contact != null)
            {
                body.Controls.Add(contact);
                body.Controls.Add(spacerMid);
            }
            body.Controls.Add(identity);

            Controls.Add(body);
            Controls.Add(footer);
            Controls.Add(header);
        }

        private void UpdateSubtitle()
        {
            string name = _name.Text.Trim();
            string company = _company.Text.Trim();
            string code = _code.Text.Trim();
            var parts = new List<string>();
            if (company.Length > 0 && !company.Equals(name, StringComparison.OrdinalIgnoreCase))
                parts.Add(company);
            else if (name.Length > 0)
                parts.Add(name);
            if (code.Length > 0)
                parts.Add(code);
            _subtitle.Text = parts.Count > 0
                ? string.Join("  ·  ", parts)
                : (_vendor ? "Vendor record" : "Customer record");
        }

        private bool SaveRecord()
        {
            if (!AppLock.HasFolder())
            {
                MessageBox.Show("Select a data folder in Settings first.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string code = _code.Text.Trim();
            string name = _name.Text.Trim();
            if (code.Length == 0 || name.Length == 0)
            {
                MessageBox.Show("Enter a code and a name.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            string baseName = _vendor ? DataFiles.Vendors : DataFiles.Customers;
            bool exists = DataFiles.ReadRecords(baseName).Any(record =>
                DataFiles.GetRecord(record, "Code").Equals(code, StringComparison.OrdinalIgnoreCase));
            if (exists && !code.Equals(_originalCode, StringComparison.OrdinalIgnoreCase))
            {
                MessageBox.Show("That code is already in use.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return false;
            }

            var fields = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Code"] = code,
                ["Name"] = name,
                ["Company"] = _company.Text.Trim(),
                ["Phone"] = _phone.Text.Trim(),
                ["Current Balance"] = _balance.Text.Trim(),
                ["Notes"] = _notes.Text.Trim(),
                ["Description"] = _notes.Text.Trim(),
                ["Terms"] = _terms.Text.Trim()
            };

            if (_vendor)
            {
                fields["Type"] = _extra.Text.Trim();
                fields["Amount"] = _contact.Text.Trim();
            }
            else
            {
                fields["Credit Limit"] = _extra.Text.Trim();
                fields["Contact Name"] = _contact.Text.Trim();
                fields["Email"] = _email.Text.Trim();
                fields["Address"] = _address.Text.Trim();
            }

            try
            {
                if (_originalCode.Length > 0)
                {
                    bool updated = DataFiles.ReplaceMatchingRow(
                        baseName,
                        record => DataFiles.GetRecord(record, "Code")
                            .Equals(_originalCode, StringComparison.OrdinalIgnoreCase),
                        DataFiles.NamedRow(baseName, fields));
                    if (!updated)
                    {
                        MessageBox.Show("Could not find that record to update.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                        return false;
                    }
                }
                else
                {
                    DataFiles.AppendNamedRow(baseName, fields);
                }

                return true;
            }
            catch (Exception ex)
            {
                MessageBox.Show(ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                return false;
            }
        }

        private static string First(Dictionary<string, string> record, params string[] keys)
        {
            foreach (var key in keys)
            {
                string value = DataFiles.GetRecord(record, key).Trim();
                if (value.Length > 0)
                    return value;
            }

            return "";
        }

        private static Panel Spacer()
        {
            return new Panel
            {
                Dock = DockStyle.Top,
                Height = 12,
                BackColor = Theme.Cream
            };
        }

        private static CardPanel Section(string title, int height, int columns, out TableLayoutPanel grid)
        {
            var card = new CardPanel
            {
                Dock = DockStyle.Top,
                Height = height
            };
            var heading = new Label
            {
                Text = title,
                Font = Theme.SectionTitle,
                ForeColor = Theme.Navy,
                AutoSize = true,
                Location = new Point(20, 12)
            };
            grid = new TableLayoutPanel
            {
                ColumnCount = columns,
                RowCount = 1,
                Location = new Point(12, 42)
            };
            float share = 100f / Math.Max(1, columns);
            for (int i = 0; i < columns; i++)
                grid.ColumnStyles.Add(new ColumnStyle(SizeType.Percent, share));
            grid.RowStyles.Add(new RowStyle(SizeType.Percent, 100));
            card.Controls.Add(heading);
            card.Controls.Add(grid);
            var table = grid;
            card.Resize += (_, _) =>
            {
                table.Width = Math.Max(180, card.ClientSize.Width - 28);
                table.Height = Math.Max(40, card.ClientSize.Height - 54);
            };
            return card;
        }

        private static TextBox PutField(
            TableLayoutPanel grid,
            int col,
            int row,
            string caption,
            int colSpan = 1,
            bool multiline = false)
        {
            while (grid.RowCount <= row)
            {
                grid.RowStyles.Add(new RowStyle(SizeType.Percent, 50));
                grid.RowCount++;
            }

            var box = new TextBox
            {
                Multiline = multiline,
                ScrollBars = multiline ? ScrollBars.Vertical : ScrollBars.None
            };
            Theme.StyleField(box);
            var cell = new Panel
            {
                Dock = DockStyle.Fill,
                Margin = new Padding(8, 4, 8, 8)
            };
            var label = new Label
            {
                Text = caption,
                Dock = DockStyle.Top,
                Height = 18
            };
            Theme.StyleFieldLabel(label);
            box.Dock = multiline ? DockStyle.Fill : DockStyle.Top;
            if (!multiline)
                box.Height = 28;
            cell.Controls.Add(box);
            cell.Controls.Add(label);
            grid.Controls.Add(cell, col, row);
            if (colSpan > 1)
                grid.SetColumnSpan(cell, colSpan);
            return box;
        }
    }
}
