namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Compact letterhead for CRC PDFs: boat lockup, contact, gold divider, faint seal.
    /// Tighter than the print letterhead so the document body keeps the page.
    /// </summary>
    internal static class PdfLetterhead
    {
        public const float Left = 36;
        public const float Right = 576;

        public static float Draw(PdfDraw g, bool brand = true)
        {
            if (!brand)
                return 28;

            DrawWatermark(g);
            return DrawBrand(g);
        }

        public static void TitleBar(PdfDraw g, float y, string title)
        {
            g.Fill(Left, y, 540, 18, Theme.Navy);
            g.Text(42, y + 13, title, 10, true, Theme.Cream);
        }

        private static void DrawWatermark(PdfDraw g)
        {
            var seal = PdfImages.Seal();
            if (seal == null)
                return;

            const float size = 340;
            g.Image(seal, (PdfDraw.PageW - size) / 2f, 214, size, size, opacity: 0.11f);
        }

        private static float DrawBrand(PdfDraw g)
        {
            const float top = 20;
            const float logoH = 44;
            float used = logoH;
            var lockup = PdfImages.Lockup();
            if (lockup != null)
            {
                float w = logoH * lockup.Width / (float)lockup.Height;
                if (w > 310)
                {
                    w = 310;
                    used = w * lockup.Height / (float)lockup.Width;
                }

                g.Image(lockup, Left, top, w, used);
            }
            else
            {
                g.Text(Left, top + 18, "CAST RIGHT", 16, PdfFace.SerifBold, Theme.Navy);
                g.Text(Left + 4, top + 34, "Catch Co.", 10, PdfFace.SerifItalic, Theme.Navy);
                g.Fill(Left + 78, top + 30, 18, 1.2f, Theme.Gold);
                g.Fill(Left + 128, top + 30, 18, 1.2f, Theme.Gold);
            }

            float contactBottom = DrawContact(g, top + 6);
            float lineY = Math.Max(top + used, contactBottom) + 8;
            g.Line(Left, lineY, 292, lineY, Theme.Navy, 1.1f);
            g.Diamond(306, lineY, 3.4f, Theme.Gold);
            g.Line(320, lineY, Right, lineY, Theme.Navy, 1.1f);
            return lineY + 10;
        }

        private static float DrawContact(PdfDraw g, float y)
        {
            string phone = First(AppState.Phone, "(253) 540-2631");
            string email = First(AppState.CompanyEmail, "jwatts@castrightcatch.com");
            string address = First(AppState.Address, "PO Box 1064, Orting, WA 98360");
            const float x = 392;
            var lines = new List<string>();
            if (phone.Length > 0) lines.Add(phone);
            if (email.Length > 0) lines.Add(email);
            lines.AddRange(SplitAddress(address));

            float row = y;
            foreach (string line in lines)
            {
                g.Diamond(x - 8, row, 2.1f, Theme.Gold);
                g.Text(x, row + 3, line, 7.5f, false, Theme.Navy);
                row += 11;
            }

            return row;
        }

        private static string[] SplitAddress(string address)
        {
            int comma = address.IndexOf(',');
            if (comma <= 0 || comma >= address.Length - 1)
                return new[] { address };
            string left = address[..comma].Trim();
            string right = address[(comma + 1)..].Trim();
            if (left.Length == 0 || right.Length == 0)
                return new[] { address };
            return new[] { left, right };
        }

        private static string First(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }
}
