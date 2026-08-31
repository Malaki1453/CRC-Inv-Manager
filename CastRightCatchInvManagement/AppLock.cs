using System.Text.Json;
using System.Text.RegularExpressions;

namespace CastRightCatchInvManagement
{
    public static class AppLock
    {
        private static readonly string SettingsPath =
            Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"settings_{SanitizeUserName(Environment.UserName)}.json");

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

                if (DateTime.TryParse(settings.TermStartDate, out var start))
                    AppState.TermStartDate = start;

                AppState.UserEmail = settings.UserEmail ?? "";
                AppState.BusinessName = settings.BusinessName ?? "";
                AppState.Address = settings.Address ?? "";
                AppState.Phone = settings.Phone ?? "";
                AppState.CompanyEmail = settings.CompanyEmail ?? "";
                AppState.Ein = settings.Ein ?? "";
                AppState.PaymentTerms = settings.PaymentTerms ?? "";
            }
            catch
            {
                // ignore bad settings file
            }
        }

        public static event Action? Changed;

        public static void SaveFolder(string folder)
        {
            AppState.InventoryFolder = folder;
            SaveSettings();
            DataFiles.EnsureStoredInvoicesFolder();
        }

        public static void SaveSettings()
        {
            var settings = new AppSettings
            {
                InventoryFolder = AppState.InventoryFolder,
                TermStartDate = AppState.TermStartDate?.ToString("yyyy-MM-dd"),
                UserEmail = AppState.UserEmail,
                BusinessName = AppState.BusinessName,
                Address = AppState.Address,
                Phone = AppState.Phone,
                CompanyEmail = AppState.CompanyEmail,
                Ein = AppState.Ein,
                PaymentTerms = AppState.PaymentTerms
            };

            string json = JsonSerializer.Serialize(settings, new JsonSerializerOptions
            {
                WriteIndented = true
            });

            File.WriteAllText(SettingsPath, json);
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
    }
}