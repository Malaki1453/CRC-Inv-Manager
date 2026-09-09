using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.Runtime.Versioning;

[assembly: SupportedOSPlatform("windows")]

var root = args.Length > 0
    ? Path.GetFullPath(args[0])
    : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", ".."));
var assets = Path.Combine(root, "CastRightCatchInvManagement", "Assets");
var sealPath = Path.Combine(assets, "logo-seal.png");
var icoPath = Path.Combine(assets, "app.ico");
if (!File.Exists(sealPath))
{
    Console.Error.WriteLine("Missing " + sealPath);
    return 1;
}

using var seal = new Bitmap(sealPath);
var cropped = CropNonBlack(seal);
var navy = Color.FromArgb(15, 42, 68);
int[] sizes = { 16, 24, 32, 48, 64, 128, 256 };
var images = new Bitmap[sizes.Length];
for (int i = 0; i < sizes.Length; i++)
    images[i] = MakeSize(cropped, sizes[i], navy);

SaveIco(icoPath, images);
foreach (var bmp in images)
    bmp.Dispose();
cropped.Dispose();
Console.WriteLine("Wrote " + icoPath);
return 0;

static Bitmap CropNonBlack(Bitmap source)
{
    int minX = source.Width, minY = source.Height, maxX = 0, maxY = 0;
    for (int y = 0; y < source.Height; y++)
    {
        for (int x = 0; x < source.Width; x++)
        {
            var p = source.GetPixel(x, y);
            if (p.A < 16)
                continue;
            if (p.R < 28 && p.G < 28 && p.B < 28)
                continue;
            if (x < minX) minX = x;
            if (y < minY) minY = y;
            if (x > maxX) maxX = x;
            if (y > maxY) maxY = y;
        }
    }

    if (maxX <= minX || maxY <= minY)
        return (Bitmap)source.Clone();

    int pad = Math.Max(4, (maxX - minX) / 40);
    minX = Math.Max(0, minX - pad);
    minY = Math.Max(0, minY - pad);
    maxX = Math.Min(source.Width - 1, maxX + pad);
    maxY = Math.Min(source.Height - 1, maxY + pad);
    return source.Clone(new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1), PixelFormat.Format32bppArgb);
}

static Bitmap MakeSize(Bitmap seal, int size, Color navy)
{
    var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
    using var g = Graphics.FromImage(bmp);
    g.SmoothingMode = SmoothingMode.AntiAlias;
    g.InterpolationMode = InterpolationMode.HighQualityBicubic;
    g.PixelOffsetMode = PixelOffsetMode.HighQuality;
    g.CompositingQuality = CompositingQuality.HighQuality;
    g.Clear(Color.Transparent);

    float inset = size <= 24 ? 0.5f : 1f;
    g.FillEllipse(new SolidBrush(navy), inset, inset, size - inset * 2f, size - inset * 2f);

    float margin = size <= 32 ? 1f : Math.Max(1.5f, size * 0.03f);
    float box = size - margin * 2f;
    float scale = Math.Min(box / seal.Width, box / seal.Height);
    float w = seal.Width * scale;
    float h = seal.Height * scale;
    float x = (size - w) / 2f;
    float y = (size - h) / 2f;
    g.DrawImage(seal, x, y, w, h);
    FinishCircle(bmp, navy);
    return bmp;
}

static void FinishCircle(Bitmap bmp, Color navy)
{
    int w = bmp.Width;
    int h = bmp.Height;
    float cx = (w - 1) / 2f;
    float cy = (h - 1) / 2f;
    float radius = Math.Min(w, h) / 2f - 0.35f;
    float sealKeep = Math.Min(w, h) * 0.44f;
    float r2 = radius * radius;
    float keep2 = sealKeep * sealKeep;
    for (int y = 0; y < h; y++)
    {
        for (int x = 0; x < w; x++)
        {
            float dx = x - cx;
            float dy = y - cy;
            float d2 = dx * dx + dy * dy;
            if (d2 > r2)
            {
                bmp.SetPixel(x, y, Color.Transparent);
                continue;
            }

            var p = bmp.GetPixel(x, y);
            if (d2 > keep2 && p.R < 40 && p.G < 40 && p.B < 40)
                bmp.SetPixel(x, y, navy);
        }
    }
}

static void SaveIco(string path, Bitmap[] images)
{
    var encoded = new List<byte[]>();
    foreach (var bmp in images)
    {
        using var ms = new MemoryStream();
        bmp.Save(ms, ImageFormat.Png);
        encoded.Add(ms.ToArray());
    }

    using var fs = File.Create(path);
    using var bw = new BinaryWriter(fs);
    bw.Write((ushort)0);
    bw.Write((ushort)1);
    bw.Write((ushort)images.Length);
    int offset = 6 + 16 * images.Length;
    for (int i = 0; i < images.Length; i++)
    {
        int w = images[i].Width;
        int h = images[i].Height;
        bw.Write((byte)(w >= 256 ? 0 : w));
        bw.Write((byte)(h >= 256 ? 0 : h));
        bw.Write((byte)0);
        bw.Write((byte)0);
        bw.Write((ushort)1);
        bw.Write((ushort)32);
        bw.Write(encoded[i].Length);
        bw.Write(offset);
        offset += encoded[i].Length;
    }

    foreach (var data in encoded)
        bw.Write(data);
}
