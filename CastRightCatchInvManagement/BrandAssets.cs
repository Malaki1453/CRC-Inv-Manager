using System.Drawing.Imaging;
using System.Reflection;
using System.Runtime.InteropServices;

namespace CastRightCatchInvManagement
{
    /// <summary>Loads logo, hero, and icon files from Assets (with brand-assests fallbacks).</summary>
    internal static class BrandAssets
    {
        public static string DirectoryPath { get; } = ResolveDirectory();

        private static readonly Lazy<Image?> SealLazy =
            new(() => Load("logo-seal.png", "favicon.PNG", "favicon.png"));
        private static readonly Lazy<Image?> WordmarkLazy =
            new(() => CropOpaque(KnockoutDark(Load("wordmark.png", "highlightedLogo.png"))));
        private static readonly Lazy<Image?> BoatLogoLazy =
            new(() => Load("logo-boat.png", "Logo.png"));
        private static readonly Lazy<Image?> HeroLazy =
            new(() => Load("hero.jpg"));
        private static readonly Lazy<Image?> HomeHeroLazy =
            new(() => Load("home-hero.jpg", "homePageBackgroundLogoOnBottom.jpg"));
        private static readonly Lazy<Image?> FooterLazy =
            new(() => Load("footer.png"));
        private static readonly Lazy<Icon?> AppIconLazy = new(LoadIcon);

        public static Image? Seal => SealLazy.Value;
        public static Image? Wordmark => WordmarkLazy.Value;
        public static Image? BoatLogo => BoatLogoLazy.Value;
        public static Image? Hero => HeroLazy.Value;
        public static Image? HomeHero => HomeHeroLazy.Value;
        public static Image? Footer => FooterLazy.Value;
        public static Icon? AppIcon => AppIconLazy.Value;

        /// <summary>Walk up from the exe until Assets or the source brand-assests folder is found.</summary>
        private static string ResolveDirectory()
        {
            string? withSeal = null;
            foreach (var startPath in ExeDirectories())
            {
                for (var dir = new DirectoryInfo(startPath); dir != null; dir = dir.Parent)
                {
                    var assets = Path.Combine(dir.FullName, "Assets");
                    // Prefer a pack that includes the boat lockup used on PDFs.
                    if (File.Exists(Path.Combine(assets, "logo-boat.png")))
                        return assets;
                    if (File.Exists(Path.Combine(dir.FullName, "logo-boat.png")))
                        return dir.FullName;
                    if (withSeal == null &&
                        (File.Exists(Path.Combine(assets, "logo-seal.png")) ||
                         File.Exists(Path.Combine(assets, "app.ico"))))
                        withSeal = assets;

                    var branded = Path.Combine(dir.FullName, "brand-assests");
                    // Dev-tree fallback: original brand files next to the solution.
                    if (Directory.Exists(branded) &&
                        File.Exists(Path.Combine(branded, "logo-boat.png")))
                        return branded;
                }
            }

            return withSeal ?? Path.Combine(AppContext.BaseDirectory, "Assets");
        }

        /// <summary>Install folder and AppContext/AppDomain base directories for the running exe.</summary>
        private static IEnumerable<string> ExeDirectories()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void add(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                try
                {
                    seen.Add(Path.GetFullPath(path));
                }
                catch
                {
                }
            }

            add(Path.GetDirectoryName(Environment.ProcessPath));
            add(AppContext.BaseDirectory);
            add(AppDomain.CurrentDomain.BaseDirectory);
            return seen;
        }

        /// <summary>Folders that may hold a sidecar PNG: resolved Assets, install root, and Assets next to the exe.</summary>
        private static IEnumerable<string> SearchDirs()
        {
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void add(string? path)
            {
                if (string.IsNullOrWhiteSpace(path))
                    return;
                try
                {
                    seen.Add(Path.GetFullPath(path));
                }
                catch
                {
                }
            }

            add(DirectoryPath);
            foreach (var exe in ExeDirectories())
            {
                add(exe);
                add(Path.Combine(exe, "Assets"));
            }

            return seen;
        }

        /// <summary>Load the first readable image among the given file names.</summary>
        private static Image? Load(params string[] names)
        {
            foreach (var name in names)
            {
                foreach (var dir in SearchDirs())
                {
                    var path = Path.Combine(dir, name);
                    var fromDisk = ReadImageFile(path);
                    if (fromDisk != null)
                        return fromDisk;
                }

                // Single-file / MSI installs may omit sidecar Assets; the PNG is also embedded in the exe.
                var embedded = LoadEmbedded(name);
                if (embedded != null)
                    return embedded;
            }

            return null;
        }

