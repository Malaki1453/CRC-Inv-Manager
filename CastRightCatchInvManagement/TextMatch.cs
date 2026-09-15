namespace CastRightCatchInvManagement
{
    /// <summary>Close-match text search across codes, names, and other stored fields.</summary>
    internal static class TextMatch
    {
        /// <summary>
        /// True when any field equals, contains, or is a close spelling of the query,
        /// or when every query word appears somewhere in the combined fields.
        /// </summary>
        public static bool MatchesAny(IEnumerable<string?> fields, string query)
        {
            query = (query ?? "").Trim();
            // An empty search is not a filter; every row should stay visible.
            if (query.Length == 0)
                return true;

            string hay = "";
            foreach (var field in fields)
            {
                string text = (field ?? "").Trim();
                // Empty cells add nothing to ranking or the combined haystack.
                if (text.Length == 0)
                    continue;
                hay += " " + text;
                // A strong hit on one field is enough to keep the row.
                if (Score(text, query) > 0)
                    return true;
            }

            var tokens = query.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            return tokens.Length > 1 && tokens.All(token => Contains(hay, token));
        }

        /// <summary>
        /// Rank how closely <paramref name="text"/> matches <paramref name="query"/>.
        /// Exact and prefix beats contains; short typos use a small Levenshtein budget.
        /// </summary>
        public static int Score(string text, string query)
        {
            text = (text ?? "").Trim();
            query = (query ?? "").Trim();
            // Blank sides cannot score; callers treat 0 as no match.
            if (text.Length == 0 || query.Length == 0)
                return 0;

            // Exact field match is the strongest hit (codes, names).
            if (text.Equals(query, StringComparison.OrdinalIgnoreCase))
                return 100;
            // Prefix match is next so typing the start of a code still ranks high.
            if (text.StartsWith(query, StringComparison.OrdinalIgnoreCase))
                return 85;
            // Substring match covers names that contain the typed word.
            if (Contains(text, query))
                return 70;

            // Fuzzy spelling is expensive and noisy on long descriptions or 1-letter queries.
            if (query.Length < 2 || text.Length > 40)
                return 0;

            int dist = Levenshtein(query, text);
            int allowed = query.Length <= 4 ? 1 : 2;
            // Allow one typo on short codes and two on longer ones.
            if (dist > 0 && dist <= allowed)
                return 50 - dist;
            return 0;
        }

        /// <summary>Case-insensitive substring test used by scoring and multi-token search.</summary>
        public static bool Contains(string value, string needle) =>
            (value ?? "").Contains(needle, StringComparison.OrdinalIgnoreCase);

        /// <summary>Edit distance between two strings, used only for short close-spelling scores.</summary>
        private static int Levenshtein(string a, string b)
        {
            a = a.ToLowerInvariant();
            b = b.ToLowerInvariant();
            int n = a.Length;
            int m = b.Length;
            // Distance to an empty string is the other string's length.
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
