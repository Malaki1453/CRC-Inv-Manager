namespace CrcInventory.Protocol;

/// <summary>First-contact payload: protocol version, product name, IT bootstrap state, and TLS pin.</summary>
public sealed class HelloResponse
{
    /// <summary>Wire protocol version the host speaks.</summary>
    public int Protocol { get; set; } = ServerOps.ProtocolVersion;

    /// <summary>Product name so a client can confirm it reached this inventory host.</summary>
    public string Name { get; set; } = "CrcInventory";

    /// <summary>True when an IT administrator already exists and bootstrap is not needed.</summary>
    public bool HasItUser { get; set; }

    /// <summary>SHA-256 pin of the server certificate the client should store.</summary>
    public string Fingerprint { get; set; } = "";
}

/// <summary>Credentials for a password sign-in, including the stay-signed-in preference.</summary>
public sealed class LoginRequest
{
    /// <summary>Account name to authenticate.</summary>
    public string Username { get; set; } = "";

    /// <summary>Plain password; verified against the stored hash and never written back.</summary>
    public string Password { get; set; } = "";

    /// <summary>When true, the host may issue a resumable session token.</summary>
    public bool StaySignedIn { get; set; }
}

/// <summary>Presents a previously issued session token to restore a signed-in session.</summary>
public sealed class ResumeRequest
{
    /// <summary>Opaque session token from a prior login with stay-signed-in enabled.</summary>
    public string Token { get; set; } = "";
}

/// <summary>Signed-in identity and policy flags returned after login or resume.</summary>
public sealed class AuthResponse
{
    /// <summary>Canonical account name for this session.</summary>
    public string Username { get; set; } = "";

    /// <summary>Display name shown in the UI; may match the username.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Account email used for notices.</summary>
    public string Email { get; set; } = "";

    /// <summary>True when the user may change administrator settings.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>True when the user may manage accounts and roles.</summary>
    public bool IsIt { get; set; }

    /// <summary>True when the user must set a new password before other work.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Whether this account currently has stay-signed-in enabled.</summary>
    public bool StaySignedIn { get; set; }

    /// <summary>JSON table-access policy for this user (group baseline plus overlay).</summary>
    public string TableAccess { get; set; } = "";

    /// <summary>Host setting: whether stay-signed-in is offered at all.</summary>
    public bool StaySignedInEnabled { get; set; }

    /// <summary>How many days a stay-signed-in token remains valid.</summary>
    public int StaySignedInDays { get; set; }

    /// <summary>Idle hours after which the desktop app should close the session.</summary>
    public int IdleCloseHours { get; set; }

    /// <summary>New or reused session token when stay-signed-in is active; otherwise null.</summary>
    public string? SessionToken { get; set; }
}

/// <summary>Current-password proof plus the replacement password for an account.</summary>
public sealed class ChangePasswordRequest
{
    /// <summary>Account to change; empty means the signed-in user.</summary>
    public string Username { get; set; } = "";

    /// <summary>Existing password, required so only the owner (or a verified session) can rotate it.</summary>
    public string CurrentPassword { get; set; } = "";

    /// <summary>Replacement password; must meet <see cref="PasswordRules"/>.</summary>
    public string NewPassword { get; set; } = "";
}

/// <summary>Shared payload for table reads, writes, counts, and archive requests.</summary>
public sealed class TableRequest
{
    /// <summary>Logical table name from <see cref="ServerOps"/> / schema (purchase_sales, sales, …).</summary>
    public string Table { get; set; } = "";

    /// <summary>When true, include archived process rows alongside live ones.</summary>
    public bool ViewOld { get; set; }

    /// <summary>When true, the client only wants the current term (host may ignore unused flags).</summary>
    public bool CurrentTermOnly { get; set; }

    /// <summary>Row id for updates; negative ids refer to archive rows.</summary>
    public long Id { get; set; }

    /// <summary>Column map for a single insert or update.</summary>
    public Dictionary<string, string>? Values { get; set; }

    /// <summary>Column maps for a bulk insert.</summary>
    public List<Dictionary<string, string>>? Rows { get; set; }

    /// <summary>Extra column names to ensure exist on the table.</summary>
    public string[]? Columns { get; set; }

    /// <summary>Optional term date used when archiving completed process rows.</summary>
    public string? Term { get; set; }
}

/// <summary>One inventory row with its live or archive id attached.</summary>
public sealed class IdFieldsDto
{
    /// <summary>Positive live id, or the negated archive id used by the client.</summary>
    public long Id { get; set; }

    /// <summary>Column values keyed by header name.</summary>
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Compact account row for the user list, including lock state.</summary>
public sealed class AccountListDto
{
    /// <summary>Sign-in name.</summary>
    public string Username { get; set; } = "";

    /// <summary>Friendly name shown in the list.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Contact email on the account.</summary>
    public string Email { get; set; } = "";

    /// <summary>True when this user is an administrator.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>True when this user is in the IT group.</summary>
    public bool IsIt { get; set; }

    /// <summary>Whether stay-signed-in is enabled on this account.</summary>
    public bool StaySignedIn { get; set; }

    /// <summary>True when IT must unlock the account after too many failed logins.</summary>
    public bool LoginLocked { get; set; }
}

/// <summary>Full account record for the editor, including whether the lookup found a user.</summary>
public sealed class AccountGetDto
{
    /// <summary>False when the username does not exist; other fields are then empty.</summary>
    public bool Found { get; set; }

    /// <summary>Sign-in name.</summary>
    public string Username { get; set; } = "";

    /// <summary>Friendly name stored on the account.</summary>
    public string DisplayName { get; set; } = "";

