using System.Globalization;
using System.Text;

namespace CastRightCatchInvManagement
{
    /// <summary>CSV read/write with quoted fields. Used only to import leftover files into SQLite.</summary>
    internal static class CsvIO
    {
        /// <summary>Read a CSV into rows of fields, skipping blank lines.</summary>
        public static List<string[]> Read(string path)
        {
            var rows = new List<string[]>();
            // Missing leftover CSVs are normal after a database has already been created.
            if (!File.Exists(path))
                return rows;

            foreach (var line in File.ReadAllLines(path))
            {
                // Blank lines are not inventory rows.
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                rows.Add(ParseLine(line).ToArray());
            }

            return rows;
        }

        /// <summary>Write a header plus data rows using the same quoting rules as <see cref="Read"/>.</summary>
        public static void Write(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Join(header));
            foreach (var row in rows)
                sb.AppendLine(Join(row));
            File.WriteAllText(path, sb.ToString());
        }

        /// <summary>UTF-8 with BOM so Excel opens the file with the right characters.</summary>
        public static void WriteExcel(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
        {
            var lines = new List<IEnumerable<string>> { header };
            lines.AddRange(rows);
            WriteExcel(path, lines);
        }

        /// <summary>Write already-built CSV rows as Excel-friendly UTF-8 with BOM.</summary>
        public static void WriteExcel(string path, IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            foreach (var row in rows)
                sb.AppendLine(Join(row));
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
        }

        /// <summary>Join fields with commas, quoting values that need it.</summary>
        public static string Join(IEnumerable<string> fields)
        {
            return string.Join(",", fields.Select(Escape));
        }

        /// <summary>Quote a cell when it contains a comma, quote, or newline so round-trip parse stays correct.</summary>
        public static string Escape(string? value)
        {
            value ??= "";
            // Quotes, commas, and line breaks would split or truncate the field if left raw.
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        /// <summary>Money cell as 0.00, or blank when the value is missing.</summary>
        public static string Money(double? value)
        {
            return value is null ? "" : value.Value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Quantity cell with up to two decimals, or blank when missing.</summary>
        public static string Qty(double? value)
        {
            return value is null ? "" : value.Value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        /// <summary>Unit-price cell with up to four decimals, or blank when missing.</summary>
        public static string Price(double? value)
        {
            return value is null ? "" : value.Value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        /// <summary>ISO date yyyy-MM-dd, or blank when the date is missing.</summary>
        public static string Date(DateTime? value)
        {
            return value is null ? "" : value.Value.ToString("yyyy-MM-dd");
        }

        /// <summary>Split one CSV line, honoring quoted commas and doubled quotes.</summary>
        private static IEnumerable<string> ParseLine(string line)
        {
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                // Inside quotes, commas are data; a doubled quote is a literal quote.
                if (quoted)
                {
                    // A quote is either an escaped "" or the end of this field.
                    if (c == '"')
                    {
                        // RFC-style escaped quote inside a quoted field.
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        // A lone quote closes the field and returns to comma-separated mode.
                        else
                        {
                            quoted = false;
                        }
                    }
                    // Any other character is payload inside the quotes.
                    else
                    {
                        field.Append(c);
                    }
                }
                // An unquoted quote starts a quoted field so later commas stay inside it.
                else if (c == '"')
                {
                    quoted = true;
                }
                // An unquoted comma is the field boundary.
                else if (c == ',')
                {
                    yield return field.ToString();
                    field.Clear();
                }
                // Ordinary characters belong to the current field.
                else
                {
                    field.Append(c);
                }
            }

            yield return field.ToString();
        }
    }
}