        /// <summary>Decode a disk image into a standalone bitmap, or null if the file is missing or unreadable.</summary>
        private static Image? ReadImageFile(string path)
        {
            if (!File.Exists(path))
                return null;
            try
            {
                using var fs = File.OpenRead(path);
                return BitmapFromStream(fs);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Open an embedded asset by LogicalName, then by any resource whose name ends with the file name.</summary>
        private static Stream? OpenEmbedded(string name)
        {
            foreach (var asm in new[] { typeof(BrandAssets).Assembly, Assembly.GetEntryAssembly() })
            {
                if (asm == null)
                    continue;
                var exact = asm.GetManifestResourceStream("Assets." + name);
                if (exact != null)
                    return exact;

                string? match = Array.Find(
                    asm.GetManifestResourceNames(),
                    n => n.EndsWith("." + name, StringComparison.OrdinalIgnoreCase)
                      || n.Equals(name, StringComparison.OrdinalIgnoreCase));
                if (match != null)
                    return asm.GetManifestResourceStream(match);
            }

            return null;
        }

        /// <summary>Read a packaged asset from the assembly when it is not on disk next to the exe.</summary>
        private static Image? LoadEmbedded(string name)
        {
            using var stream = OpenEmbedded(name);
            return stream == null ? null : BitmapFromStream(stream);
        }

        /// <summary>Copy the stream into memory first so GDI+ can decode single-file resource streams.</summary>
        private static Image? BitmapFromStream(Stream stream)
        {
            try
            {
                using var ms = new MemoryStream();
                stream.CopyTo(ms);
                ms.Position = 0;
                using var img = Image.FromStream(ms);
                return new Bitmap(img);
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Make near-black pixels transparent so a dark wordmark works on navy.</summary>
        private static Image? KnockoutDark(Image? source, int threshold = 36)
        {
            // Optional overlay; missing wordmark should not crash paint.
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
                // Wordmark PNGs ship on a black field; drop that so navy shows through.
                if (r <= threshold && g <= threshold && b <= threshold)
                    pixels[i] = 0;
            }

            Marshal.Copy(pixels, 0, data.Scan0, count);
            bmp.UnlockBits(data);
            return bmp;
        }

        /// <summary>Trim fully transparent padding so the wordmark sits tight in the hero.</summary>
        private static Image? CropOpaque(Image? source)
        {
            // Non-bitmap images cannot be scanned pixel-by-pixel.
            if (source is not Bitmap bmp)
                return source;

            int minX = bmp.Width, minY = bmp.Height, maxX = 0, maxY = 0;
            for (int y = 0; y < bmp.Height; y++)
            {
                for (int x = 0; x < bmp.Width; x++)
                {
                    // Ignore near-clear pixels left by the knockout.
                    if (bmp.GetPixel(x, y).A < 16)
                        continue;
                    // Left edge of remaining opaque pixels.
                    if (x < minX) minX = x;
                    // Top edge of remaining opaque pixels.
                    if (y < minY) minY = y;
                    // Right edge of remaining opaque pixels.
                    if (x > maxX) maxX = x;
                    // Bottom edge of remaining opaque pixels.
                    if (y > maxY) maxY = y;
                }
            }

            // Entire image was transparent; keep the original bitmap.
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

        /// <summary>Prefer app.ico; otherwise convert the seal bitmap to an icon.</summary>
        private static Icon? LoadIcon()
        {
            foreach (var icoPath in new[]
            {
                Path.Combine(DirectoryPath, "app.ico"),
                Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "app.ico"),
            })
            {
                // Prefer the packaged .ico when the asset pack includes it (Assets\ or install root).
                if (!File.Exists(icoPath))
                    continue;
                try
                {
                    return new Icon(icoPath);
                }
                // Corrupt ico: try the next path, then embedded, then bitmap conversion.
                catch
                {
                }
            }

            using (var stream = OpenEmbedded("app.ico"))
            {
                if (stream != null)
                {
                    try
                    {
                        return new Icon(stream);
                    }
                    catch
                    {
                    }
                }
            }

            // No ico, or it failed: convert the seal bitmap to an icon.
            if (Seal is Bitmap bmp)
            {
                try
                {
                    IntPtr handle = bmp.GetHicon();
                    return (Icon)Icon.FromHandle(handle).Clone();
                }
                // HICON conversion can fail on some bitmaps; windows keep the default icon.
                catch
                {
                    return null;
                }
            }

            return null;
        }
    }
}
