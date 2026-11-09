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
            return DataFiles.ReplaceStoredPdf(
                DataFiles.PdfKindSalesOrder,
                draft.SoNumber.Trim(),
                fileName,
                Draw(draft).ToPdf());
        }

        /// <summary>Rebuild the sales-order PDF from stored sales lines, or null when none exist.</summary>
        public static string? SaveFromSo(string? soNumber)
        {
            var rows = DataFiles.FindSalesBySo(soNumber);
            return rows.Count == 0 ? null : Save(DraftFromRows(rows));
        }

        /// <summary>Header and lines from sales-table rows plus the customer master for addresses.</summary>
        private static SalesOrderDraft DraftFromRows(IReadOnlyList<Dictionary<string, string>> rows)
        {
            var first = rows[0];
            string code = DataFiles.GetRecord(first, "Customer Code");
            string name = DataFiles.GetRecord(first, "Customer");
            var customer = FindCustomer(code, name);
            string billing = customer == null ? "" : DataFiles.CustomerBillingAddress(customer);
            string ship = customer == null ? billing : DataFiles.CustomerShippingAddress(customer);
            var warehouses = customer == null
                ? new List<DataFiles.CustomerWarehouse>()
                : DataFiles.CustomerWarehouses(customer);
            DateTime order = ParseDate(DataFiles.GetRecord(first, "Order Date")) ?? DateTime.Today;
            DateTime release = ParseDate(DataFiles.GetRecord(first, "Ship Date")) ?? order;
            DateTime due = ParseDate(DataFiles.GetRecord(first, "Due Date"))
                ?? ParseDate(DataFiles.GetRecord(first, DataFiles.PaymentTermColumn))
                ?? DataFiles.DueDateFromTerms(order, customer == null ? "" : DataFiles.GetRecord(customer, "Terms"));
            return new SalesOrderDraft
            {
                SoNumber = DataFiles.GetRecord(first, "SO #"),
                OrderDate = order,
                ReleaseDate = release,
                DueDate = due,
                CustomerCode = code,
                CustomerName = name,
                Address = ship,
                BillingAddress = billing,
                CustomerPhone = customer == null ? "" : DataFiles.GetRecord(customer, "Phone"),
                Contact = customer == null ? "" : DataFiles.GetRecord(customer, "Contact Name"),
                Email = customer == null ? "" : DataFiles.GetRecord(customer, "Email"),
                ContactPhone = customer == null ? "" : DataFiles.GetRecord(customer, "Phone"),
                Warehouse = warehouses.Count > 0 ? warehouses[0].Name : DataFiles.GetRecord(first, "Location"),
                CustomerPo = DataFiles.SalePo(first),
                Status = DataFiles.GetRecord(first, "Status"),
                FreightTerms = DataFiles.GetRecord(first, DataFiles.FreightTermsColumn),
                Lines = rows.Select(row => new SalesOrderLine
                {
                    PoNumber = DataFiles.SalePo(row),
                    ItemCode = DataFiles.GetRecord(row, "Item Code"),
                    LotNumber = DataFiles.SaleLot(row),
                    Description = DataFiles.GetRecord(row, "Description"),
                    Coo = DataFiles.GetRecord(row, "COO"),
                    UnitSize = DataFiles.GetRecord(row, "Pack Size"),
                    Cases = DataFiles.GetRecord(row, "CS"),
                    Volume = DataFiles.GetRecord(row, "Volume"),
                    Price = DataFiles.GetRecord(row, "Sell Price / LB"),
                    Amount = DataFiles.GetRecord(row, "Amount")
                }).ToList()
            };
        }

        /// <summary>Visible customer whose code or name matches the sale.</summary>
        private static Dictionary<string, string>? FindCustomer(string code, string name)
        {
            foreach (var record in DataFiles.ReadRecords(DataFiles.Customers))
            {
                bool byCode = code.Length > 0 &&
                    DataFiles.GetRecord(record, "Code").Equals(code, StringComparison.OrdinalIgnoreCase);
                bool byName = name.Length > 0 &&
                    DataFiles.GetRecord(record, "Name").Equals(name, StringComparison.OrdinalIgnoreCase);
                if (byCode || byName)
                    return record;
            }

            return null;
        }

        /// <summary>Parse a stored date cell, or null when the cell is blank or not a date.</summary>
        private static DateTime? ParseDate(string? text)
        {
            return DateTime.TryParse(text, out var date) ? date.Date : null;
        }

        /// <summary>Lay out letterhead, customer/ship-to, and the pick-ticket item table.</summary>
        private static PdfDraw Draw(SalesOrderDraft draft)
        {
            var g = new PdfDraw();
            float y = PdfLetterhead.Draw(g, email: PdfLetterhead.SalesOrderEmail);
            PdfLetterhead.TitleBar(g, y, "CUSTOMER");
            y += 28;

            y = PdfFieldFlow.Pair(g, y, "Customer:", draft.CustomerName, "Customer Code:", draft.CustomerCode, 9);
            y = PdfFieldFlow.Pair(
                g, y, "Contact:", draft.Contact, "Phone:",
                FirstNonEmpty(draft.ContactPhone, draft.CustomerPhone));
            y = PdfFieldFlow.Pair(g, y, "Ship To:", draft.Address, "Email:", draft.Email);

            string billing = (draft.BillingAddress ?? "").Trim();
            string ship = (draft.Address ?? "").Trim();
            if (billing.Length > 0 &&
                !billing.Equals(ship, StringComparison.OrdinalIgnoreCase))
                y = PdfFieldFlow.Left(g, y, "Bill To:", billing, 8, 460);

            y += 10;
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
            g.Text(490, y + 13, PdfItemText.Clip(draft.Warehouse, 16), 8, false, Theme.Ink);

            y += 18;
            y += 10;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "FREIGHT TERMS", 6.5f, true, Theme.Cream);
            g.Text(280, y + 11, "PAYMENT TERM", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, draft.FreightTerms, 8, false, Theme.Ink);
            g.Text(280, y + 13, draft.DueDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);

            y += 18;
            y += 12;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "ITEM", 6.5f, true, Theme.Cream);
            g.Text(114, y + 11, "LOT #", 6.5f, true, Theme.Cream);
            g.Text(196, y + 11, "DESCRIPTION", 6.5f, true, Theme.Cream);
            g.Text(372, y + 11, "UNIT", 6.5f, true, Theme.Cream);
            g.Text(430, y + 11, "CS", 6.5f, true, Theme.Cream);
            g.Text(500, y + 11, "VOLUME", 6.5f, true, Theme.Cream);

            y += 16;
            float tableTop = y;
            float rowH = 26;
            int max = Math.Max(8, draft.Lines.Count);
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
                g.Text(42, ly + 12, PdfItemText.Clip(line.ItemCode, 12), 7.5f, false, Theme.Ink);
                g.Text(114, ly + 12, PdfItemText.Clip(line.LotNumber, 14), 7.5f, false, Theme.Ink);
                PdfItemText.Draw(g, 196, ly, line.Description, line.ItemCode, line.Coo, 28);
                g.Text(372, ly + 12, PdfItemText.Clip(line.UnitSize, 8), 7.5f, false, Theme.Ink);
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
