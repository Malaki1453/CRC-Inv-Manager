using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using CrcInventory.Protocol;
using Konscious.Security.Cryptography;

namespace CastRightCatchInvManagement
{
    /// <summary>Signed-in user as loaded from the database and admins.json.</summary>
    internal sealed class AppAccount
    {
        public string Username { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Email { get; set; } = "";
        public bool IsAdmin { get; set; }
        public bool IsIt { get; set; }
        public bool MustChangePassword { get; set; }
        public bool StaySignedIn { get; set; }
        public bool LoginLocked { get; set; }
        public string? SessionToken { get; set; }
    }

    /// <summary>
    /// Logins, roles, and password hashing (Argon2id). Roles also live in admins.json
    /// so every computer shares who is IT or administrator.
    /// </summary>
    internal static class Accounts
    {
        public const string FileName = "admins.json";

        private static readonly JsonSerializerOptions JsonOptions = new()
        {
            WriteIndented = true,
            PropertyNameCaseInsensitive = true
        };

        /// <summary>admins.json in the shared inventory folder, or null when no folder is set.</summary>
        public static string? GetFilePath()
        {
            // Roles live with the database; a missing folder means we cannot read them yet.
            if (string.IsNullOrWhiteSpace(AppState.InventoryFolder) ||
                !Directory.Exists(AppState.InventoryFolder))
                return null;

            return Path.Combine(AppState.InventoryFolder, FileName);
        }

        /// <summary>Create an empty admins.json on a local folder so roles have a file to share.</summary>
        public static void EnsureFile()
        {
            // Server clients read roles over the wire, not from a local JSON file.
            if (DataLink.IsRemote)
                return;
            string? path = GetFilePath();
            // Sign-in has not chosen a folder yet.
            if (path == null)
                return;

            // First run on this folder: empty admin and IT lists until bootstrap.
            if (!File.Exists(path))
                WriteFile(new List<string>(), new List<string>());
        }

        /// <summary>True when at least one IT user exists so the bootstrap screen can hide.</summary>
        public static bool HasItUser()
        {
            // The server reports this so clients do not read admins.json themselves.
            if (DataLink.IsRemote)
                return DataLink.HasItUser;
            // admins.json is the shared source of IT names.
            if (ReadIt().Count > 0)
                return true;

            // The database may not exist yet on first run.
            try
            {
                return SqliteInventory.ListAccounts().Any(row => row.IsIt);
            }
            // A missing or locked database still needs the create-IT screen.
            catch
            {
                // A missing or locked database still needs the create-IT screen.
                return false;
            }
        }

        /// <summary>Administrator usernames from admins.json (or the server).</summary>
        public static List<string> ReadAdmins() => ReadFile().Admins;

        /// <summary>IT usernames from admins.json (or the server).</summary>
        public static List<string> ReadIt() => ReadFile().It;

        /// <summary>True when this username is listed as an administrator.</summary>
        public static bool IsAdmin(string? username) => Contains(ReadAdmins(), username);

        /// <summary>True when this username is listed as IT.</summary>
        public static bool IsIt(string? username) => Contains(ReadIt(), username);

        /// <summary>Random 10-character password that meets the capital/number/symbol policy.</summary>
        public static string GenerateTemporaryPassword()
        {
            const string upper = "ABCDEFGHJKLMNPQRSTUVWXYZ";
            const string lower = "abcdefghijkmnopqrstuvwxyz";
            const string digits = "23456789";
            const string symbols = "!@#$%*?-";
            const string all = upper + lower + digits + symbols;
            var chars = new char[10];
            chars[0] = upper[RandomNumberGenerator.GetInt32(upper.Length)];
            chars[1] = lower[RandomNumberGenerator.GetInt32(lower.Length)];
            chars[2] = digits[RandomNumberGenerator.GetInt32(digits.Length)];
            chars[3] = symbols[RandomNumberGenerator.GetInt32(symbols.Length)];
            for (int i = 4; i < chars.Length; i++)
                chars[i] = all[RandomNumberGenerator.GetInt32(all.Length)];

            for (int i = chars.Length - 1; i > 0; i--)
            {
                int j = RandomNumberGenerator.GetInt32(i + 1);
                (chars[i], chars[j]) = (chars[j], chars[i]);
            }

            return new string(chars);
        }

        /// <summary>Add this username to the administrator list and database roles.</summary>
        public static void AddAdmin(string username) => AddName(admin: true, username);

        /// <summary>Add this username to the IT list and database roles.</summary>
        public static void AddIt(string username) => AddName(admin: false, username);

