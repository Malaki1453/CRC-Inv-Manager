using System.Globalization;

namespace CastRightCatchInvManagement
{
    /// <summary>Draws an invoice PDF from an InvoiceDraft, including company info from Settings.</summary>
    internal static class InvoiceDocument
    {
        /// <summary>Draw an invoice PDF from the draft and store it by invoice number.</summary>
        public static string Save(InvoiceDraft draft)
        {
            string party = draft.Received
                ? FirstNonEmpty(draft.VendorName, draft.VendorCode, draft.CustomerName)
                : FirstNonEmpty(draft.CustomerCode, draft.CustomerName);
            string fileName = SanitizeFile($"Invoice {draft.InvoiceNumber} - {party}.pdf");
            return DataFiles.ReplaceStoredPdf(
                DataFiles.PdfKindInvoice,
                draft.InvoiceNumber.Trim(),
                fileName,
                Draw(draft).ToPdf());
        }

        /// <summary>Lay out letterhead or vendor header, customer fields, item table, and totals.</summary>
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
            string partyName = received ? draft.VendorName : draft.CustomerName;
            string partyCode = received ? draft.VendorCode : draft.CustomerCode;

            float y = PdfLetterhead.Draw(g, brand: !received, email: PdfLetterhead.InvoiceEmail);
            // Received invoices print the vendor as issuer instead of our letterhead.
            if (received)
            {
                g.Fill(36, y, 4, 44, Theme.Gold);
                g.Text(50, y + 14, company.ToUpperInvariant(), 13, true, Theme.Navy);
                g.Text(50, y + 28, address, 8, false, Theme.Muted);
                g.Text(50, y + 40, string.IsNullOrWhiteSpace(email) ? phone : phone + "    " + email, 8, false, Theme.Muted);
                y += 50;
            }

            PdfLetterhead.TitleBar(g, y, received ? "VENDOR" : "CUSTOMER");
            y += 28;
            y = PdfFieldFlow.Pair(
                g, y,
                received ? "Vendor:" : "Customer:", partyName,
                received ? "Vendor Code:" : "Customer Code:", partyCode,
                9);
            y = PdfFieldFlow.Pair(g, y, "Sold To:", draft.SoldTo, "Ship To:", draft.ShipTo);

            y += 10;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "INVOICE #", 6.5f, true, Theme.Cream);
            g.Text(150, y + 11, "DATE", 6.5f, true, Theme.Cream);
            g.Text(250, y + 11, received ? "PO #" : "SO #", 6.5f, true, Theme.Cream);
            g.Text(360, y + 11, "TERMS", 6.5f, true, Theme.Cream);
            g.Text(470, y + 11, "SHIP DATE", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, draft.InvoiceNumber, 8, false, Theme.Ink);
            g.Text(150, y + 13, draft.InvoiceDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(250, y + 13, orderNo, 8, false, Theme.Ink);
            g.Text(360, y + 13, defaultTerms, 8, false, Theme.Ink);
            g.Text(470, y + 13, draft.ShipDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);

            y += 18;
            y += 10;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "SHIP VIA", 6.5f, true, Theme.Cream);
            g.Text(250, y + 11, received ? "CONTACT" : "SALES REP", 6.5f, true, Theme.Cream);
            g.Text(470, y + 11, "TAX ID", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, draft.ShipVia, 8, false, Theme.Ink);
            g.Text(250, y + 13, draft.SalesRep, 8, false, Theme.Ink);
            g.Text(470, y + 13, ein, 8, false, Theme.Ink);

