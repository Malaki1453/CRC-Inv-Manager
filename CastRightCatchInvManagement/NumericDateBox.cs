using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Date field that only accepts digits and keeps MM/DD/YYYY when the date is real.</summary>
    internal sealed class NumericDateBox : TextBox
    {
        private string _digits = "";
        private bool _formatting;
        private DateTime? _value;

        public event EventHandler? DateChanged;

        public DateTime? Value => _value;

        public NumericDateBox()
        {
            MaxLength = 10;
            PlaceholderText = "MM/DD/YYYY";
            Theme.StyleField(this);
            Font = Theme.Small;
        }

        public void ClearDate()
        {
            _digits = "";
            _value = null;
            Render();
            DateChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
                return;
            if (!char.IsDigit(e.KeyChar))
            {
                e.Handled = true;
                return;
            }

            e.Handled = true;
            if (_digits.Length >= 8)
                return;
            AcceptDigits(_digits + e.KeyChar);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                if (_digits.Length > 0)
                    AcceptDigits(_digits[..^1]);
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                AcceptDigits("");
                return;
            }

            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                string pasted = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                AcceptDigits(Digits(pasted));
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (_formatting)
                return;
            AcceptDigits(Digits(Text));
        }

        private void AcceptDigits(string digits)
        {
            if (digits.Length > 8)
                digits = digits[..8];
            _digits = digits;
            _value = Parse(_digits);
            ForeColor = _digits.Length == 8 && _value == null ? Theme.Danger : Theme.Ink;
            Render();
            DateChanged?.Invoke(this, EventArgs.Empty);
        }

        private void Render()
        {
            string shown = Format(_digits);
            if (Text == shown)
                return;
            _formatting = true;
            int caret = shown.Length;
            Text = shown;
            SelectionStart = caret;
            _formatting = false;
        }

        private static string Format(string digits)
        {
            if (digits.Length <= 2)
                return digits;
            if (digits.Length <= 4)
                return digits[..2] + "/" + digits[2..];
            return digits[..2] + "/" + digits[2..4] + "/" + digits[4..];
        }

        private static DateTime? Parse(string digits)
        {
            if (digits.Length != 8)
                return null;
            if (!int.TryParse(digits[..2], out int month) ||
                !int.TryParse(digits[2..4], out int day) ||
                !int.TryParse(digits[4..], out int year))
                return null;
            if (year < 1900 || year > 2100)
                return null;
            try
            {
                return new DateTime(year, month, day);
            }
            catch
            {
                return null;
            }
        }

        public static bool TryParseCell(string? text, out DateTime date)
        {
            text = (text ?? "").Trim();
            date = default;
            if (text.Length == 0)
                return false;
            string[] formats =
            {
                "yyyy-MM-dd", "MM/dd/yyyy", "M/d/yyyy", "MM-dd-yyyy",
                "yyyy-MM-dd HH:mm", "yyyy-MM-dd HH:mm:ss"
            };
            if (DateTime.TryParseExact(
                    text,
                    formats,
                    CultureInfo.InvariantCulture,
                    DateTimeStyles.None,
                    out date))
            {
                date = date.Date;
                return true;
            }

            if (DateTime.TryParse(text, CultureInfo.CurrentCulture, DateTimeStyles.None, out date))
            {
                date = date.Date;
                return true;
            }

            return false;
        }

        public static bool InRange(DateTime date, DateTime? from, DateTime? to)
        {
            if (from != null && date < from.Value.Date)
                return false;
            if (to != null && date > to.Value.Date)
                return false;
            return true;
        }

        private static string Digits(string text)
        {
            var chars = (text ?? "").Where(char.IsDigit).ToArray();
            return new string(chars);
        }
    }
}
