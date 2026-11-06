using System.Drawing.Imaging;
using System.Globalization;
using System.IO.Compression;
using System.Runtime.InteropServices;
using System.Text;

namespace CastRightCatchInvManagement
{
    /// <summary>Built-in PDF fonts: Helvetica, Helvetica-Bold, Times-Bold, Times-Italic.</summary>
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

        /// <summary>Start a graphics state so ToStream can restore with Q.</summary>
        public PdfDraw()
        {
            _s.Append("q\n");
        }

        /// <summary>Filled rectangle. yTop is from the top of the page like WinForms.</summary>
        public void Fill(float x, float yTop, float w, float h, Color color)
        {
            float y = PageH - yTop - h;
            Rgb(color);
            _s.Append(" rg ");
            _s.Append(F(x)); _s.Append(' '); _s.Append(F(y)); _s.Append(' ');
            _s.Append(F(w)); _s.Append(' '); _s.Append(F(h));
            _s.Append(" re f\n");
        }

        /// <summary>Hairline box in the default grid-line color.</summary>
        public void Rect(float x, float yTop, float w, float h)
        {
            Stroke(x, yTop, w, h, Color.FromArgb(140, 158, 178), 0.6f);
        }

        /// <summary>Stroked rectangle in PDF space (origin bottom-left).</summary>
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

        /// <summary>Hairline in the default grid-line color.</summary>
        public void Line(float x1, float y1Top, float x2, float y2Top)
        {
            Line(x1, y1Top, x2, y2Top, Color.FromArgb(140, 158, 178), 0.6f);
        }

        /// <summary>Stroke a line; y values are from the top of the page.</summary>
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

        /// <summary>Filled diamond used on the letterhead divider.</summary>
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

        /// <summary>Draw Helvetica or Helvetica-Bold text.</summary>
        public void Text(float x, float yTop, string? text, float size, bool bold, Color color,
            bool center = false, float width = 0)
        {
            Text(x, yTop, text, size, bold ? PdfFace.SansBold : PdfFace.Sans, color, center, width);
        }

