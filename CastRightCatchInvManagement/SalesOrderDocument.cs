using System.Globalization;
using System.Text;

namespace CastRightCatchInvManagement
{
    /// <summary>Draws a sales-order / pick-ticket PDF from a SalesOrderDraft.</summary>
    internal static class SalesOrderDocument
    {
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
                Build(draft));
        }

        private static byte[] Build(SalesOrderDraft draft)
        {
            string content = BuildPage(draft);
            var body = Encoding.ASCII.GetBytes(content);
            var objects = new List<byte[]>
            {
                Obj("<< /Type /Catalog /Pages 2 0 R >>"),
                Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"),
                Obj("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << /Font << /F1 5 0 R /F2 6 0 R >> >> >>"),
                Stream(body),
                Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"),
                Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>")
            };

            using var ms = new MemoryStream();
            void Write(string text) => ms.Write(Encoding.ASCII.GetBytes(text));

            Write("%PDF-1.4\n");
            var offsets = new List<long> { 0 };
            for (int i = 0; i < objects.Count; i++)
            {
                offsets.Add(ms.Position);
                Write($"{i + 1} 0 obj\n");
                ms.Write(objects[i], 0, objects[i].Length);
                Write("\nendobj\n");
            }

            long xref = ms.Position;
            Write($"xref\n0 {objects.Count + 1}\n");
            Write("0000000000 65535 f \n");
            for (int i = 1; i < offsets.Count; i++)
                Write($"{offsets[i]:0000000000} 00000 n \n");

            Write($"trailer << /Size {objects.Count + 1} /Root 1 0 R >>\nstartxref\n{xref}\n%%EOF");
            return ms.ToArray();
        }

        private static string BuildPage(SalesOrderDraft draft)
        {
            var g = new PdfDraw();
            string company = FirstNonEmpty(AppState.BusinessName, "Cast Right Catch Co, LLC");
            string address = FirstNonEmpty(AppState.Address, "P.O Box 1064");
            string phone = FirstNonEmpty(AppState.Phone, "(253) 540-2631");

            g.Fill(36, 36, 4, 58, Theme.Gold);
            g.Text(430, 48, company, 11, true, Theme.Navy);
            g.Text(430, 62, address, 8, false, Theme.Muted);
            if (!address.Contains("Orting", StringComparison.OrdinalIgnoreCase) &&
                AppState.Address.Length == 0)
                g.Text(430, 74, "Orting, WA 98360", 8, false, Theme.Muted);
            g.Text(430, 86, phone, 8, false, Theme.Muted);

            g.Fill(36, 102, 540, 22, Theme.Navy);
            g.Text(42, 117, "SALES ORDER / PICK TICKET", 12, true, Theme.Cream);

            float y = 138;
            g.Text(36, y, "Customer Code:", 8, true, Theme.Navy);
            g.Text(130, y, draft.CustomerCode, 9, false, Theme.Ink);
            g.Text(330, y, "Contact:", 8, true, Theme.Navy);
            g.Text(390, y, draft.Contact, 9, false, Theme.Ink);

            y += 14;
            g.Text(36, y, "Customer:", 8, true, Theme.Navy);
            g.Text(130, y, draft.CustomerName, 9, false, Theme.Ink);
            g.Text(330, y, "Email:", 8, true, Theme.Navy);
            g.Text(390, y, Clip(draft.Email, 36), 8, false, Theme.Ink);

            y += 14;
            g.Text(36, y, "Ship To:", 8, true, Theme.Navy);
            var addressLines = (draft.Address ?? "").Replace("\r", "").Split('\n');
            g.Text(130, y, addressLines.Length > 0 ? addressLines[0] : "", 8, false, Theme.Ink);
            g.Text(330, y, "Phone:", 8, true, Theme.Navy);
            g.Text(390, y, FirstNonEmpty(draft.ContactPhone, draft.CustomerPhone), 8, false, Theme.Ink);

            y += 14;
            if (addressLines.Length > 1)
                g.Text(130, y, addressLines[1], 8, false, Theme.Ink);
            if (draft.CustomerPhone.Length > 0 && draft.ContactPhone.Length > 0)
            {
                g.Text(36, y, "Phone:", 8, true, Theme.Navy);
                g.Text(130, y, draft.CustomerPhone, 8, false, Theme.Ink);
            }

            y += 22;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "WAREHOUSE", 6.5f, true, Theme.Cream);
            g.Text(186, y + 11, "RELEASE DATE", 6.5f, true, Theme.Cream);
            g.Text(280, y + 11, "CUSTOMER PO", 6.5f, true, Theme.Cream);
            g.Text(400, y + 11, "SALES ORDER NO.", 6.5f, true, Theme.Cream);
            g.Text(508, y + 11, "ORDER DATE", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, draft.Warehouse, 8, false, Theme.Ink);
            g.Text(186, y + 13, draft.ReleaseDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(280, y + 13, draft.CustomerPo, 8, false, Theme.Ink);
            g.Text(400, y + 13, draft.SoNumber, 8, false, Theme.Ink);
            g.Text(508, y + 13, draft.OrderDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);

            y += 26;
            g.Text(36, y, "Freight Co:", 8, true, Theme.Navy);
            g.Text(110, y, draft.FreightCompany, 8, false, Theme.Ink);
            g.Text(330, y, "Freight Terms:", 8, true, Theme.Navy);
            g.Text(420, y, draft.FreightTerms, 8, false, Theme.Ink);

            y += 18;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "ITEM CODE", 6.5f, true, Theme.Cream);
            g.Text(114, y + 11, "LOT NO", 6.5f, true, Theme.Cream);
            g.Text(196, y + 11, "DESCRIPTION", 6.5f, true, Theme.Cream);
            g.Text(390, y + 11, "UNIT SIZE", 6.5f, true, Theme.Cream);
            g.Text(462, y + 11, "CASES", 6.5f, true, Theme.Cream);
            g.Text(520, y + 11, "VOLUME (LBS)", 6.5f, true, Theme.Cream);

            y += 16;
            float tableTop = y;
            float rowH = 18;
            int rows = Math.Max(12, draft.Lines.Count);
            g.Rect(36, y, 540, rows * rowH);

            for (int i = 0; i < draft.Lines.Count && i < rows; i++)
            {
                var line = draft.Lines[i];
                float ly = y + i * rowH;
                if (i % 2 == 1)
                    g.Fill(36.5f, ly, 539, rowH, Theme.GridAlt);
                g.Text(42, ly + 12, Clip(line.ItemCode, 12), 7.5f, false, Theme.Ink);
                g.Text(114, ly + 12, Clip(line.LotNumber, 14), 7.5f, false, Theme.Ink);
                g.Text(196, ly + 12, Clip(line.Description, 32), 7.5f, false, Theme.Ink);
                g.Text(390, ly + 12, Clip(line.UnitSize, 10), 7.5f, false, Theme.Ink);
                g.Text(462, ly + 12, FormatQty(line.Cases), 7.5f, false, Theme.Ink);
                g.TextRight(572, ly + 12, FormatQty(line.Volume), 7.5f, false, Theme.Ink);
            }

            float tableBottom = tableTop + rows * rowH;
            g.Line(108, tableTop, 108, tableBottom);
            g.Line(190, tableTop, 190, tableBottom);
            g.Line(384, tableTop, 384, tableBottom);
            g.Line(456, tableTop, 456, tableBottom);
            g.Line(512, tableTop, 512, tableBottom);

            float ty = tableBottom + 10;
            g.TextRight(456, ty + 12, "Total Cases", 8, true, Theme.Navy);
            g.Text(462, ty + 12, FormatQty(draft.TotalCases.ToString(CultureInfo.InvariantCulture)), 8, false, Theme.Ink);
            g.TextRight(512, ty + 12, "Total Volume", 8, true, Theme.Navy);
            g.TextRight(572, ty + 12, FormatQty(draft.TotalVolume.ToString(CultureInfo.InvariantCulture)), 8, false, Theme.Ink);

            g.Text(36, 760, "This document is a sales order / pick ticket. Confirm lot, cases, and weight before shipping.", 7, false, Theme.Muted);
            g.Text(36, 776, "WAREHOUSE SIGNATURE ________________________________     DATE ______________", 8, false, Theme.Ink);

            return g.ToStream();
        }

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

        private static byte[] Obj(string body) => Encoding.ASCII.GetBytes(body);

        private static byte[] Stream(byte[] data)
        {
            var header = Encoding.ASCII.GetBytes($"<< /Length {data.Length} >>\nstream\n");
            var end = Encoding.ASCII.GetBytes("\nendstream");
            var result = new byte[header.Length + data.Length + end.Length];
            Buffer.BlockCopy(header, 0, result, 0, header.Length);
            Buffer.BlockCopy(data, 0, result, header.Length, data.Length);
            Buffer.BlockCopy(end, 0, result, header.Length + data.Length, end.Length);
            return result;
        }
    }
}