        /// <summary>Remove administrator access if at least one admin would remain.</summary>
        public static bool RemoveAdmin(string username, out string error) =>
            RemoveName(admin: true, username, out error);

        /// <summary>Remove IT access if at least one IT user would remain.</summary>
        public static bool RemoveIt(string username, out string error) =>
            RemoveName(admin: false, username, out error);

        /// <summary>Delete a user after checking they are not the last IT or administrator.</summary>
        public static bool DeleteUser(string username, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            // The grid can fire delete with no selected username.
            if (username.Length == 0)
            {
                error = "Choose a user.";
                return false;
            }

            // Signing yourself out mid-delete would leave the app with no session.
            if (username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                error = "You cannot delete the account you are signed in with.";
                return false;
            }

            var accounts = List();
            var target = accounts.FirstOrDefault(a =>
                a.Username.Equals(username, StringComparison.OrdinalIgnoreCase));
            // Stale grid rows after another PC already deleted the account.
            if (target == null)
            {
                error = "Could not find that user.";
                return false;
            }

            // Someone must remain able to add users and unlock logins.
            if (target.IsIt && accounts.Count(a => a.IsIt) <= 1)
            {
                error = "There must be at least one IT user.";
                return false;
            }

            // Someone must remain able to change Admin management.
            if (target.IsAdmin && accounts.Count(a => a.IsAdmin) <= 1)
            {
                error = "There must be at least one administrator.";
                return false;
            }

            // Drop IT first so the last-IT check in RemoveName still sees them listed.
            if (target.IsIt && !RemoveIt(username, out error))
                return false;
            // Drop admin after IT so the last-admin check still sees them listed.
            if (target.IsAdmin && !RemoveAdmin(username, out error))
                return false;

            // Role lists were updated; fail if the login row itself cannot be removed.
            if (!SqliteInventory.DeleteAccount(username))
            {
                error = "Could not delete that user.";
                return false;
            }

            return true;
        }

        /// <summary>How many login rows exist, or 0 when the database is not open yet.</summary>
        public static int AccountCount()
        {
            // No folder/server means there is no accounts table to count.
            if (!AppLock.HasFolder())
                return 0;

            try
            {
                return SqliteInventory.CountAccounts();
            }
            // Treat a locked DB as empty so first-run UI can still appear.
            catch
            {
                // Treat a locked DB as empty so first-run UI can still appear.
                return 0;
            }
        }

        /// <summary>Create the first IT user on this PC (and admin if none exist). Not used on clients.</summary>
        public static bool CreateFirstItUser(string username, string password, string? displayName, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            displayName = string.IsNullOrWhiteSpace(displayName) ? username : displayName.Trim();
            password ??= "";

            // Bootstrap needs a login name even when display name is blank.
            if (username.Length == 0)
            {
                error = "Enter a username.";
                return false;
            }

            // Weak passwords are rejected before we write the first account.
            if (!PasswordMeetsPolicy(password, out error))
                return false;
            // Clients must not invent the first IT user; that belongs on the host.
            if (DataLink.IsRemote)
            {
                error = "Create the first IT user on the server PC (CrcInventoryServer --bootstrap).";
                return false;
            }

            // Re-run bootstrap with the same password upgrades a leftover row into IT.
            if (SqliteInventory.TryGetAccount(username, out string hash, out string salt, out _, out _, out _))
            {
                // A different password means someone else already owns this username.
                if (!VerifyPassword(password, hash, salt))
                {
                    error = "That username is already in use.";
                    return false;
                }

                // Legacy PBKDF2 hashes are upgraded now that we know the plaintext.
                if (!hash.StartsWith("$argon2id$", StringComparison.Ordinal))
                    SetPassword(username, password, out _);
            }
            // No leftover row: insert the first local SQLite login.
            else
            {
                HashPassword(password, out hash, out salt);
                // Unique-username constraint is the usual insert failure.
                if (!SqliteInventory.InsertAccount(username, displayName, hash, salt, AppState.UserEmail))
                {
                    error = "That username is already in use.";
                    return false;
                }
            }

            AddIt(username);
            // First IT also becomes admin so Admin management is not locked out.
            if (ReadAdmins().Count == 0)
                AddAdmin(username);
            SqliteInventory.SetAccountRoles(username, IsAdmin(username), true);
            return true;
        }

        /// <summary>Same as CreateFirstItUser; kept for older callers.</summary>
        public static bool CreateAdmin(string username, string password, string? displayName, out string error) =>
            CreateFirstItUser(username, password, displayName, out error);