        /// <summary>Draw text in the chosen face; skip blanks so we do not emit empty Tj.</summary>
        public void Text(float x, float yTop, string? text, float size, PdfFace face, Color color,
            bool center = false, float width = 0)
        {
            text ??= "";
            // Empty strings would still consume a text object in the stream.
            if (text.Length == 0)
                return;

            float y = PageH - yTop;
            // Center within a cell when invoices pass a column width.
            if (center && width > 0)
                x += (width - Estimate(text, size, face == PdfFace.SansBold)) / 2f;

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

        /// <summary>Right-align text by subtracting the Helvetica width from the right edge.</summary>
        public void TextRight(float right, float yTop, string? text, float size, bool bold, Color color)
        {
            text ??= "";
            Text(right - Estimate(text, size, bold), yTop, text, size, bold, color);
        }

        /// <summary>Split text on existing line breaks, then wrap to maxWidth using Helvetica widths.</summary>
        public static List<string> WrapLines(string? text, float size, bool bold, float maxWidth)
        {
            var lines = new List<string>();
            foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
            {
                string rest = paragraph.TrimEnd();
                if (rest.Length == 0)
                {
                    lines.Add("");
                    continue;
                }

                while (rest.Length > 0)
                {
                    if (Estimate(rest, size, bold) <= maxWidth)
                    {
                        lines.Add(rest);
                        break;
                    }

                    int take = FitLength(rest, size, bold, maxWidth);
                    int space = rest.LastIndexOf(' ', take);
                    if (space > 0)
                        take = space;
                    lines.Add(rest[..take].TrimEnd());
                    rest = rest[take..].TrimStart();
                }
            }

            if (lines.Count == 0)
                lines.Add("");
            return lines;
        }

        /// <summary>Draw wrapped text. Returns the y after the last line (one line even when empty).</summary>
        public float TextFlow(
            float x,
            float yTop,
            string? text,
            float size,
            bool bold,
            Color color,
            float maxWidth,
            float lineHeight)
        {
            var lines = WrapLines(text, size, bold, maxWidth);
            for (int i = 0; i < lines.Count; i++)
                Text(x, yTop + i * lineHeight, lines[i], size, bold, color);
            return yTop + Math.Max(1, lines.Count) * lineHeight;
        }

        /// <summary>Largest prefix of text whose Helvetica width is still within maxWidth.</summary>
        private static int FitLength(string text, float size, bool bold, float maxWidth)
        {
            int lo = 1;
            int hi = text.Length;
            while (lo < hi)
            {
                int mid = (lo + hi + 1) / 2;
                if (Estimate(text[..mid], size, bold) <= maxWidth)
                    lo = mid;
                else
                    hi = mid - 1;
            }

            return Math.Max(1, lo);
        }

        /// <summary>Place a prepared image; optional opacity uses an ExtGState for watermarks.</summary>
        public void Image(PdfSoftImage? image, float x, float yTop, float w, float h, float opacity = 1f)
        {
            // Missing assets or zero size would emit a broken Do operator.
            if (image == null || w <= 0 || h <= 0)
                return;

            string name = "Im" + (_images.Count + 1);
            _images.Add(new XImage(name, image));
            float y = PageH - yTop - h;
            _s.Append("q ");
            // Watermarks need a graphics state; fully opaque images skip it.
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

        /// <summary>Close the graphics state and return the page content stream.</summary>
        public string ToStream()
        {
            _s.Append("Q\n");
            return _s.ToString();
        }

        /// <summary>Build a one-page PDF 1.4 file with fonts, images, and optional opacity.</summary>
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
                // Space-separate ExtGState names in the resource dictionary.
                if (gsRes.Length > 0)
                    gsRes.Append(' ');
                gsRes.Append('/').Append(_gs[i].Name).Append(' ').Append(gsStart + i).Append(" 0 R");
            }

            int obj = imgStart;
            foreach (var image in _images)
            {
                // Space-separate image XObject names.
                if (xObject.Length > 0)
                    xObject.Append(' ');
                xObject.Append('/').Append(image.Name).Append(' ').Append(obj).Append(" 0 R");
                obj += image.Data.Alpha == null ? 1 : 2;
            }

            var pageDict = new StringBuilder();
            pageDict.Append("<< /Type /Page /Parent 2 0 R /MediaBox [0 0 612 792] /Contents 4 0 R /Resources << ");
            pageDict.Append("/Font << /F1 5 0 R /F2 6 0 R /F3 7 0 R /F4 8 0 R >> ");
            // Omit empty resource dictionaries so Acrobat stays happy.
            if (xObject.Length > 0)
                pageDict.Append("/XObject << ").Append(xObject).Append(" >> ");
            // Omit empty ExtGState so Acrobat stays happy when there are no watermarks.
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
                // Soft mask object follows the RGB image when the PNG had transparency.
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

        /// <summary>Emit r g b (0–1) for the next fill or stroke operator.</summary>
        private void Rgb(Color color)
        {
            _s.Append(F(color.R / 255f)); _s.Append(' ');
            _s.Append(F(color.G / 255f)); _s.Append(' ');
            _s.Append(F(color.B / 255f));
        }

        /// <summary>Resource name for the four Type1 fonts in the page dictionary.</summary>
        private static string FontName(PdfFace face) => face switch
        {
            PdfFace.SansBold => "F2",
            PdfFace.SerifBold => "F3",
            PdfFace.SerifItalic => "F4",
            _ => "F1"
        };

        /// <summary>Adobe Helvetica AFM widths in 1/1000 em for ASCII 32–126.</summary>
        private static readonly ushort[] HelveticaW =
        {
            278,278,355,556,556,889,667,191,333,333,389,584,278,333,278,278,
            556,556,556,556,556,556,556,556,556,556,278,278,584,584,584,556,
            1015,667,667,722,722,667,611,778,722,278,500,667,556,833,722,778,
            667,778,722,667,611,722,667,944,667,667,611,278,278,278,469,556,
            333,556,556,500,556,556,278,556,556,222,222,500,222,833,556,556,
            556,556,333,500,278,556,500,722,500,500,500,334,260,334,584
        };

        /// <summary>Adobe Helvetica-Bold AFM widths in 1/1000 em for ASCII 32–126.</summary>
        private static readonly ushort[] HelveticaBoldW =
        {
            278,333,474,556,556,889,722,238,333,333,389,584,278,333,278,278,
            556,556,556,556,556,556,556,556,556,556,333,333,584,584,584,611,
            975,722,722,722,722,667,611,778,722,278,556,722,611,833,722,778,
            667,778,722,667,611,722,667,944,667,667,611,333,278,333,584,556,
            333,556,611,556,611,556,333,611,611,278,278,556,278,889,611,611,
            611,611,389,556,333,611,556,778,556,556,500,389,280,389,584
        };

        /// <summary>True Helvetica advance so right-aligned lines share one right edge.</summary>
        private static float Estimate(string text, float size, bool bold = false)
        {
            var table = bold ? HelveticaBoldW : HelveticaW;
            int units = 0;
            foreach (char c in text)
            {
                int i = c - 32;
                units += (uint)i < (uint)table.Length ? table[i] : 500;
            }

            return units * size / 1000f;
        }

        /// <summary>Invariant number for PDF operators (no locale commas).</summary>
        internal static string F(float n) => n.ToString("0.###", CultureInfo.InvariantCulture);

        /// <summary>Escape ( ) \\ and drop non-ASCII so the literal string stays valid.</summary>
        private static string Esc(string text)
        {
            var sb = new StringBuilder(text.Length);
            foreach (char c in text)
            {
                // PDF literal strings treat these as syntax unless escaped.
                if (c is '(' or ')' or '\\')
                    sb.Append('\\');
                // Win-1252 is not declared; replace other glyphs so the file still parses.
                if (c < 32 || c > 126)
                    sb.Append('?');
                // Printable ASCII can go into the PDF literal as-is.
                else
                    sb.Append(c);
            }

            return sb.ToString();
        }

        /// <summary>ASCII object body without the obj/endobj wrappers.</summary>
        private static byte[] Obj(string body) => Encoding.ASCII.GetBytes(body);

        /// <summary>Content stream object: dictionary, stream, bytes, endstream.</summary>
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

        /// <summary>Flate-encoded RGB image XObject, optionally pointing at a soft mask.</summary>
        private static byte[] ImageObj(PdfSoftImage image, int maskObj)
        {
            var dict = new StringBuilder();
            dict.Append("<< /Type /XObject /Subtype /Image /Width ").Append(image.Width);
            dict.Append(" /Height ").Append(image.Height);
            dict.Append(" /ColorSpace /DeviceRGB /BitsPerComponent 8 /Filter /FlateDecode");
            // SMask makes the letterhead PNG keep its transparent background.
            if (maskObj > 0)
                dict.Append(" /SMask ").Append(maskObj).Append(" 0 R");
            dict.Append(" /Length ").Append(image.Rgb.Length).Append(" >>\nstream\n");
            return Concat(Encoding.ASCII.GetBytes(dict.ToString()), image.Rgb, Encoding.ASCII.GetBytes("\nendstream"));
        }

        /// <summary>DeviceGray alpha XObject used as the image soft mask.</summary>
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

        /// <summary>Join dictionary, stream bytes, and endstream without extra copies of strings.</summary>
        private static byte[] Concat(byte[] a, byte[] b, byte[] c)
        {
            var result = new byte[a.Length + b.Length + c.Length];
            Buffer.BlockCopy(a, 0, result, 0, a.Length);
            Buffer.BlockCopy(b, 0, result, a.Length, b.Length);
            Buffer.BlockCopy(c, 0, result, a.Length + b.Length, c.Length);
            return result;
        }

        /// <summary>Named image resource on the page (Im1, Im2, …).</summary>
        private readonly record struct XImage(string Name, PdfSoftImage Data);
    }

