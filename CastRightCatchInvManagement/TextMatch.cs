namespace CastRightCatchInvManagement
{
    /// <summary>Close-match text search across codes, names, and other stored fields.</summary>
    internal static class TextMatch
    {
        public static bool MatchesAny(IEnumerable<string?> fields, string query)
        {
            query = (query ?? "").Trim();
            if (query.Length == 0)
                return true;

            string hay = "";
            foreach (var field in fields)
            {
                string text = (field ?? "").Trim();
                if (text.Length == 0)
                    continue;
                hay += " " + text;
                if (Score(text, query) > 0)
                    return true;
            }

            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tokens.Length > 1 && tokens.All(token => Contains(hay, token));
        }

        public static int Score(string text, string query)
        {
            text = (text ?? "").Trim();
            query = (query ?? "").Trim();
            if (text.Length == 0 || query.Length == 0)
                return 0;

            if (text.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 100;
            if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 85;
            if (Contains(text, query))
                return 70;

            if (query.Length < 2 || text.Length > 40)
                return 0;

            int dist = Levenshtein(query, text);
            int allowed = query.Length <= 4 ? 1 : 2;
            if (dist > 0 && dist <= allowed)
                return 50 - dist;
            return 0;
        }

        public static bool Contains(string value, string needle) =>
            (value ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase);

        private static int Levenshtein(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            int n = a.Length;
            int m = b.Length;
            if (n == 0)
                return m;
            if (m == 0)
                return n;

            var prev = new int[m + 1];
            var curr = new int[m + 1];
            for (int j = 0; j <= m; j++)
                prev[j] = j;

            for (int i = 1; i <= n; i++)
            {
                curr[0] = i;
                for (int j = 1; j <= m; j++)
                {
                    int cost = a[i - 1] == b[j - 1] ? 0 : 1;
                    curr[j] = Math.Min(Math.Min(curr[j - 1] + 1, prev[j] + 1), prev[j - 1] + cost);
                }

                (prev, curr) = (curr, prev);
            }

            return prev[m];
        }
    }
}