        /// <summary>Add a regular user with a temporary password they must change at sign-in.</summary>
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

            // IT must pick a login name before we generate a password.
            if (username.Length == 0)
            {
                error = "Enter a username.";
                return false;
            }

            password ??= "";
            // Blank password means generate one so the email can include it.
            if (password.Length == 0)
                password = GenerateTemporaryPassword();
            // Weak generated or typed passwords must not be stored or emailed.
            if (!PasswordMeetsPolicy(password, out error))
                return false;

            // Clients insert through the server so hashes stay on the host (not local SQLite).
            if (DataLink.IsRemote)
            {
                try
                {
                    DataLink.Call<bool>(ServerOps.AccountsInsert, new AccountWriteRequest
                    {
                        Username = username,
                        DisplayName = displayName,
                        Email = email,
                        Password = password
                    });
                    return true;
                }
                // Duplicate username and permission errors come back as the server message.
                catch (Exception ex)
                {
                    // Duplicate username and permission errors come back as the server message.
                    error = ex.Message;
                    return false;
                }
            }

            HashPassword(password, out string hash, out string salt);
            // Unique username is enforced in SQLite as well as on the server.
            if (!SqliteInventory.InsertAccount(username, displayName, hash, salt, email))
            {
                error = "That username is already in use.";
                return false;
            }

            SqliteInventory.SetMustChangePassword(username, true);
            return true;
        }

        /// <summary>Hash and store a new password. Optional mustChange forces a change at next sign-in.</summary>
        public static bool SetPassword(string username, string password, out string error, bool? mustChange = null)
        {
            error = "";
            username = (username ?? "").Trim();
            password ??= "";
            // Reset from the user grid needs a selected account.
            if (username.Length == 0)
            {
                error = "Choose a user.";
                return false;
            }

            // IT reset and own-change both use the same complexity rules.
            if (!PasswordMeetsPolicy(password, out error))
                return false;

            // Clients cannot write hashes locally; the server stores them (not local SQLite).
            if (DataLink.IsRemote)
            {
                try
                {
                    DataLink.Call<bool>(ServerOps.AccountsPassword, new AccountWriteRequest
                    {
                        Username = username,
                        Password = password,
                        MustChange = mustChange ?? false
                    });
                    return true;
                }
                // Server rejects weak passwords and unknown users with a message.
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            HashPassword(password, out string hash, out string salt);
            // Unknown username after a concurrent delete.
            if (!SqliteInventory.UpdateAccountPassword(username, hash, salt))
            {
                error = "Could not update that password.";
                return false;
            }

            // IT reset sets must-change; own-password change clears it.
            if (mustChange.HasValue)
                SqliteInventory.SetMustChangePassword(username, mustChange.Value);
            return true;
        }

        /// <summary>Change the signed-in user's password after verifying the current one.</summary>
        public static bool ChangeOwnPassword(string username, string currentPassword, string newPassword, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            // The server checks the current password so the client never sees the hash.
            if (DataLink.IsRemote)
            {
                try
                {
                    DataLink.Call<bool>(ServerOps.AuthChangePassword, new ChangePasswordRequest
                    {
                        Username = username,
                        CurrentPassword = currentPassword,
                        NewPassword = newPassword
                    });
                    return true;
                }
                // Wrong current password or policy errors come back as the server message.
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            // Local change still needs the stored hash to verify the current password.
            if (!SqliteInventory.TryGetAccount(username, out string hash, out string salt, out _, out _, out _))
            {
                error = "Could not find that account.";
                return false;
            }

            // Wrong current password must not replace the hash.
            if (!VerifyPassword(currentPassword ?? "", hash, salt))
            {
                error = "Current password is not right.";
                return false;
            }

            return SetPassword(username, newPassword, out error, mustChange: false);
        }

        /// <summary>Rename a login and keep admin/IT lists and the current session in sync.</summary>
        public static bool RenameUser(string oldUsername, string newUsername, out string error)
        {
            error = "";
            oldUsername = (oldUsername ?? "").Trim();
            newUsername = (newUsername ?? "").Trim();
            // Both names are required so we do not blank a login.
            if (oldUsername.Length == 0 || newUsername.Length == 0)
            {
                error = "Enter a username.";
                return false;
            }

            // Case-only edits are treated as no-op so we do not hit the unique index.
            if (oldUsername.Equals(newUsername, StringComparison.OrdinalIgnoreCase))
                return true;

            // Do not steal another person's login name.
            if (SqliteInventory.TryGetAccount(newUsername, out _, out _, out _, out _, out _))
            {
                error = "That username is already in use.";
                return false;
            }

            // Unique-index or missing-row failures leave both names unchanged.
            if (!SqliteInventory.RenameAccount(oldUsername, newUsername))
            {
                error = "Could not change that username.";
                return false;
            }

            RenameInFile(oldUsername, newUsername);
            // Keep the in-memory session matching the new login name.
            if (oldUsername.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
                AppState.CurrentUsername = newUsername;
            return true;
        }

        /// <summary>All accounts with admin/IT flags merged from admins.json and the database.</summary>
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
                    IsIt = it.Contains(row.Username) || row.IsIt,
                    StaySignedIn = row.StaySignedIn,
                    LoginLocked = row.LoginLocked
                })
                .ToList();
        }