    /// <summary>Contact email stored on the account.</summary>
    public string Email { get; set; } = "";

    /// <summary>True when this user is an administrator.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>True when this user is in the IT group.</summary>
    public bool IsIt { get; set; }

    /// <summary>True when the user must change password at next sign-in.</summary>
    public bool MustChangePassword { get; set; }

    /// <summary>Whether stay-signed-in is enabled on this account.</summary>
    public bool StaySignedIn { get; set; }

    /// <summary>Stored table-access overlay JSON for this user.</summary>
    public string TableAccess { get; set; } = "";
}

/// <summary>Write payload reused by account create, update, rename, password, and access ops.</summary>
public sealed class AccountWriteRequest
{
    /// <summary>Target username for most account operations.</summary>
    public string Username { get; set; } = "";

    /// <summary>New display name when updating profile fields.</summary>
    public string? DisplayName { get; set; }

    /// <summary>New email when updating profile fields.</summary>
    public string? Email { get; set; }

    /// <summary>New password when creating or resetting an account.</summary>
    public string? Password { get; set; }

    /// <summary>Current username when renaming.</summary>
    public string? OldUsername { get; set; }

    /// <summary>Replacement username when renaming.</summary>
    public string? NewUsername { get; set; }

    /// <summary>Whether the user must change password after an IT reset.</summary>
    public bool MustChange { get; set; }

    /// <summary>Stay-signed-in enabled flag when setting that preference.</summary>
    public bool Enabled { get; set; }

    /// <summary>Administrator role flag when setting roles.</summary>
    public bool IsAdmin { get; set; }

    /// <summary>IT role flag when setting roles.</summary>
    public bool IsIt { get; set; }

    /// <summary>Table-access overlay JSON when IT/admin writes access policy.</summary>
    public string? Json { get; set; }
}

/// <summary>Non-secret bank-account row shown in the banking list.</summary>
public sealed class BankRowDto
{
    /// <summary>Primary key of the bank_accounts row.</summary>
    public long Id { get; set; }

    /// <summary>Nickname for this account in the app.</summary>
    public string Name { get; set; } = "";

    /// <summary>Bank name.</summary>
    public string Bank { get; set; } = "";

    /// <summary>Last four digits of the account number.</summary>
    public string Last4 { get; set; } = "";

    /// <summary>Free-form notes.</summary>
    public string Notes { get; set; } = "";
}

/// <summary>Write payload for bank rows and Plaid live-link fields.</summary>
public sealed class BankWriteRequest
{
    /// <summary>Row to update, delete, or link; unused on insert.</summary>
    public long Id { get; set; }

    /// <summary>Nickname to store.</summary>
    public string? Name { get; set; }

    /// <summary>Bank name to store.</summary>
    public string? Bank { get; set; }

    /// <summary>Last four digits to store.</summary>
    public string? Last4 { get; set; }

    /// <summary>Notes to store.</summary>
    public string? Notes { get; set; }

    /// <summary>Plaid access token; sealed at rest on the host.</summary>
    public string? AccessToken { get; set; }

    /// <summary>Plaid item id for this link.</summary>
    public string? ItemId { get; set; }

    /// <summary>Plaid account id for this link.</summary>
    public string? AccountId { get; set; }

    /// <summary>Plaid transactions cursor so sync can resume.</summary>
    public string? Cursor { get; set; }
}

/// <summary>Decrypted Plaid link fields for an administrator.</summary>
public sealed class BankLinkDto
{
    /// <summary>Opened Plaid access token, or empty when none is stored.</summary>
    public string AccessToken { get; set; } = "";

    /// <summary>Plaid item id.</summary>
    public string ItemId { get; set; } = "";

    /// <summary>Plaid account id.</summary>
    public string AccountId { get; set; } = "";

    /// <summary>Last transactions cursor.</summary>
    public string Cursor { get; set; } = "";
}

/// <summary>Lookup or write of a stored PDF by kind and document key.</summary>
public sealed class PdfRequest
{
    /// <summary>Document kind (invoice, purchase, …) used as part of the primary key.</summary>
    public string Kind { get; set; } = "";

    /// <summary>Document key (invoice number, etc.) used as part of the primary key.</summary>
    public string Key { get; set; } = "";

    /// <summary>File name to store or display.</summary>
    public string? FileName { get; set; }

    /// <summary>PDF bytes when saving; unused on get/has/delete.</summary>
    public byte[]? Content { get; set; }
}

/// <summary>Stored PDF bytes plus the file name to present to the user.</summary>
public sealed class PdfDto
{
    /// <summary>Original file name of the stored PDF.</summary>
    public string FileName { get; set; } = "";

    /// <summary>Raw PDF content.</summary>
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

/// <summary>Administrator and IT username lists from admins.json.</summary>
public sealed class RolesDto
{
    /// <summary>Usernames in the administrator group.</summary>
    public List<string> Admins { get; set; } = new();

    /// <summary>Usernames in the IT group.</summary>
    public List<string> It { get; set; } = new();
}

/// <summary>Bulk write of application settings (SMTP, Plaid, stay-signed-in, …).</summary>
public sealed class SettingsWriteRequest
{
    /// <summary>Setting keys and values; secret keys are sealed by the host.</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Per-user UI preference map keyed by preference name.</summary>
public sealed class PrefsWriteRequest
{
    /// <summary>Preference keys and values for the signed-in user.</summary>
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>Windows-user to email mapping used for outbound notices.</summary>
public sealed class UserEmailRequest
{
    /// <summary>Windows account name stored in app_users.</summary>
    public string WindowsUser { get; set; } = "";

    /// <summary>Email to store; unused on read.</summary>
    public string? Email { get; set; }
}
