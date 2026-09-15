using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Sale PDF stored by customer PO #.</summary>
    internal static class SaleDocument
    {
        /// <summary>Build and store a sale PDF for this customer PO, or null when no lines exist.</summary>
        public static string? SaveFromPo(string? po)
        {
            var rows = DataFiles.FindSalesByPo(po);
            return rows.Count == 0 ? null : Save(rows);
        }

        /// <summary>Draw a sale PDF from the given lines and store it by customer PO #.</summary>
        public static string Save(IReadOnlyList<Dictionary<string, string>> rows)
        {
            // A sale PDF needs at least one product line.
            if (rows.Count == 0)
                throw new InvalidOperationException("This sale has no lines.");

            var first = rows[0];
            string po = DataFiles.SalePo(first);
            string customer = DataFiles.GetRecordAny(first, "Customer", "Customer Code");
            string title = "Sale " + po + (customer.Length > 0 ? " - " + customer : "");
            return PdfFile.Save(DataFiles.PdfKindSale, po, title, BuildPage(rows));
        }

        /// <summary>Lay out letterhead, customer fields, and the item table for the sale PDF.</summary>
        private static PdfDraw BuildPage(IReadOnlyList<Dictionary<string, string>> rows)
        {
            var first = rows[0];
            var g = new PdfDraw();
            float y = PdfLetterhead.Draw(g);
            PdfLetterhead.TitleBar(g, y, "SALE");
            y += 28;
            g.Text(36, y, "Customer:", 8, true, Theme.Navy);
            g.Text(110, y, DataFiles.GetRecord(first, "Customer"), 9, false, Theme.Ink);
            g.Text(330, y, "Customer Code:", 8, true, Theme.Navy);
            g.Text(430, y, DataFiles.GetRecord(first, "Customer Code"), 9, false, Theme.Ink);

            y += 14;
            g.Text(36, y, "Terms:", 8, true, Theme.Navy);
            g.Text(110, y, DataFiles.GetRecord(first, "Customer Terms"), 8, false, Theme.Ink);
            g.Text(330, y, "Status:", 8, true, Theme.Navy);
            g.Text(430, y, DataFiles.GetRecord(first, "Status"), 8, false, Theme.Ink);

            y += 22;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "CUSTOMER PO", 6.5f, true, Theme.Cream);
            g.Text(186, y + 11, "SO #", 6.5f, true, Theme.Cream);
            g.Text(300, y + 11, "SHIP DATE", 6.5f, true, Theme.Cream);
            g.Text(420, y + 11, "DUE DATE", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, DataFiles.SalePo(first), 8, false, Theme.Ink);
            g.Text(186, y + 13, DataFiles.GetRecord(first, "SO #"), 8, false, Theme.Ink);
            g.Text(300, y + 13, DataFiles.GetRecord(first, "Ship Date"), 8, false, Theme.Ink);
            g.Text(420, y + 13, DataFiles.GetRecord(first, "Due Date"), 8, false, Theme.Ink);

            y += 26;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "ITEM", 6.5f, true, Theme.Cream);
            g.Text(114, y + 11, "LOT #", 6.5f, true, Theme.Cream);
            g.Text(196, y + 11, "DESCRIPTION", 6.5f, true, Theme.Cream);
            g.Text(372, y + 11, "CS", 6.5f, true, Theme.Cream);
            g.Text(420, y + 11, "VOLUME", 6.5f, true, Theme.Cream);
            g.Text(478, y + 11, "PRICE / LB", 6.5f, true, Theme.Cream);
            g.Text(538, y + 11, "AMOUNT", 6.5f, true, Theme.Cream);

            y += 16;
            float tableTop = y;
            float rowH = 18;
            int max = Math.Max(12, rows.Count);
            g.Rect(36, y, 540, max * rowH);

            decimal volume = 0;
            decimal amount = 0;
            for (int i = 0; i < rows.Count && i < max; i++)
            {
                var row = rows[i];
                float ly = y + i * rowH;
                // Zebra-stripe odd rows so the item table is easier to scan.
                if (i % 2 == 1)
                    g.Fill(36.5f, ly, 539, rowH, Theme.GridAlt);
                g.Text(42, ly + 12, Clip(DataFiles.GetRecord(row, "Item Code"), 12), 7.5f, false, Theme.Ink);
                g.Text(114, ly + 12, Clip(DataFiles.SaleLot(row), 14), 7.5f, false, Theme.Ink);
                g.Text(196, ly + 12, Clip(DataFiles.GetRecord(row, "Description"), 28), 7.5f, false, Theme.Ink);
                g.Text(372, ly + 12, Qty(DataFiles.GetRecord(row, "CS")), 7.5f, false, Theme.Ink);
                string vol = DataFiles.GetRecord(row, "Volume");
                g.Text(420, ly + 12, Qty(vol), 7.5f, false, Theme.Ink);
                g.Text(478, ly + 12, Money(DataFiles.GetRecord(row, "Sell Price / LB")), 7.5f, false, Theme.Ink);
                g.TextRight(572, ly + 12, Money(DataFiles.GetRecord(row, "Amount")), 7.5f, false, Theme.Ink);
                volume += InvoiceLineRow.ParseNumber(vol);
                amount += InvoiceLineRow.ParseNumber(DataFiles.GetRecord(row, "Amount"));
            }

            float tableBottom = tableTop + max * rowH;
            g.Line(108, tableTop, 108, tableBottom);
            g.Line(190, tableTop, 190, tableBottom);
            g.Line(366, tableTop, 366, tableBottom);
            g.Line(414, tableTop, 414, tableBottom);
            g.Line(472, tableTop, 472, tableBottom);
            g.Line(532, tableTop, 532, tableBottom);

            float ty = tableBottom + 10;
            g.TextRight(414, ty + 12, "Total Volume", 8, true, Theme.Navy);
            g.Text(420, ty + 12, volume.ToString("0.###", CultureInfo.InvariantCulture), 8, false, Theme.Ink);
            g.TextRight(532, ty + 12, "Total", 8, true, Theme.Navy);
            g.TextRight(572, ty + 12, amount.ToString("0.00", CultureInfo.InvariantCulture), 8, false, Theme.Ink);

            g.Text(36, 776, "This document is a sale record. Confirm item, lot, cases, and weight before shipping.", 7, false, Theme.Muted);
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