        /// <summary>Verify username and password, or fail with a lock/wrong-password message.</summary>
        public static bool TrySignIn(
            string username,
            string password,
            out AppAccount? account,
            out string error,
            bool staySignedIn = false)
        {
            account = null;
            error = "";
            username = (username ?? "").Trim();
            // Empty fields should not count as a failed login attempt.
            if (username.Length == 0 || string.IsNullOrEmpty(password))
            {
                error = "Enter a username and password.";
                return false;
            }

            // Clients authenticate on the server so lockouts are shared.
            if (DataLink.IsRemote)
            {
                try
                {
                    var auth = DataLink.Call<AuthResponse>(ServerOps.AuthLogin, new LoginRequest
                    {
                        Username = username,
                        Password = password,
                        StaySignedIn = staySignedIn
                    });
                    account = FromAuth(auth);
                    return true;
                }
                // Wrong password, lock, and server-down all surface as the server message.
                catch (Exception ex)
                {
                    // Wrong password, lock, and server-down all surface as the server message.
                    error = ex.Message;
                    return false;
                }
            }

            // Locked accounts fail before we even compare the hash.
            if (!SqliteInventory.AllowLogin(username, out error))
                return false;

            // Same generic message whether the user is missing or the password is wrong.
            if (!SqliteInventory.TryGetAccount(username, out string hash, out string salt, out string display, out string email, out bool mustChange))
            {
                error = "That username or password is not right.";
                return false;
            }

            // Count the failure toward lockout instead of saying "wrong password" only.
            if (!VerifyPassword(password, hash, salt))
            {
                error = SqliteInventory.NoteLoginFailure(username);
                return false;
            }

            SqliteInventory.ClearLoginFails(username);

            // Successful login is a chance to upgrade leftover PBKDF2 hashes.
            if (!hash.StartsWith("$argon2id$", StringComparison.Ordinal))
                SetPassword(username, password, out _);

            account = new AppAccount
            {
                Username = username,
                DisplayName = display.Length > 0 ? display : username,
                Email = email,
                IsAdmin = IsAdmin(username),
                IsIt = IsIt(username),
                MustChangePassword = mustChange,
                StaySignedIn = SqliteInventory.GetStaySignedIn(username)
            };
            return true;
        }

        /// <summary>IT clears a sign-in lock after confirming the person on the phone.</summary>
        public static bool UnlockLogin(string username, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            // The user grid can fire unlock with no selection.
            if (username.Length == 0)
            {
                error = "Pick a user.";
                return false;
            }

            // Regular users must call IT rather than unlocking themselves.
            if (!AppState.IsIt && !AppState.IsAdmin)
            {
                error = "IT can unlock this account.";
                return false;
            }

            // Clients clear the lock on the host so every PC sees it.
            if (DataLink.IsRemote)
            {
                try
                {
                    DataLink.Call<bool>(
                        ServerOps.AccountsUnlock,
                        new AccountWriteRequest { Username = username });
                    return true;
                }
                // Permission or transport errors must not look like a successful unlock.
                catch (Exception ex)
                {
                    error = ex.Message;
                    return false;
                }
            }

            SqliteInventory.ClearLoginFails(username);
            return true;
        }

        /// <summary>Copy this account into AppState and load table access plus shared settings.</summary>
        public static void Apply(AppAccount account)
        {
            AppState.CurrentUsername = account.Username;
            AppState.CurrentDisplayName = account.DisplayName;
            AppState.IsAdmin = account.IsAdmin;
            AppState.IsIt = account.IsIt;
            AppState.StaySignedIn = account.StaySignedIn;
            // Keep a blank email rather than overwriting a typed Settings value with empty.
            if (!string.IsNullOrWhiteSpace(account.Email))
                AppState.UserEmail = account.Email;
            AppState.CurrentDisplayName = account.DisplayName;
            TableAccess.Apply(account.Username);
            AppLock.LoadSharedSettings();
        }

