using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Konscious.Security.Cryptography;

namespace CastRightCatchInvManagement
{
    internal sealed class AppAccount
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool IsIt { get; set; }
        public bool MustChangePassword { get; set; }
    }

    internal static class Accounts
    {
        public const string FileName = "admins.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        public static string? GetFilePath()
        {
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, FileName);
        }

        public static void EnsureFile()
        {
            string? path = GetFilePath();
            if (path == null)
                return;

            if (!File.Exists(path))
                WriteFile(new List<string>(), new List<string>());
        }

        public static bool HasItUser()
        {
            if (ReadIt().Count > 0)
                return true;

            try
            {
                return SqliteInventory.ListAccounts().Any(row => row.IsIt);
            }
            catch
            {
                return false;
            }
        }

        public static List<string> ReadAdmins() => ReadFile().Admins;

        public static List<string> ReadIt() => ReadFile().It;

        public static bool IsAdmin(string? username) => Contains(ReadAdmins(), username);

        public static bool IsIt(string? username) => Contains(ReadIt(), username);

        public static readonly string[] SecurityQuestionBank =
        {
            "What city were you born in?",
            "What was the name of your first pet?",
            "What is your mother's maiden name?",
            "What was the name of your first school?",
            "What street did you grow up on?",
            "What was the make of your first car?",
            "What is your oldest sibling's middle name?",
            "What was your childhood nickname?"
        };

        public static string GenerateTemporaryPassword()
        {
            const string letters = "ABCDEFGHJKLMNPQRSTUVWXYZabcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string all = letters + digits;
            var chars = new char[6];
            chars[0] = letters[RandomNumberGenerator.GetInt32(letters.Length)];
            chars[1] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
            for (int i = 2; i < chars.Length; i++)
                chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

            for (int i = chars.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars);
        }

        public static void AddAdmin(string username) => AddName(admin: true, username);

        public static void AddIt(string username) => AddName(admin: false, username);

        public static bool RemoveAdmin(string username, out string error) =>
            RemoveName(admin: true, username, out error);

        public static bool RemoveIt(string username, out string error) =>
            RemoveName(admin: false, username, out error);

        public static bool DeleteUser(string username, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            if (username.Length == 0)
            {
                error = "Choose a user.";
                return false;
            }

            if (username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                error = "You cannot delete the account you are signed in with.";
                return false;
            }

            var accounts = List();
            var target = accounts.FirstOrDefault(a =>
                a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            if (target == null)
            {
                error = "Could not find that user.";
                return false;
            }

            if (target.IsIt && accounts.Count(a => a.IsIt) <= 1)
            {
                error = "There must be at least one IT user.";
                return false;
            }

            if (target.IsAdmin && accounts.Count(a => a.IsAdmin) <= 1)
            {
                error = "There must be at least one administrator.";
                return false;
            }

            if (target.IsIt && !RemoveIt(username, out error))
                return false;
            if (target.IsAdmin && !RemoveAdmin(username, out error))
                return false;

            if (!SqliteInventory.DeleteAccount(username))
            {
                error = "Could not delete that user.";
                return false;
            }

            return true;
        }

        public static int AccountCount()
        {
            if (!AppLock.HasFolder())
                return 0;

            try
            {
                return SqliteInventory.CountAccounts();
            }
            catch
            {
                return 0;
            }
        }

        public static bool CreateFirstItUser(string username, string password, string? displayName, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim();
            password ??= "";

            if (username.Length == 0)
            {
                error = "Enter a username.";
                return false;
            }

            if (!PasswordMeetsPolicy(password, out error))
                return false;

            if (SqliteInventory.TryGetAccount(username, out string hash, out string salt, out _, out _, out _))
            {
                if (!VerifyPassword(password, hash, salt))
                {
                    error = "That username is already in use.";
                    return false;
                }

                if (!hash.StartsWith("$argon2id$", StringComparison.Ordinal))
                    SetPassword(username, password, out _);
            }
            else
            {
                HashPassword(password, out hash, out salt);
                if (!SqliteInventory.InsertAccount(username, displayName, hash, salt, AppState.UserEmail))
                {
                    error = "That username is already in use.";
                    return false;
                }
            }

            AddIt(username);
            if (ReadAdmins().Count == 0)
                AddAdmin(username);
            SqliteInventory.SetAccountRoles(username, IsAdmin(username), true);
            return true;
        }

        public static bool CreateAdmin(string username, string password, string? displayName, out string error) =>
            CreateFirstItUser(username, password, displayName, out error);

        public static bool CreateUser(
            string username,
            string password,
            string? displayName,
            string? email,
            out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim();
            email = (email ?? "").Trim();

            if (username.Length == 0)
            {
                error = "Enter a username.";
                return false;
            }

            password ??= "";
            if (password.Length == 0)
                password = GenerateTemporaryPassword();
            if (!PasswordMeetsPolicy(password, out error, minimumLength: 6))
                return false;

            HashPassword(password, out string hash, out string salt);
            if (!SqliteInventory.InsertAccount(username, displayName, hash, salt, email))
            {
                error = "That username is already in use.";
                return false;
            }

            SqliteInventory.SetMustChangePassword(username, true);
            return true;
        }

        public static bool SetPassword(string username, string password, out string error, bool? mustChange = null)
        {
            error = "";
            username = (username ?? "").Trim();
            password ??= "";
            if (username.Length == 0)
            {
                error = "Choose a user.";
                return false;
            }

            if (!PasswordMeetsPolicy(password, out error, minimumLength: mustChange == true ? 6 : PasswordMinLength))
                return false;

            HashPassword(password, out string hash, out string salt);
            if (!SqliteInventory.UpdateAccountPassword(username, hash, salt))
            {
                error = "Could not update that password.";
                return false;
            }

            if (mustChange.HasValue)
                SqliteInventory.SetMustChangePassword(username, mustChange.Value);
            return true;
        }

        public static bool ChangeOwnPassword(string username, string currentPassword, string newPassword, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            if (!SqliteInventory.TryGetAccount(username, out string hash, out string salt, out _, out _, out _))
            {
                error = "Could not find that account.";
                return false;
            }

            if (!VerifyPassword(currentPassword ?? "", hash, salt))
            {
                error = "Current password is not right.";
                return false;
            }

            return SetPassword(username, newPassword, out error, mustChange: false);
        }

        public static bool RenameUser(string oldUsername, string newUsername, out string error)
        {
            error = "";
            oldUsername = (oldUsername ?? "").Trim();
            newUsername = (newUsername ?? "").Trim();
            if (oldUsername.Length == 0 || newUsername.Length == 0)
            {
                error = "Enter a username.";
                return false;
            }

            if (oldUsername.Equals(newUsername, StringComparison.OrdinalIgnoreCase))
                return true;

            if (SqliteInventory.TryGetAccount(newUsername, out _, out _, out _, out _, out _))
            {
                error = "That username is already in use.";
                return false;
            }

            if (!SqliteInventory.RenameAccount(oldUsername, newUsername))
            {
                error = "Could not change that username.";
                return false;
            }

            RenameInFile(oldUsername, newUsername);
            if (oldUsername.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                AppState.CurrentUsername = newUsername;
            return true;
        }

        public static List<AppAccount> List()
        {
            var admins = new HashSet<string>(ReadAdmins(), StringComparer.OrdinalIgnoreCase);
            var it = new HashSet<string>(ReadIt(), StringComparer.OrdinalIgnoreCase);
            return SqliteInventory.ListAccounts()
                .Select(row => new AppAccount
                {
                    Username = row.Username,
                    DisplayName = row.DisplayName.Length > 0 ? row.DisplayName : row.Username,
                    Email = row.Email,
                    IsAdmin = admins.Contains(row.Username) || row.IsAdmin,
                    IsIt = it.Contains(row.Username) || row.IsIt
                })
                .ToList();
        }

        public static bool TrySignIn(string username, string password, out AppAccount? account, out string error)
        {
            account = null;
            error = "";
            username = (username ?? "").Trim();
            if (username.Length == 0 || string.IsNullOrEmpty(password))
            {
                error = "Enter a username and password.";
                return false;
            }

            if (!SqliteInventory.TryGetAccount(username, out string hash, out string salt, out string display, out string email, out bool mustChange))
            {
                error = "That username or password is not right.";
                return false;
            }

            if (!VerifyPassword(password, hash, salt))
            {
                error = "That username or password is not right.";
                return false;
            }

            if (!hash.StartsWith("$argon2id$", StringComparison.Ordinal))
                SetPassword(username, password, out _);

            account = new AppAccount
            {
                Username = username,
                DisplayName = display.Length > 0 ? display : username,
                Email = email,
                IsAdmin = IsAdmin(username),
                IsIt = IsIt(username),
                MustChangePassword = mustChange
            };
            return true;
        }

        public static void Apply(AppAccount account)
        {
            AppState.CurrentUsername = account.Username;
            AppState.CurrentDisplayName = account.DisplayName;
            AppState.IsAdmin = account.IsAdmin;
            AppState.IsIt = account.IsIt;
            if (!string.IsNullOrWhiteSpace(account.Email))
                AppState.UserEmail = account.Email;
            AppState.CurrentDisplayName = account.DisplayName;
        }

        public static bool HasSecurityQuestions(string username)
        {
            return SqliteInventory.TryGetSecurityQuestions(username, out _, out _, out _);
        }

        public static bool TryGetSecurityQuestions(string username, out string q1, out string q2, out string q3)
        {
            return SqliteInventory.TryGetSecurityQuestions(username, out q1, out q2, out q3);
        }

        public static bool SetSecurityQuestions(
            string username,
            string q1, string a1,
            string q2, string a2,
            string q3, string a3,
            out string error)
        {
            error = "";
            q1 = (q1 ?? "").Trim();
            q2 = (q2 ?? "").Trim();
            q3 = (q3 ?? "").Trim();
            a1 = NormalizeAnswer(a1);
            a2 = NormalizeAnswer(a2);
            a3 = NormalizeAnswer(a3);
            if (q1.Length == 0 || q2.Length == 0 || q3.Length == 0 ||
                a1.Length == 0 || a2.Length == 0 || a3.Length == 0)
            {
                error = "Choose three questions and answers.";
                return false;
            }

            if (q1.Equals(q2, StringComparison.OrdinalIgnoreCase) ||
                q1.Equals(q3, StringComparison.OrdinalIgnoreCase) ||
                q2.Equals(q3, StringComparison.OrdinalIgnoreCase))
            {
                error = "Pick three different questions.";
                return false;
            }

            HashPassword(a1, out string h1, out _);
            HashPassword(a2, out string h2, out _);
            HashPassword(a3, out string h3, out _);
            SqliteInventory.SetSecurityQuestions(username, q1, h1, q2, h2, q3, h3);
            return true;
        }

        public static bool VerifySecurityAnswers(string username, string a1, string a2, string a3)
        {
            if (!SqliteInventory.TryGetSecurityAnswerHashes(username, out string h1, out string h2, out string h3))
                return false;
            if (h1.Length == 0 || h2.Length == 0 || h3.Length == 0)
                return false;

            return VerifyPassword(NormalizeAnswer(a1), h1, "argon2id") &&
                   VerifyPassword(NormalizeAnswer(a2), h2, "argon2id") &&
                   VerifyPassword(NormalizeAnswer(a3), h3, "argon2id");
        }

        private static string NormalizeAnswer(string? answer)
        {
            answer = (answer ?? "").Trim().ToLowerInvariant();
            while (answer.Contains("  ", StringComparison.Ordinal))
                answer = answer.Replace("  ", " ", StringComparison.Ordinal);
            return answer;
        }

        private static AdminsFile ReadFile()
        {
            string? path = GetFilePath();
            if (path == null || !File.Exists(path))
                return new AdminsFile();

            try
            {
                var file = JsonSerializer.Deserialize<AdminsFile>(File.ReadAllText(path), JsonOptions)
                    ?? new AdminsFile();
                file.Admins = Clean(file.Admins);
                file.It = Clean(file.It);
                return file;
            }
            catch
            {
                return new AdminsFile();
            }
        }

        private static void AddName(bool admin, string username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return;

            var file = ReadFile();
            var list = admin ? file.Admins : file.It;
            if (!list.Any(name => name.Equals(username, StringComparison.OrdinalIgnoreCase)))
                list.Add(username);
            WriteFile(file.Admins, file.It);
            SqliteInventory.SetAccountRoles(username, Contains(file.Admins, username), Contains(file.It, username));
        }

        private static bool RemoveName(bool admin, string username, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            var file = ReadFile();
            var list = admin ? file.Admins : file.It;
            if (list.Count <= 1 && Contains(list, username))
            {
                error = admin
                    ? "There must be at least one administrator."
                    : "There must be at least one IT user.";
                return false;
            }

            list.RemoveAll(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
            WriteFile(file.Admins, file.It);
            SqliteInventory.SetAccountRoles(username, Contains(file.Admins, username), Contains(file.It, username));
            return true;
        }

        private static bool Contains(IEnumerable<string> names, string? username)
        {
            username = (username ?? "").Trim();
            if (username.Length == 0)
                return false;

            return names.Any(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        private static List<string> Clean(List<string>? names)
        {
            return (names ?? new List<string>())
                .Select(name => (name ?? "").Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        private static void RenameInFile(string oldUsername, string newUsername)
        {
            var file = ReadFile();
            ReplaceName(file.Admins, oldUsername, newUsername);
            ReplaceName(file.It, oldUsername, newUsername);
            WriteFile(file.Admins, file.It);
        }

        private static void ReplaceName(List<string> names, string oldUsername, string newUsername)
        {
            for (int i = 0; i < names.Count; i++)
            {
                if (names[i].Equals(oldUsername, StringComparison.OrdinalIgnoreCase))
                    names[i] = newUsername;
            }
        }

        private static void WriteFile(List<string> admins, List<string> it)
        {
            string? path = GetFilePath();
            if (path == null)
                return;

            var file = new AdminsFile { Admins = Clean(admins), It = Clean(it) };
            string json = JsonSerializer.Serialize(file, JsonOptions);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, path, overwrite: true);
            File.Delete(temp);
        }

        private const int PasswordMinLength = 8;
        private const int ArgonMemoryKb = 19456;
        private const int ArgonIterations = 2;
        private const int ArgonParallelism = 1;
        private const int ArgonHashLength = 32;

        private static bool PasswordMeetsPolicy(string password, out string error, int? minimumLength = null)
        {
            error = "";
            int min = minimumLength ?? PasswordMinLength;
            if (password.Length < min)
            {
                error = "Password must be at least " + min + " characters.";
                return false;
            }

            return true;
        }

        private static void HashPassword(string password, out string hash, out string salt)
        {
            byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
            byte[] hashBytes = Argon2Hash(password, saltBytes, ArgonMemoryKb, ArgonIterations, ArgonParallelism);
            salt = "argon2id";
            hash = "$argon2id$v=19$m=" + ArgonMemoryKb +
                   ",t=" + ArgonIterations +
                   ",p=" + ArgonParallelism +
                   "$" + Convert.ToBase64String(saltBytes) +
                   "$" + Convert.ToBase64String(hashBytes);
        }

        private static bool VerifyPassword(string password, string hash, string salt)
        {
            try
            {
                if (hash.StartsWith("$argon2id$", StringComparison.Ordinal))
                    return VerifyArgon2(password, hash);

                byte[] saltBytes = Convert.FromBase64String(salt);
                byte[] expected = Convert.FromBase64String(hash);
                byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                    password,
                    saltBytes,
                    100_000,
                    HashAlgorithmName.SHA256,
                    expected.Length);
                return CryptographicOperations.FixedTimeEquals(actual, expected);
            }
            catch
            {
                return false;
            }
        }

        private static bool VerifyArgon2(string password, string encoded)
        {
            string[] parts = encoded.Split('$');
            if (parts.Length != 6)
                return false;

            int memory = ArgonMemoryKb;
            int iterations = ArgonIterations;
            int parallelism = ArgonParallelism;
            foreach (var piece in parts[3].Split(','))
            {
                if (piece.StartsWith("m=", StringComparison.Ordinal) &&
                    int.TryParse(piece[2..], out int m))
                    memory = m;
                else if (piece.StartsWith("t=", StringComparison.Ordinal) &&
                         int.TryParse(piece[2..], out int t))
                    iterations = t;
                else if (piece.StartsWith("p=", StringComparison.Ordinal) &&
                         int.TryParse(piece[2..], out int p))
                    parallelism = p;
            }

            byte[] saltBytes = Convert.FromBase64String(parts[4]);
            byte[] expected = Convert.FromBase64String(parts[5]);
            byte[] actual = Argon2Hash(password, saltBytes, memory, iterations, parallelism);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        private static byte[] Argon2Hash(string password, byte[] salt, int memoryKb, int iterations, int parallelism)
        {
            using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
            {
                Salt = salt,
                DegreeOfParallelism = Math.Max(1, parallelism),
                Iterations = Math.Max(1, iterations),
                MemorySize = Math.Max(8, memoryKb)
            };
            return argon.GetBytes(ArgonHashLength);
        }

        private sealed class AdminsFile
        {
            [JsonPropertyName("admins")]
            public List<string> Admins { get; set; } = new();

            [JsonPropertyName("it")]
            public List<string> It { get; set; } = new();
        }
    }
}
