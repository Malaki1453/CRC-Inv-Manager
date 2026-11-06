namespace CastRightCatchInvManagement
{
    /// <summary>Two-column labeled fields that wrap and push the rest of the PDF down.</summary>
    internal static class PdfFieldFlow
    {
        public const float LeftLabelX = 36;
        public const float LeftValueX = 110;
        public const float RightLabelX = 330;
        public const float RightValueX = 420;
        public const float LeftValueW = 208;
        public const float RightValueW = 156;
        public const float Line = 12f;

        /// <summary>Draw a left and right labeled value. Returns y below the taller wrapped side.</summary>
        public static float Pair(
            PdfDraw g,
            float y,
            string leftLabel,
            string? leftValue,
            string rightLabel,
            string? rightValue,
            float valueSize = 8)
        {
            g.Text(LeftLabelX, y, leftLabel, 8, true, Theme.Navy);
            g.Text(RightLabelX, y, rightLabel, 8, true, Theme.Navy);
            var left = PdfDraw.WrapLines(leftValue, valueSize, false, LeftValueW);
            var right = PdfDraw.WrapLines(rightValue, valueSize, false, RightValueW);
            int n = Math.Max(left.Count, right.Count);
            for (int i = 0; i < n; i++)
            {
                if (i < left.Count)
                    g.Text(LeftValueX, y + i * Line, left[i], valueSize, false, Theme.Ink);
                if (i < right.Count)
                    g.Text(RightValueX, y + i * Line, right[i], valueSize, false, Theme.Ink);
            }

            return y + n * Line + 2;
        }

        /// <summary>Draw a labeled value on the left only. Returns y below the wrapped block.</summary>
        public static float Left(
            PdfDraw g,
            float y,
            string label,
            string? value,
            float valueSize = 8,
            float maxWidth = LeftValueW)
        {
            g.Text(LeftLabelX, y, label, 8, true, Theme.Navy);
            return g.TextFlow(LeftValueX, y, value, valueSize, false, Theme.Ink, maxWidth, Line) + 2;
        }

        /// <summary>Draw wrapped lines at x. Returns the height used.</summary>
        public static float Block(PdfDraw g, float x, float y, string? text, float maxWidth, float size = 8)
        {
            float next = g.TextFlow(x, y, text, size, false, Theme.Ink, maxWidth, Line);
            return Math.Max(Line, next - y);
        }
    }
}
