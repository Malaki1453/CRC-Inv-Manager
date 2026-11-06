namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Compact letterhead for CRC PDFs: full-width top bar with the boat lockup on the left.
    /// </summary>
    internal static class PdfLetterhead
    {
        /// <summary>Top-right email on invoice PDFs.</summary>
        public const string InvoiceEmail = "ar@castrightcatch.com";
        /// <summary>Top-right email on sales-order PDFs.</summary>
        public const string SalesOrderEmail = "sales@castrightcatch.com";
        /// <summary>Top-right email on purchase-order PDFs.</summary>
        public const string PurchaseEmail = "purchases@castrightcatch.com";
        /// <summary>Top-right email on sale PDFs.</summary>
        public const string SaleEmail = "sales@castrightcatch.com";

        public const float Left = 36;
        public const float Right = 576;

        /// <summary>Paint brand lockup and watermark, or a short top margin when branding is off.</summary>
        public static float Draw(PdfDraw g, bool brand = true, string? email = null)
        {
            // Internal drafts skip the letterhead so the body starts higher.
            if (!brand)
                return 28;

            DrawWatermark(g);
            return DrawBrand(g, email);
        }

        /// <summary>Navy title strip used under the letterhead on invoices and similar PDFs.</summary>
        public static void TitleBar(PdfDraw g, float y, string title)
        {
            g.Fill(Left, y, 540, 18, Theme.Navy);
            g.Text(42, y + 13, title, 10, true, Theme.Cream);
        }

        /// <summary>Faint centered seal so the page still reads as CRC without covering text.</summary>
        private static void DrawWatermark(PdfDraw g)
        {
            var seal = PdfImages.Seal();
            // No seal asset means a clean page rather than a missing-image box.
            if (seal == null)
                return;

            const float size = 340;
            g.Image(seal, (PdfDraw.PageW - size) / 2f, 214, size, size, opacity: 0.11f);
        }

        /// <summary>Full-bleed cream bar across the top, boat lockup on the left, contact on the right.</summary>
        private static float DrawBrand(PdfDraw g, string? email)
        {
            const float bannerH = 84;
            const float pad = 14;
            const float logoH = 66;
            g.Fill(0, 0, PdfDraw.PageW, bannerH, Theme.Cream);
            g.Fill(0, bannerH - 3, PdfDraw.PageW, 3, Theme.Gold);

            var lockup = PdfImages.Lockup();
            // Prefer the boat lockup when the brand PNG is available.
            if (lockup != null)
            {
                float w = logoH * lockup.Width / (float)lockup.Height;
                g.Image(lockup, pad, (bannerH - 3 - logoH) / 2f, w, logoH);
            }
            // Wordmark fallback when the lockup image is missing from the build.
            else
            {
                g.Text(pad, 32, "CAST RIGHT", 16, PdfFace.SerifBold, Theme.Navy);
                g.Text(pad + 4, 50, "Catch Co.", 10, PdfFace.SerifItalic, Theme.Navy);
            }

            DrawContact(g, bannerH, email);
            return bannerH + 10;
        }

        /// <summary>Phone, email, and address on the right of the top bar, sharing one right edge.</summary>
        private static void DrawContact(PdfDraw g, float bannerH, string? email)
        {
            string phone = First(AppState.Phone, "(253) 540-2631");
            string mail = First(email, AppState.CompanyEmail, "jwatts@castrightcatch.com");
            string address = First(AppState.Address, "PO Box 1064, Orting, WA 98360");
            var lines = new List<string>();
            // Skip empty contact rows so the list stays tight.
            if (phone.Length > 0) lines.Add(phone);
            if (mail.Length > 0) lines.Add(mail);
            lines.AddRange(SplitAddress(address));

            float block = lines.Count * 11;
            float row = Math.Max(16, (bannerH - 3 - block) / 2f);
            foreach (string line in lines)
            {
                g.TextRight(Right, row + 3, line, 7.5f, false, Theme.Navy);
                row += 11;
            }
        }

        /// <summary>Split "street, city" onto two lines when both sides have text.</summary>
        private static string[] SplitAddress(string address)
        {
            int comma = address.IndexOf(',');
            // Keep a one-line address when there is no usable city split.
            if (comma <= 0 || comma >= address.Length - 1)
                return new[] { address };
            string left = address[..comma].Trim();
            string right = address[(comma + 1)..].Trim();
            // A trailing or leading comma is not a real two-line address.
            if (left.Length == 0 || right.Length == 0)
                return new[] { address };
            return new[] { left, right };
        }

        /// <summary>First non-blank value so settings override the printed defaults.</summary>
        private static string First(params string?[] values) =>
            values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
    }
}