        /// <summary>Map a server AuthResponse into AppAccount and apply session policy.</summary>
        private static AppAccount FromAuth(AuthResponse auth)
        {
            AppState.StaySignedInEnabled = auth.StaySignedInEnabled;
            // Zero means the server omitted the value; keep the local default.
            if (auth.StaySignedInDays > 0)
                AppState.StaySignedInDays = auth.StaySignedInDays;
            // Zero means the server omitted idle hours; keep the local default.
            if (auth.IdleCloseHours > 0)
                AppState.IdleCloseHours = auth.IdleCloseHours;

            return new AppAccount
            {
                Username = auth.Username,
                DisplayName = auth.DisplayName.Length > 0 ? auth.DisplayName : auth.Username,
                Email = auth.Email,
                IsAdmin = auth.IsAdmin,
                IsIt = auth.IsIt,
                MustChangePassword = auth.MustChangePassword,
                StaySignedIn = auth.StaySignedIn,
                SessionToken = auth.SessionToken
            };
        }

        public static int StaySignedInDays => Math.Max(1, AppState.StaySignedInDays);
        public static TimeSpan IdleCloseAfter =>
            TimeSpan.FromHours(Math.Max(1, AppState.IdleCloseHours));

        /// <summary>
        /// Write a session for this PC when Stay signed in was checked at login.
        /// Length comes from Admin. Skipped when they still must change their password.
        /// </summary>
        public static void RememberSignIn(AppAccount account)
        {
            ClearLocalSession();
            // Admin off, unchecked box, or forced password change must not persist a session.
            if (!AppState.StaySignedInEnabled || !account.StaySignedIn || account.MustChangePassword)
                return;

            // Clients store the server-issued token; they cannot mint one locally.
            if (DataLink.IsRemote)
            {
                // Login without a token cannot be resumed later on this PC.
                if (string.IsNullOrWhiteSpace(account.SessionToken))
                    return;
                WriteLocalSession(
                    account.Username,
                    account.SessionToken,
                    DateTime.Now.AddDays(StaySignedInDays));
                return;
            }

            byte[] bytes = RandomNumberGenerator.GetBytes(32);
            string token = Convert.ToBase64String(bytes);
            DateTime expires = DateTime.Now.AddDays(StaySignedInDays);
            SqliteInventory.InsertSession(account.Username, HashSessionToken(token), expires);
            WriteLocalSession(account.Username, token, expires);
        }

        /// <summary>Restore a stay-signed-in session from this PC’s local token, if it is still valid.</summary>
        public static bool TryRestoreSession()
        {
            // No data folder or Admin turned Stay signed in off.
            if (!AppLock.HasFolder() || !AppState.StaySignedInEnabled)
                return false;

            var local = ReadLocalSession();
            // First launch on this PC, or they already signed out.
            if (local == null)
                return false;

            // Expired tokens must not skip the login screen.
            if (local.Value.Expires <= DateTime.Now)
            {
                ClearLocalSession();
                return false;
            }

            // Server decides whether the token is still valid.
            if (DataLink.IsRemote)
            {
                try
                {
                    var auth = DataLink.Call<AuthResponse>(
                        ServerOps.AuthResume,
                        new ResumeRequest { Token = local.Value.Token });
                    Apply(FromAuth(auth));
                    AppState.StaySignedIn = true;
                    return true;
                }
                // Bad or revoked token: drop it so the login screen appears.
                catch
                {
                    // Bad or revoked token: drop it so the login screen appears.
                    ClearLocalSession();
                    return false;
                }
            }

            string? username = SqliteInventory.FindSessionUsername(HashSessionToken(local.Value.Token));
            // Token was deleted on another PC or does not match the stored username.
            if (string.IsNullOrWhiteSpace(username) ||
                !username.Equals(local.Value.Username, StringComparison.OrdinalIgnoreCase))
            {
                ClearLocalSession();
                return false;
            }

            // Locked or must-change accounts must sign in interactively.
            if (!SqliteInventory.AllowLogin(username, out _) ||
                !SqliteInventory.TryGetAccount(username, out _, out _, out string display, out string email, out bool mustChange) ||
                mustChange)
            {
                ClearLocalSession();
                return false;
            }

            Apply(new AppAccount
            {
                Username = username,
                DisplayName = display.Length > 0 ? display : username,
                Email = email,
                IsAdmin = IsAdmin(username),
                IsIt = IsIt(username),
                StaySignedIn = true
            });
            return true;
        }

