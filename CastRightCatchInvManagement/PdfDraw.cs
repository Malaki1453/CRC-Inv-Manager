using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace CastRightCatchInvManagement
{
    internal enum PdfFace
    {
        Sans,
        SansBold,
        SerifBold,
        SerifItalic
    }

    /// <summary>One-page PDF canvas with Helvetica, Times, and optional images.</summary>
    internal sealed class PdfDraw
    {
        public const float PageH = 792;
        public const float PageW = 612;

        private readonly StringBuilder _s = new();
        private readonly List<XImage> _images = new();
        private readonly List<(string Name, float Opacity)> _gs = new();

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
            Stroke(x, yTop, w, h, Color.FromArgb(140, 158, 178), 0.6f);
        }

        public void Stroke(float x, float yTop, float w, float h, Color color, float width)
        {
            float y = PageH - yTop - h;
            Rgb(color);
            _s.Append(" RG ");
            _s.Append(F(width));
            _s.Append(" w ");
            _s.Append(F(x)); _s.Append(' '); _s.Append(F(y)); _s.Append(' ');
            _s.Append(F(w)); _s.Append(' '); _s.Append(F(h));
            _s.Append(" re S\n");
        }

        public void Line(float x1, float y1Top, float x2, float y2Top)
        {
            Line(x1, y1Top, x2, y2Top, Color.FromArgb(140, 158, 178), 0.6f);
        }

        public void Line(float x1, float y1Top, float x2, float y2Top, Color color, float width)
        {
            Rgb(color);
            _s.Append(" RG ");
            _s.Append(F(width));
            _s.Append(" w ");
            _s.Append(F(x1)); _s.Append(' '); _s.Append(F(PageH - y1Top));
            _s.Append(" m ");
            _s.Append(F(x2)); _s.Append(' '); _s.Append(F(PageH - y2Top));
            _s.Append(" l S\n");
        }

        public void Diamond(float cx, float yTop, float size, Color color)
        {
            float cy = PageH - yTop;
            Rgb(color);
            _s.Append(" rg ");
            _s.Append(F(cx)); _s.Append(' '); _s.Append(F(cy + size)); _s.Append(" m ");
            _s.Append(F(cx + size)); _s.Append(' '); _s.Append(F(cy)); _s.Append(" l ");
            _s.Append(F(cx)); _s.Append(' '); _s.Append(F(cy - size)); _s.Append(" l ");
            _s.Append(F(cx - size)); _s.Append(' '); _s.Append(F(cy)); _s.Append(" l f\n");
        }

        public void Text(float x, float yTop, string? text, float size, bool bold, Color color,
            bool center = false, float width = 0)
        {
            Text(x, yTop, text, size, bold ? PdfFace.SansBold : PdfFace.Sans, color, center, width);
        }

        public void Text(float x, float yTop, string? text, float size, PdfFace face, Color color,
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
            _s.Append(FontName(face));
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
            Text(right - Estimate(text, size), yTop, text, size, bold, color);
        }

        public void Image(PdfSoftImage? image, float x, float yTop, float w, float h, float opacity = 1f)
        {
            if (image == null || w <= 0 || h <= 0)
                return;

            string name = "Im" + (_images.Count + 1);
            _images.Add(new XImage(name, image));
            float y = PageH - yTop - h;
            _s.Append("q ");
            if (opacity < 0.999f)
            {
                string gs = "GS" + (_gs.Count + 1);
                _gs.Add((gs, opacity));
                _s.Append('/');
                _s.Append(gs);
                _s.Append(" gs ");
            }

            _s.Append(F(w)); _s.Append(" 0 0 ");
            _s.Append(F(h)); _s.Append(' ');
            _s.Append(F(x)); _s.Append(' ');
            _s.Append(F(y));
            _s.Append(" cm /");
            _s.Append(name);
            _s.Append(" Do Q\n");
        }

        public string ToStream()
        {
            _s.Append("Q\n");
            return _s.ToString();
        }

        public byte[] ToPdf()
        {
            string content = ToStream();
            var body = Encoding.ASCII.GetBytes(content);
            var objects = new List<byte[]>();
            int fontEnd = 8;
            int gsStart = fontEnd + 1;
            int imgStart = gsStart + _gs.Count;

            var xObject = new StringBuilder();
            var gsRes = new StringBuilder();
            for (int i = 0; i < _gs.Count; i++)
            {
                if (gsRes.Length > 0)
                    gsRes.Append(' ');
                gsRes.Append('/').Append(_gs[i].Name).Append(' ').Append(gsStart + i).Append(" 0 R");
            }

            int obj = imgStart;
            foreach (var image in _images)
            {
                if (xObject.Length > 0)
                    xObject.Append(' ');
                xObject.Append('/').Append(image.Name).Append(' ').Append(obj).Append(" 0 R");
                obj += image.Data.Alpha == null ? 1 : 2;
            }

            var pageDict = new StringBuilder();
            pageDict.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << ");
            pageDict.Append("/Font << /F1 5 0 R /F2 6 0 R /F3 7 0 R /F4 8 0 R >> ");
            if (xObject.Length > 0)
                pageDict.Append("/XObject << ").Append(xObject).Append(" >> ");
            if (gsRes.Length > 0)
                pageDict.Append("/ExtGState << ").Append(gsRes).Append(" >> ");
            pageDict.Append(">> >>");

            objects.Add(Obj("<< /Type /Catalog /Pages 2 0 R >>"));
            objects.Add(Obj("<< /Type /Pages /Kids [3 0 R] /Count 1 >>"));
            objects.Add(Obj(pageDict.ToString()));
            objects.Add(Stream(body));
            objects.Add(Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica >>"));
            objects.Add(Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Helvetica-Bold >>"));
            objects.Add(Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Bold >>"));
            objects.Add(Obj("<< /Type /Font /Subtype /Type1 /BaseFont /Times-Italic >>"));
            foreach (var gs in _gs)
                objects.Add(Obj($"<< /Type /ExtGState /ca {F(gs.Opacity)} /CA {F(gs.Opacity)} >>"));
            foreach (var image in _images)
            {
                int self = objects.Count + 1;
                int mask = image.Data.Alpha == null ? 0 : self + 1;
                objects.Add(ImageObj(image.Data, mask));
                if (image.Data.Alpha != null)
                    objects.Add(MaskObj(image.Data));
            }

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

        private void Rgb(Color color)
        {
            _s.Append(F(color.R / 255f)); _s.Append(' ');
            _s.Append(F(color.G / 255f)); _s.Append(' ');
            _s.Append(F(color.B / 255f));
        }

        private static string FontName(PdfFace face) => face switch
        {
            PdfFace.SansBold => "F2",
            PdfFace.SerifBold => "F3",
            PdfFace.SerifItalic => "F4",
            _ => "F1"
        };

        private static float Estimate(string text, float size) => text.Length * size * 0.5f;

        internal static string F(float n) => n.ToString("0.###", CultureInfo.InvariantCulture);

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

        private static byte[] ImageObj(PdfSoftImage image, int maskObj)
        {
            var dict = new StringBuilder();
            dict.Append("<< /Type /XObject /Subtype /Image /Width ").Append(image.Width);
            dict.Append(" /Height ").Append(image.Height);
            dict.Append(" /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode");
            if (maskObj > 0)
                dict.Append(" /SMask ").Append(maskObj).Append(" 0 R");
            dict.Append(" /Length ").Append(image.Rgb.Length).Append(" >>\nstream\n");
            return Concat(Encoding.ASCII.GetBytes(dict.ToString()), image.Rgb, Encoding.ASCII.GetBytes("\nendstream"));
        }

        private static byte[] MaskObj(PdfSoftImage image)
        {
            var alpha = image.Alpha ?? Array.Empty<byte>();
            var dict = new StringBuilder();
            dict.Append("<< /Type /XObject /Subtype /Image /Width ").Append(image.Width);
            dict.Append(" /Height ").Append(image.Height);
            dict.Append(" /ColorSpace /DeviceGray /BitsPerComponent 8 /Filter /FlateDecode /Length ");
            dict.Append(alpha.Length).Append(" >>\nstream\n");
            return Concat(Encoding.ASCII.GetBytes(dict.ToString()), alpha, Encoding.ASCII.GetBytes("\nendstream"));
        }

        private static byte[] Concat(byte[] a, byte[] b, byte[] c)
        {
            var result = new byte[a.Length + b.Length + c.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            Buffer.BlockCopy(c, 0, result, a.Length + b.Length, c.Length);
            return result;
        }

        private readonly record struct XImage(string Name, PdfSoftImage Data);
    }

    internal sealed class PdfSoftImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[] Rgb { get; init; } = Array.Empty<byte>();
        public byte[]? Alpha { get; init; }
    }

    internal static class PdfImages
    {
        private static PdfSoftImage? _lockup;
        private static PdfSoftImage? _seal;

        public static PdfSoftImage? Lockup() =>
            _lockup ??= Prepare(BrandAssets.BoatLogo, knockoutGray: true);

        public static PdfSoftImage? Seal() =>
            _seal ??= Prepare(BrandAssets.Seal, knockoutDark: true, keepAlpha: true);

        public static PdfSoftImage? Prepare(
            Image? source,
            bool knockoutDark = false,
            bool knockoutGray = false,
            float fade = 0f,
            bool keepAlpha = false)
        {
            if (source == null)
                return null;

            using var sized = Fit(source, 720, 480);
            var bmp = sized.Clone(new Rectangle(0, 0, sized.Width, sized.Height), PixelFormat.Format32bppArgb);
            try
            {
                var data = bmp.LockBits(
                    new Rectangle(0, 0, bmp.Width, bmp.Height),
                    ImageLockMode.ReadOnly,
                    PixelFormat.Format32bppArgb);
                int strideInts = Math.Abs(data.Stride) / 4;
                int count = strideInts * bmp.Height;
                var pixels = new int[count];
                Marshal.Copy(data.Scan0, pixels, 0, count);
                bmp.UnlockBits(data);

                int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
                var alpha = new byte[bmp.Width * bmp.Height];
                var rgb = new byte[bmp.Width * bmp.Height * 3];
                bool anyAlpha = false;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int argb = pixels[y * strideInts + x];
                        int a = (argb >> 24) & 255;
                        int r = (argb >> 16) & 255;
                        int g = (argb >> 8) & 255;
                        int b = argb & 255;
                        if (knockoutDark && r <= 36 && g <= 36 && b <= 36)
                            a = 0;
                        if (knockoutGray && Math.Abs(r - g) < 14 && Math.Abs(g - b) < 14 && r is >= 70 and <= 210)
                            a = 0;
                        if (a < 16)
                        {
                            a = 0;
                            r = 255;
                            g = 255;
                            b = 255;
                        }
                        else if (fade > 0)
                        {
                            r = (int)(r + (255 - r) * fade);
                            g = (int)(g + (255 - g) * fade);
                            b = (int)(b + (255 - b) * fade);
                        }

                        int i = y * bmp.Width + x;
                        alpha[i] = (byte)a;
                        rgb[i * 3] = (byte)r;
                        rgb[i * 3 + 1] = (byte)g;
                        rgb[i * 3 + 2] = (byte)b;
                        if (a < 250)
                            anyAlpha = true;
                        if (a < 16)
                            continue;
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }

                if (maxX <= minX || maxY <= minY)
                    return null;

                int pad = 2;
                minX = Math.Max(0, minX - pad);
                minY = Math.Max(0, minY - pad);
                maxX = Math.Min(bmp.Width - 1, maxX + pad);
                maxY = Math.Min(bmp.Height - 1, maxY + pad);
                int w = maxX - minX + 1;
                int h = maxY - minY + 1;
                var cropRgb = new byte[w * h * 3];
                var cropA = new byte[w * h];
                for (int y = 0; y < h; y++)
                {
                    for (int x = 0; x < w; x++)
                    {
                        int src = (minY + y) * bmp.Width + (minX + x);
                        int dst = y * w + x;
                        cropRgb[dst * 3] = rgb[src * 3];
                        cropRgb[dst * 3 + 1] = rgb[src * 3 + 1];
                        cropRgb[dst * 3 + 2] = rgb[src * 3 + 2];
                        cropA[dst] = alpha[src];
                    }
                }

                return new PdfSoftImage
                {
                    Width = w,
                    Height = h,
                    Rgb = Deflate(cropRgb),
                    Alpha = keepAlpha && anyAlpha ? Deflate(cropA) : null
                };
            }
            finally
            {
                bmp.Dispose();
            }
        }

        private static Bitmap Fit(Image source, int maxW, int maxH)
        {
            int w = source.Width;
            int h = source.Height;
            float scale = Math.Min(1f, Math.Min(maxW / (float)w, maxH / (float)h));
            int dw = Math.Max(1, (int)Math.Round(w * scale));
            int dh = Math.Max(1, (int)Math.Round(h * scale));
            var bmp = new Bitmap(dw, dh, PixelFormat.Format32bppArgb);
            using var g = Graphics.FromImage(bmp);
            g.Clear(Color.Transparent);
            g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
            g.DrawImage(source, 0, 0, dw, dh);
            return bmp;
        }

        private static byte[] Deflate(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
                zip.Write(data, 0, data.Length);
            return ms.ToArray();
        }
    }
}
