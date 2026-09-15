using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Purchase-order PDF stored by PO #.</summary>
    internal static class PurchaseDocument
    {
        /// <summary>Build and store a purchase-order PDF for this PO, or null when no lines exist.</summary>
        public static string? SaveFromPo(string? po)
        {
            var rows = DataFiles.FindPurchasesByPo(po);
            return rows.Count == 0 ? null : Save(rows);
        }

        /// <summary>Draw a purchase-order PDF from the given lines and store it by PO #.</summary>
        public static string Save(IReadOnlyList<Dictionary<string, string>> rows)
        {
            // A PO PDF needs at least one product line.
            if (rows.Count == 0)
                throw new InvalidOperationException("This purchase has no lines.");

            var first = rows[0];
            string po = DataFiles.GetRecord(first, "PO #").Trim();
            string vendor = DataFiles.GetRecordAny(first, "Vendor", "Vendor Code");
            string title = "Purchase " + po + (vendor.Length > 0 ? " - " + vendor : "");
            return PdfFile.Save(DataFiles.PdfKindPurchase, po, title, BuildPage(rows));
        }

        /// <summary>Lay out letterhead, header fields, and the item table for the purchase PDF.</summary>
        private static PdfDraw BuildPage(IReadOnlyList<Dictionary<string, string>> rows)
        {
            var first = rows[0];
            var g = new PdfDraw();
            float y = PdfLetterhead.Draw(g);
            PdfLetterhead.TitleBar(g, y, "PURCHASE ORDER");
            y += 28;
            g.Text(36, y, "Vendor:", 8, true, Theme.Navy);
            g.Text(110, y, DataFiles.GetRecord(first, "Vendor"), 9, false, Theme.Ink);
            g.Text(330, y, "Vendor Code:", 8, true, Theme.Navy);
            g.Text(420, y, DataFiles.GetRecord(first, "Vendor Code"), 9, false, Theme.Ink);

            y += 14;
            g.Text(36, y, "Terms:", 8, true, Theme.Navy);
            g.Text(110, y, DataFiles.GetRecord(first, "Vendor Terms"), 8, false, Theme.Ink);
            g.Text(330, y, "Location:", 8, true, Theme.Navy);
            g.Text(420, y, DataFiles.GetRecord(first, "Location"), 8, false, Theme.Ink);

            y += 22;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "PO #", 6.5f, true, Theme.Cream);
            g.Text(150, y + 11, "AGREEMENT", 6.5f, true, Theme.Cream);
            g.Text(250, y + 11, "SHIP DATE", 6.5f, true, Theme.Cream);
            g.Text(350, y + 11, "ARRIVAL", 6.5f, true, Theme.Cream);
            g.Text(450, y + 11, "STATUS", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, DataFiles.GetRecord(first, "PO #"), 8, false, Theme.Ink);
            g.Text(150, y + 13, DataFiles.GetRecord(first, "Agreement Date"), 8, false, Theme.Ink);
            g.Text(250, y + 13, DataFiles.GetRecord(first, "Ship Date"), 8, false, Theme.Ink);
            g.Text(350, y + 13, DataFiles.GetRecord(first, "Arrival Date"), 8, false, Theme.Ink);
            g.Text(450, y + 13, DataFiles.GetRecord(first, "Status"), 8, false, Theme.Ink);

            y += 26;
            g.Text(36, y, "Forwarder:", 8, true, Theme.Navy);
            g.Text(110, y, DataFiles.GetRecord(first, "Forwarder"), 8, false, Theme.Ink);
            g.Text(330, y, "Logistics:", 8, true, Theme.Navy);
            g.Text(400, y, DataFiles.GetRecord(first, "Logistics"), 8, false, Theme.Ink);

            y += 18;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "ITEM", 6.5f, true, Theme.Cream);
            g.Text(114, y + 11, "DESCRIPTION", 6.5f, true, Theme.Cream);
            g.Text(320, y + 11, "PACK", 6.5f, true, Theme.Cream);
            g.Text(372, y + 11, "CS", 6.5f, true, Theme.Cream);
            g.Text(420, y + 11, "VOLUME", 6.5f, true, Theme.Cream);
            g.Text(478, y + 11, "PRICE / LB", 6.5f, true, Theme.Cream);
            g.Text(538, y + 11, "TOTAL", 6.5f, true, Theme.Cream);

            y += 16;
            float tableTop = y;
            float rowH = 18;
            int max = Math.Max(10, rows.Count);
            g.Rect(36, y, 540, max * rowH);

            decimal volume = 0;
            decimal total = 0;
            for (int i = 0; i < rows.Count && i < max; i++)
            {
                var row = rows[i];
                float ly = y + i * rowH;
                // Zebra-stripe odd rows so the item table is easier to scan.
                if (i % 2 == 1)
                    g.Fill(36.5f, ly, 539, rowH, Theme.GridAlt);
                g.Text(42, ly + 12, Clip(DataFiles.GetRecord(row, "Item Code"), 12), 7.5f, false, Theme.Ink);
                g.Text(114, ly + 12, Clip(DataFiles.GetRecord(row, "Description"), 34), 7.5f, false, Theme.Ink);
                g.Text(320, ly + 12, Clip(DataFiles.GetRecord(row, "Pack Size"), 8), 7.5f, false, Theme.Ink);
                g.Text(372, ly + 12, Qty(DataFiles.GetRecord(row, "CS")), 7.5f, false, Theme.Ink);
                string vol = DataFiles.GetRecord(row, "Volume");
                g.Text(420, ly + 12, Qty(vol), 7.5f, false, Theme.Ink);
                g.Text(478, ly + 12, Money(DataFiles.GetRecord(row, "Price Paid / LB")), 7.5f, false, Theme.Ink);
                g.TextRight(572, ly + 12, Money(DataFiles.GetRecord(row, "Total Cost")), 7.5f, false, Theme.Ink);
                volume += InvoiceLineRow.ParseNumber(vol);
                total += InvoiceLineRow.ParseNumber(DataFiles.GetRecord(row, "Total Cost"));
            }

            float tableBottom = tableTop + max * rowH;
            g.Line(108, tableTop, 108, tableBottom);
            g.Line(314, tableTop, 314, tableBottom);
            g.Line(366, tableTop, 366, tableBottom);
            g.Line(414, tableTop, 414, tableBottom);
            g.Line(472, tableTop, 472, tableBottom);
            g.Line(532, tableTop, 532, tableBottom);

            float ty = tableBottom + 10;
            g.TextRight(414, ty + 12, "Total Volume", 8, true, Theme.Navy);
            g.Text(420, ty + 12, volume.ToString("0.###", CultureInfo.InvariantCulture), 8, false, Theme.Ink);
            g.TextRight(532, ty + 12, "Total Cost", 8, true, Theme.Navy);
            g.TextRight(572, ty + 12, total.ToString("0.00", CultureInfo.InvariantCulture), 8, false, Theme.Ink);

            g.Text(36, 776, "This document is a purchase order. Confirm item, cases, and weight on receipt.", 7, false, Theme.Muted);
            return g;
        }

        /// <summary>Format a quantity, leaving blank cells empty instead of printing 0.</summary>
        private static string Qty(string? value)
        {
            decimal n = InvoiceLineRow.ParseNumber(value);
            return n == 0 && string.IsNullOrWhiteSpace(value) ? "" : n.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>Format money, leaving blank cells empty instead of printing 0.00.</summary>
        private static string Money(string? value)
        {
            decimal n = InvoiceLineRow.ParseNumber(value);
            return n == 0 && string.IsNullOrWhiteSpace(value) ? "" : n.ToString("0.00", CultureInfo.InvariantCulture);
        }

        /// <summary>Trim text that would overflow a PDF column, adding a trailing period.</summary>
        private static string Clip(string? text, int max)
        {
            text ??= "";
            return text.Length <= max ? text : text[..(max - 1)] + ".";
        }
    }
}
