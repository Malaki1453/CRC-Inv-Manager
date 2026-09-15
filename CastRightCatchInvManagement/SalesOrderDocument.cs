using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Draws a sales-order / pick-ticket PDF from a SalesOrderDraft.</summary>
    internal static class SalesOrderDocument
    {
        /// <summary>Draw a sales-order PDF from the draft and store it by SO #.</summary>
        public static string Save(SalesOrderDraft draft)
        {
            string customer = string.IsNullOrWhiteSpace(draft.CustomerCode)
                ? draft.CustomerName
                : draft.CustomerCode;
            string fileName = SanitizeFile($"Sales Order {draft.SoNumber} - {customer}.pdf");
            return DataFiles.SaveStoredPdf(
                DataFiles.PdfKindSalesOrder,
                draft.SoNumber.Trim(),
                fileName,
                Draw(draft).ToPdf());
        }

        /// <summary>Lay out letterhead, customer/ship-to, and the pick-ticket item table.</summary>
        private static PdfDraw Draw(SalesOrderDraft draft)
        {
            var g = new PdfDraw();
            float y = PdfLetterhead.Draw(g);
            PdfLetterhead.TitleBar(g, y, "SALES ORDER");
            y += 28;

            g.Text(36, y, "Customer:", 8, true, Theme.Navy);
            g.Text(110, y, draft.CustomerName, 9, false, Theme.Ink);
            g.Text(330, y, "Customer Code:", 8, true, Theme.Navy);
            g.Text(420, y, draft.CustomerCode, 9, false, Theme.Ink);

            y += 14;
            g.Text(36, y, "Contact:", 8, true, Theme.Navy);
            g.Text(110, y, draft.Contact, 9, false, Theme.Ink);
            g.Text(330, y, "Phone:", 8, true, Theme.Navy);
            g.Text(420, y, FirstNonEmpty(draft.ContactPhone, draft.CustomerPhone), 8, false, Theme.Ink);

            y += 14;
            var addressLines = (draft.Address ?? "").Replace("\r", "").Split('\n');
            g.Text(36, y, "Ship To:", 8, true, Theme.Navy);
            g.Text(110, y, addressLines.Length > 0 ? Clip(addressLines[0], 36) : "", 8, false, Theme.Ink);
            g.Text(330, y, "Email:", 8, true, Theme.Navy);
            g.Text(420, y, Clip(draft.Email, 28), 8, false, Theme.Ink);

            // Second address line only when Ship To is multi-line.
            if (addressLines.Length > 1 && addressLines[1].Trim().Length > 0)
            {
                y += 12;
                g.Text(110, y, Clip(addressLines[1], 36), 8, false, Theme.Ink);
            }

            y += 22;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "SO #", 6.5f, true, Theme.Cream);
            g.Text(150, y + 11, "CUSTOMER PO", 6.5f, true, Theme.Cream);
            g.Text(280, y + 11, "ORDER DATE", 6.5f, true, Theme.Cream);
            g.Text(390, y + 11, "RELEASE", 6.5f, true, Theme.Cream);
            g.Text(490, y + 11, "WAREHOUSE", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, draft.SoNumber, 8, false, Theme.Ink);
            g.Text(150, y + 13, draft.CustomerPo, 8, false, Theme.Ink);
            g.Text(280, y + 13, draft.OrderDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(390, y + 13, draft.ReleaseDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(490, y + 13, Clip(draft.Warehouse, 16), 8, false, Theme.Ink);

            y += 26;
            g.Text(36, y, "Freight Terms:", 8, true, Theme.Navy);
            g.Text(110, y, draft.FreightTerms, 8, false, Theme.Ink);

            y += 18;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "ITEM", 6.5f, true, Theme.Cream);
            g.Text(114, y + 11, "LOT #", 6.5f, true, Theme.Cream);
            g.Text(196, y + 11, "DESCRIPTION", 6.5f, true, Theme.Cream);
            g.Text(372, y + 11, "UNIT", 6.5f, true, Theme.Cream);
            g.Text(430, y + 11, "CS", 6.5f, true, Theme.Cream);
            g.Text(500, y + 11, "VOLUME", 6.5f, true, Theme.Cream);

            y += 16;
            float tableTop = y;
            float rowH = 18;
            int max = Math.Max(10, draft.Lines.Count);
            g.Rect(36, y, 540, max * rowH);

            decimal cases = 0;
            decimal volume = 0;
            for (int i = 0; i < draft.Lines.Count && i < max; i++)
            {
                var line = draft.Lines[i];
                float ly = y + i * rowH;
                // Zebra-stripe odd rows so the pick ticket is easier to scan.
                if (i % 2 == 1)
                    g.Fill(36.5f, ly, 539, rowH, Theme.GridAlt);
                g.Text(42, ly + 12, Clip(line.ItemCode, 12), 7.5f, false, Theme.Ink);
                g.Text(114, ly + 12, Clip(line.LotNumber, 14), 7.5f, false, Theme.Ink);
                g.Text(196, ly + 12, Clip(line.Description, 28), 7.5f, false, Theme.Ink);
                g.Text(372, ly + 12, Clip(line.UnitSize, 8), 7.5f, false, Theme.Ink);
                g.Text(430, ly + 12, FormatQty(line.Cases), 7.5f, false, Theme.Ink);
                g.TextRight(572, ly + 12, FormatQty(line.Volume), 7.5f, false, Theme.Ink);
                cases += InvoiceLineRow.ParseNumber(line.Cases);
                volume += InvoiceLineRow.ParseNumber(line.Volume);
            }

            float tableBottom = tableTop + max * rowH;
            g.Line(108, tableTop, 108, tableBottom);
            g.Line(190, tableTop, 190, tableBottom);
            g.Line(366, tableTop, 366, tableBottom);
            g.Line(424, tableTop, 424, tableBottom);
            g.Line(492, tableTop, 492, tableBottom);

            float ty = tableBottom + 10;
            g.TextRight(424, ty + 12, "Total Cases", 8, true, Theme.Navy);
            g.Text(430, ty + 12, cases.ToString("0.###", CultureInfo.InvariantCulture), 8, false, Theme.Ink);
            g.TextRight(532, ty + 12, "Total Volume", 8, true, Theme.Navy);
            g.TextRight(572, ty + 12, volume.ToString("0.###", CultureInfo.InvariantCulture), 8, false, Theme.Ink);

            g.Text(36, 776, "This document is a sales order. Confirm item, lot, cases, and weight before shipping.", 7, false, Theme.Muted);
            return g;
        }

        /// <summary>Format a quantity, leaving blank cells empty instead of printing 0.</summary>
        private static string FormatQty(string? value)
        {
            decimal n = InvoiceLineRow.ParseNumber(value);
            // A blank cell should not print 0; a typed zero still prints.
            if (n == 0 && string.IsNullOrWhiteSpace(value))
                return "";
            return n.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Trim text that would overflow a PDF column, adding a trailing period.</summary>
        private static string Clip(string? text, int max)
        {
            text ??= "";
            return text.Length <= max ? text : text[..(max - 1)] + ".";
        }

        /// <summary>First non-blank value, used for contact phone fallbacks.</summary>
        private static string FirstNonEmpty(params string?[] values)
        {
            return values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v)) ?? "";
        }

        /// <summary>Replace characters that cannot appear in a stored PDF file name.</summary>
        private static string SanitizeFile(string name)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                name = name.Replace(c, '-');
            return name;
        }
    }
}
