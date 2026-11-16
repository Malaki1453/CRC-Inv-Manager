using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>CRC letterhead statement of open invoices for one customer or vendor.</summary>
    internal static class AgingStatementDocument
    {
        private const float RowH = 18f;
        private const float FooterY = 748f;
        private const float TableLeft = 36f;
        private const float TableWidth = 540f;

        /// <summary>Draw the statement and open it in the in-app viewer with a named file.</summary>
        public static void Show(AgingParty party)
        {
            if (party.Invoices.Count == 0)
            {
                MessageBox.Show(
                    "There are no open invoices to put on a statement.",
                    "Statement PDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            byte[] pdf = Draw(party);
            string label = party.Received ? "Payables" : "Statement";
            string title = label + " — " + party.Name;
            string fileName = PdfFile.Sanitize(title + ".pdf");
            DataFiles.OpenGeneratedPdf(pdf, fileName, title);
        }

        /// <summary>Letterhead, party block, invoice table, and bucket totals. Extra invoices continue on later pages.</summary>
        private static byte[] Draw(AgingParty party)
        {
            var pages = new List<PdfDraw>();
            PdfDraw g = NewPage(party, pages.Count, out float y);
            y = DrawTableHeader(g, y);
            int rowIndex = 0;

            foreach (var line in party.Invoices)
            {
                if (y + RowH > FooterY)
                {
                    DrawFooter(g, party, pages.Count + 1);
                    pages.Add(g);
                    g = NewPage(party, pages.Count, out y);
                    y = DrawTableHeader(g, y);
                }

                DrawInvoiceRow(g, y, line, rowIndex++);
                y += RowH;
            }

            if (y + 110 > FooterY)
            {
                DrawFooter(g, party, pages.Count + 1);
                pages.Add(g);
                g = NewPage(party, pages.Count, out y);
            }

            y += 8;
            DrawTotals(g, y, party);
            DrawFooter(g, party, pages.Count + 1);
            pages.Add(g);

            if (pages.Count > 1)
            {
                for (int i = 0; i < pages.Count; i++)
                    pages[i].TextRight(PdfLetterhead.Right, 776, $"Page {i + 1} of {pages.Count}", 7, false, Theme.Muted);
            }

            return PdfDraw.Combine(pages);
        }

        /// <summary>Letterhead, title, and customer or vendor fields. Continuation pages skip the address block.</summary>
        private static PdfDraw NewPage(AgingParty party, int pageIndex, out float y)
        {
            var g = new PdfDraw();
            string email = party.Received ? PdfLetterhead.PurchaseEmail : PdfLetterhead.InvoiceEmail;
            y = PdfLetterhead.Draw(g, brand: true, email: email);
            string heading = party.Received ? "PAYABLES STATEMENT" : "STATEMENT OF ACCOUNT";
            if (pageIndex > 0)
                heading += " (CONTINUED)";
            PdfLetterhead.TitleBar(g, y, heading);
            y += 28;
            y = PdfFieldFlow.Pair(
                g, y,
                party.Received ? "Vendor:" : "Customer:", party.Name,
                party.Received ? "Vendor Code:" : "Customer Code:", party.Code);
            // Address and statement date only on the first page so later pages start at the table.
            if (pageIndex == 0)
            {
                y = PdfFieldFlow.Pair(
                    g, y,
                    "Address:", party.Address,
                    "Statement Date:", DateTime.Today.ToString("MM/dd/yy", CultureInfo.InvariantCulture));
                string note = party.Received
                    ? "Open invoices still payable to this vendor."
                    : "Open invoices still due from this customer.";
                g.Text(PdfLetterhead.Left, y + 4, note, 8, false, Theme.Muted);
                y += 18;
            }

            y += 8;
            return g;
        }

        /// <summary>Navy column strip for the invoice list.</summary>
        private static float DrawTableHeader(PdfDraw g, float y)
        {
            g.Fill(TableLeft, y, TableWidth, 16, Theme.Navy);
            g.Text(42, y + 11, "INVOICE #", 6.5f, true, Theme.Cream);
            g.Text(126, y + 11, "DATE", 6.5f, true, Theme.Cream);
            g.Text(184, y + 11, "DUE DATE", 6.5f, true, Theme.Cream);
            g.Text(252, y + 11, "REF", 6.5f, true, Theme.Cream);
            g.TextRight(380, y + 11, "AMOUNT", 6.5f, true, Theme.Cream);
            g.TextRight(456, y + 11, "PAID", 6.5f, true, Theme.Cream);
            g.TextRight(532, y + 11, "BALANCE", 6.5f, true, Theme.Cream);
            g.Text(540, y + 11, "DAYS", 6.5f, true, Theme.Cream);
            return y + 16;
        }

        /// <summary>One invoice line; zebra stripe odd rows so the list is easier to scan.</summary>
        private static void DrawInvoiceRow(PdfDraw g, float y, AgingInvoiceLine line, int rowIndex)
        {
            if (rowIndex % 2 == 1)
                g.Fill(TableLeft + 0.5f, y, TableWidth - 1, RowH, Theme.GridAlt);
            g.Rect(TableLeft, y, TableWidth, RowH);
            g.Text(42, y + 12, Clip(line.Number, 14), 7.5f, false, Theme.Ink);
            g.Text(126, y + 12, ShortDate(line.Date), 7.5f, false, Theme.Ink);
            g.Text(184, y + 12, ShortDate(line.DueDate), 7.5f, false, Theme.Ink);
            g.Text(252, y + 12, Clip(line.Reference, 10), 7.5f, false, Theme.Ink);
            g.TextRight(380, y + 12, MoneyFormat.Display(line.Amount), 7.5f, false, Theme.Ink);
            g.TextRight(456, y + 12, MoneyFormat.Display(line.Paid), 7.5f, false, Theme.Ink);
            g.TextRight(532, y + 12, MoneyFormat.Display(line.Outstanding), 7.5f, false, Theme.Ink);
            string days = line.Days == int.MinValue ? "-" : line.Days.ToString(CultureInfo.InvariantCulture);
            g.Text(540, y + 12, days, 7.5f, false, Theme.Ink);
        }

        /// <summary>Bucket subtotals and the amount still due.</summary>
        private static void DrawTotals(PdfDraw g, float y, AgingParty party)
        {
            decimal current = 0, d30 = 0, d60 = 0, d90 = 0, older = 0, none = 0, total = 0;
            foreach (var line in party.Invoices)
            {
                total += line.Outstanding;
                switch (line.Bucket)
                {
                    case "Current": current += line.Outstanding; break;
                    case "1–30": d30 += line.Outstanding; break;
                    case "31–60": d60 += line.Outstanding; break;
                    case "61–90": d90 += line.Outstanding; break;
                    case "No due date": none += line.Outstanding; break;
                    default: older += line.Outstanding; break;
                }
            }

            const float boxW = 200f;
            float boxX = PdfLetterhead.Right - boxW;
            float boxH = none > 0 ? 112f : 96f;
            g.Rect(boxX, y, boxW, boxH);
            float row = y;
            TotalLine(g, boxX, row, "Current", current);
            row += 16;
            TotalLine(g, boxX, row, "1-30 days", d30);
            row += 16;
            TotalLine(g, boxX, row, "31-60 days", d60);
            row += 16;
            TotalLine(g, boxX, row, "61-90 days", d90);
            row += 16;
            TotalLine(g, boxX, row, "90+ days", older);
            if (none > 0)
            {
                row += 16;
                TotalLine(g, boxX, row, "No due date", none);
            }

            row += 16;
            g.Fill(boxX, row, boxW, 16, Theme.Navy);
            g.Text(boxX + 8, row + 11, party.Received ? "TOTAL PAYABLE" : "TOTAL DUE", 8, true, Theme.Cream);
            g.TextRight(PdfLetterhead.Right - 8, row + 11, MoneyFormat.Display(total), 8, true, Theme.Cream);
        }

        /// <summary>One bucket row inside the totals box.</summary>
        private static void TotalLine(PdfDraw g, float boxX, float y, string label, decimal value)
        {
            g.Text(boxX + 8, y + 12, label, 8, true, Theme.Navy);
            g.TextRight(PdfLetterhead.Right - 8, y + 12, MoneyFormat.Display(value), 8, false, Theme.Ink);
            g.Line(boxX, y + 16, PdfLetterhead.Right, y + 16);
        }

        /// <summary>Interest / remit note under the table.</summary>
        private static void DrawFooter(PdfDraw g, AgingParty party, int pageNumber)
        {
            _ = pageNumber;
            if (party.Received)
            {
                g.Text(36, FooterY, "THESE INVOICES REMAIN OPEN AND PAYABLE TO THE VENDOR.", 6.5f, false, Theme.Muted);
                g.Text(36, FooterY + 12, "FIGURES MATCH THE AGING REPORT AT THE STATEMENT DATE.", 6.5f, false, Theme.Muted);
            }
            else
            {
                g.Text(36, FooterY, "PLEASE REMIT THE TOTAL DUE. INTEREST MAY BE CHARGED AT 1.5% PER MONTH ON OVERDUE ACCOUNTS.", 6.5f, false, Theme.Muted);
                g.Text(36, FooterY + 12, "IMPORTANT: NO CLAIMS OR REDUCTIONS ALLOWED UNLESS MADE IMMEDIATELY ON RECEIPT OF GOODS.", 6.5f, false, Theme.Muted);
            }
        }

        /// <summary>mm/dd/yy when the cell parses; otherwise the stored text clipped.</summary>
        private static string ShortDate(string text)
        {
            if (DateTime.TryParse(text, out var date))
                return date.ToString("MM/dd/yy", CultureInfo.InvariantCulture);
            return Clip(text, 8);
        }

        /// <summary>Trim to a column width so invoice numbers do not run into the date.</summary>
        private static string Clip(string text, int max)
        {
            text = (text ?? "").Trim();
            if (text.Length <= max)
                return text;
            return text[..max];
        }
    }
}
