namespace CastRightCatchInvManagement
{
    /// <summary>Review queued add / edit / delete requests and accept them into the live tables.</summary>
    public class PendingChanges : Form, INavigationPage
    {
        private DataGridView _grid = null!;
        private Button _accept = null!;
        private Button _reject = null!;
        private readonly List<List<Dictionary<string, string>>> _groups = new();

        /// <summary>Build the Review page and load waiting add/edit/delete requests.</summary>
        public PendingChanges()
        {
            Text = "Review";
            UiStyle.ApplyChildPage(this);
            Padding = new Padding(28, 16, 28, 24);
            BuildUi();
            DataFiles.DataChanged += LoadTable;
            LoadTable();
        }

        /// <summary>Reload queued changes when this page is shown.</summary>
        public void HighlightCurrentPage() => LoadTable();

        /// <summary>Lay out the review grid and Accept / Reject actions.</summary>
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
                Text = "Shift-click to select more than one. Right-click for Accept or Reject. Purchase and sales deletes stay hidden until an administrator reviews them.",
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
            _accept = new Button { Text = "Accept", Size = new Size(130, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Theme.StyleGoldButton(_accept);
            _accept.Click += (_, _) => Review(accept: true);
            _reject = new Button { Text = "Reject", Size = new Size(130, 34), Anchor = AnchorStyles.Right | AnchorStyles.Top };
            Theme.StyleNavyButton(_reject);
            _reject.Click += (_, _) => Review(accept: false);
            actions.Controls.Add(_accept);
            actions.Controls.Add(_reject);
            actions.Resize += (_, _) =>
            {
                _accept.Location = new Point(Math.Max(150, actions.Width - 150), 8);
                _reject.Location = new Point(Math.Max(8, actions.Width - 290), 8);
            };

            _grid = new DataGridView { Dock = DockStyle.Fill };
            Theme.StyleGrid(_grid);
            _grid.ReadOnly = true;
            _grid.AllowUserToAddRows = false;
            _grid.MultiSelect = true;
            _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
            _grid.AutoGenerateColumns = false;
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "When", HeaderText = "Requested", FillWeight = 18 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Who", HeaderText = "User", FillWeight = 16 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "Act", HeaderText = "Action", FillWeight = 12 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "What", HeaderText = "Changes (old → new)", FillWeight = 48 });
            _grid.Columns.Add(new DataGridViewTextBoxColumn { Name = "State", HeaderText = "Status", FillWeight = 14 });
            _grid.AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill;
            _grid.SelectionChanged += (_, _) => SyncReviewActions();
            _grid.CellDoubleClick += (_, e) =>
            {
                // Header clicks and stale indexes have no pending row to open.
                if (e.RowIndex < 0 || e.RowIndex >= _groups.Count)
                    return;
                OpenDetails(_groups[e.RowIndex]);
            };
            _grid.CellMouseClick += (_, e) =>
            {
                if (e.Button != MouseButtons.Right || e.RowIndex < 0 || e.RowIndex >= _groups.Count)
                    return;
                if (!_grid.Rows[e.RowIndex].Selected)
                {
                    _grid.ClearSelection();
                    _grid.Rows[e.RowIndex].Selected = true;
                }

                ShowReviewMenu();
            };

            var card = new CardPanel { Dock = DockStyle.Fill, Padding = new Padding(1) };
            card.Controls.Add(_grid);

            Controls.Add(card);
            Controls.Add(actions);
            Controls.Add(intro);
            Controls.Add(title);
        }

        /// <summary>Show only requests still waiting for Accept or Reject.</summary>
        private void LoadTable()
        {
            GridLoadHost.Run(
                _grid,
                token =>
                {
                    var rows = new List<Dictionary<string, string>>();
                    foreach (var record in DataFiles.ReadRecords(DataFiles.PendingChanges))
                    {
                        token.ThrowIfCancellationRequested();
                        // Already decided rows stay in history but not on this queue.
                        if (!DataFiles.GetRecord(record, "Status").Equals("pending", StringComparison.OrdinalIgnoreCase))
                            continue;
                        // Only administrators review purchase/sales deletes.
                        if (DataFiles.GetRecord(record, "Action")
                                .Equals("delete", StringComparison.OrdinalIgnoreCase) &&
                            !AppState.IsAdmin)
                            continue;
                        rows.Add(record);
                    }

                    return DataFiles.GroupPendingReviews(rows);
                },
                groups =>
                {
                    _grid.MultiSelect = true;
                    _grid.SelectionMode = DataGridViewSelectionMode.FullRowSelect;
                    _groups.Clear();
                    _grid.Rows.Clear();
                    foreach (var members in groups)
                    {
                        _groups.Add(members);
                        var record = members[0];
                        _grid.Rows.Add(
                            DataFiles.GetRecord(record, "Requested At"),
                            DataFiles.GetRecord(record, "Requested By"),
                            DataFiles.GetRecord(record, "Action"),
                            DataFiles.PendingGroupSummary(members),
                            DataFiles.GetRecord(record, "Status"));
                    }

                    SyncReviewActions();
                });
        }

        /// <summary>Every selected review group (Shift-click can select more than one).</summary>
        private List<List<Dictionary<string, string>>> SelectedGroups()
        {
            var list = new List<List<Dictionary<string, string>>>();
            foreach (DataGridViewRow row in _grid.SelectedRows)
            {
                if (row.Index < 0 || row.Index >= _groups.Count)
                    continue;
                list.Add(_groups[row.Index]);
            }

            if (list.Count == 0 && _grid.CurrentRow != null)
            {
                int index = _grid.CurrentRow.Index;
                if (index >= 0 && index < _groups.Count)
                    list.Add(_groups[index]);
            }

            return list;
        }

        /// <summary>Accept all / Reject all only when more than one review row is selected.</summary>
        private void SyncReviewActions()
        {
            bool many = SelectedGroups().Count > 1;
            _accept.Text = many ? "Accept all" : "Accept";
            _reject.Text = many ? "Reject all" : "Reject";
        }

        /// <summary>Right-click Accept / Reject for the current selection.</summary>
        private void ShowReviewMenu()
        {
            SyncReviewActions();
            bool many = SelectedGroups().Count > 1;
            UiStyle.RowContextMenuShown = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add(many ? "Accept all" : "Accept", null, (_, _) => Review(accept: true));
            menu.Items.Add(many ? "Reject all" : "Reject", null, (_, _) => Review(accept: false));
            menu.Show(_grid, _grid.PointToClient(Control.MousePosition));
        }

        /// <summary>Open the whole purchase or sales order, including hidden waiting-delete lines.</summary>
        private void OpenDetails(List<Dictionary<string, string>> members)
        {
            var sample = DataFiles.PendingOrderSample(members);
            string table = DataFiles.GetRecord(members[0], "Table");
            string title = table.Equals(DataFiles.Sales, StringComparison.OrdinalIgnoreCase)
                ? "Sale"
                : table.Equals(DataFiles.PurchaseSales, StringComparison.OrdinalIgnoreCase)
                    ? "Purchase"
                    : table.Equals(DataFiles.Customers, StringComparison.OrdinalIgnoreCase)
                        ? "Customer"
                        : table.Equals(DataFiles.Vendors, StringComparison.OrdinalIgnoreCase)
                            ? "Vendor"
                            : table.Equals(DataFiles.ItemCodes, StringComparison.OrdinalIgnoreCase)
                                ? "Inventory"
                                : "Pending change";
            RecordDetailsForm.ShowRecord(this, title, sample, table, includeHidden: true);
        }

        /// <summary>Accept the selected request into live tables, or reject and undo it.</summary>
        private void Review(bool accept)
        {
            var selected = SelectedGroups();
            // Accept/Reject need a selected request so we do not guess.
            if (selected.Count == 0)
            {
                ToastAlert.Error(this, "Select a pending change first.");
                return;
            }

            int done = 0;
            int skipped = 0;
            bool ok = true;
            foreach (var group in selected)
            {
                if (group.Count == 0)
                    continue;
                var record = group[0];
                string table = DataFiles.GetRecord(record, "Table");
                if (DataFiles.GetRecord(record, "Action").Equals("delete", StringComparison.OrdinalIgnoreCase) &&
                    DataAccess.IsTradeTable(table) &&
                    !AppState.IsAdmin)
                {
                    skipped++;
                    continue;
                }

                if (!DataFiles.ReviewPendingGroup(group, accept))
                    ok = false;
                else
                    done++;
            }

            if (done == 0)
            {
                ToastAlert.Error(this, skipped > 0
                    ? "An administrator must review purchase and sales deletes."
                    : "Could not update that request.");
                return;
            }

            string verb = accept ? "accepted" : "rejected";
            ToastAlert.Success(this, done == 1
                ? "The change was " + verb + "."
                : done + " changes were " + verb + ".");
            if (!ok)
                ToastAlert.Error(this, "Some requests could not be updated.");
            LoadTable();
        }
    }
}
