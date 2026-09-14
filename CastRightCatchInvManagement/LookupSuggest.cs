namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Suggestion list under a plain text box. The list shows "code - name" or "name - code";
    /// picking one writes only the matching field value into the box via <see cref="Picked"/>.
    /// </summary>
    internal sealed class LookupSuggest : IDisposable
    {
        public readonly record struct Hit(string Code, string Name, string Extra, string Search = "")
        {
            public string DisplayName => Name.Length > 0 ? Name : Code;

            public string Label(bool codeFirst)
            {
                if (Code.Length == 0)
                    return DisplayName;
                if (DisplayName.Length == 0)
                    return Code;
                return codeFirst ? Code + " - " + DisplayName : DisplayName + " - " + Code;
            }

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
            _box.KeyDown += OnKeyDown;
            _box.Leave += (_, _) =>
            {
                if (ListHasMouse())
                    return;
                Hide();
            };
            _list.MouseDown += (_, e) =>
            {
                int index = _list.IndexFromPoint(e.Location);
                if (index >= 0)
                    _list.SelectedIndex = index;
                PickSelected();
            };
            _list.LostFocus += (_, _) =>
            {
                if (!_box.Focused)
                    Hide();
            };
        }

        public void Dispose()
        {
            Hide();
            _list.Dispose();
        }

        private void Filter()
        {
            if (_applying || !_box.Focused)
                return;

            string needle = _box.Text.Trim();
            if (needle.Length == 0)
            {
                Hide();
                return;
            }

            _hits.Clear();
            foreach (var hit in _source())
            {
                if (hit.Matches(needle))
                    _hits.Add(hit);
                if (_hits.Count >= 12)
                    break;
            }

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

        private static int Rank(Hit hit, string needle)
        {
            if (hit.Code.Equals(needle, StringComparison.OrdinalIgnoreCase) ||
                hit.DisplayName.Equals(needle, StringComparison.OrdinalIgnoreCase))
                return 0;
            if (hit.Code.StartsWith(needle, StringComparison.OrdinalIgnoreCase) ||
                hit.DisplayName.StartsWith(needle, StringComparison.OrdinalIgnoreCase))
                return 1;
            return 2;
        }

        private void OnKeyDown(object? sender, KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Escape)
            {
                Hide();
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Down)
            {
                if (!_list.Visible)
                    Filter();
                if (_list.Items.Count > 0)
                    _list.SelectedIndex = Math.Min(_list.Items.Count - 1, _list.SelectedIndex + 1);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Up)
            {
                if (_list.Visible && _list.Items.Count > 0)
                    _list.SelectedIndex = Math.Max(0, _list.SelectedIndex - 1);
                e.SuppressKeyPress = true;
                return;
            }

            if (e.KeyCode == Keys.Enter && _list.Visible)
            {
                PickSelected();
                e.SuppressKeyPress = true;
            }
        }

        private void PickSelected()
        {
            int index = _list.SelectedIndex;
            if (index < 0 || index >= _hits.Count)
                return;

            var hit = _hits[index];
            _applying = true;
            Hide();
            _picked(hit);
            _applying = false;
        }

        private void ShowList()
        {
            var host = _box.FindForm();
            if (host == null)
                return;

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
            if (y + height > host.ClientSize.Height)
                y = Math.Max(0, local.Y - _box.Height - height);
            _list.SetBounds(x, y, width, height);
            _list.Visible = true;
            _list.BringToFront();
        }

        private void Hide()
        {
            _list.Visible = false;
            _list.Items.Clear();
            _hits.Clear();
        }

        private bool ListHasMouse()
        {
            if (!_list.Visible)
                return false;
            return _list.ClientRectangle.Contains(_list.PointToClient(Control.MousePosition));
        }
    }
}