            y += 18;
            y += 12;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "ITEM", 6.5f, true, Theme.Cream);
            g.Text(114, y + 11, "LOT #", 6.5f, true, Theme.Cream);
            g.Text(196, y + 11, "DESCRIPTION", 6.5f, true, Theme.Cream);
            g.Text(372, y + 11, "WEIGHT", 6.5f, true, Theme.Cream);
            g.Text(440, y + 11, "PRICE", 6.5f, true, Theme.Cream);
            g.Text(538, y + 11, "AMOUNT", 6.5f, true, Theme.Cream);

            y += 16;
            float tableTop = y;
            float rowH = 26;
            int rows = Math.Max(6, draft.Lines.Count);
            g.Rect(36, y, 540, rows * rowH);

            for (int i = 0; i < draft.Lines.Count && i < rows; i++)
            {
                var line = draft.Lines[i];
                float ly = y + i * rowH;
                // Zebra-stripe odd rows so the item table is easier to scan.
                if (i % 2 == 1)
                    g.Fill(36.5f, ly, 539, rowH, Theme.GridAlt);
                g.Text(42, ly + 12, PdfItemText.Clip(line.ProductId, 12), 7.5f, false, Theme.Ink);
                g.Text(114, ly + 12, PdfItemText.Clip(line.LotNumber, 14), 7.5f, false, Theme.Ink);
                PdfItemText.Draw(g, 196, ly, line.Description, line.ProductId, line.Coo, 28);
                g.Text(372, ly + 12, FormatQty(line.Weight), 7.5f, false, Theme.Ink);
                g.Text(440, ly + 12, FormatMoney(InvoiceLineRow.ParseNumber(line.Price)), 7.5f, false, Theme.Ink);
                g.TextRight(572, ly + 12, FormatMoney(line.Amount), 7.5f, false, Theme.Ink);
            }

            float tableBottom = tableTop + rows * rowH;
            g.Line(108, tableTop, 108, tableBottom);
            g.Line(190, tableTop, 190, tableBottom);
            g.Line(366, tableTop, 366, tableBottom);
            g.Line(434, tableTop, 434, tableBottom);
            g.Line(528, tableTop, 528, tableBottom);

            float ty = tableBottom + 10;
            g.TextRight(366, ty + 12, "Total Weight", 8, true, Theme.Navy);
            g.Text(372, ty + 12, FormatQty(draft.TotalWeight.ToString(CultureInfo.InvariantCulture)), 8, false, Theme.Ink);
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

            // Footer wording depends on whether we issued or received the invoice.
            if (received)
            {
                g.Text(36, 748, "VENDOR INVOICE AS RECEIVED. CAST RIGHT CATCH IS THE RECEIVING COMPANY.", 6.5f, false, Theme.Muted);
                g.Text(36, 760, "IMPORTANT: NO CLAIMS OR REDUCTIONS ALLOWED UNLESS MADE IMMEDIATELY ON RECEIPT OF GOODS.", 6.5f, false, Theme.Muted);
                g.Text(36, 776, "RECEIVED BY ________________________________", 8, false, Theme.Ink);
            }
            // Issued invoices print our overdue-interest and customer-signature footer.
            else
            {
                g.Text(36, 748, "INTEREST MAY BE CHARGED AT THE RATE OF 1.5% PER MONTH ON ALL OVERDUE ACCOUNTS.", 6.5f, false, Theme.Muted);
                g.Text(36, 760, "IMPORTANT: NO CLAIMS OR REDUCTIONS ALLOWED UNLESS MADE IMMEDIATELY ON RECEIPT OF GOODS.", 6.5f, false, Theme.Muted);
                g.Text(36, 776, "CUSTOMER SIGNATURE ________________________________", 8, false, Theme.Ink);
            }

            return g;
        }

        /// <summary>One discount/freight/tax row; zero amounts stay blank instead of printing 0.00.</summary>
        private static void DrawTotalRow(PdfDraw g, float y, string label, decimal value)
        {
            g.TextRight(528, y + 12, label, 8, true, Theme.Navy);
            // Zero discount/freight/tax stays blank so the totals box is not noisy.
            if (value != 0)
                g.TextRight(572, y + 12, FormatMoney(value), 8, false, Theme.Ink);
            g.Line(422, y + 16, 576, y + 16);
        }

        /// <summary>Format a money amount with two decimal places.</summary>
        private static string FormatMoney(decimal value) => MoneyFormat.Display(value);

        /// <summary>Format a quantity, leaving blank cells empty instead of printing 0.</summary>
        private static string FormatQty(string? value)
        {
            decimal n = InvoiceLineRow.ParseNumber(value);
            // A blank cell should not print 0; a typed zero still prints.
            if (n == 0 && string.IsNullOrWhiteSpace(value))
                return "";
            return n.ToString("0.###", CultureInfo.InvariantCulture);
        }

        /// <summary>First non-blank value, used for company and party fallbacks.</summary>
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
