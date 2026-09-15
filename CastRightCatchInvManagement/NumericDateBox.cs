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

        /// <summary>Style the box and cap length at MM/DD/YYYY.</summary>
        public NumericDateBox()
        {
            MaxLength = 10;
            PlaceholderText = "MM/DD/YYYY";
            Theme.StyleField(this);
            Font = Theme.Small;
        }

        /// <summary>Clear the typed date and notify listeners so filters drop this bound.</summary>
        public void ClearDate()
        {
            ResetParts();
            _value = null;
            Render();
            DateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Accept digits and / or . only; other keys are swallowed so letters cannot enter a date.</summary>
        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            // Let Backspace, Tab, and other control keys through.
            if (char.IsControl(e.KeyChar))
                return;

            e.Handled = true;
            // Replacing a full selection starts a new date instead of inserting into the old one.
            if (SelectionLength == Text.Length && Text.Length > 0)
                ResetParts();

            // Slash or period advances month → day → year.
            if (e.KeyChar is '.' or '/')
            {
                PlaceSeparator();
                return;
            }

            // Letters and symbols are not part of MM/DD/YYYY.
            if (!char.IsDigit(e.KeyChar))
                return;

            AcceptDigit(e.KeyChar);
        }

        /// <summary>Backspace edits parts; Delete clears; Ctrl+V parses a pasted date.</summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            // Backspace walks month/day/year rather than deleting raw text.
            if (e.KeyCode == Keys.Back)
            {
                e.SuppressKeyPress = true;
                Backspace();
                return;
            }

            // Delete wipes the whole date, matching a "clear this filter" gesture.
            if (e.KeyCode == Keys.Delete)
            {
                e.SuppressKeyPress = true;
                ClearDate();
                return;
            }

            // Paste must go through the same MM/DD/YYYY parser as typing.
            if (e.Control && e.KeyCode == Keys.V)
            {
                e.SuppressKeyPress = true;
                string pasted = Clipboard.ContainsText() ? Clipboard.GetText() : "";
                ReadText(pasted);
            }
        }

        /// <summary>Re-parse when Text is set from code, unless Render is writing the formatted value.</summary>
        protected override void OnTextChanged(EventArgs e)
        {
            // Ignore the Text assignment that Render itself just made.
            if (_formatting)
                return;
            ReadText(Text);
        }

        /// <summary>Expand a two-digit year and pad month/day when the user leaves the box.</summary>
        protected override void OnLeave(EventArgs e)
        {
            SnapToFullDate();
            base.OnLeave(e);
        }

        /// <summary>Append a digit to month, then day, then year, advancing parts at two digits.</summary>
        private void AcceptDigit(char digit)
        {
            if (_part == 0)
            {
                // Month already has two digits; treat this key as the start of the day.
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
                // Day already has two digits; treat this key as the start of the year.
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

            // Year is complete (YYYY); extra digits would make an invalid date.
            if (_year.Length >= 4)
                return;
            _year += digit;
            Apply();
        }

        /// <summary>Move from month to day, or day to year, once that part has at least one digit.</summary>
        private void PlaceSeparator()
        {
            if (_part == 0)
            {
                // A leading slash with no month is ignored.
                if (_month.Length == 0)
                    return;
                _sepMonth = true;
                _part = 1;
                Apply();
                return;
            }

            if (_part == 1)
            {
                // Extra slash before any day digit just keeps the month separator visible.
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

            // Slash after the day with no year yet still shows the second separator.
            if (_year.Length == 0 && _day.Length > 0)
            {
                _sepDay = true;
                Apply();
            }
        }

        /// <summary>Delete the last typed piece: year digits, then separators, then day, then month.</summary>
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

        /// <summary>Parse typed or pasted text into month/day/year parts, with or without separators.</summary>
        private void ReadText(string text)
        {
            ResetParts();
            text = (text ?? "").Trim();
            // Blank paste or clear leaves the box empty.
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

        /// <summary>When the date is valid, rewrite the box as MM/DD/YYYY so two-digit years become four.</summary>
        private void SnapToFullDate()
        {
            // Incomplete or invalid dates stay as typed so the user can finish them.
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

        /// <summary>Rebuild the DateTime, flag impossible dates in red, and refresh the displayed text.</summary>
        private void Apply()
        {
            _value = TryBuild();
            ForeColor = PartsLookComplete() && _value == null ? Theme.Danger : Theme.Ink;
            Render();
            DateChanged?.Invoke(this, EventArgs.Empty);
        }

        /// <summary>Build a DateTime when month, day, and a 2- or 4-digit year are present and valid.</summary>
        private DateTime? TryBuild()
        {
            // Partial typing (month only, etc.) is not yet a date.
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
                // 13/40/2026 and similar calendar-invalid values stay null and paint red.
                return null;
            }
        }

        /// <summary>True when month, day, and a 2- or 4-digit year have all been entered.</summary>
        private bool PartsLookComplete() =>
            _month.Length >= 1 && _day.Length >= 1 && (_year.Length == 2 || _year.Length == 4);

        /// <summary>Write the formatted parts back to Text without re-entering the parser.</summary>
        private void Render()
        {
            string shown = FormatParts();
            // Avoid a TextChanged loop when the display already matches.
            if (Text == shown)
                return;
            _formatting = true;
            Text = shown;
            SelectionStart = shown.Length;
            _formatting = false;
        }

        /// <summary>Join month/day/year with slashes only once those parts (or their separators) exist.</summary>
        private string FormatParts()
        {
            string shown = _month;
            if (_sepMonth || _day.Length > 0 || _year.Length > 0)
                shown += "/" + _day;
            if (_sepDay || _year.Length > 0)
                shown += "/" + _year;
            return shown;
        }

        /// <summary>Clear month, day, year, and separator flags for a fresh date.</summary>
        private void ResetParts()
        {
            _month = "";
            _day = "";
            _year = "";
            _sepMonth = false;
            _sepDay = false;
            _part = 0;
        }

        /// <summary>Parse a grid cell as a date, trying MM/DD/YYYY-style text first, then culture parse.</summary>
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

        /// <summary>True when <paramref name="date"/> is on or after from and on or before to (inclusive).</summary>
        public static bool InRange(DateTime date, DateTime? from, DateTime? to)
        {
            if (from != null && date < from.Value.Date)
                return false;
            if (to != null && date > to.Value.Date)
                return false;
            return true;
        }

        /// <summary>Parse slash/dot/dash dates or 6- and 8-digit runs as month/day/year.</summary>
        private static bool TryParseFlexible(string text, out DateTime date)
        {
            date = default;
            char[] marks = { '/', '.', '-' };
            if (text.IndexOfAny(marks) >= 0)
            {
                string[] parts = text.Split(marks, StringSplitOptions.RemoveEmptyEntries);
                // Need month, day, and year; two-part values are not dates.
                if (parts.Length != 3)
                    return false;
                if (!int.TryParse(parts[0], out int a) ||
                    !int.TryParse(parts[1], out int b) ||
                    !int.TryParse(parts[2], out int c))
                    return false;
                // A 4-digit first part is YYYY-MM-DD (ISO leftover CSVs).
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

        /// <summary>Build a calendar date or fail on impossible month/day/year combinations.</summary>
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
                // Invalid calendar values (month 13, Feb 30) are not dates.
                return false;
            }
        }

        /// <summary>Map a two-digit year through the current culture's 100-year window.</summary>
        private static int ExpandYear(int year, int digitCount)
        {
            if (digitCount != 2)
                return year;
            return CultureInfo.CurrentCulture.Calendar.ToFourDigitYear(year);
        }

        /// <summary>Keep only digits, truncated to <paramref name="max"/> so month/day stay two digits.</summary>
        private static string TakeDigits(string text, int max)
        {
            string digits = Digits(text);
            return digits.Length <= max ? digits : digits[..max];
        }

        /// <summary>Strip non-digit characters from pasted or imported text.</summary>
        private static string Digits(string text)
        {
            var chars = (text ?? "").Where(char.IsDigit).ToArray();
            return new string(chars);
        }
    }
}
