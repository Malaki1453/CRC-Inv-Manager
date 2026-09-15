using System.Text.Json;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Shared-folder lock and settings. This PC only stores the folder path in a local JSON file.
    /// Company info, numbering, and SMTP live in crc_inventory.db so every computer shares them.
    /// </summary>
    public static class AppLock
    {
        private static readonly string SettingsPath =
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"settings_{SanitizeUserName(Environment.UserName)}.json");

        private static bool _loadingShared;

        /// <summary>True when a shared folder exists locally or this PC is connected to the server.</summary>
        public static bool HasFolder()
        {
            // Remote clients have no local inventory folder; the server is the lock.
            if (DataLink.IsRemote)
                return true;

            return !string.IsNullOrWhiteSpace(AppState.InventoryFolder) &&
                   Directory.Exists(AppState.InventoryFolder);
        }

        /// <summary>Read this PC's last folder or server host from the local JSON file.</summary>
        public static void LoadSavedFolder()
        {
            // First run on this PC has nothing to restore.
            if (!File.Exists(SettingsPath))
                return;

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                // Empty or invalid JSON deserializes to null.
                if (settings == null)
                    return;

                // Only restore a folder that still exists on disk.
                if (!string.IsNullOrWhiteSpace(settings.InventoryFolder) &&
                    Directory.Exists(settings.InventoryFolder))
                {
                    AppState.InventoryFolder = settings.InventoryFolder;
                }

                AppState.ServerHost = settings.ServerHost ?? "";
                // Ignore out-of-range ports left by a hand-edited file.
                if (settings.ServerPort > 0 && settings.ServerPort <= 65535)
                    AppState.ServerPort = settings.ServerPort;
                AppState.ServerFingerprint = settings.ServerFingerprint ?? "";
                AppState.UseServer = DataLink.UseInventoryServer && settings.UseServer;

                ApplyLocalFallback(settings);
            }
            catch
            {
                // ignore bad settings file
            }
        }

        /// <summary>Load company, SMTP, and session policy from the shared database.</summary>
        public static void LoadSharedSettings()
        {
            // Nothing to read until a folder or server is available.
            if (!HasFolder())
                return;

            try
            {
                _loadingShared = true;
                SqliteInventory.EnsureCreated();
                var shared = SqliteInventory.ReadPublicSettings();
                // Empty DB: seed it from whatever this PC already has in memory.
                if (shared.Count == 0)
                {
                    _loadingShared = false;
                    SaveSettings();
                    return;
                }

                ApplyShared(shared);
                // Admins also get SMTP/Plaid secrets that public reads omit.
                if (AppState.IsAdmin)
                    ApplyShared(SqliteInventory.ReadSettings());
                SqliteInventory.ApplyAdminSmtp();

                string? email = SqliteInventory.ReadUserEmail(Environment.UserName);
                // Prefer the shared email for this Windows user.
                if (email != null)
                    AppState.UserEmail = email;
                else if (!string.IsNullOrWhiteSpace(AppState.UserEmail))
                    // Seed the shared row from this PC's last-known address.
                    SqliteInventory.WriteUserEmail(Environment.UserName, AppState.UserEmail);
            }
            catch
            {
                // keep whatever was loaded from the local file
            }
            finally
            {
                _loadingShared = false;
            }
        }

        public static event Action? Changed;

        /// <summary>Point this PC at a shared folder and drop any server connection.</summary>
        public static void SaveFolder(string folder)
        {
            AppState.UseServer = false;
            AppState.InventoryFolder = folder;
            DataLink.Disconnect();
            SaveSettings();
        }

        /// <summary>Remember the inventory server endpoint on this PC only.</summary>
        public static void SaveServer(string host, int port, string fingerprint)
        {
            AppState.UseServer = true;
            AppState.ServerHost = (host ?? "").Trim();
            AppState.ServerPort = port > 0 ? port : DataLink.DefaultPort;
            AppState.ServerFingerprint = fingerprint ?? "";
            WriteLocalJson();
        }

        /// <summary>Write local JSON and, when allowed, company settings to the shared database.</summary>
        public static bool SaveSettings()
        {
            bool ok = true;
            try
            {
                WriteLocalJson();
            }
            catch
            {
                // Local JSON may be locked or the folder read-only; still try the shared write.
                ok = false;
            }

            // Skip the shared write while LoadSharedSettings is applying rows (avoids a loop).
            if (!_loadingShared && (HasFolder() || DataLink.IsRemote))
            {
                try
                {
                    // Remote non-admins cannot overwrite company-wide settings.
                    if (AppState.IsAdmin || !DataLink.IsRemote)
                        SqliteInventory.WriteSettings(CurrentShared());
                    SqliteInventory.WriteUserEmail(Environment.UserName, AppState.UserEmail);
                    // Keep the signed-in account row in sync with Settings.
                    if (!string.IsNullOrWhiteSpace(AppState.CurrentUsername))
                        SqliteInventory.UpdateAccountEmail(AppState.CurrentUsername, AppState.UserEmail);
                }
                catch
                {
                    // Shared write can fail while the local path still saved.
                    ok = false;
                }
            }

            NotifyChanged();
            return ok;
        }

        /// <summary>Writes SMTP login fields to the shared database and checks they round-trip.</summary>
        public static bool SaveSmtp()
        {
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["smtp_host"] = string.IsNullOrWhiteSpace(AppState.SmtpHost)
                    ? Mailer.DefaultHost
                    : AppState.SmtpHost.Trim(),
                ["smtp_port"] = (AppState.SmtpPort > 0 ? AppState.SmtpPort : Mailer.DefaultPort).ToString(),
                ["smtp_user"] = AppState.SmtpUser ?? "",
                ["smtp_password"] = AppState.SmtpPassword ?? "",
                ["smtp_ssl"] = AppState.SmtpSsl ? "1" : "0"
            };
            try
            {
                SqliteInventory.WriteSettings(values);
                var check = SqliteInventory.ReadSettings();
                check.TryGetValue("smtp_user", out var user);
                check.TryGetValue("smtp_password", out var password);
                return string.Equals(user ?? "", AppState.SmtpUser ?? "", StringComparison.Ordinal) &&
                       string.Equals(password ?? "", AppState.SmtpPassword ?? "", StringComparison.Ordinal);
            }
            catch
            {
                // Shared DB unavailable; caller should keep the SMTP form open.
                return false;
            }
        }

        /// <summary>Tell open windows that folder or company settings changed.</summary>
        public static void NotifyChanged() => Changed?.Invoke();

        /// <summary>Disable nav buttons until a data folder or server is available; Settings stays enabled.</summary>
        public static void ApplyNavLock(params Button[] buttons)
        {
            bool unlocked = HasFolder();

            foreach (var btn in buttons)
            {
                // Settings must stay clickable so a folder can still be chosen.
                if (btn.Name == "btnSettings")
                    continue;

                btn.Enabled = unlocked;
            }
        }

        /// <summary>Persist this PC's folder or server endpoint next to the exe.</summary>
        private static void WriteLocalJson()
        {
            var settings = new AppSettings
            {
                InventoryFolder = AppState.InventoryFolder,
                UseServer = AppState.UseServer,
                ServerHost = AppState.ServerHost,
                ServerPort = AppState.ServerPort,
                ServerFingerprint = AppState.ServerFingerprint
            };

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SettingsPath, json);
        }

        /// <summary>Company, numbering, SMTP, Plaid, and stay-signed-in policy for the shared database.</summary>
        private static Dictionary<string, string> CurrentShared()
        {
            return new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["term_start"] = AppState.TermStartDate?.ToString("yyyy-MM-dd") ?? "",
                ["business_name"] = AppState.BusinessName ?? "",
                ["address"] = AppState.Address ?? "",
                ["phone"] = AppState.Phone ?? "",
                ["company_email"] = AppState.CompanyEmail ?? "",
                ["ein"] = AppState.Ein ?? "",
                ["payment_terms"] = AppState.PaymentTerms ?? "",
                ["sales_order_pattern"] = AppState.SalesOrderPattern ?? "",
                ["sales_order_start"] = AppState.SalesOrderStart ?? "",
                ["product_number_pattern"] = AppState.ProductNumberPattern ?? "",
                ["product_number_start"] = AppState.ProductNumberStart ?? "",
                ["reuse_missing_numbers"] = AppState.ReuseMissingNumbers ? "1" : "0",
                ["smtp_host"] = string.IsNullOrWhiteSpace(AppState.SmtpHost)
                    ? Mailer.DefaultHost
                    : AppState.SmtpHost.Trim(),
                ["smtp_port"] = (AppState.SmtpPort > 0 ? AppState.SmtpPort : Mailer.DefaultPort).ToString(),
                ["smtp_user"] = AppState.SmtpUser ?? "",
                ["smtp_password"] = AppState.SmtpPassword ?? "",
                ["smtp_ssl"] = AppState.SmtpSsl ? "1" : "0",
                ["plaid_client_id"] = AppState.PlaidClientId ?? "",
                ["plaid_secret"] = AppState.PlaidSecret ?? "",
                ["plaid_env"] = AppState.PlaidEnv ?? "sandbox",
                ["plaid_sync_hours"] = AppState.PlaidSyncHours.ToString(),
                ["plaid_last_sync"] = AppState.PlaidLastSync?.ToString("o") ?? "",
                ["stay_signed_in_enabled"] = AppState.StaySignedInEnabled ? "1" : "0",
                ["stay_signed_in_days"] = AppState.StaySignedInDays.ToString(),
                ["idle_close_hours"] = AppState.IdleCloseHours.ToString()
            };
        }

        /// <summary>Copy shared-database keys into AppState, keeping current values when a key is missing.</summary>
        private static void ApplyShared(Dictionary<string, string> shared)
        {
            // Ignore a blank or unparsable term_start rather than clearing a good in-memory date.
            if (shared.TryGetValue("term_start", out var term) &&
                DateTime.TryParse(term, out var start))
                AppState.TermStartDate = start;

            AppState.BusinessName = Get(shared, "business_name", AppState.BusinessName);
            AppState.Address = Get(shared, "address", AppState.Address);
            AppState.Phone = Get(shared, "phone", AppState.Phone);
            AppState.CompanyEmail = Get(shared, "company_email", AppState.CompanyEmail);
            AppState.Ein = Get(shared, "ein", AppState.Ein);
            AppState.PaymentTerms = Get(shared, "payment_terms", AppState.PaymentTerms);
            AppState.SalesOrderPattern = Get(shared, "sales_order_pattern", AppState.SalesOrderPattern);
            AppState.SalesOrderStart = Get(shared, "sales_order_start", AppState.SalesOrderStart);
            AppState.ProductNumberPattern = Get(shared, "product_number_pattern", AppState.ProductNumberPattern);
            AppState.ProductNumberStart = Get(shared, "product_number_start", AppState.ProductNumberStart);
            AppState.ReuseMissingNumbers =
                Get(shared, "reuse_missing_numbers", AppState.ReuseMissingNumbers ? "1" : "0") != "0";
            string smtpHost = Get(shared, "smtp_host", AppState.SmtpHost);
            AppState.SmtpHost = string.IsNullOrWhiteSpace(smtpHost) ? Mailer.DefaultHost : smtpHost.Trim();
            // Invalid or missing port would break Mailer; fall back to the default.
            if (int.TryParse(Get(shared, "smtp_port", AppState.SmtpPort.ToString()), out int port) && port > 0)
                AppState.SmtpPort = port;
            else
                AppState.SmtpPort = Mailer.DefaultPort;
            AppState.SmtpUser = Get(shared, "smtp_user", AppState.SmtpUser);
            AppState.SmtpPassword = Get(shared, "smtp_password", AppState.SmtpPassword);
            AppState.SmtpSsl = Get(shared, "smtp_ssl", AppState.SmtpSsl ? "1" : "0") != "0";
            AppState.PlaidClientId = Get(shared, "plaid_client_id", AppState.PlaidClientId);
            AppState.PlaidSecret = Get(shared, "plaid_secret", AppState.PlaidSecret);
            AppState.PlaidEnv = Get(shared, "plaid_env", AppState.PlaidEnv);
            // Only 0 (off), 1, or 3 hours are valid auto-sync choices.
            if (int.TryParse(Get(shared, "plaid_sync_hours", AppState.PlaidSyncHours.ToString()), out int hours) &&
                (hours == 0 || hours == 1 || hours == 3))
                AppState.PlaidSyncHours = hours;
            if (DateTime.TryParse(Get(shared, "plaid_last_sync", ""), out var lastSync))
                AppState.PlaidLastSync = lastSync;
            AppState.StaySignedInEnabled =
                Get(shared, "stay_signed_in_enabled", AppState.StaySignedInEnabled ? "1" : "0") != "0";
            // Zero or junk would disable stay-signed-in immediately.
            if (int.TryParse(Get(shared, "stay_signed_in_days", AppState.StaySignedInDays.ToString()), out int days) &&
                days > 0)
                AppState.StaySignedInDays = days;
            // Zero would close the app on the next idle tick.
            if (int.TryParse(Get(shared, "idle_close_hours", AppState.IdleCloseHours.ToString()), out int idle) &&
                idle > 0)
                AppState.IdleCloseHours = idle;
        }

        /// <summary>Apply company fields from the old local JSON when the shared DB is not available yet.</summary>
        private static void ApplyLocalFallback(AppSettings settings)
        {
            // Older local JSON stored the term date before it moved to the shared DB.
            if (DateTime.TryParse(settings.TermStartDate, out var start))
                AppState.TermStartDate = start;

            AppState.UserEmail = settings.UserEmail ?? AppState.UserEmail;
            AppState.BusinessName = settings.BusinessName ?? AppState.BusinessName;
            AppState.Address = settings.Address ?? AppState.Address;
            AppState.Phone = settings.Phone ?? AppState.Phone;
            AppState.CompanyEmail = settings.CompanyEmail ?? AppState.CompanyEmail;
            AppState.Ein = settings.Ein ?? AppState.Ein;
            AppState.PaymentTerms = settings.PaymentTerms ?? AppState.PaymentTerms;
            AppState.SalesOrderPattern = settings.SalesOrderPattern ?? AppState.SalesOrderPattern;
            AppState.SalesOrderStart = settings.SalesOrderStart ?? AppState.SalesOrderStart;
            AppState.ProductNumberPattern = settings.ProductNumberPattern ?? AppState.ProductNumberPattern;
            AppState.ProductNumberStart = settings.ProductNumberStart ?? AppState.ProductNumberStart;
        }

        /// <summary>Read a shared-settings key, or keep the in-memory fallback when it is missing.</summary>
        private static string Get(Dictionary<string, string> map, string key, string fallback)
        {
            return map.TryGetValue(key, out var value) ? value ?? "" : fallback;
        }

        /// <summary>Make the Windows user name safe as a settings file name.</summary>
        private static string SanitizeUserName(string userName)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                userName = userName.Replace(c, '_');

            return string.IsNullOrWhiteSpace(userName) ? "user" : userName;
        }
    }

    /// <summary>Local JSON shape. Folder path or server host/port/fingerprint live per PC.</summary>
    public class AppSettings
    {
        public string? InventoryFolder { get; set; }
        public bool UseServer { get; set; }
        public string? ServerHost { get; set; }
        public int ServerPort { get; set; }
        public string? ServerFingerprint { get; set; }
        public string? TermStartDate { get; set; }
        public string? UserEmail { get; set; }
        public string? BusinessName { get; set; }
        public string? Address { get; set; }
        public string? Phone { get; set; }
        public string? CompanyEmail { get; set; }
        public string? Ein { get; set; }
        public string? PaymentTerms { get; set; }
        public string? SalesOrderPattern { get; set; }
        public string? SalesOrderStart { get; set; }
        public string? ProductNumberPattern { get; set; }
        public string? ProductNumberStart { get; set; }
    }

    /// <summary>In-memory session: folder, signed-in user, company info, and the Current/Old view.</summary>
    public static class AppState
    {
        public static string? InventoryFolder { get; set; }
        /// <summary>This PC talks to the inventory server. Later the host can be filled in without typing it.</summary>
        public static bool UseServer { get; set; }
        public static string ServerHost { get; set; } = "";
        public static int ServerPort { get; set; } = 7443;
        public static string ServerFingerprint { get; set; } = "";
        public static DateTime? TermStartDate { get; set; }
        public static string UserEmail { get; set; } = "";
        public static string BusinessName { get; set; } = "";
        public static string Address { get; set; } = "";
        public static string Phone { get; set; } = "";
        public static string CompanyEmail { get; set; } = "";
        public static string Ein { get; set; } = "";
        public static string PaymentTerms { get; set; } = "";
        public static string SalesOrderPattern { get; set; } = "";
        public static string SalesOrderStart { get; set; } = "";
        public static string ProductNumberPattern { get; set; } = "";
        public static string ProductNumberStart { get; set; } = "";
        /// <summary>When true, the next number fills the lowest unused value from the start.</summary>
        public static bool ReuseMissingNumbers { get; set; } = true;
        /// <summary>Old toggle: process pages show archive rows plus live rows.</summary>
        public static bool ViewingOldInventory { get; set; }
        public static string CurrentUsername { get; set; } = "";
        public static string CurrentDisplayName { get; set; } = "";
        public static bool IsAdmin { get; set; }
        public static bool IsIt { get; set; }
        /// <summary>This PC remembered the login because Stay signed in was checked at sign-in.</summary>
        public static bool StaySignedIn { get; set; }
        /// <summary>Shared Admin switch. When off, nobody can stay signed in.</summary>
        public static bool StaySignedInEnabled { get; set; } = true;
        /// <summary>How many days a stay-signed-in session lasts. Set on Admin.</summary>
        public static int StaySignedInDays { get; set; } = 30;
        /// <summary>Close the app after this many idle hours when Stay signed in is on. Set on Admin.</summary>
        public static int IdleCloseHours { get; set; } = 5;
        public static string SmtpHost { get; set; } = Mailer.DefaultHost;
        public static int SmtpPort { get; set; } = Mailer.DefaultPort;
        public static string SmtpUser { get; set; } = "";
        public static string SmtpPassword { get; set; } = "";
        public static bool SmtpSsl { get; set; } = true;
        public static string PlaidClientId { get; set; } = "";
        public static string PlaidSecret { get; set; } = "";
        public static string PlaidEnv { get; set; } = "sandbox";
        /// <summary>0 = off, 1 or 3 = auto-sync hours while the app is open.</summary>
        public static int PlaidSyncHours { get; set; } = 1;
        public static DateTime? PlaidLastSync { get; set; }
        public static HashSet<string> DeniedTables { get; set; } = new(StringComparer.OrdinalIgnoreCase);

        public static bool SignedIn => !string.IsNullOrWhiteSpace(CurrentUsername);

        /// <summary>Clear the signed-in user, roles, and table denials for this session.</summary>
        public static void SignOut()
        {
            CurrentUsername = "";
            CurrentDisplayName = "";
            IsAdmin = false;
            IsIt = false;
            StaySignedIn = false;
            ViewingOldInventory = false;
            DeniedTables = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            DataAccess.Clear();
        }
    }
}