    /// <summary>Flate RGB (and optional alpha) ready to embed in a PDF.</summary>
    internal sealed class PdfSoftImage
    {
        public int Width { get; init; }
        public int Height { get; init; }
        public byte[] Rgb { get; init; } = Array.Empty<byte>();
        public byte[]? Alpha { get; init; }
    }

    /// <summary>Cached lockup and seal bitmaps prepared for PDF (knockout + crop + deflate).</summary>
    internal static class PdfImages
    {
        private static PdfSoftImage? _lockup;
        private static PdfSoftImage? _seal;

        /// <summary>Boat lockup composited on cream so the PNG's clear backdrop matches the banner.</summary>
        public static PdfSoftImage? Lockup() =>
            _lockup ??= Prepare(BrandAssets.BoatLogo, matte: Theme.Cream);

        /// <summary>Seal watermark with dark background knocked out, prepared once.</summary>
        public static PdfSoftImage? Seal() =>
            _seal ??= Prepare(BrandAssets.Seal, knockoutDark: true, keepAlpha: true);

        /// <summary>Convert a brand image to cropped, deflated RGB (and alpha) for PDF.</summary>
        public static PdfSoftImage? Prepare(
            Image? source,
            bool knockoutDark = false,
            bool knockoutGray = false,
            float fade = 0f,
            bool keepAlpha = false,
            Color? matte = null)
        {
            // Missing brand files should omit the image rather than throw.
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
                int matteR = matte?.R ?? 255;
                int matteG = matte?.G ?? 255;
                int matteB = matte?.B ?? 255;
                for (int y = 0; y < bmp.Height; y++)
                {
                    for (int x = 0; x < bmp.Width; x++)
                    {
                        int argb = pixels[y * strideInts + x];
                        int a = (argb >> 24) & 255;
                        int r = (argb >> 16) & 255;
                        int g = (argb >> 8) & 255;
                        int b = argb & 255;
                        // Navy/black photo backdrops would print as a box on the cream page.
                        if (knockoutDark && r <= 36 && g <= 36 && b <= 36)
                            a = 0;
                        // Gray studio backdrop around the boat lockup should not print.
                        if (knockoutGray && Math.Abs(r - g) < 16 && Math.Abs(g - b) < 16 && r is >= 48 and <= 180)
                            a = 0;
                        // Crop against coverage before flattening so cream padding is not part of the box.
                        int srcA = a;
                        if (matte.HasValue)
                        {
                            // Clear pixels keep leftover studio RGB; use the banner color instead of blending that gray.
                            if (srcA < 16)
                            {
                                r = matteR;
                                g = matteG;
                                b = matteB;
                            }
                            else
                            {
                                float t = srcA / 255f;
                                r = (int)Math.Round(r * t + matteR * (1f - t));
                                g = (int)Math.Round(g * t + matteG * (1f - t));
                                b = (int)Math.Round(b * t + matteB * (1f - t));
                            }
                            if (!keepAlpha)
                                a = 255;
                        }
                        else if (srcA < 16)
                        {
                            // Near-transparent pixels become white so JPEG-like fringes disappear.
                            a = 0;
                            r = 255;
                            g = 255;
                            b = 255;
                        }
                        // Optional wash toward white for a lighter watermark.
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
                        // Opaque pixels do not need a soft mask.
                        if (a < 250)
                            anyAlpha = true;
                        // Transparent pixels are not part of the crop box.
                        if (srcA < 16)
                            continue;
                        // Grow the crop box to every opaque pixel.
                        if (x < minX) minX = x;
                        if (y < minY) minY = y;
                        if (x > maxX) maxX = x;
                        if (y > maxY) maxY = y;
                    }
                }

                // Entirely knocked-out images should not become a blank XObject.
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
                // Clone bitmap is not using-disposed earlier because LockBits needs it alive.
                bmp.Dispose();
            }
        }

        /// <summary>Downscale large brand PNGs so the PDF stays small.</summary>
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

        /// <summary>zlib-compress image bytes for /Filter /FlateDecode.</summary>
        private static byte[] Deflate(byte[] data)
        {
            using var ms = new MemoryStream();
            using (var zip = new ZLibStream(ms, CompressionLevel.SmallestSize, leaveOpen: true))
                zip.Write(data, 0, data.Length);
            return ms.ToArray();
        }
    }
}
