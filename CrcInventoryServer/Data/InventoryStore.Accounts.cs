using CrcInventory.Protocol;
using Microsoft.Data.Sqlite;

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
            return Convert.ToInt32(cmd.ExecuteScalar());
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
            cmd.Parameters.AddWithValue("$user", username);
            using var reader = cmd.ExecuteReader();
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
                       COALESCE(stay_signed_in, 0)
                FROM app_accounts
                ORDER BY username COLLATE NOCASE;
                """;
            using var reader = cmd.ExecuteReader();
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
                    StaySignedIn = !reader.IsDBNull(5) && reader.GetInt32(5) != 0
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
            cmd.Parameters.AddWithValue("$user", username);
            cmd.Parameters.AddWithValue("$name", displayName ?? "");
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.Parameters.AddWithValue("$salt", salt);
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$at", NowStamp());
            cmd.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
            cmd.Parameters.AddWithValue("$it", isIt ? 1 : 0);
            cmd.Parameters.AddWithValue("$must", mustChange ? 1 : 0);
            try
            {
                if (cmd.ExecuteNonQuery() <= 0)
                    return false;
            }
            catch (SqliteException)
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
            cmd.Parameters.AddWithValue("$name", displayName ?? "");
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            return cmd.ExecuteNonQuery() > 0;
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
            cmd.Parameters.AddWithValue("$hash", hash);
            cmd.Parameters.AddWithValue("$salt", salt);
            cmd.Parameters.AddWithValue("$user", username);
            bool updated = cmd.ExecuteNonQuery() > 0;
            if (updated)
                DeleteSessionsForUserUnlocked(username);
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
            cmd.Parameters.AddWithValue("$flag", mustChange ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
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
            cmd.Parameters.AddWithValue("$new", newUsername);
            cmd.Parameters.AddWithValue("$old", oldUsername);
            try
            {
                if (cmd.ExecuteNonQuery() <= 0)
                    return false;
                RenameSessionsUnlocked(oldUsername, newUsername);
            }
            catch (SqliteException)
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
            cmd.Parameters.AddWithValue("$email", email ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
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
            cmd.Parameters.AddWithValue("$user", username);
            if (cmd.ExecuteNonQuery() <= 0)
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
            cmd.Parameters.AddWithValue("$flag", enabled ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
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
            cmd.Parameters.AddWithValue("$admin", isAdmin ? 1 : 0);
            cmd.Parameters.AddWithValue("$it", isIt ? 1 : 0);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }

        Roles.Ensure(username, isAdmin, isIt);
    }

    public string GetTableAccess(string username)
    {
        if (!TryGetAccountRecord(username, out var record))
            return "";
        return record.TableAccess;
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
            cmd.Parameters.AddWithValue("$json", json ?? "");
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }
    }

    public RecoverQuestionsResponse SecurityQuestions(string username)
    {
        var result = new RecoverQuestionsResponse();
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return result;

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT security_q1, security_q2, security_q3 FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return result;

            result.Q1 = reader.IsDBNull(0) ? "" : reader.GetString(0);
            result.Q2 = reader.IsDBNull(1) ? "" : reader.GetString(1);
            result.Q3 = reader.IsDBNull(2) ? "" : reader.GetString(2);
            result.Found = result.Q1.Length > 0 && result.Q2.Length > 0 && result.Q3.Length > 0;
            return result;
        }
    }

    public void SetSecurityQuestions(
        string username,
        string q1, string a1,
        string q2, string a2,
        string q3, string a3)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return;

        Passwords.Hash(Passwords.NormalizeAnswer(a1), out string h1, out _);
        Passwords.Hash(Passwords.NormalizeAnswer(a2), out string h2, out _);
        Passwords.Hash(Passwords.NormalizeAnswer(a3), out string h3, out _);

        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                """
                UPDATE app_accounts SET
                    security_q1 = $q1, security_a1 = $a1,
                    security_q2 = $q2, security_a2 = $a2,
                    security_q3 = $q3, security_a3 = $a3
                WHERE username = $user;
                """;
            cmd.Parameters.AddWithValue("$q1", q1 ?? "");
            cmd.Parameters.AddWithValue("$a1", h1);
            cmd.Parameters.AddWithValue("$q2", q2 ?? "");
            cmd.Parameters.AddWithValue("$a2", h2);
            cmd.Parameters.AddWithValue("$q3", q3 ?? "");
            cmd.Parameters.AddWithValue("$a3", h3);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.ExecuteNonQuery();
        }
    }

    public bool VerifySecurityAnswers(string username, string a1, string a2, string a3)
    {
        username = (username ?? "").Trim();
        if (username.Length == 0)
            return false;

        string h1, h2, h3;
        lock (_gate)
        {
            using var db = Open();
            using var cmd = db.CreateCommand();
            cmd.CommandText =
                "SELECT security_a1, security_a2, security_a3 FROM app_accounts WHERE username = $user;";
            cmd.Parameters.AddWithValue("$user", username);
            using var reader = cmd.ExecuteReader();
            if (!reader.Read())
                return false;
            h1 = reader.IsDBNull(0) ? "" : reader.GetString(0);
            h2 = reader.IsDBNull(1) ? "" : reader.GetString(1);
            h3 = reader.IsDBNull(2) ? "" : reader.GetString(2);
        }

        return Passwords.Verify(Passwords.NormalizeAnswer(a1), h1, "argon2id") &&
               Passwords.Verify(Passwords.NormalizeAnswer(a2), h2, "argon2id") &&
               Passwords.Verify(Passwords.NormalizeAnswer(a3), h3, "argon2id");
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
            cmd.Parameters.AddWithValue("$hash", tokenHash);
            cmd.Parameters.AddWithValue("$user", username);
            cmd.Parameters.AddWithValue("$exp", expiresAt.ToString("o"));
            cmd.Parameters.AddWithValue("$at", NowStamp());
            cmd.ExecuteNonQuery();
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
            cmd.Parameters.AddWithValue("$hash", tokenHash);
            cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
            return cmd.ExecuteScalar()?.ToString();
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
            cmd.Parameters.AddWithValue("$hash", tokenHash);
            cmd.ExecuteNonQuery();
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
        cmd.Parameters.AddWithValue("$user", username);
        cmd.ExecuteNonQuery();
    }

    private void RenameSessionsUnlocked(string oldUsername, string newUsername)
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "UPDATE app_sessions SET username = $new WHERE username = $old;";
        cmd.Parameters.AddWithValue("$new", newUsername);
        cmd.Parameters.AddWithValue("$old", oldUsername);
        cmd.ExecuteNonQuery();
    }

    private void DeleteExpiredSessionsUnlocked()
    {
        using var db = Open();
        using var cmd = db.CreateCommand();
        cmd.CommandText = "DELETE FROM app_sessions WHERE expires_at < $now;";
        cmd.Parameters.AddWithValue("$now", DateTime.Now.ToString("o"));
        cmd.ExecuteNonQuery();
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