        /// <summary>Sign out on this PC, drop Stay signed in, and reopen at the login screen.</summary>
        public static void LogOutAndRestart()
        {
            IdleWatch.Stop();
            BankLiveWatch.Stop();
            ForgetThisPc();
            AppState.SignOut();
            Application.Restart();
        }

        /// <summary>Remove this PC’s stay-signed-in token. Other computers are unchanged.</summary>
        public static void ForgetThisPc()
        {
            var local = ReadLocalSession();
            // Drop the hashed token on the host so this PC cannot resume.
            if (local != null)
                SqliteInventory.DeleteSession(HashSessionToken(local.Value.Token));
            ClearLocalSession();
        }

        /// <summary>Delete this PC’s protected session file and any leftover legacy JSON.</summary>
        public static void ClearLocalSession()
        {
            TryDelete(LocalSessionPath());
            TryDelete(LegacySessionPath());
        }

        /// <summary>Per-user Stay signed in flag; turning it off also forgets this PC.</summary>
        public static void SetStaySignedIn(string username, bool enabled)
        {
            SqliteInventory.SetStaySignedIn(username, enabled);
            // Only the current session is cleared; other PCs keep their tokens until expiry.
            if (!enabled &&
                username.Equals(AppState.CurrentUsername, StringComparison.OrdinalIgnoreCase))
            {
                AppState.StaySignedIn = false;
                ClearLocalSession();
            }
        }

