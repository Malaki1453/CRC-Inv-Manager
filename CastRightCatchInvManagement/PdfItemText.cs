namespace CastRightCatchInvManagement
{
    /// <summary>Item description text on purchase, sale, sales-order, and invoice PDFs.</summary>
    internal static class PdfItemText
    {
        /// <summary>Second-line origin block: | COO : country | processed in : country.</summary>
        public static string Origin(string? itemCode, string? coo = null)
        {
            string origin = First(coo, DataFiles.ItemMasterField(itemCode, "COO"));
            string proc = DataFiles.ItemMasterField(itemCode, "Proc Country");
            string text = "| COO : " + origin;
            // Same country is already on COO; do not repeat it as processed-in.
            if (proc.Length > 0 &&
                !proc.Equals(origin, StringComparison.OrdinalIgnoreCase))
                text += " | processed in : " + proc;
            return text;
        }

        /// <summary>Product name on the first line, origin sections on the second.</summary>
        public static void Draw(
            PdfDraw g,
            float x,
            float y,
            string? description,
            string? itemCode,
            string? coo,
            int clip)
        {
            g.Text(x, y + 11, Clip(description, clip), 7.5f, false, Theme.Ink);
            g.Text(x, y + 22, Clip(Origin(itemCode, coo), Math.Max(clip, 42)), 6.5f, false, Theme.Muted);
        }

        /// <summary>Trim text that would overflow a PDF column, adding a trailing period.</summary>
        public static string Clip(string? text, int max)
        {
            text ??= "";
            return text.Length <= max ? text : text[..(max - 1)] + ".";
        }

        private static string First(params string?[] values)
        {
            foreach (string? value in values)
            {
                if (!string.IsNullOrWhiteSpace(value))
                    return value.Trim();
            }

            return "";
        }
    }
}
