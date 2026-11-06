namespace CastRightCatchInvManagement
{
    /// <summary>Named warehouse addresses on New Customer. Empty rows are not saved.</summary>
    internal sealed class WarehouseListEditor : Panel
    {
        private readonly List<Row> _rows = new();
        private readonly Button _add;

        /// <summary>Fires after a row is added, removed, or the control is resized.</summary>
        public event EventHandler? Changed;

        /// <summary>Build one blank warehouse row and an add button.</summary>
        public WarehouseListEditor()
        {
            BackColor = Theme.Cream;
            _add = new Button
            {
                Text = "Add warehouse",
                Size = new Size(140, 28)
            };
            Theme.StyleOutlineButton(_add);
            _add.Click += (_, _) =>
            {
                AddRow("", "");
                Relayout();
            };
            Controls.Add(_add);
            AddRow("", "");
            Relayout();
        }

        /// <summary>Replace the rows. Always leaves one blank row so they can type immediately.</summary>
        public void Set(IEnumerable<DataFiles.CustomerWarehouse> warehouses)
        {
            foreach (var row in _rows.ToArray())
                RemoveRow(row, relayout: false);
            foreach (var item in warehouses)
            {
                if (item.Address.Trim().Length == 0)
                    continue;
                AddRow(item.Name, item.Address);
            }

            if (_rows.Count == 0)
                AddRow("", "");
            Relayout();
        }

        /// <summary>Named warehouses with an address. Blank rows are skipped.</summary>
        public List<DataFiles.CustomerWarehouse> Collect()
        {
            var list = new List<DataFiles.CustomerWarehouse>();
            foreach (var row in _rows)
            {
                string address = row.Address.Text.Trim();
                if (address.Length == 0)
                    continue;
                list.Add(new DataFiles.CustomerWarehouse(row.Name.Text.Trim(), address));
            }

            return list;
        }

        /// <summary>Stretch row widths when the parent card is resized.</summary>
        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            Relayout();
        }

        /// <summary>One name + address + remove row at the bottom of the list.</summary>
        private void AddRow(string name, string address)
        {
            var row = new Row();
            row.Name.Text = name;
            row.Address.Text = address;
            row.Remove.Click += (_, _) => RemoveRow(row, relayout: true);
            _rows.Add(row);
            Controls.Add(row.Host);
        }

        /// <summary>Drop a row. Keep one blank row so the list is never empty.</summary>
        private void RemoveRow(Row row, bool relayout)
        {
            _rows.Remove(row);
            Controls.Remove(row.Host);
            row.Host.Dispose();
            if (_rows.Count == 0)
                AddRow("", "");
            if (relayout)
                Relayout();
        }

        /// <summary>Stack rows then the add button and grow this panel to fit.</summary>
        private void Relayout()
        {
            const int rowH = 56;
            int width = Math.Max(240, Width);
            int y = 0;
            foreach (var row in _rows)
            {
                row.Host.SetBounds(0, y, width, rowH);
                row.LayoutFields(width);
                y += rowH + 8;
            }

            _add.Location = new Point(0, y);
            Height = _add.Bottom;
            Changed?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>One warehouse line: name, address, remove.</summary>
        private sealed class Row
        {
            public Panel Host { get; } = new() { BackColor = Theme.Cream };
            public TextBox Name { get; }
            public TextBox Address { get; }
            public Button Remove { get; }

            public Row()
            {
                Name = new TextBox { PlaceholderText = "Name" };
                Address = new TextBox
                {
                    Multiline = true,
                    AcceptsReturn = true,
                    PlaceholderText = "Street, city, state, ZIP"
                };
                Remove = new Button { Text = "Remove", Size = new Size(80, 26) };
                Theme.StyleField(Name);
                Theme.StyleField(Address);
                Theme.BindAutoVerticalScroll(Address);
                Theme.StyleOutlineButton(Remove);
                Host.Controls.Add(Name);
                Host.Controls.Add(Address);
                Host.Controls.Add(Remove);
            }

            /// <summary>Name on the left, remove on the right, address fills the middle.</summary>
            public void LayoutFields(int width)
            {
                Name.SetBounds(0, 14, 150, 26);
                Remove.Location = new Point(Math.Max(240, width - 80), 14);
                int addressLeft = 158;
                int addressWidth = Math.Max(80, Remove.Left - addressLeft - 8);
                Address.SetBounds(addressLeft, 0, addressWidth, 52);
            }
        }
    }
}
