using System.Text.Json;
using AutoUpdaterDotNET;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Forced update check against GitHub Releases. A newer release MSI (or zip) is downloaded
    /// and installed before the workspace opens. Offline or "no release yet" does not block launch.
    /// </summary>
    internal static class AppUpdate
    {
        public const string GitHubOwner = "Malaki1453";
        public const string GitHubRepo = "CRC-Inv-Manager";
        public const string LatestReleaseApi =
            "https://api.github.com/repos/" + GitHubOwner + "/" + GitHubRepo + "/releases/latest";

        /// <summary>
        /// Check GitHub for a newer version. Returns false when an update is downloading and the
        /// process should exit; true when the app may continue (up to date, or the check failed).
        /// Must run on the UI thread (AutoUpdater.NET requirement).
        /// </summary>
        public static bool TryContinueAfterCheck(LoadingForm splash)
        {
            splash.SetStatus("Checking for updates…");

            bool updating = false;
            AutoUpdater.AppTitle = "Cast Right Catch Inventory";
            AutoUpdater.Mandatory = true;
            AutoUpdater.UpdateMode = Mode.ForcedDownload;
            AutoUpdater.ShowSkipButton = false;
            AutoUpdater.ShowRemindLaterButton = false;
            AutoUpdater.ReportErrors = false;
            AutoUpdater.Synchronous = true;
            AutoUpdater.RunUpdateAsAdmin = true;
            AutoUpdater.TopMost = true;
            AutoUpdater.HttpUserAgent = "CastRightCatchInventory/" + InstalledVersion();
            AutoUpdater.InstalledVersion = InstalledVersion();
            AutoUpdater.SetOwner(splash);

            string? token = ReadGitHubToken();
            // Private GitHub repos need a PAT so latest-release JSON and the MSI can be downloaded.
            if (token != null)
            {
                var auth = new BasicAuthentication("x-access-token", token);
                AutoUpdater.BasicAuthXML = AutoUpdater.BasicAuthDownload = auth;
            }

            AutoUpdater.ParseUpdateInfoEvent -= OnGitHubRelease;
            AutoUpdater.ParseUpdateInfoEvent += OnGitHubRelease;
            AutoUpdater.ApplicationExitEvent -= OnUpdating;
            AutoUpdater.ApplicationExitEvent += OnUpdating;

            void OnUpdating()
            {
                updating = true;
            }

            try
            {
                AutoUpdater.Start(LatestReleaseApi);
            }
            catch
            {
                // GitHub unreachable, no releases yet, or a bad feed: keep launching.
                updating = false;
            }

            return !updating;
        }

        /// <summary>AssemblyVersion from this exe (four-part, compared to the GitHub release tag).</summary>
        public static Version InstalledVersion()
        {
            return typeof(AppUpdate).Assembly.GetName().Version ?? new Version(1, 0, 0, 0);
        }

        /// <summary>
        /// GitHub /releases/latest JSON → AutoUpdater feed.
        /// Prefers an .msi asset (the VS Setup output), then a .zip.
        /// Tag v1.0.1 becomes version 1.0.1.0 so it compares cleanly with AssemblyVersion.
        /// </summary>
        private static void OnGitHubRelease(ParseUpdateInfoEventArgs args)
        {
            string body = args.RemoteData ?? "";
            // XML feed (update.xml on the release): let AutoUpdater's default parser handle it.
            if (body.Length == 0 || body.TrimStart().StartsWith('<'))
                return;
            // Not GitHub JSON (HTML error page, empty): leave UpdateInfo unset so no update runs.
            if (!body.TrimStart().StartsWith('{'))
                return;

            using var doc = JsonDocument.Parse(body);
            var root = doc.RootElement;
            string tag = root.TryGetProperty("tag_name", out var tagEl) ? tagEl.GetString() ?? "" : "";
            Version? remote = ParseReleaseVersion(tag);
            // Tag is missing or not a version (e.g. "nightly"): ignore this release.
            if (remote == null)
                return;

            string? download = FindUpdateAsset(root);
            // Release has no MSI/zip attached: nothing to install.
            if (string.IsNullOrWhiteSpace(download))
                return;

            string changelog = root.TryGetProperty("html_url", out var html) ? html.GetString() ?? "" : "";
            args.UpdateInfo = new UpdateInfoEventArgs
            {
                CurrentVersion = remote.ToString(),
                DownloadURL = download,
                ChangelogURL = changelog,
                Mandatory = new Mandatory
                {
                    Value = true,
                    UpdateMode = Mode.ForcedDownload
                }
            };
        }

        /// <summary>v1.0.1 or 1.0.1 → 1.0.1.0. Null when the tag is not a version.</summary>
        internal static Version? ParseReleaseVersion(string tag)
        {
            tag = (tag ?? "").Trim();
            // GitHub tags are usually v1.0.1; AssemblyVersion has no 'v'.
            if (tag.StartsWith("v", StringComparison.OrdinalIgnoreCase))
                tag = tag[1..];
            if (!Version.TryParse(tag, out var version))
                return null;
            // Version.TryParse("1.0.1") leaves Revision -1, which compares as older than 1.0.1.0.
            int build = version.Build < 0 ? 0 : version.Build;
            int revision = version.Revision < 0 ? 0 : version.Revision;
            return new Version(version.Major, version.Minor, build, revision);
        }

        /// <summary>Prefer the Setup MSI; zip is the AutoUpdater.NET fallback.</summary>
        private static string? FindUpdateAsset(JsonElement release)
        {
            if (!release.TryGetProperty("assets", out var assets) || assets.ValueKind != JsonValueKind.Array)
                return null;

            string? zip = null;
            foreach (var asset in assets.EnumerateArray())
            {
                string name = asset.TryGetProperty("name", out var nameEl) ? nameEl.GetString() ?? "" : "";
                string url = asset.TryGetProperty("browser_download_url", out var urlEl) ? urlEl.GetString() ?? "" : "";
                if (url.Length == 0)
                    continue;
                // VS Installer Projects output: CRC-Inventory-Setup.msi (or any .msi on the release).
                if (name.EndsWith(".msi", StringComparison.OrdinalIgnoreCase))
                    return url;
                if (zip == null && name.EndsWith(".zip", StringComparison.OrdinalIgnoreCase))
                    zip = url;
            }

            return zip;
        }

        /// <summary>
        /// Optional PAT for a private repo. Env CRC_GITHUB_TOKEN / GH_TOKEN, or
        /// %ProgramData%\Cast Right Catch\github-update.token (IT can drop a file, not in git).
        /// </summary>
        private static string? ReadGitHubToken()
        {
            string? env = Environment.GetEnvironmentVariable("CRC_GITHUB_TOKEN")
                ?? Environment.GetEnvironmentVariable("GH_TOKEN");
            if (!string.IsNullOrWhiteSpace(env))
                return env.Trim();

            string path = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData),
                "Cast Right Catch",
                "github-update.token");
            if (!File.Exists(path))
                return null;
            try
            {
                string text = File.ReadAllText(path).Trim();
                return text.Length > 0 ? text : null;
            }
            catch
            {
                return null;
            }
        }
    }
}
