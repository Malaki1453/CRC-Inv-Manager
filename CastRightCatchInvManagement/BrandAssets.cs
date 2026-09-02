using System.Drawing.Imaging;
using System.Runtime.InteropServices;

namespace CastRightCatchInvManagement
{
    /// <summary>Loads logo, hero, and icon files from Assets (with brand-assests fallbacks).</summary>
    internal static class BrandAssets
    {
        public static string DirectoryPath { get; } = ResolveDirectory();

        public static Image? Seal { get; } = Load("logo-seal.png", "favicon.PNG", "favicon.png");
        public static Image? Wordmark { get; } = CropOpaque(KnockoutDark(Load("wordmark.png", "highlightedLogo.png")));
        public static Image? BoatLogo { get; } = Load("logo-boat.png", "Logo.png");
        public static Image? Hero { get; } = Load("hero.jpg");
        public static Image? HomeHero { get; } = Load("home-hero.jpg", "homePageBackgroundLogoOnBottom.jpg");
        public static Image? Footer { get; } = Load("footer.png");

        public static Icon? AppIcon { get; } = LoadIcon();

        private static string ResolveDirectory()
        {
            var start = new DirectoryInfo(AppDomain.CurrentDomain.BaseDirectory);
            for (var dir = start; dir != null; dir = dir.Parent)
            {
                var assets = Path.Combine(dir.FullName, "Assets");
                if (File.Exists(Path.Combine(assets, "logo-seal.png")) ||
                    File.Exists(Path.Combine(assets, "app.ico")))
                    return assets;

                var branded = Path.Combine(dir.FullName, "brand-assests");
                if (Directory.Exists(branded))
                    return branded;
            }

            return Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets");
        }

        private static Image? Load(params string[] names)
        {
            foreach (var name in names)
            {
                var path = Path.Combine(DirectoryPath, name);
                if (!File.Exists(path))
                    continue;

                try
                {
                    using var fs = File.OpenRead(path);
                    using var img = Image.FromStream(fs);
                    return new Bitmap(img);
                }
                catch
                {
                    // skip unreadable files
                }
            }

            return null;
        }

        private static Image? KnockoutDark(Image? source, int threshold = 36)
        {
            if (source == null)
                return null;

            var bmp = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var g = Graphics.FromImage(bmp))
                g.DrawImage(source, 0, 0, source.Width, source.Height);

            var data = bmp.LockBits(
                new Rectangle(0, 0, bmp.Width, bmp.Height),
                ImageLockMode.ReadWrite,
                PixelFormat.Format32bppArgb);

            int count = Math.Abs(data.Stride) * bmp.Height / 4;
            var pixels = new int[count];
            Marshal.Copy(data.Scan0, pixels, 0, count);
            for (int i = 0; i < pixels.Length; i++)
            {
                int argb = pixels[i];
                int r = (argb >> 16) & 255;
                int g = (argb >> 8) & 255;
                int b = argb & 255;
                if (r <= threshold && g <= threshold && b <= threshold)
                    pixels[i] = 0;
            }

            Marshal.Copy(pixels, 0, data.Scan0, count);
            bmp.UnlockBits(data);
            return bmp;
        }

        private static Image? CropOpaque(Image? source)
        {
            if (source is not Bitmap bmp)
                return source;

            int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    if (bmp.GetPixel(x, y).A < 16)
                        continue;
                    if (x < minX) minX = x;
                    if (y < minY) minY = y;
                    if (x > maxX) maxX = x;
                    if (y > maxY) maxY = y;
                }
            }

            if (maxX <= minX || maxY <= minY)
                return bmp;

            int pad = 4;
            minX = Math.Max(0, minX - pad);
            minY = Math.Max(0, minY - pad);
            maxX = Math.Min(bmp.Width - 1, maxX + pad);
            maxY = Math.Min(bmp.Height - 1, maxY + pad);

            var rect = new Rectangle(minX, minY, maxX - minX + 1, maxY - minY + 1);
            return bmp.Clone(rect, PixelFormat.Format32bppArgb);
        }

        private static Icon? LoadIcon()
        {
            var icoPath = Path.Combine(DirectoryPath, "app.ico");
            if (File.Exists(icoPath))
            {
                try
                {
                    return new Icon(icoPath);
                }
                catch
                {
                    // fall through to bitmap conversion
                }
            }

            if (Seal is Bitmap bmp)
            {
                try
                {
                    IntPtr handle = bmp.GetHicon();
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }
}
