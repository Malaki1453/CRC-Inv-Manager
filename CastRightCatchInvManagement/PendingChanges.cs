namespace CastRightCatchInvManagement
{
    /// <summary>Review queued add / edit / delete requests and accept them into the live tables.</summary>
    public class PendingChanges : Form, INavigationPage
    {
        private DataGridView _grid = null!;
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

        /// <summary>Lay out the review grid. Accept and Reject are on the details popup.</summary>
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
                Text = "Double-click a request to see each field. Changed fields show old value → new value, with Accept and Reject on that window. Shift-click and right-click to accept or reject more than one. Purchase and sales deletes stay hidden until an administrator reviews them.",
                Font = Theme.Body,
                ForeColor = Theme.Muted,
                Dock = DockStyle.Top,
                Height = 44
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

        /// <summary>Right-click Accept / Reject for the current selection.</summary>
        private void ShowReviewMenu()
        {
            bool many = SelectedGroups().Count > 1;
            UiStyle.RowContextMenuShown = true;
            var menu = new ContextMenuStrip();
            menu.Items.Add(many ? "Accept all" : "Accept", null, (_, _) => Review(accept: true));
            menu.Items.Add(many ? "Reject all" : "Reject", null, (_, _) => Review(accept: false));
            menu.Show(_grid, _grid.PointToClient(Control.MousePosition));
        }

        /// <summary>Open field-by-field review with Accept and Reject. Changed values show old → new.</summary>
        private void OpenDetails(List<Dictionary<string, string>> members)
        {
            if (ReviewDetailsForm.ShowReview(this, members))
                LoadTable();
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

                try
                {
                    if (!DataFiles.ReviewPendingGroup(group, accept))
                        ok = false;
                    else
                        done++;
                }
                catch (Exception ex)
                {
                    ok = false;
                    ToastAlert.Error(this, ex.Message);
                }
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
