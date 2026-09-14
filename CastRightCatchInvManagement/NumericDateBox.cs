using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Date field for MM/DD/YYYY. Accepts MM/DD/YY, M/D/YY (6/1/26), and . in place of /.
    /// A two-digit year becomes four digits when the box is left.
    /// </summary>
    internal sealed class NumericDateBox : TextBox
    {
        private string _month = "";
        private string _day = "";
        private string _year = "";
        private bool _sepMonth;
        private bool _sepDay;
        private int _part;
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
            ResetParts();
            _value = null;
            Render();
            DateChanged?.Invoke(this, EventArgs.Empty);
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            if (char.IsControl(e.KeyChar))
                return;

            e.Handled = true;
            if (SelectionLength == Text.Length && Text.Length > 0)
                ResetParts();

            if (e.KeyChar is '.' or '/')
            {
                PlaceSeparator();
                return;
            }

            if (!char.IsDigit(e.KeyChar))
                return;

            AcceptDigit(e.KeyChar);
        }

        protected override void OnKeyDown(KeyEventArgs e)
        {
            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                Backspace();
                return;
            }

            if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                ClearDate();
                return;
            }

            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                string pasted = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                ReadText(pasted);
            }
        }

        protected override void OnTextChanged(EventArgs e)
        {
            if (_formatting)
                return;
            ReadText(Text);
        }

        protected override void OnLeave(EventArgs e)
        {
            SnapToFullDate();
            base.OnLeave(e);
        }

        private void AcceptDigit(char digit)
        {
            if (_part == 0)
            {
                if (_month.Length >= 2)
                    _part = 1;
                else
                {
                    _month += digit;
                    if (_month.Length == 2)
                        _part = 1;
                    Apply();
                    return;
                }
            }

            if (_part == 1)
            {
                if (_day.Length >= 2)
                    _part = 2;
                else
                {
                    _day += digit;
                    if (_day.Length == 2)
                        _part = 2;
                    Apply();
                    return;
                }
            }

            if (_year.Length >= 4)
                return;
            _year += digit;
            Apply();
        }

        private void PlaceSeparator()
        {
            if (_part == 0)
            {
                if (_month.Length == 0)
                    return;
                _sepMonth = true;
                _part = 1;
                Apply();
                return;
            }

            if (_part == 1)
            {
                if (_day.Length == 0)
                {
                    _sepMonth = true;
                    Apply();
                    return;
                }

                _sepDay = true;
                _part = 2;
                Apply();
                return;
            }

            if (_year.Length == 0 && _day.Length > 0)
            {
                _sepDay = true;
                Apply();
            }
        }

        private void Backspace()
        {
            if (_year.Length > 0)
            {
                _year = _year[..^1];
                _part = 2;
                Apply();
                return;
            }

            if (_sepDay)
            {
                _sepDay = false;
                _part = 1;
                Apply();
                return;
            }

            if (_day.Length > 0)
            {
                _day = _day[..^1];
                _part = 1;
                Apply();
                return;
            }

            if (_sepMonth)
            {
                _sepMonth = false;
                _part = 0;
                Apply();
                return;
            }

            if (_month.Length > 0)
            {
                _month = _month[..^1];
                _part = 0;
                Apply();
            }
        }

        private void ReadText(string text)
        {
            ResetParts();
            text = (text ?? "").Trim();
            if (text.Length == 0)
            {
                Apply();
                return;
            }

            char[] marks = { '/', '.', '-' };
            if (text.IndexOfAny(marks) < 0)
            {
                string digits = Digits(text);
                if (digits.Length > 8)
                    digits = digits[..8];
                if (digits.Length <= 2)
                    _month = digits;
                else if (digits.Length <= 4)
                {
                    _month = digits[..2];
                    _day = digits[2..];
                }
                else
                {
                    _month = digits[..2];
                    _day = digits[2..4];
                    _year = digits[4..];
                }
            }
            else
            {
                string[] parts = text.Split(marks);
                if (parts.Length > 0)
                    _month = TakeDigits(parts[0], 2);
                if (parts.Length > 1)
                {
                    _sepMonth = true;
                    _day = TakeDigits(parts[1], 2);
                }

                if (parts.Length > 2)
                {
                    _sepDay = true;
                    _year = TakeDigits(parts[2], 4);
                }
            }

            if (_year.Length > 0 || _sepDay)
                _part = 2;
            else if (_day.Length > 0 || _sepMonth || _month.Length == 2)
                _part = 1;
            else
                _part = 0;
            Apply();
        }

        private void SnapToFullDate()
        {
            if (_value is not DateTime date)
                return;
            _month = date.Month.ToString("00", CultureInfo.InvariantCulture);
            _day = date.Day.ToString("00", CultureInfo.InvariantCulture);
            _year = date.Year.ToString("0000", CultureInfo.InvariantCulture);
            _sepMonth = true;
            _sepDay = true;
            _part = 2;
            Render();
        }

        private void Apply()
        {
            _value = TryBuild();
            ForeColor = PartsLookComplete() && _value == null ? Theme.Danger : Theme.Ink;
            Render();
            DateChanged?.Invoke(this, EventArgs.Empty);
        }

        private DateTime? TryBuild()
        {
            if (!PartsLookComplete())
                return null;
            if (!int.TryParse(_month, out int month) ||
                !int.TryParse(_day, out int day) ||
                !int.TryParse(_year, out int year))
                return null;
            year = ExpandYear(year, _year.Length);
            try
            {
                return new DateTime(year, month, day);
            }
            catch
            {
                return null;
            }
        }

        private bool PartsLookComplete() =>
            _month.Length >= 1 && _day.Length >= 1 && (_year.Length == 2 || _year.Length == 4);

        private void Render()
        {
            string shown = FormatParts();
            if (Text == shown)
                return;
            _formatting = true;
            Text = shown;
            SelectionStart = shown.Length;
            _formatting = false;
        }

        private string FormatParts()
        {
            string shown = _month;
            if (_sepMonth || _day.Length > 0 || _year.Length > 0)
                shown += "/" + _day;
            if (_sepDay || _year.Length > 0)
                shown += "/" + _year;
            return shown;
        }

        private void ResetParts()
        {
            _month = "";
            _day = "";
            _year = "";
            _sepMonth = false;
            _sepDay = false;
            _part = 0;
        }

        public static bool TryParseCell(string? text, out DateTime date)
        {
            text = (text ?? "").Trim();
            date = default;
            if (text.Length == 0)
                return false;
            if (TryParseFlexible(text, out date))
                return true;
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

        private static bool TryParseFlexible(string text, out DateTime date)
        {
            date = default;
            char[] marks = { '/', '.', '-' };
            if (text.IndexOfAny(marks) >= 0)
            {
                string[] parts = text.Split(marks, StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length != 3)
                    return false;
                if (!int.TryParse(parts[0], out int a) ||
                    !int.TryParse(parts[1], out int b) ||
                    !int.TryParse(parts[2], out int c))
                    return false;
                if (parts[0].Length == 4)
                    return TryMake(b, c, cYear: a, out date);
                return TryMake(a, b, ExpandYear(c, parts[2].Length), out date);
            }

            string digits = Digits(text);
            if (digits.Length == 8)
            {
                return TryMake(
                    int.Parse(digits[..2], CultureInfo.InvariantCulture),
                    int.Parse(digits[2..4], CultureInfo.InvariantCulture),
                    int.Parse(digits[4..], CultureInfo.InvariantCulture),
                    out date);
            }

            if (digits.Length == 6)
            {
                return TryMake(
                    int.Parse(digits[..2], CultureInfo.InvariantCulture),
                    int.Parse(digits[2..4], CultureInfo.InvariantCulture),
                    ExpandYear(int.Parse(digits[4..], CultureInfo.InvariantCulture), 2),
                    out date);
            }

            return false;
        }

        private static bool TryMake(int month, int day, int cYear, out DateTime date)
        {
            date = default;
            try
            {
                date = new DateTime(cYear, month, day);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private static int ExpandYear(int year, int digitCount)
        {
            if (digitCount != 2)
                return year;
            return CultureInfo.CurrentCulture.Calendar.ToFourDigitYear(year);
        }

        private static string TakeDigits(string text, int max)
        {
            string digits = Digits(text);
            return digits.Length <= max ? digits : digits[..max];
        }

        private static string Digits(string text)
        {
            var chars = (text ?? "").Where(char.IsDigit).ToArray();
            return new string(chars);
        }
    }
}
