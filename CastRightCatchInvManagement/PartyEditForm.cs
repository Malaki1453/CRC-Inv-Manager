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

            Text = editing
                ? (vendor ? "Edit Vendor" : "Edit Customer")
                : (vendor ? "Add Vendor" : "Add Customer");
            StartPosition = FormStartPosition.CenterParent;
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MinimizeBox = false;
            MaximizeBox = false;
            ShowInTaskbar = false;
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(560, vendor ? 520 : 620);
            BackColor = Theme.Cream;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var card = new CardPanel
            {
                Dock = DockStyle.Fill,
                Padding = new Padding(20, 16, 20, 16)
            };

            int y = 16;
            _code = AddField(card, "CODE", 20, y, 160);
            _name = AddField(card, "NAME", 200, y, 300);
            y += 54;
            _company = AddField(card, "COMPANY", 20, y, 300);
            _phone = AddField(card, "PHONE", 340, y, 160);
            y += 54;
            _balance = AddField(card, "CURRENT BALANCE", 20, y, 160);
            _terms = AddField(card, "TERMS", 200, y, 140);
            _extra = AddField(card, vendor ? "TYPE" : "CREDIT LIMIT", 360, y, 140);
            y += 54;
            if (!vendor)
            {
                _contact = AddField(card, "CONTACT NAME", 20, y, 240);
                _email = AddField(card, "EMAIL", 280, y, 220);
                y += 54;
                _address = AddMultiline(card, "ADDRESS", 20, y, 480, 48);
                y += 78;
            }
            else
            {
                _contact = AddField(card, "AMOUNT", 20, y, 160);
                _email = new TextBox { Visible = false };
                _address = new TextBox { Visible = false };
                y += 54;
            }

            _notes = AddMultiline(card, "NOTES", 20, y, 480, 72);

            if (record != null)
            {
                _code.Text = DataFiles.GetRecord(record, "Code");
                _name.Text = DataFiles.GetRecord(record, "Name");
                _company.Text = First(record, "Company", "Name");
                _phone.Text = DataFiles.GetRecord(record, "Phone");
                _balance.Text = DataFiles.GetRecord(record, "Current Balance");
                _terms.Text = DataFiles.GetRecord(record, "Terms");
                _notes.Text = DataFiles.GetRecord(record, "Notes");
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

            var save = new Button
            {
                Text = editing ? "Save" : "Add",
                Size = new Size(110, 34),
                DialogResult = DialogResult.None
            };
            Theme.StyleGoldButton(save);
            save.Click += (_, _) =>
            {
                if (SaveRecord())
                    DialogResult = DialogResult.OK;
            };

            var cancel = new Button
            {
                Text = "Cancel",
                Size = new Size(90, 34),
                DialogResult = DialogResult.Cancel
            };
            Theme.StyleOutlineButton(cancel);
            CancelButton = cancel;

            var footer = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 56,
                BackColor = Theme.Cream
            };
            footer.Controls.Add(save);
            footer.Controls.Add(cancel);
            footer.Resize += (_, _) =>
            {
                save.Location = new Point(Math.Max(120, footer.Width - 230), 10);
                cancel.Location = new Point(Math.Max(20, footer.Width - 110), 10);
            };

            Controls.Add(card);
            Controls.Add(footer);
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

        private static TextBox AddField(Control parent, string caption, int x, int y, int width)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var box = new TextBox();
            Theme.StyleField(box);
            box.Location = new Point(x, y + 16);
            box.Size = new Size(width, 26);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }

        private static TextBox AddMultiline(Control parent, string caption, int x, int y, int width, int height)
        {
            var label = new Label { Text = caption };
            Theme.StyleFieldLabel(label);
            label.Location = new Point(x, y);
            var box = new TextBox
            {
                Multiline = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(x, y + 16),
                Size = new Size(width, height)
            };
            Theme.StyleField(box);
            parent.Controls.Add(label);
            parent.Controls.Add(box);
            return box;
        }
    }
}