        /// <summary>SHA-256 of the raw token so the database never stores the resume secret.</summary>
        private static string HashSessionToken(string token)
        {
            byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""));
            return Convert.ToHexString(bytes);
        }

        private static readonly byte[] SessionEntropy = Encoding.UTF8.GetBytes("CastRightCatch.session.v1");

        /// <summary>DPAPI session file under this Windows user’s LocalAppData.</summary>
        private static string LocalSessionPath()
        {
            return Path.Combine(LocalDataFolder(), "session.dat");
        }

        /// <summary>Per-user CastRightCatch folder for session.dat.</summary>
        private static string LocalDataFolder()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CastRightCatch");
            Directory.CreateDirectory(folder);
            return folder;
        }

        /// <summary>Old plaintext session JSON next to the exe; migrated then deleted.</summary>
        private static string LegacySessionPath()
        {
            return Path.Combine(
                AppDomain.CurrentDomain.BaseDirectory,
                $"session_{SanitizeFilePart(Environment.UserName)}.json");
        }

        /// <summary>Windows user name safe for a file, or "user" when blank.</summary>
        private static string SanitizeFilePart(string userName)
        {
            foreach (var c in Path.GetInvalidFileNameChars())
                userName = userName.Replace(c, '_');
            // Empty Windows names would produce a trailing underscore file.
            return string.IsNullOrWhiteSpace(userName) ? "user" : userName;
        }

        /// <summary>Protect the token for this Windows user and remove the legacy JSON copy.</summary>
        private static void WriteLocalSession(string username, string token, DateTime expires)
        {
            var payload = new LocalSession
            {
                Username = username,
                Token = token,
                Expires = expires.ToString("o")
            };
            byte[] json = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload, JsonOptions));
            byte[] protectedBytes = ProtectedData.Protect(json, SessionEntropy, DataProtectionScope.CurrentUser);
            File.WriteAllBytes(LocalSessionPath(), protectedBytes);
            TryDelete(LegacySessionPath());
        }

        /// <summary>Read the DPAPI session, or migrate a leftover legacy JSON file once.</summary>
        private static (string Username, string Token, DateTime Expires)? ReadLocalSession()
        {
            var modern = ReadProtectedSession(LocalSessionPath());
            // Prefer the protected file; it is the current format.
            if (modern != null)
                return modern;

            var legacy = ReadLegacySession();
            // No session on this PC.
            if (legacy == null)
                return null;

            WriteLocalSession(legacy.Value.Username, legacy.Value.Token, legacy.Value.Expires);
            return legacy;
        }

        /// <summary>Unprotect session.dat for this Windows user, or delete it if it is corrupt.</summary>
        private static (string Username, string Token, DateTime Expires)? ReadProtectedSession(string path)
        {
            // First launch has no session file yet.
            if (!File.Exists(path))
                return null;
            try
            {
                byte[] protectedBytes = File.ReadAllBytes(path);
                byte[] json = ProtectedData.Unprotect(protectedBytes, SessionEntropy, DataProtectionScope.CurrentUser);
                return ParseSession(Encoding.UTF8.GetString(json));
            }
            // Another Windows user or a truncated file cannot be decrypted.
            catch
            {
                // Another Windows user or a truncated file cannot be decrypted.
                TryDelete(path);
                return null;
            }
        }

        /// <summary>Read then delete the old plaintext session next to the exe.</summary>
        private static (string Username, string Token, DateTime Expires)? ReadLegacySession()
        {
            string path = LegacySessionPath();
            // No leftover plaintext session on this PC.
            if (!File.Exists(path))
                return null;
            try
            {
                return ParseSession(File.ReadAllText(path));
            }
            // Corrupt legacy JSON is discarded rather than blocking sign-in.
            catch
            {
                // Corrupt legacy JSON is discarded rather than blocking sign-in.
                return null;
            }
            finally
            {
                // Always remove plaintext so the token is not left on disk.
                TryDelete(path);
            }
        }

        /// <summary>Parse username, token, and expiry; reject incomplete payloads.</summary>
        private static (string Username, string Token, DateTime Expires)? ParseSession(string json)
        {
            var payload = JsonSerializer.Deserialize<LocalSession>(json, JsonOptions);
            // Missing fields would resume as a blank user.
            if (payload == null ||
                string.IsNullOrWhiteSpace(payload.Username) ||
                string.IsNullOrWhiteSpace(payload.Token) ||
                !DateTime.TryParse(payload.Expires, out var expires))
                return null;
            return (payload.Username, payload.Token, expires);
        }

        /// <summary>Best-effort delete; a locked file must not fail sign-out.</summary>
        private static void TryDelete(string path)
        {
            try
            {
                // Skip delete when the file was already removed.
                if (File.Exists(path))
                    File.Delete(path);
            }
            // Keep going even if the local file is locked.
            catch
            {
                // keep going even if the local file is locked
            }
        }

        /// <summary>JSON shape of the local stay-signed-in token.</summary>
        private sealed class LocalSession
        {
            public string? Username { get; set; }
            public string? Token { get; set; }
            public string? Expires { get; set; }
        }

        /// <summary>Load admin and IT names from the server or admins.json.</summary>
        private static AdminsFile ReadFile()
        {
            // Clients never open the shared JSON; roles come over the encrypted stream.
            if (DataLink.IsRemote)
            {
                try
                {
                    var roles = DataLink.Call<RolesDto>(ServerOps.RolesRead);
                    return new AdminsFile
                    {
                        Admins = Clean(roles.Admins),
                        It = Clean(roles.It)
                    };
                }
                // Offline/permission errors should not crash pages that list roles.
                catch
                {
                    // Offline/permission errors should not crash pages that list roles.
                    return new AdminsFile();
                }
            }

            string? path = GetFilePath();
            // First run or no folder yet: empty lists until EnsureFile writes one.
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
            // Corrupt admins.json is treated as empty rather than blocking the app.
            catch
            {
                // Corrupt admins.json is treated as empty rather than blocking the app.
                return new AdminsFile();
            }
        }

        /// <summary>Append a username to admin or IT if it is not already listed.</summary>
        private static void AddName(bool admin, string username)
        {
            username = (username ?? "").Trim();
            // Blank names would pollute admins.json.
            if (username.Length == 0)
                return;

            var file = ReadFile();
            var list = admin ? file.Admins : file.It;
            // Roles are case-insensitive; skip duplicates from repeated Add clicks.
            if (!list.Any(name => name.Equals(username, StringComparison.OrdinalIgnoreCase)))
                list.Add(username);
            WriteFile(file.Admins, file.It);
            SqliteInventory.SetAccountRoles(username, Contains(file.Admins, username), Contains(file.It, username));
        }

        /// <summary>Remove a role unless this person is the last remaining admin or IT user.</summary>
        private static bool RemoveName(bool admin, string username, out string error)
        {
            error = "";
            username = (username ?? "").Trim();
            var file = ReadFile();
            var list = admin ? file.Admins : file.It;
            // Never leave the company with nobody who can manage users or Admin settings.
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

        /// <summary>Case-insensitive membership; blank names never match.</summary>
        private static bool Contains(IEnumerable<string> names, string? username)
        {
            username = (username ?? "").Trim();
            // Empty lookups would match trimmed blanks in a dirty file.
            if (username.Length == 0)
                return false;

            return names.Any(name => name.Equals(username, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>Trim, drop blanks, and keep the first spelling of each name.</summary>
        private static List<string> Clean(List<string>? names)
        {
            return (names ?? new List<string>())
                .Select(name => (name ?? "").Trim())
                .Where(name => name.Length > 0)
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToList();
        }

        /// <summary>Keep admin and IT lists pointing at the renamed login.</summary>
        private static void RenameInFile(string oldUsername, string newUsername)
        {
            var file = ReadFile();
            ReplaceName(file.Admins, oldUsername, newUsername);
            ReplaceName(file.It, oldUsername, newUsername);
            WriteFile(file.Admins, file.It);
        }

        /// <summary>Replace a matching name in place so list order stays the same.</summary>
        private static void ReplaceName(List<string> names, string oldUsername, string newUsername)
        {
            for (int i = 0; i < names.Count; i++)
            {
                // Match ignoring case so CRC and crc both rename.
                if (names[i].Equals(oldUsername, StringComparison.OrdinalIgnoreCase))
                    names[i] = newUsername;
            }
        }

        /// <summary>Write roles to the server, or atomically replace admins.json locally.</summary>
        private static void WriteFile(List<string> admins, List<string> it)
        {
            var file = new AdminsFile { Admins = Clean(admins), It = Clean(it) };
            // Clients persist roles on the host so every PC shares them.
            if (DataLink.IsRemote)
            {
                DataLink.Call<bool>(ServerOps.RolesWrite, new RolesDto
                {
                    Admins = file.Admins,
                    It = file.It
                });
                return;
            }

            string? path = GetFilePath();
            // No folder yet — skip rather than writing next to the exe.
            if (path == null)
                return;

            string json = JsonSerializer.Serialize(file, JsonOptions);
            string temp = path + ".tmp";
            File.WriteAllText(temp, json);
            File.Copy(temp, path, overwrite: true);
            File.Delete(temp);
        }

        private const int ArgonMemoryKb = 19456;
        private const int ArgonIterations = 2;
        private const int ArgonParallelism = 1;
        private const int ArgonHashLength = 32;

        /// <summary>Shared length and character rules from PasswordRules.</summary>
        private static bool PasswordMeetsPolicy(string password, out string error) =>
            PasswordRules.Meets(password, out error);

        /// <summary>Argon2id encoded hash plus a marker salt for VerifyPassword.</summary>
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

        /// <summary>Verify Argon2id, or legacy PBKDF2, without throwing on bad stored data.</summary>
        private static bool VerifyPassword(string password, string hash, string salt)
        {
            try
            {
                // Current hashes are PHC-encoded Argon2id.
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
            // Corrupt base64 or truncated hashes are treated as a wrong password.
            catch
            {
                // Corrupt base64 or truncated hashes are treated as a wrong password.
                return false;
            }
        }

        /// <summary>Parse PHC Argon2id parameters and compare in constant time.</summary>
        private static bool VerifyArgon2(string password, string encoded)
        {
            string[] parts = encoded.Split('$');
            // Unexpected encoding is not a valid stored password.
            if (parts.Length != 6)
                return false;

            int memory = ArgonMemoryKb;
            int iterations = ArgonIterations;
            int parallelism = ArgonParallelism;
            foreach (var piece in parts[3].Split(','))
            {
                // Use the parameters stored with the hash so older hashes still verify.
                if (piece.StartsWith("m=", StringComparison.Ordinal) &&
                    int.TryParse(piece[2..], out int m))
                    memory = m;
                // Time cost (iterations) stored with older hashes.
                else if (piece.StartsWith("t=", StringComparison.Ordinal) &&
                         int.TryParse(piece[2..], out int t))
                    iterations = t;
                // Parallelism stored with older hashes.
                else if (piece.StartsWith("p=", StringComparison.Ordinal) &&
                         int.TryParse(piece[2..], out int p))
                    parallelism = p;
            }

            byte[] saltBytes = Convert.FromBase64String(parts[4]);
            byte[] expected = Convert.FromBase64String(parts[5]);
            byte[] actual = Argon2Hash(password, saltBytes, memory, iterations, parallelism);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }

        /// <summary>Argon2id digest using the given memory, time, and parallelism.</summary>
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

        /// <summary>admins.json shape: administrator and IT username lists.</summary>
        private sealed class AdminsFile
        {
            [JsonPropertyName("admins")]
            public List<string> Admins { get; set; } = new();

            [JsonPropertyName("it")]
            public List<string> It { get; set; } = new();
        }
    }
}
