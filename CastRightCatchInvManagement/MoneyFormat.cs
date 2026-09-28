using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>USD display ($1,234.56) and typed-amount rules (digits and one decimal point).</summary>
    internal static class MoneyFormat
    {
        private static readonly CultureInfo Usd = CultureInfo.GetCultureInfo("en-US");
        private static readonly HashSet<string> MoneyHeaders = new(StringComparer.OrdinalIgnoreCase)
        {
            "Amount", "Paid", "Outstanding", "Discount", "Tax", "Freight",
            "Credit Limit", "Current Balance", "Claim Value", "Value",
            "Total Cost", "Price Paid / LB", "Price / LB",
            "Sell Price / LB", "Overhead / LB", "Freight / LB", "Forwarder / LB",
            "Other / LB", "Revenue", "COGS", "Cogs", "Profit", "Gross profit",
            "Gross", "Limit", "On File", "Overdue", "Due", "Total", "Sub Total",
            "Invoice Total", "Price", "Avg cost / lb", "Total cost", "Minimum Profit", "Price / lb"
        };

        /// <summary>USD with a dollar sign, grouping commas, and two decimals.</summary>
        public static string Display(decimal amount) => amount.ToString("C2", Usd);

        /// <summary>Two decimals without a dollar sign (per-lb totals that are not currency labels).</summary>
        public static string Plain(decimal amount) => amount.ToString("0.00", CultureInfo.InvariantCulture);

        /// <summary>Format a stored cell as USD, or leave blank / non-numeric text alone.</summary>
        public static string Display(string? stored)
        {
            if (string.IsNullOrWhiteSpace(stored))
                return "";
            return TryParse(stored, out decimal amount) ? Display(amount) : stored.Trim();
        }

        /// <summary>USD for a filled amount; blank stays blank so empty inputs do not become $0.00.</summary>
        public static string DisplayOrBlank(decimal amount) =>
            amount == 0 ? "" : Display(amount);

        /// <summary>Plain digits for editing (no $ or commas).</summary>
        public static string Editable(decimal amount) =>
            amount.ToString("0.##", CultureInfo.InvariantCulture);

        /// <summary>Value to store in the database: invariant 0.00, or blank.</summary>
        public static string Store(string? text)
        {
            if (string.IsNullOrWhiteSpace(text))
                return "";
            return TryParse(text, out decimal amount)
                ? amount.ToString("0.00", CultureInfo.InvariantCulture)
                : "";
        }

        /// <summary>True when this grid/header name is a dollar amount, not a quantity or label.</summary>
        public static bool IsMoneyHeader(string? header)
        {
            string name = (header ?? "").Trim();
            if (name.Length == 0)
                return false;
            if (name.Contains("Company", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Terms", StringComparison.OrdinalIgnoreCase) ||
                name.Contains('%') ||
                name.Contains("Date", StringComparison.OrdinalIgnoreCase) ||
                name.Contains("Volume", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("CS", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("Pack Size", StringComparison.OrdinalIgnoreCase))
                return false;
            if (MoneyHeaders.Contains(name))
                return true;
            if (name.Contains("Price", StringComparison.OrdinalIgnoreCase))
                return true;
            // Current Balance is money; a bare "Balance" (inventory remaining lb) is not.
            if (name.Contains("Cost", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("Volume", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("/ LB", StringComparison.OrdinalIgnoreCase) &&
                !name.Contains("/ lb", StringComparison.OrdinalIgnoreCase))
                return true;
            if (name.Equals("Total Cost / LB", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("TOTAL / LB", StringComparison.OrdinalIgnoreCase))
                return false;
            return name.EndsWith(" / LB", StringComparison.OrdinalIgnoreCase) &&
                   (name.Contains("Freight", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Overhead", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Forwarder", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Other", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Price", StringComparison.OrdinalIgnoreCase) ||
                    name.Contains("Cost", StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Parse $1,234.50, (50), or 1234.5. False when the text is not a number.</summary>
        public static bool TryParse(string? text, out decimal amount)
        {
            amount = 0;
            text = (text ?? "").Trim();
            if (text.Length == 0)
                return false;

            bool negative = text.StartsWith('(') && text.EndsWith(')');
            text = text.Replace("$", "").Replace(",", "").Replace("(", "").Replace(")", "").Trim();
            if (!decimal.TryParse(text, NumberStyles.Number, CultureInfo.InvariantCulture, out amount) &&
                !decimal.TryParse(text, NumberStyles.Number, Usd, out amount) &&
                !decimal.TryParse(text, NumberStyles.Number, CultureInfo.CurrentCulture, out amount))
                return false;

            if (negative)
                amount = -Math.Abs(amount);
            return true;
        }

        /// <summary>Keep digits, an optional leading minus, and at most one decimal point.</summary>
        public static string Filter(string? text, bool allowNegative = true)
        {
            text ??= "";
            var chars = new char[text.Length];
            int n = 0;
            bool dot = false;
            bool digit = false;
            foreach (char c in text)
            {
                if (c is '$' or ',' or ' ')
                    continue;
                if (c == '-' && allowNegative && n == 0 && !digit)
                {
                    chars[n++] = c;
                    continue;
                }

                if (char.IsDigit(c))
                {
                    chars[n++] = c;
                    digit = true;
                    continue;
                }

                if (c == '.' && !dot)
                {
                    chars[n++] = c;
                    dot = true;
                }
            }

            return new string(chars, 0, n);
        }

        /// <summary>
        /// Restrict typing to numbers and one decimal. While focused the box stays editable;
        /// on leave it shows USD. Set <paramref name="formatOnLeave"/> false for tax-% boxes.
        /// </summary>
        public static void BindInput(TextBox box, bool formatOnLeave = true, bool allowNegative = true)
        {
            box.KeyPress += (_, e) =>
            {
                if (char.IsControl(e.KeyChar))
                    return;
                string next = ReplaceSelection(box, e.KeyChar.ToString());
                e.Handled = Filter(next, allowNegative) != next;
            };
            box.TextChanged += (_, _) =>
            {
                if (!box.Focused)
                    return;
                string filtered = Filter(box.Text, allowNegative);
                if (filtered == box.Text)
                    return;
                int caret = box.SelectionStart;
                box.Text = filtered;
                box.SelectionStart = Math.Min(caret, filtered.Length);
            };
            box.Enter += (_, _) =>
            {
                if (!TryParse(box.Text, out decimal amount))
                    return;
                string editable = amount == 0 && string.IsNullOrWhiteSpace(Filter(box.Text, allowNegative))
                    ? ""
                    : Editable(amount);
                if (box.Text == editable)
                    return;
                box.Text = editable;
                box.SelectAll();
            };
            if (formatOnLeave)
            {
                box.Leave += (_, _) => Show(box);
            }
        }

        /// <summary>Show the current box value as USD, or leave it blank.</summary>
        public static void Show(TextBox box)
        {
            if (string.IsNullOrWhiteSpace(box.Text))
                return;
            if (!TryParse(box.Text, out decimal amount))
            {
                box.Text = Filter(box.Text);
                return;
            }

            box.Text = Display(amount);
        }

        /// <summary>Format money cells on any themed grid without changing stored values.</summary>
        public static void WireGrid(DataGridView grid)
        {
            grid.CellFormatting -= FormatGridCell;
            grid.CellFormatting += FormatGridCell;
        }

        private static void FormatGridCell(object? sender, DataGridViewCellFormattingEventArgs e)
        {
            if (sender is not DataGridView grid || e.RowIndex < 0 || e.ColumnIndex < 0)
                return;
            var col = grid.Columns[e.ColumnIndex];
            string key = col.Tag as string ?? col.Name;
            if (!IsMoneyHeader(col.HeaderText) && !IsMoneyHeader(key))
                return;
            string raw = e.Value?.ToString() ?? "";
            if (raw.Length == 0)
                return;
            if (!TryParse(raw, out decimal amount))
                return;
            e.Value = Display(amount);
            e.FormattingApplied = true;
            if (e.CellStyle != null)
                e.CellStyle.Alignment = DataGridViewContentAlignment.MiddleRight;
        }

        private static string ReplaceSelection(TextBox box, string typed)
        {
            string text = box.Text ?? "";
            int start = Math.Max(0, box.SelectionStart);
            int length = Math.Max(0, box.SelectionLength);
            if (start > text.Length)
                start = text.Length;
            if (start + length > text.Length)
                length = text.Length - start;
            return text.Remove(start, length).Insert(start, typed);
        }
    }
}
