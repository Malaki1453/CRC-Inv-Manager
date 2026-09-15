using CrcInventory.Protocol;
using System.Data.Common;

namespace CrcInventory.Server;

internal sealed partial class InventoryStore
{
    /// <summary>Number of rows in app_accounts.</summary>
    public int CountAccounts()
    {
        lock (_gate)
        {
            using var db = Open(); // live database connection
            using var cmd = db.CreateCommand(); // SQL command for the account count
            cmd.CommandText = "SELECT COUNT(*) FROM app_accounts;";
            return Convert.ToInt32(cmd.Scalar(_engine));
        }
    }

    /// <summary>Loads one account by username, including hash and role flags; false when missing.</summary>
    public bool TryGetAccountRecord(string username, out AccountRecord record)
    {
        record = default;
        username = (username ?? "").Trim();
        // Blank names are not stored and must not hit the table.
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
            // Unknown username is a failed lookup, not an exception.
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

    /// <summary>Account editor DTO; Found is false when the username does not exist.</summary>
    public AccountGetDto? GetAccount(string username)
    {
        // Missing users still return a DTO so the client can show "not found".
        if (!TryGetAccountRecord(username, out var record))
            return new AccountGetDto { Found = false };

        return ToDto(record);
    }

    /// <summary>All accounts for the IT user list, including IT-lock state.</summary>
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

    /// <summary>Creates an account and updates admins.json; false on blank name, weak password, or duplicate.</summary>
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
        // Accounts need a sign-in name.
        if (username.Length == 0)
            return false;
        // Reject passwords that fail the shared policy before hashing.
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
            // Unique-constraint failures are expected on duplicate usernames.
            try
            {
                // Zero rows means the insert did not land.
                if (cmd.Exec(_engine) <= 0)
                    return false;
            }
            // Duplicate username (or similar constraint) is reported as failure, not a crash.
            catch (DbException)
            {
                return false;
            }
        }

