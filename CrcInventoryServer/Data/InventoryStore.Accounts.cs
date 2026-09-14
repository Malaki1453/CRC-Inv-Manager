using CrcInventory.Protocol;
using System.Data.Common;

namespace CrcInventory.Server;

internal sealed partial class InventoryStore
{
    public int CountAccounts()
    {
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COUNT(*) FROM app_accounts;";
            return Convert.ToInt32(cmd.Scalar(_engine));
        }
    }

    public bool TryGetAccountRecord(string username, out AccountRecord record)
    {
        record = default;
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return false;

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT username, display_name, email, password_hash, password_salt,
                       COALESCE(must_change_password, 0), COALESCE(is_admin, 0),
                       COALESCE(is_it, 0), COALESCE(stay_signed_in, 0),
                       COALESCE(table_access, '')
                FROM app_accounts WHERE username = $user;
                """;
            cmd.AddParam("$user", username);
            using var reader = cmd.Query(_engine);
            if (!reader.Read())
                return false;

            record = new AccountRecord
            {
                Username = reader.GetString(0),
                DisplayName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
                PasswordHash = reader.IsDBNull(3) ? "" : reader.GetString(3),
                PasswordSalt = reader.IsDBNull(4) ? "" : reader.GetString(4),
                MustChangePassword = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                IsAdmin = !reader.IsDBNull(6) && reader.GetInt32(6) != 0,
                IsIt = !reader.IsDBNull(7) && reader.GetInt32(7) != 0,
                StaySignedIn = !reader.IsDBNull(8) && reader.GetInt32(8) != 0,
                TableAccess = reader.IsDBNull(9) ? "" : reader.GetString(9)
            };
            return true;
        }
    }

    public AccountGetDto? GetAccount(string username)
    {
        if (!TryGetAccountRecord(username, out var record))
            return new AccountGetDto { Found = false };

        return ToDto(record);
    }

    public List<AccountListDto> ListAccounts()
    {
        lock (_gate)
        {
            var list = new List<AccountListDto>();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT username, display_name, email,
                       COALESCE(is_admin, 0), COALESCE(is_it, 0),
                       COALESCE(stay_signed_in, 0),
                       COALESCE(login_lock_until, '')
                FROM app_accounts
                ORDER BY username COLLATE NOCASE;
                """;
            using var reader = cmd.Query(_engine);
            while (reader.Read())
            {
                string username = reader.IsDBNull(0) ? "" : reader.GetString(0);
                list.Add(new AccountListDto
                {
                    Username = username,
                    DisplayName = reader.IsDBNull(1) ? "" : reader.GetString(1),
                    Email = reader.IsDBNull(2) ? "" : reader.GetString(2),
                    IsAdmin = Roles.IsAdmin(username) || (!reader.IsDBNull(3) && reader.GetInt32(3) != 0),
                    IsIt = Roles.IsIt(username) || (!reader.IsDBNull(4) && reader.GetInt32(4) != 0),
                    StaySignedIn = !reader.IsDBNull(5) && reader.GetInt32(5) != 0,
                    LoginLocked = RecoveryGuard.IsItLock(reader.IsDBNull(6) ? "" : reader.GetValue(6)?.ToString())
                });
            }

            return list;
        }
    }

    public bool InsertAccount(
        string username,
        string displayName,
        string password,
        string email,
        bool isAdmin,
        bool isIt,
        bool mustChange)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return false;
        if (!Passwords.MeetsPolicy(password, out _))
            return false;

        Passwords.Hash(password, out string hash, out string salt);
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO app_accounts
                    (username, display_name, password_hash, password_salt, email, created_at,
                     is_admin, is_it, must_change_password)
                VALUES ($user, $name, $hash, $salt, $email, $at, $admin, $it, $must);
                """;
            cmd.AddParam("$user", username);
            cmd.AddParam("$name", displayName ?? "");
            cmd.AddParam("$hash", hash);
            cmd.AddParam("$salt", salt);
            cmd.AddParam("$email", email ?? "");
            cmd.AddParam("$at", NowStamp());
            cmd.AddParam("$admin", isAdmin ? 1 : 0);
            cmd.AddParam("$it", isIt ? 1 : 0);
            cmd.AddParam("$must", mustChange ? 1 : 0);
            try
            {
                if (cmd.Exec(_engine) <= 0)
                    return false;
            }
            catch (DbException)
            {
                return false;
            }
        }

        Roles.Ensure(username, isAdmin, isIt);
        return true;
    }

    public bool UpdateAccount(string username, string displayName, string email)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return false;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE app_accounts
                SET display_name = $name, email = $email
                WHERE username = $user;
                """;
            cmd.AddParam("$name", displayName ?? "");
            cmd.AddParam("$email", email ?? "");
            cmd.AddParam("$user", username);
            return cmd.Exec(_engine) > 0;
        }
    }

    public bool UpdateAccountPassword(string username, string password)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0 || !Passwords.MeetsPolicy(password, out _))
            return false;

        Passwords.Hash(password, out string hash, out string salt);
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE app_accounts
                SET password_hash = $hash, password_salt = $salt
                WHERE username = $user;
                """;
            cmd.AddParam("$hash", hash);
            cmd.AddParam("$salt", salt);
            cmd.AddParam("$user", username);
            bool updated = cmd.Exec(_engine) > 0;
            if (updated)
            {
                DeleteSessionsForUserUnlocked(username);
                ClearRecoveryFailsUnlocked(username);
                ClearLoginFailsUnlocked(username);
            }
            return updated;
        }
    }

    public void SetMustChangePassword(string username, bool mustChange)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET must_change_password = $flag WHERE username = $user;";
            cmd.AddParam("$flag", mustChange ? 1 : 0);
            cmd.AddParam("$user", username);
            cmd.Exec(_engine);
        }
    }

    public bool RenameAccount(string oldUsername, string newUsername)
    {
        oldUsername = (oldUsername ?? "").Trim();
        newUsername = (newUsername ?? "").Trim();
        if (oldUsername.Length == 0 || newUsername.Length == 0)
            return false;
        if (oldUsername.Equals(newUsername, StringComparison.OrdinalIgnoreCase))
            return true;

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET username = $new WHERE username = $old;";
            cmd.AddParam("$new", newUsername);
            cmd.AddParam("$old", oldUsername);
            try
            {
                if (cmd.Exec(_engine) <= 0)
                    return false;
                RenameSessionsUnlocked(oldUsername, newUsername);
            }
            catch (DbException)
            {
                return false;
            }
        }

        Roles.Rename(oldUsername, newUsername);
        return true;
    }

    public void UpdateAccountEmail(string username, string email)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET email = $email WHERE username = $user;";
            cmd.AddParam("$email", email ?? "");
            cmd.AddParam("$user", username);
            cmd.Exec(_engine);
        }
    }

    public bool DeleteAccount(string username)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return false;

        lock (_gate)
        {
            DeleteSessionsForUserUnlocked(username);
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_accounts WHERE username = $user;";
            cmd.AddParam("$user", username);
            if (cmd.Exec(_engine) <= 0)
                return false;
        }

        Roles.Remove(username);
        return true;
    }

    public bool GetStaySignedIn(string username)
    {
        if (!TryGetAccountRecord(username, out var record))
            return false;
        return record.StaySignedIn;
    }

    public void SetStaySignedIn(string username, bool enabled)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET stay_signed_in = $flag WHERE username = $user;";
            cmd.AddParam("$flag", enabled ? 1 : 0);
            cmd.AddParam("$user", username);
            cmd.Exec(_engine);
            if (!enabled)
                DeleteSessionsForUserUnlocked(username);
        }
    }

    public void SetAccountRoles(string username, bool isAdmin, bool isIt)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "UPDATE app_accounts SET is_admin = $admin, is_it = $it WHERE username = $user;";
            cmd.AddParam("$admin", isAdmin ? 1 : 0);
            cmd.AddParam("$it", isIt ? 1 : 0);
            cmd.AddParam("$user", username);
            cmd.Exec(_engine);
        }

        Roles.Ensure(username, isAdmin, isIt);
    }

    public string GetStoredTableAccess(string username)
    {
        if (!TryGetAccountRecord(username, out var record))
            return "";
        return record.TableAccess;
    }

    public string GetTableAccess(string username)
    {
        if (!TryGetAccountRecord(username, out var record))
            return "";
        var groups = SplitGroups(GetAccessGroup(username));
        string baseline = groups.Count == 0
            ? ""
            : groups.Count == 1
                ? GetGroupAccess(groups[0])
                : AccessFilter.Merge(groups.Select(GetGroupAccess));
        return AccessFilter.Overlay(baseline, record.TableAccess);
    }

    public string GetAccessGroup(string username)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return "";
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(access_group, '') FROM app_accounts WHERE username = $user;";
            cmd.AddParam("$user", username);
            return (cmd.Scalar(_engine)?.ToString() ?? "").Trim();
        }
    }

    private static List<string> SplitGroups(string stored)
    {
        if (string.IsNullOrWhiteSpace(stored))
            return new List<string>();
        return stored
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    public string GetGroupAccess(string name)
    {
        name = (name ?? "").Trim();
        if (name.Length == 0)
            return "";
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(table_access, '') FROM access_groups WHERE name = $name;";
            cmd.AddParam("$name", name);
            return cmd.Scalar(_engine)?.ToString() ?? "";
        }
    }

    public void SetTableAccess(string username, string json)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET table_access = $json WHERE username = $user;";
            cmd.AddParam("$json", json ?? "");
            cmd.AddParam("$user", username);
            cmd.Exec(_engine);
        }
    }

    private void ClearRecoveryFailsUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE app_accounts SET recover_fails = 0, recover_lock_until = '' WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    public bool AllowLogin(string username, out string error)
    {
        error = "";
        username = (username ?? "").Trim();
        if (username.Length == 0)
        {
            error = "Enter a username and password.";
            return false;
        }

        lock (_gate)
        {
            string untilText = "";
            using (var db = Open())
            using (var cmd = db.CreateCommand())
            {
                cmd.CommandText =
                    "SELECT COALESCE(login_lock_until, '') FROM app_accounts WHERE username = $user;";
                cmd.AddParam("$user", username);
                untilText = cmd.Scalar(_engine)?.ToString() ?? "";
            }

            if (RecoveryGuard.IsItLock(untilText))
            {
                error = RecoveryGuard.ItLockMessage;
                return false;
            }

            if (DateTime.TryParse(untilText, out var until) && until > DateTime.Now)
            {
                error = RecoveryGuard.LockedMessage(until);
                return false;
            }

            if (untilText.Length > 0)
                ClearLoginTimeLockUnlocked(username);
            return true;
        }
    }

    public string NoteLoginFailure(string username)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return "That username or password is not right.";

        lock (_gate)
        {
            using var db = Open();
            using var read = db.CreateCommand();
            read.CommandText =
                "SELECT COALESCE(login_fails, 0) FROM app_accounts WHERE username = $user;";
            read.AddParam("$user", username);
            object? raw = read.Scalar(_engine);
            if (raw == null)
                return "That username or password is not right.";

            int fails = Convert.ToInt32(raw) + 1;
            var penalty = RecoveryGuard.NextLoginPenalty(fails);

            using var write = db.CreateCommand();
            write.CommandText =
                """
                UPDATE app_accounts
                SET login_fails = $fails, login_lock_until = $until
                WHERE username = $user;
                """;
            write.AddParam("$fails", fails);
            write.AddParam("$until", penalty.Until);
            write.AddParam("$user", username);
            write.Exec(_engine);

            return penalty.Message;
        }
    }

    public void ClearLoginFails(string username)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;
        lock (_gate)
            ClearLoginFailsUnlocked(username);
    }

    private void ClearLoginFailsUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE app_accounts SET login_fails = 0, login_lock_until = '' WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    private void ClearLoginTimeLockUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE app_accounts SET login_lock_until = '' WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    public string InsertSession(string username, DateTime expiresAt)
    {
        string token = Passwords.NewSessionToken();
        string tokenHash = Passwords.HashSessionToken(token);
        lock (_gate)
        {
            DeleteExpiredSessionsUnlocked();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                INSERT INTO app_sessions (token_hash, username, expires_at, created_at)
                VALUES ($hash, $user, $exp, $at);
                """;
            cmd.AddParam("$hash", tokenHash);
            cmd.AddParam("$user", username);
            cmd.AddParam("$exp", expiresAt.ToString("o"));
            cmd.AddParam("$at", NowStamp());
            cmd.Exec(_engine);
        }

        return token;
    }

    public string? FindSessionUsername(string token)
    {
        string tokenHash = Passwords.HashSessionToken(token);
        lock (_gate)
        {
            DeleteExpiredSessionsUnlocked();
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                SELECT username FROM app_sessions
                WHERE token_hash = $hash AND expires_at >= $now
                LIMIT 1;
                """;
            cmd.AddParam("$hash", tokenHash);
            cmd.AddParam("$now", DateTime.Now.ToString("o"));
            return cmd.Scalar(_engine)?.ToString();
        }
    }

    public void DeleteSession(string token)
    {
        string tokenHash = Passwords.HashSessionToken(token);
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_sessions WHERE token_hash = $hash;";
            cmd.AddParam("$hash", tokenHash);
            cmd.Exec(_engine);
        }
    }

    public void DeleteSessionsForUser(string username)
    {
        lock (_gate)
            DeleteSessionsForUserUnlocked(username);
    }

    public AuthResponse ToAuth(AccountRecord record, string? sessionToken = null) => new()
    {
        Username = record.Username,
        DisplayName = record.DisplayName.Length > 0 ? record.DisplayName : record.Username,
        Email = record.Email,
        IsAdmin = Roles.IsAdmin(record.Username) || record.IsAdmin,
        IsIt = Roles.IsIt(record.Username) || record.IsIt,
        MustChangePassword = record.MustChangePassword,
        StaySignedIn = record.StaySignedIn,
        TableAccess = record.TableAccess,
        StaySignedInEnabled = StaySignedInEnabled(),
        StaySignedInDays = StaySignedInDays(),
        IdleCloseHours = IdleCloseHours(),
        SessionToken = sessionToken
    };

    private AccountGetDto ToDto(AccountRecord record) => new()
    {
        Found = true,
        Username = record.Username,
        DisplayName = record.DisplayName,
        Email = record.Email,
        IsAdmin = Roles.IsAdmin(record.Username) || record.IsAdmin,
        IsIt = Roles.IsIt(record.Username) || record.IsIt,
        MustChangePassword = record.MustChangePassword,
        StaySignedIn = record.StaySignedIn,
        TableAccess = record.TableAccess
    };

    private void DeleteSessionsForUserUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM app_sessions WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    private void RenameSessionsUnlocked(string oldUsername, string newUsername)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE app_sessions SET username = $new WHERE username = $old;";
        cmd.AddParam("$new", newUsername);
        cmd.AddParam("$old", oldUsername);
        cmd.Exec(_engine);
    }

    private void DeleteExpiredSessionsUnlocked()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM app_sessions WHERE expires_at < $now;";
        cmd.AddParam("$now", DateTime.Now.ToString("o"));
        cmd.Exec(_engine);
    }
}

internal struct AccountRecord
{
    public string Username;
    public string DisplayName;
    public string Email;
    public string PasswordHash;
    public string PasswordSalt;
    public bool MustChangePassword;
    public bool IsAdmin;
    public bool IsIt;
    public bool StaySignedIn;
    public string TableAccess;
}
