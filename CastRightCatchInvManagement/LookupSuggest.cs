namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Suggestion list under a plain text box. The list shows "code - name" or "name - code";
    /// picking one writes only the matching field value into the box via <see cref="Picked"/>.
    /// </summary>
    internal sealed class LookupSuggest : IDisposable
    {
        /// <summary>One suggestion row: code, display name, extra label, and optional extra search text.</summary>
        public readonly record struct Hit(string Code, string Name, string Extra, string Search = "")
        {
            public string DisplayName => Name.Length > 0 ? Name : Code;

            /// <summary>Label shown in the list: code first or name first, skipping a blank side.</summary>
            public string Label(bool codeFirst)
            {
                // A missing code still shows the party or product name.
                if (Code.Length == 0)
                    return DisplayName;
                // A code-only row (no name) should not print a dangling dash.
                if (DisplayName.Length == 0)
                    return Code;
                return codeFirst ? Code + " - " + DisplayName : DisplayName + " - " + Code;
            }

            /// <summary>True when the typed text matches code, name, or extra search tokens.</summary>
            public bool Matches(string needle)
            {
                return Code.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                       DisplayName.Contains(needle, StringComparison.OrdinalIgnoreCase) ||
                       Search.Contains(needle, StringComparison.OrdinalIgnoreCase);
            }
        }

        private readonly TextBox _box;
        private readonly ListBox _list;
        private readonly Func<IReadOnlyList<Hit>> _source;
        private readonly bool _codeFirst;
        private readonly Action<Hit> _picked;
        private readonly int _minListWidth;
        private readonly List<Hit> _hits = new();
        private bool _applying;
        private bool _placed;

        /// <summary>Attach a popup list to <paramref name="box"/> that fills from <paramref name="source"/> as the user types.</summary>
        public LookupSuggest(
            TextBox box,
            Func<IReadOnlyList<Hit>> source,
            bool codeFirst,
            Action<Hit> picked,
            int minListWidth = 240)
        {
            _box = box;
            _source = source;
            _codeFirst = codeFirst;
            _picked = picked;
            _minListWidth = Math.Max(120, minListWidth);
            _list = new ListBox
            {
                Visible = false,
                TabStop = false,
                IntegralHeight = false,
                Font = Theme.Body,
                BackColor = Theme.Paper,
                ForeColor = Theme.Ink,
                BorderStyle = BorderStyle.FixedSingle,
                Height = 8
            };
            _box.TextChanged += (_, _) => Filter();
            _box.PreviewKeyDown += OnPreviewKeyDown;
            _box.KeyDown += OnKeyDown;
            _box.Leave += (_, _) =>
            {
                // Keep the list open while the mouse is over it so a click can pick a row.
                if (ListHasMouse())
                    return;
                Hide();
            };
            _list.MouseDown += (_, e) =>
            {
                int index = _list.IndexFromPoint(e.Location);
                // Clicking a row selects it before PickSelected reads SelectedIndex.
                if (index >= 0)
                    _list.SelectedIndex = index;
                PickSelected();
            };
            _list.LostFocus += (_, _) =>
            {
                // Closing only when both box and list lost focus avoids flicker during a pick.
                if (!_box.Focused)
                    Hide();
            };
        }

        /// <summary>Hide the list and dispose the popup control.</summary>
        public void Dispose()
        {
            Hide();
            _list.Dispose();
        }

        /// <summary>Rebuild the visible hits for the current box text, or hide when there are none.</summary>
        private void Filter()
        {
            // Ignore changes caused by applying a pick, and do not pop up when the box is not focused.
            if (_applying || !_box.Focused)
                return;

            string needle = _box.Text.Trim();
            // An empty box should not show every vendor/customer.
            if (needle.Length == 0)
            {
                Hide();
                return;
            }

            _hits.Clear();
            foreach (var hit in _source())
            {
                // needle is the typed box text; keep hits whose code, name, or extra search tokens contain it.
                if (hit.Matches(needle))
                    _hits.Add(hit);
                // Cap the list so typing a short letter does not flood the form.
                if (_hits.Count >= 12)
                    break;
            }

            // No close matches: hide rather than show an empty popup.
            if (_hits.Count == 0)
            {
                Hide();
                return;
            }

            _hits.Sort((a, b) => Rank(a, needle).CompareTo(Rank(b, needle)));
            _list.BeginUpdate();
            _list.Items.Clear();
            foreach (var hit in _hits)
                _list.Items.Add(hit.Label(_codeFirst));
            _list.EndUpdate();
            _list.SelectedIndex = 0;
            ShowList();
        }

        /// <summary>Sort key: exact, then prefix, then other substring matches.</summary>
        private static int Rank(Hit hit, string needle)
        {
            // Exact code or name should sit at the top of the list.
            if (hit.Code.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
                hit.DisplayName.Equals(needle, StringComparison.OrdinalIgnoreCase))
                return 0;
            // Prefix matches next so typing the start of a code still ranks high.
            if (hit.Code.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ||
                hit.DisplayName.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 2;
        }

        /// <summary>Tab is a dialog key; mark it as input so KeyDown can accept the suggestion.</summary>
        private void OnPreviewKeyDown(object? sender, PreviewKeyDownEventArgs e)
        {
            // Tab is normally a dialog key (next field). Mark it as input so KeyDown can accept the suggestion first.
            if (e.KeyCode == Keys.Tab)
                e.IsInputKey = true;
        }

        /// <summary>Arrow keys move the highlight; Enter or Tab picks; Escape closes without changing the box.</summary>
        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            // Escape dismisses suggestions without applying a hit.
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.SuppressKeyPress = true;
                return;
            }

            // Down opens the list if needed, then moves the highlight.
            if (e.KeyCode == Keys.Down)
            {
                // List is hidden: rebuild hits from the current box text before moving.
                if (!_list.Visible)
                    Filter();
                // Only bump SelectedIndex when there is at least one suggestion.
                if (_list.Items.Count > 0)
                    _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                e.SuppressKeyPress = true;
                return;
            }

            // Up only moves when the list is already showing.
            if (e.KeyCode == Keys.Up)
            {
                // Ignore Up when the popup is hidden or empty so the caret stays in the box.
                if (_list.Visible && _list.Items.Count > 0)
                    _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                e.SuppressKeyPress = true;
                return;
            }

            // Enter commits the highlighted suggestion into the box.
            if (e.KeyCode == Keys.Enter && _list.Visible)
            {
                PickSelected();
                e.SuppressKeyPress = true;
                return;
            }

            // Tab acts as Enter: accept the highlighted suggestion, then move to the next field.
            if (e.KeyCode == Keys.Tab)
            {
                // List is hidden: rebuild hits so Tab can still accept a match on the typed text.
                if (!_list.Visible)
                    Filter();
                // List is showing after that: pick the highlighted hit into the box (same as Enter).
                if (_list.Visible)
                    PickSelected();
                e.SuppressKeyPress = true;
                _box.FindForm()?.SelectNextControl(_box, !e.Shift, true, true, true);
            }
        }

        /// <summary>Apply the highlighted hit to the box and close the list.</summary>
        private void PickSelected()
        {
            int index = _list.SelectedIndex;
            // No highlight means the click or Enter should do nothing.
            if (index < 0 || index >= _hits.Count)
                return;

            var hit = _hits[index];
            _applying = true;
            Hide();
            _picked(hit);
            _applying = false;
        }

        /// <summary>Place the popup under the box, flipping above when it would clip the form.</summary>
        private void ShowList()
        {
            var host = _box.FindForm();
            // The list is parented on the form; skip if the box is not on a form yet.
            if (host == null)
                return;

            // Add the list once so later filters only move it.
            if (!_placed)
            {
                host.Controls.Add(_list);
                _placed = true;
            }

            Point screen = _box.PointToScreen(new Point(0, _box.Height));
            Point local = host.PointToClient(screen);
            int width = Math.Max(_box.Width, _minListWidth);
            int height = Math.Min(_hits.Count, 8) * (_list.ItemHeight + 2) + 4;
            int x = Math.Max(0, Math.Min(local.X, Math.Max(0, host.ClientSize.Width - width)));
            int y = local.Y;
            // Flip above the box when there is not enough room below.
            if (y + height > host.ClientSize.Height)
                y = Math.Max(0, local.Y - _box.Height - height);
            _list.SetBounds(x, y, width, height);
            _list.Visible = true;
            _list.BringToFront();
        }

        /// <summary>Hide the popup and drop cached hits.</summary>
        private void Hide()
        {
            _list.Visible = false;
            _list.Items.Clear();
            _hits.Clear();
        }

        /// <summary>True when the mouse is over the visible list, so Leave on the box should not hide it yet.</summary>
        private bool ListHasMouse()
        {
            // Hidden list cannot own the mouse, so Leave on the box should hide immediately.
            if (!_list.Visible)
                return false;
            return _list.ClientRectangle.Contains(_list.PointToClient(Control.MousePosition));
        }
    }
}
