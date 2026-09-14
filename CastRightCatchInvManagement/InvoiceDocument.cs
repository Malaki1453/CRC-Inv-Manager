using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Draws an invoice PDF from an InvoiceDraft, including company info from Settings.</summary>
    internal static class InvoiceDocument
    {
        public static string Save(InvoiceDraft draft)
        {
            string party = draft.Received
                ? FirstNonEmpty(draft.VendorName, draft.VendorCode, draft.CustomerName)
                : FirstNonEmpty(draft.CustomerCode, draft.CustomerName);
            string fileName = SanitizeFile($"Invoice {draft.InvoiceNumber} - {party}.pdf");
            return DataFiles.SaveStoredPdf(
                DataFiles.PdfKindInvoice,
                draft.InvoiceNumber.Trim(),
                fileName,
                Draw(draft).ToPdf());
        }

        private static PdfDraw Draw(InvoiceDraft draft)
        {
            var g = new PdfDraw();
            bool received = draft.Received;
            string company = received
                ? FirstNonEmpty(draft.IssuerName, draft.VendorName, "Vendor")
                : FirstNonEmpty(AppState.BusinessName, "Cast Right Catch Co.");
            string address = received
                ? draft.IssuerAddress
                : FirstNonEmpty(AppState.Address, "PO Box 1064, Orting, WA 98360");
            string phone = received
                ? draft.IssuerPhone
                : FirstNonEmpty(AppState.Phone, "(253) 540-2631");
            string email = received
                ? ""
                : FirstNonEmpty(AppState.CompanyEmail, "jwatts@castrightcatch.com");
            string ein = received ? "" : FirstNonEmpty(AppState.Ein, "41-3723454");
            string defaultTerms = FirstNonEmpty(draft.Terms, AppState.PaymentTerms, "NET 15 DAYS");
            string orderNo = received ? draft.PoNumber : draft.SoNumber;
            string partyCode = received ? draft.VendorCode : draft.CustomerCode;

            float y = PdfLetterhead.Draw(g, brand: !received);
            if (received)
            {
                g.Fill(36, y, 4, 44, Theme.Gold);
                g.Text(50, y + 14, company.ToUpperInvariant(), 13, true, Theme.Navy);
                g.Text(50, y + 28, address, 8, false, Theme.Muted);
                g.Text(50, y + 40, string.IsNullOrWhiteSpace(email) ? phone : phone + "    " + email, 8, false, Theme.Muted);
                y += 50;
            }

            g.Text(36, y + 16, "INVOICE", 16, PdfFace.SerifBold, Theme.Navy);
            g.Rect(430, y, 146, 36);
            g.Fill(430, y, 146, 12, Theme.Navy);
            g.Text(454, y + 9, "INVOICE NO.", 6.5f, true, Theme.Cream);
            g.Text(508, y + 9, "DATE", 6.5f, true, Theme.Cream);
            g.TextRight(490, y + 28, draft.InvoiceNumber, 9, true, Theme.Ink);
            g.Text(516, y + 28, draft.InvoiceDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink, center: true, width: 52);
            g.Line(494, y, 494, y + 36);
            if (ein.Length > 0)
                g.Text(430, y + 48, "TAX ID# " + ein, 8, false, Theme.Ink);

            y += 56;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, received ? "PO #" : "SO #", 6.5f, true, Theme.Cream);
            g.Text(108, y + 11, "ORDER DATE", 6.5f, true, Theme.Cream);
            g.Text(186, y + 11, "TERMS", 6.5f, true, Theme.Cream);
            g.Text(280, y + 11, "SHIP VIA", 6.5f, true, Theme.Cream);
            g.Text(400, y + 11, received ? "CONTACT" : "SALES REP", 6.5f, true, Theme.Cream);
            g.Text(478, y + 11, "SHIP DATE", 6.5f, true, Theme.Cream);
            g.Text(542, y + 11, received ? "VEND ID" : "CUST ID", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, orderNo, 8, false, Theme.Ink);
            g.Text(108, y + 13, draft.InvoiceDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(186, y + 13, defaultTerms, 8, false, Theme.Ink);
            g.Text(280, y + 13, draft.ShipVia, 8, false, Theme.Ink);
            g.Text(400, y + 13, draft.SalesRep, 8, false, Theme.Ink);
            g.Text(478, y + 13, draft.ShipDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(542, y + 13, partyCode, 8, false, Theme.Ink);

            y += 32;
            g.Text(36, y, "Sold To:", 8, true, Theme.Navy);
            g.Text(306, y, "Ship To:", 8, true, Theme.Navy);
            y += 12;
            DrawBlock(g, 36, y, draft.SoldTo);
            DrawBlock(g, 306, y, draft.ShipTo);

            y += 48;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "PO #", 6.5f, true, Theme.Cream);
            g.Text(108, y + 11, "PRODUCT", 6.5f, true, Theme.Cream);
            g.Text(168, y + 11, "LOT #", 6.5f, true, Theme.Cream);
            g.Text(250, y + 11, "ORD", 6.5f, true, Theme.Cream);
            g.Text(286, y + 11, "SHIP", 6.5f, true, Theme.Cream);
            g.Text(322, y + 11, "DESCRIPTION", 6.5f, true, Theme.Cream);
            g.Text(430, y + 11, "WEIGHT", 6.5f, true, Theme.Cream);
            g.Text(486, y + 11, "PRICE", 6.5f, true, Theme.Cream);
            g.Text(538, y + 11, "AMOUNT", 6.5f, true, Theme.Cream);

            y += 16;
            float tableTop = y;
            float rowH = 18;
            int rows = Math.Max(8, draft.Lines.Count);
            g.Rect(36, y, 540, rows * rowH);

            for (int i = 0; i < draft.Lines.Count && i < rows; i++)
            {
                var line = draft.Lines[i];
                float ly = y + i * rowH;
                if (i % 2 == 1)
                    g.Fill(36.5f, ly, 539, rowH, Theme.GridAlt);
                g.Text(42, ly + 12, Clip(line.PoNumber, 12), 7.5f, false, Theme.Ink);
                g.Text(108, ly + 12, Clip(line.ProductId, 9), 7.5f, false, Theme.Ink);
                g.Text(168, ly + 12, Clip(line.LotNumber, 14), 7.5f, false, Theme.Ink);
                g.Text(250, ly + 12, line.Ordered, 7.5f, false, Theme.Ink);
                g.Text(286, ly + 12, line.Shipped, 7.5f, false, Theme.Ink);
                g.Text(322, ly + 12, Clip(line.Description, 20), 7.5f, false, Theme.Ink);
                g.Text(430, ly + 12, FormatQty(line.Weight), 7.5f, false, Theme.Ink);
                g.Text(486, ly + 12, FormatMoney(InvoiceLineRow.ParseNumber(line.Price)), 7.5f, false, Theme.Ink);
                g.TextRight(572, ly + 12, FormatMoney(line.Amount), 7.5f, false, Theme.Ink);
            }

            float tableBottom = tableTop + rows * rowH;
            g.Line(102, tableTop, 102, tableBottom);
            g.Line(162, tableTop, 162, tableBottom);
            g.Line(244, tableTop, 244, tableBottom);
            g.Line(280, tableTop, 280, tableBottom);
            g.Line(316, tableTop, 316, tableBottom);
            g.Line(422, tableTop, 422, tableBottom);
            g.Line(478, tableTop, 478, tableBottom);
            g.Line(528, tableTop, 528, tableBottom);

            float ty = tableBottom + 6;
            g.TextRight(420, ty + 12, "Total Weight", 8, true, Theme.Navy);
            g.Text(430, ty + 12, FormatQty(draft.TotalWeight.ToString(CultureInfo.InvariantCulture)), 8, false, Theme.Ink);
            g.TextRight(528, ty + 12, "Sub Total", 8, true, Theme.Navy);
            g.TextRight(572, ty + 12, FormatMoney(draft.SubTotal), 8, false, Theme.Ink);

            ty += 16;
            g.Rect(422, ty, 154, 64);
            DrawTotalRow(g, ty, "Discount", draft.Discount);
            DrawTotalRow(g, ty + 16, "Freight", draft.Freight);
            string taxLabel = draft.TaxIsPercent && draft.TaxRate != 0
                ? $"Tax {draft.TaxRate.ToString("0.##", CultureInfo.InvariantCulture)}%"
                : "Tax Total";
            DrawTotalRow(g, ty + 32, taxLabel, draft.Tax);
            g.Fill(422, ty + 48, 154, 16, Theme.Navy);
            g.TextRight(528, ty + 59, "INVOICE TOTAL", 8, true, Theme.Cream);
            g.TextRight(572, ty + 59, FormatMoney(draft.InvoiceTotal), 8, true, Theme.Cream);

            if (received)
            {
                g.Text(36, 748, "VENDOR INVOICE AS RECEIVED. CAST RIGHT CATCH IS THE RECEIVING COMPANY.", 6.5f, false, Theme.Muted);
                g.Text(36, 760, "IMPORTANT: NO CLAIMS OR REDUCTIONS ALLOWED UNLESS MADE IMMEDIATELY ON RECEIPT OF GOODS.", 6.5f, false, Theme.Muted);
                g.Text(36, 776, "RECEIVED BY ________________________________", 8, false, Theme.Ink);
            }
            else
            {
                g.Text(36, 748, "INTEREST MAY BE CHARGED AT THE RATE OF 1.5% PER MONTH ON ALL OVERDUE ACCOUNTS.", 6.5f, false, Theme.Muted);
                g.Text(36, 760, "IMPORTANT: NO CLAIMS OR REDUCTIONS ALLOWED UNLESS MADE IMMEDIATELY ON RECEIPT OF GOODS.", 6.5f, false, Theme.Muted);
                g.Text(36, 776, "CUSTOMER SIGNATURE ________________________________", 8, false, Theme.Ink);
            }

            return g;
        }

        private static void DrawTotalRow(PdfDraw g, float y, string label, decimal value)
        {
            g.TextRight(528, y + 12, label, 8, true, Theme.Navy);
            if (value != 0)
                g.TextRight(572, y + 12, FormatMoney(value), 8, false, Theme.Ink);
            g.Line(422, y + 16, 576, y + 16);
        }

        private static void DrawBlock(PdfDraw g, float x, float y, string text)
        {
            var lines = (text ?? "").Replace("\r", "").Split('\n');
            for (int i = 0; i < Math.Min(3, lines.Length); i++)
                g.Text(x, y + i * 11, Clip(lines[i], 46), 8, false, Theme.Ink);
        }

        private static string FormatMoney(decimal value) => value.ToString("0.00", CultureInfo.InvariantCulture);

        private static string FormatQty(string? value)
        {
            decimal n = InvoiceLineRow.ParseNumber(value);
            if (n == 0 && string.IsNullOrWhiteSpace(value))
                return "";
            return n.ToString("0.###", CultureInfo.InvariantCulture);
        }

        private static string Clip(string? text, int max)
        {
            text ??= "";
            return text.Length <= max ? text : text[..(max - 1)] + ".";
        }

        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        }

        private static string SanitizeFile(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return name;
        }
    }
}
