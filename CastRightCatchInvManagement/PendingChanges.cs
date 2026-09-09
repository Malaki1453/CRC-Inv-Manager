namespace CastRightCatchInvManagement
{
    /// <summary>Review queued add / edit / delete requests and accept them into the live tables.</summary>
    public class PendingChanges : Form, INavigationPage
    {
        private DataGridView _grid = null!;
        private readonly List<Dictionary<string, string>> _rows = new();

        public PendingChanges()
        {
            Text = "Review";
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);
            BuildUi();
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        public void HighlightCurrentPage() => LoadTable();

        private void BuildUi()
        {
            var title = new Label
            {
                Text = "Review",
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                Dock = DockStyle.Top,
                Height = 40
            };
            var intro = new Label
            {
                Text = "Waiting rows stay in their tables with Record Status until you Accept (Live, or delete) or Reject (undo).",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 28
            };

            var actions = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 52,
                BackColor = Theme.Cream
            };
            var accept = new Button { Text = "Accept", Size = new Size(120, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Theme.StyleGoldButton(accept);
            accept.Click += (_, _) => Review(accept: true);
            var reject = new Button { Text = "Reject", Size = new Size(110, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Theme.StyleNavyButton(reject);
            reject.Click += (_, _) => Review(accept: false);
            actions.Controls.Add(accept);
            actions.Controls.Add(reject);
            actions.Resize += (_, _) =>
            {
                accept.Location = new Point(Math.Max(140, actions.Width - 140), 8);
                reject.Location = new Point(Math.Max(8, actions.Width - 260), 8);
            };

            _grid = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(_grid);
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.MultiSelect = false;
            _grid.AutoGenerateColumns = false;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "When", HeaderText = "Requested", FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Who", HeaderText = "User", FillWeight = 16 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Act", HeaderText = "Action", FillWeight = 12 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "What", HeaderText = "Summary", FillWeight = 40 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Status", FillWeight = 14 });
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.CellDoubleClick += (_, e) =>
            {
                if (e.RowIndex < 0 || e.RowIndex >= _rows.Count)
                    return;
                RecordDetailsForm.ShowRecord(this, "Pending change", _rows[e.RowIndex]);
            };

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            card.Controls.Add(_grid);

            Controls.Add(card);
            Controls.Add(actions);
            Controls.Add(intro);
            Controls.Add(title);
        }

        private void LoadTable()
        {
            _rows.Clear();
            _grid.Rows.Clear();
            foreach (var record in DataFiles.ReadRecords(DataFiles.PendingChanges))
            {
                if (!DataFiles.GetRecord(record, "Status").Equals("pending", StringComparison.OrdinalIgnoreCase))
                    continue;
                _rows.Add(record);
                _grid.Rows.Add(
                    DataFiles.GetRecord(record, "Requested At"),
                    DataFiles.GetRecord(record, "Requested By"),
                    DataFiles.GetRecord(record, "Action"),
                    DataFiles.GetRecord(record, "Summary"),
                    DataFiles.GetRecord(record, "Status"));
            }
        }

        private Dictionary<string, string>? Selected()
        {
            if (_grid.CurrentRow == null)
                return null;
            int index = _grid.CurrentRow.Index;
            return index >= 0 && index < _rows.Count ? _rows[index] : null;
        }

        private void Review(bool accept)
        {
            var record = Selected();
            if (record == null)
            {
                ToastAlert.Error(this, "Select a pending change first.");
                return;
            }

            bool ok = accept ? DataFiles.AcceptPending(record) : DataFiles.RejectPending(record);
            if (!ok)
            {
                ToastAlert.Error(this, "Could not update that request.");
                return;
            }

            ToastAlert.Success(this, accept ? "The change was accepted." : "The change was rejected.");
            LoadTable();
        }
    }
}
