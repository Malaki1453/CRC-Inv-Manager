using System.Globalization;
using System.Text;

namespace CastRightCatchInvManagement
{
    internal static class InvoiceDocument
    {
        public static string Save(InvoiceDraft draft)
        {
            string customer = string.IsNullOrWhiteSpace(draft.CustomerCode)
                ? draft.CustomerName
                : draft.CustomerCode;
            string fileName = SanitizeFile($"Invoice {draft.InvoiceNumber} - {customer}.pdf");
            return DataFiles.SaveStoredPdf(
                DataFiles.PdfKindInvoice,
                draft.InvoiceNumber.Trim(),
                fileName,
                Build(draft));
        }

        private static byte[] Build(InvoiceDraft draft)
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

        private static string BuildPage(InvoiceDraft draft)
        {
            var g = new PdfDraw();
            string company = FirstNonEmpty(AppState.BusinessName, "Cast Right Catch Co.");
            string address = FirstNonEmpty(AppState.Address, "PO Box 1064, Orting, WA 98360");
            string phone = FirstNonEmpty(AppState.Phone, "(253) 540-2631");
            string email = FirstNonEmpty(AppState.CompanyEmail, "jwatts@castrightcatch.com");
            string ein = FirstNonEmpty(AppState.Ein, "41-3723454");
            string defaultTerms = FirstNonEmpty(draft.Terms, AppState.PaymentTerms, "NET 15 DAYS");

            g.Fill(36, 36, 4, 70, Theme.Gold);
            g.Text(50, 48, company.ToUpperInvariant(), 16, true, Theme.Navy);
            g.Text(50, 66, address, 8, false, Theme.Muted);
            g.Text(50, 78, $"{phone}    {email}", 8, false, Theme.Muted);

            g.Text(306, 48, "INVOICE", 22, true, Theme.Navy, center: true);
            g.Rect(430, 36, 146, 42);
            g.Fill(430, 36, 146, 14, Theme.Navy);
            g.Text(454, 46, "INVOICE NO.", 6.5f, true, Theme.Cream);
            g.Text(508, 46, "DATE", 6.5f, true, Theme.Cream);
            g.Text(452, 66, draft.InvoiceNumber, 9, true, Theme.Ink, center: true, width: 52);
            g.Text(516, 66, draft.InvoiceDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink, center: true, width: 52);
            g.Line(494, 36, 494, 78);
            g.Text(430, 88, $"TAX ID# {ein}", 8, false, Theme.Ink);

            float y = 108;
            g.Fill(36, y, 540, 16, Theme.Navy);
            g.Text(42, y + 11, "SO #", 6.5f, true, Theme.Cream);
            g.Text(108, y + 11, "ORDER DATE", 6.5f, true, Theme.Cream);
            g.Text(186, y + 11, "TERMS", 6.5f, true, Theme.Cream);
            g.Text(280, y + 11, "SHIP VIA", 6.5f, true, Theme.Cream);
            g.Text(400, y + 11, "SALES REP", 6.5f, true, Theme.Cream);
            g.Text(478, y + 11, "SHIP DATE", 6.5f, true, Theme.Cream);
            g.Text(542, y + 11, "CUST ID", 6.5f, true, Theme.Cream);

            y += 16;
            g.Rect(36, y, 540, 18);
            g.Text(42, y + 13, draft.SoNumber, 8, false, Theme.Ink);
            g.Text(108, y + 13, draft.InvoiceDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(186, y + 13, defaultTerms, 8, false, Theme.Ink);
            g.Text(280, y + 13, draft.ShipVia, 8, false, Theme.Ink);
            g.Text(400, y + 13, draft.SalesRep, 8, false, Theme.Ink);
            g.Text(478, y + 13, draft.ShipDate.ToString("MM/dd/yyyy"), 8, false, Theme.Ink);
            g.Text(542, y + 13, draft.CustomerCode, 8, false, Theme.Ink);

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

            g.Text(36, 748, "INTEREST MAY BE CHARGED AT THE RATE OF 1.5% PER MONTH ON ALL OVERDUE ACCOUNTS.", 6.5f, false, Theme.Muted);
            g.Text(36, 760, "IMPORTANT: NO CLAIMS OR REDUCTIONS ALLOWED UNLESS MADE IMMEDIATELY ON RECEIPT OF GOODS.", 6.5f, false, Theme.Muted);
            g.Text(36, 776, "CUSTOMER SIGNATURE ________________________________", 8, false, Theme.Ink);

            return g.ToStream();
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

    internal sealed class PdfDraw
    {
        private readonly StringBuilder _s = new();
        private const float PageH = 792;

        public PdfDraw()
        {
            _s.Append("q\n");
        }

        public void Fill(float x, float yTop, float w, float h, Color color)
        {
            float y = PageH - yTop - h;
            Rgb(color);
            _s.Append(" rg ");
            _s.Append(F(x)); _s.Append(' '); _s.Append(F(y)); _s.Append(' ');
            _s.Append(F(w)); _s.Append(' '); _s.Append(F(h));
            _s.Append(" re f\n");
        }

        public void Rect(float x, float yTop, float w, float h)
        {
            float y = PageH - yTop - h;
            _s.Append("0.55 0.62 0.70 RG 0.6 w ");
            _s.Append(F(x)); _s.Append(' '); _s.Append(F(y)); _s.Append(' ');
            _s.Append(F(w)); _s.Append(' '); _s.Append(F(h));
            _s.Append(" re S\n");
        }

        public void Line(float x1, float y1Top, float x2, float y2Top)
        {
            _s.Append("0.55 0.62 0.70 RG 0.6 w ");
            _s.Append(F(x1)); _s.Append(' '); _s.Append(F(PageH - y1Top));
            _s.Append(" m ");
            _s.Append(F(x2)); _s.Append(' '); _s.Append(F(PageH - y2Top));
            _s.Append(" l S\n");
        }

        public void Text(float x, float yTop, string? text, float size, bool bold, Color color,
            bool center = false, float width = 0)
        {
            text ??= "";
            if (text.Length == 0)
                return;

            float y = PageH - yTop;
            if (center && width > 0)
                x += (width - Estimate(text, size)) / 2f;

            Rgb(color);
            _s.Append(" rg BT /");
            _s.Append(bold ? "F2" : "F1");
            _s.Append(' ');
            _s.Append(F(size));
            _s.Append(" Tf ");
            _s.Append(F(x));
            _s.Append(' ');
            _s.Append(F(y));
            _s.Append(" Td (");
            _s.Append(Esc(text));
            _s.Append(") Tj ET\n");
        }

        public void TextRight(float right, float yTop, string? text, float size, bool bold, Color color)
        {
            text ??= "";
            float x = right - Estimate(text, size);
            Text(x, yTop, text, size, bold, color);
        }

        public string ToStream()
        {
            _s.Append("Q\n");
            return _s.ToString();
        }

        private void Rgb(Color color)
        {
            _s.Append(F(color.R / 255f)); _s.Append(' ');
            _s.Append(F(color.G / 255f)); _s.Append(' ');
            _s.Append(F(color.B / 255f));
        }

        private static float Estimate(string text, float size) => text.Length * size * 0.5f;

        private static string F(float n) => n.ToString("0.###", CultureInfo.InvariantCulture);

        private static string Esc(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                if (c is '(' or ')' or '\\')
                    sb.Append('\\');
                if (c < 32 || c > 126)
                    sb.Append('?');
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }
    }
}
