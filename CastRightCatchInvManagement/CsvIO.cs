using System.Globalization;
using System.Text;

namespace CastRightCatchInvManagement
{
    internal static class CsvIO
    {
        public static List<string[]> Read(string path)
        {
            var rows = new List<string[]>();
            if (!File.Exists(path))
                return rows;

            foreach (var line in File.ReadAllLines(path))
            {
                if (string.IsNullOrWhiteSpace(line))
                    continue;
                rows.Add(ParseLine(line).ToArray());
            }

            return rows;
        }

        public static void Write(string path, IEnumerable<string> header, IEnumerable<IEnumerable<string>> rows)
        {
            var sb = new StringBuilder();
            sb.AppendLine(Join(header));
            foreach (var row in rows)
                sb.AppendLine(Join(row));
            File.WriteAllText(path, sb.ToString());
        }

        public static string Join(IEnumerable<string> fields)
        {
            return string.Join(",", fields.Select(Escape));
        }

        public static string Escape(string? value)
        {
            value ??= "";
            if (value.Contains('"') || value.Contains(',') || value.Contains('\n') || value.Contains('\r'))
                return "\"" + value.Replace("\"", "\"\"") + "\"";
            return value;
        }

        public static string Money(double? value)
        {
            return value is null ? "" : value.Value.ToString("0.00", CultureInfo.InvariantCulture);
        }

        public static string Qty(double? value)
        {
            return value is null ? "" : value.Value.ToString("0.##", CultureInfo.InvariantCulture);
        }

        public static string Price(double? value)
        {
            return value is null ? "" : value.Value.ToString("0.####", CultureInfo.InvariantCulture);
        }

        public static string Date(DateTime? value)
        {
            return value is null ? "" : value.Value.ToString("yyyy-MM-dd");
        }

        private static IEnumerable<string> ParseLine(string line)
        {
            var field = new StringBuilder();
            bool quoted = false;
            for (int i = 0; i < line.Length; i++)
            {
                char c = line[i];
                if (quoted)
                {
                    if (c == '"')
                    {
                        if (i + 1 < line.Length && line[i + 1] == '"')
                        {
                            field.Append('"');
                            i++;
                        }
                        else
                        {
                            quoted = false;
                        }
                    }
                    else
                    {
                        field.Append(c);
                    }
                }
                else if (c == '"')
                {
                    quoted = true;
                }
                else if (c == ',')
                {
                    yield return field.ToString();
                    field.Clear();
                }
                else
                {
                    field.Append(c);
                }
            }

            yield return field.ToString();
        }
    }
}