        Roles.Ensure(username, isAdmin, isIt);
        return true;
    }

    /// <summary>Updates display name and email; false when the username is blank or missing.</summary>
    public bool UpdateAccount(string username, string displayName, string email)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
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

    /// <summary>Rehashes the password, drops sessions, and clears lockouts; false on blank name or weak password.</summary>
    public bool UpdateAccountPassword(string username, string password)
    {
        username = (username ?? "").Trim();
        // Blank names or policy failures must not rewrite the hash.
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
            // A new password invalidates stay-signed-in tokens and clears brute-force counters.
            if (updated)
            {
                DeleteSessionsForUserUnlocked(username);
                ClearRecoveryFailsUnlocked(username);
                ClearLoginFailsUnlocked(username);
            }
            return updated;
        }
    }

    /// <summary>Sets or clears must-change-password; no-ops on a blank name.</summary>
    public void SetMustChangePassword(string username, bool mustChange)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
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

    /// <summary>Renames an account and its sessions/roles; false on blank names, missing row, or duplicate new name.</summary>
    public bool RenameAccount(string oldUsername, string newUsername)
    {
        oldUsername = (oldUsername ?? "").Trim();
        newUsername = (newUsername ?? "").Trim();
        // Both names are required for a rename.
        if (oldUsername.Length == 0 || newUsername.Length == 0)
            return false;
        // Same name (ignoring case) is already the desired state.
        if (oldUsername.Equals(newUsername, StringComparison.OrdinalIgnoreCase))
            return true;

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "UPDATE app_accounts SET username = $new WHERE username = $old;";
            cmd.AddParam("$new", newUsername);
            cmd.AddParam("$old", oldUsername);
            // Unique-constraint on the new name is a failed rename, not a crash.
            try
            {
                // Zero rows means the old username was not found.
                if (cmd.Exec(_engine) <= 0)
                    return false;
                RenameSessionsUnlocked(oldUsername, newUsername);
            }
            // Unique-constraint on the new name is reported as failure, not a crash.
            catch (DbException)
            {
                return false;
            }
        }

        Roles.Rename(oldUsername, newUsername);
        return true;
    }

    /// <summary>Updates only the email column; no-ops on a blank name.</summary>
    public void UpdateAccountEmail(string username, string email)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
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

    /// <summary>Deletes the account, its sessions, and role membership; false when missing.</summary>
    public bool DeleteAccount(string username)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
        if (username.Length == 0)
            return false;

        lock (_gate)
        {
            DeleteSessionsForUserUnlocked(username);
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "DELETE FROM app_accounts WHERE username = $user;";
            cmd.AddParam("$user", username);
            // Zero rows means the user was already gone.
            if (cmd.Exec(_engine) <= 0)
                return false;
        }

        Roles.Remove(username);
        return true;
    }

    /// <summary>Stay-signed-in flag for the account, or false when the user is missing.</summary>
    public bool GetStaySignedIn(string username)
    {
        // Missing users do not have the preference.
        if (!TryGetAccountRecord(username, out var record))
            return false;
        return record.StaySignedIn;
    }

    /// <summary>Sets stay-signed-in; turning it off also drops that user's session tokens.</summary>
    public void SetStaySignedIn(string username, bool enabled)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
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
            // Disabled stay-signed-in must not leave resumable tokens around.
            if (!enabled)
                DeleteSessionsForUserUnlocked(username);
        }
    }

    /// <summary>Writes admin/IT flags on the account and mirrors them into admins.json.</summary>
    public void SetAccountRoles(string username, bool isAdmin, bool isIt)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
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

    /// <summary>Raw table_access overlay JSON stored on the account, or empty when missing.</summary>
    public string GetStoredTableAccess(string username)
    {
        // Missing users have no overlay.
        if (!TryGetAccountRecord(username, out var record))
            return "";
        return record.TableAccess;
    }

    /// <summary>Effective table_access: merged group baseline overlaid with the user's JSON.</summary>
    public string GetTableAccess(string username)
    {
        // Missing users have no policy.
        if (!TryGetAccountRecord(username, out var record))
            return "";
        var groups = SplitGroups(GetAccessGroup(username));
        // No groups (or empty group JSON) is an empty baseline: all tables allowed until overlay denies.
        string baseline = groups.Count == 0
            ? ""
            : groups.Count == 1
                ? GetGroupAccess(groups[0])
                : AccessFilter.Merge(groups.Select(GetGroupAccess));
        return AccessFilter.Overlay(baseline, record.TableAccess);
    }

    /// <summary>Comma/semicolon-separated access-group names stored on the account.</summary>
    public string GetAccessGroup(string username)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
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

    /// <summary>Splits a stored access_group string on commas and semicolons.</summary>
    private static List<string> SplitGroups(string stored)
    {
        // Empty storage means no groups.
        if (string.IsNullOrWhiteSpace(stored))
            return new List<string>();
        return stored
            .Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(name => name.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>table_access JSON for one named access group, or empty when missing.</summary>
    public string GetGroupAccess(string name)
    {
        name = (name ?? "").Trim();
        // Blank group names are not stored.
        if (name.Length == 0)
            return "";
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText = "SELECT COALESCE(table_access, '') FROM access_groups WHERE name = $name;";
            cmd.AddParam("$name", name);
            // Missing group or empty JSON: no denials, so all tables are allowed.
            return cmd.Scalar(_engine)?.ToString() ?? "";
        }
    }

    /// <summary>Writes the user's table_access overlay JSON; no-ops on a blank name.</summary>
    public void SetTableAccess(string username, string json)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
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

    /// <summary>Clears recovery-question failure counters (caller already holds <c>_gate</c>).</summary>
    private void ClearRecoveryFailsUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE app_accounts SET recover_fails = 0, recover_lock_until = '' WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    /// <summary>False when the account is IT-locked or still inside a timed lock; sets <paramref name="error"/>.</summary>
    public bool AllowLogin(string username, out string error)
    {
        error = "";
        username = (username ?? "").Trim();
        // Sign-in requires a username.
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

            // IT lock is not time-based; only IT can clear it.
            if (RecoveryGuard.IsItLock(untilText))
            {
                error = RecoveryGuard.ItLockMessage;
                return false;
            }

            // A future timestamp means the 15-minute window is still active.
            if (DateTime.TryParse(untilText, out var until) && until > DateTime.Now)
            {
                error = RecoveryGuard.LockedMessage(until);
                return false;
            }

            // An expired timestamp should be cleared so the next failure starts a fresh window.
            if (untilText.Length > 0)
                ClearLoginTimeLockUnlocked(username);
            return true;
        }
    }

    /// <summary>Increments login failures and returns the lock/tries message; generic text when the user is missing.</summary>
    public string NoteLoginFailure(string username)
    {
        username = (username ?? "").Trim();
        // Do not reveal whether the username exists.
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
            // Unknown username gets the same wording as a wrong password.
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

    /// <summary>Clears login-failure counters and lock; no-ops on a blank name.</summary>
    public void ClearLoginFails(string username)
    {
        username = (username ?? "").Trim();
        // Blank names cannot match a row.
        if (username.Length == 0)
            return;
        lock (_gate)
            ClearLoginFailsUnlocked(username);
    }

    /// <summary>Clears login_fails and login_lock_until (caller already holds <c>_gate</c>).</summary>
    private void ClearLoginFailsUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE app_accounts SET login_fails = 0, login_lock_until = '' WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    /// <summary>Clears only an expired timed lock, leaving the failure count in place.</summary>
    private void ClearLoginTimeLockUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText =
            "UPDATE app_accounts SET login_lock_until = '' WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    /// <summary>Stores a hashed stay-signed-in token and returns the raw token for the client.</summary>
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

    /// <summary>Username for a still-valid session token, or null when missing/expired.</summary>
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

    /// <summary>Deletes one session by the raw token the client holds.</summary>
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

    /// <summary>Deletes every session for <paramref name="username"/>.</summary>
    public void DeleteSessionsForUser(string username)
    {
        lock (_gate)
            DeleteSessionsForUserUnlocked(username);
    }

    /// <summary>Builds the signed-in AuthResponse, combining table flags with admins.json roles.</summary>
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

    /// <summary>Maps an account record to the editor DTO with Found = true.</summary>
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

    /// <summary>Deletes app_sessions rows for one user (caller already holds <c>_gate</c>).</summary>
    private void DeleteSessionsForUserUnlocked(string username)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM app_sessions WHERE username = $user;";
        cmd.AddParam("$user", username);
        cmd.Exec(_engine);
    }

    /// <summary>Points existing sessions at the new username after a rename.</summary>
    private void RenameSessionsUnlocked(string oldUsername, string newUsername)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE app_sessions SET username = $new WHERE username = $old;";
        cmd.AddParam("$new", newUsername);
        cmd.AddParam("$old", oldUsername);
        cmd.Exec(_engine);
    }

    /// <summary>Deletes stay-signed-in rows whose expires_at is in the past.</summary>
    private void DeleteExpiredSessionsUnlocked()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM app_sessions WHERE expires_at < $now;";
        cmd.AddParam("$now", DateTime.Now.ToString("o"));
        cmd.Exec(_engine);
    }
}

/// <summary>One app_accounts row used for login, resume, and account editor mapping.</summary>
internal struct AccountRecord
{
    /// <summary>Sign-in name.</summary>
    public string Username;
    /// <summary>Friendly name; may be empty.</summary>
    public string DisplayName;
    /// <summary>Contact email.</summary>
    public string Email;
    /// <summary>Argon2id PHC string or legacy PBKDF2 hash.</summary>
    public string PasswordHash;
    /// <summary>Legacy PBKDF2 salt, or the argon2id marker.</summary>
    public string PasswordSalt;
    /// <summary>True when the user must set a new password before other work.</summary>
    public bool MustChangePassword;
    /// <summary>Administrator flag stored on the row.</summary>
    public bool IsAdmin;
    /// <summary>IT flag stored on the row.</summary>
    public bool IsIt;
    /// <summary>Whether stay-signed-in is enabled for this account.</summary>
    public bool StaySignedIn;
    /// <summary>User overlay table_access JSON.</summary>
    public string TableAccess;
}
