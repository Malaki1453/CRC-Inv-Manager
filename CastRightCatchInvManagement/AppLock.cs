using System.Text.Json;

namespace CastRightCatchInvManagement
{
    public static class AppLock
    {
        private static readonly string SettingsPath =
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"settings_{SanitizeUserName(Environment.UserName)}.json");

        private static bool _loadingShared;

        public static bool HasFolder()
        {
            return !string.IsNullOrWhiteSpace(AppState.InventoryFolder) &&
                   Directory.Exists(AppState.InventoryFolder);
        }

        public static void LoadSavedFolder()
        {
            if (!File.Exists(SettingsPath))
                return;

            try
            {
                var settings = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(SettingsPath));
                if (settings == null)
                    return;

                if (!string.IsNullOrWhiteSpace(settings.InventoryFolder) &&
                    Directory.Exists(settings.InventoryFolder))
                {
                    AppState.InventoryFolder = settings.InventoryFolder;
                }

                ApplyLocalFallback(settings);
            }
            catch
            {
                // ignore bad settings file
            }
        }

        public static void LoadSharedSettings()
        {
            if (!HasFolder())
                return;

            try
            {
                _loadingShared = true;
                SqliteInventory.EnsureCreated();
                var shared = SqliteInventory.ReadSettings();
                if (shared.Count == 0)
                {
                    _loadingShared = false;
                    SaveSettings();
                    return;
                }

                ApplyShared(shared);

                string? email = SqliteInventory.ReadUserEmail(Environment.UserName);
                if (email != null)
                    AppState.UserEmail = email;
                else if (!string.IsNullOrWhiteSpace(AppState.UserEmail))
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

        public static void SaveFolder(string folder)
        {
            AppState.InventoryFolder = folder;
            SaveSettings();
            DataFiles.EnsureStoredInvoicesFolder();
            DataFiles.EnsureStoredSalesOrdersFolder();
        }

        public static void SaveSettings()
        {
            WriteLocalJson();

            if (!_loadingShared && HasFolder())
            {
                try
                {
                    SqliteInventory.WriteSettings(CurrentShared());
                    SqliteInventory.WriteUserEmail(Environment.UserName, AppState.UserEmail);
                }
                catch
                {
                    // keep the local folder pointer even if the database is busy
                }
            }

            Changed?.Invoke();
        }

        public static void ApplyNavLock(params Button[] buttons)
        {
            bool unlocked = HasFolder();

            foreach (var btn in buttons)
            {
                if (btn.Name == "btnSettings")
                    continue;

                btn.Enabled = unlocked;
            }
        }

        private static void WriteLocalJson()
        {
            var settings = new AppSettings
            {
                InventoryFolder = AppState.InventoryFolder
            };

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SettingsPath, json);
        }

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
                ["product_number_start"] = AppState.ProductNumberStart ?? ""
            };
        }

        private static void ApplyShared(Dictionary<string, string> shared)
        {
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
        }

        private static void ApplyLocalFallback(AppSettings settings)
        {
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

        private static string Get(Dictionary<string, string> map, string key, string fallback)
        {
            return map.TryGetValue(key, out var value) ? value ?? "" : fallback;
        }

        private static string SanitizeUserName(string userName)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                userName = userName.Replace(c, '_');

            return string.IsNullOrWhiteSpace(userName) ? "user" : userName;
        }
    }

    public class AppSettings
    {
        public string? InventoryFolder { get; set; }
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

    public static class AppState
    {
        public static string? InventoryFolder { get; set; }
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
    }
}
