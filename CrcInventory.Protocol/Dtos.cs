namespace CrcInventory.Protocol;

public sealed class HelloResponse
{
    public int Protocol { get; set; } = ServerOps.ProtocolVersion;
    public string Name { get; set; } = "CrcInventory";
    public bool HasItUser { get; set; }
    public string Fingerprint { get; set; } = "";
}

public sealed class LoginRequest
{
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public bool StaySignedIn { get; set; }
}

public sealed class ResumeRequest
{
    public string Token { get; set; } = "";
}

public sealed class AuthResponse
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsIt { get; set; }
    public bool MustChangePassword { get; set; }
    public bool StaySignedIn { get; set; }
    public string TableAccess { get; set; } = "";
    public bool StaySignedInEnabled { get; set; }
    public int StaySignedInDays { get; set; }
    public int IdleCloseHours { get; set; }
    public string? SessionToken { get; set; }
}

public sealed class RecoverQuestionsRequest
{
    public string Username { get; set; } = "";
}

public sealed class RecoverQuestionsResponse
{
    public bool Found { get; set; }
    public string Q1 { get; set; } = "";
    public string Q2 { get; set; } = "";
    public string Q3 { get; set; } = "";
}

public sealed class RecoverRequest
{
    public string Username { get; set; } = "";
    public string A1 { get; set; } = "";
    public string A2 { get; set; } = "";
    public string A3 { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class ChangePasswordRequest
{
    public string Username { get; set; } = "";
    public string CurrentPassword { get; set; } = "";
    public string NewPassword { get; set; } = "";
}

public sealed class TableRequest
{
    public string Table { get; set; } = "";
    public bool ViewOld { get; set; }
    public bool CurrentTermOnly { get; set; }
    public long Id { get; set; }
    public Dictionary<string, string>? Values { get; set; }
    public List<Dictionary<string, string>>? Rows { get; set; }
    public string[]? Columns { get; set; }
    public string? Term { get; set; }
}

public sealed class IdFieldsDto
{
    public long Id { get; set; }
    public Dictionary<string, string> Fields { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class AccountListDto
{
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsIt { get; set; }
    public bool StaySignedIn { get; set; }
}

public sealed class AccountGetDto
{
    public bool Found { get; set; }
    public string Username { get; set; } = "";
    public string DisplayName { get; set; } = "";
    public string Email { get; set; } = "";
    public bool IsAdmin { get; set; }
    public bool IsIt { get; set; }
    public bool MustChangePassword { get; set; }
    public bool StaySignedIn { get; set; }
    public string TableAccess { get; set; } = "";
}

public sealed class AccountWriteRequest
{
    public string Username { get; set; } = "";
    public string? DisplayName { get; set; }
    public string? Email { get; set; }
    public string? Password { get; set; }
    public string? OldUsername { get; set; }
    public string? NewUsername { get; set; }
    public bool MustChange { get; set; }
    public bool Enabled { get; set; }
    public bool IsAdmin { get; set; }
    public bool IsIt { get; set; }
    public string? Json { get; set; }
    public string? Q1 { get; set; }
    public string? A1 { get; set; }
    public string? Q2 { get; set; }
    public string? A2 { get; set; }
    public string? Q3 { get; set; }
    public string? A3 { get; set; }
}

public sealed class BankRowDto
{
    public long Id { get; set; }
    public string Name { get; set; } = "";
    public string Bank { get; set; } = "";
    public string Last4 { get; set; } = "";
    public string Notes { get; set; } = "";
}

public sealed class BankWriteRequest
{
    public long Id { get; set; }
    public string? Name { get; set; }
    public string? Bank { get; set; }
    public string? Last4 { get; set; }
    public string? Notes { get; set; }
    public string? AccessToken { get; set; }
    public string? ItemId { get; set; }
    public string? AccountId { get; set; }
    public string? Cursor { get; set; }
}

public sealed class BankLinkDto
{
    public string AccessToken { get; set; } = "";
    public string ItemId { get; set; } = "";
    public string AccountId { get; set; } = "";
    public string Cursor { get; set; } = "";
}

public sealed class PdfRequest
{
    public string Kind { get; set; } = "";
    public string Key { get; set; } = "";
    public string? FileName { get; set; }
    public byte[]? Content { get; set; }
}

public sealed class PdfDto
{
    public string FileName { get; set; } = "";
    public byte[] Content { get; set; } = Array.Empty<byte>();
}

public sealed class RolesDto
{
    public List<string> Admins { get; set; } = new();
    public List<string> It { get; set; } = new();
}

public sealed class SettingsWriteRequest
{
    public Dictionary<string, string> Values { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

public sealed class UserEmailRequest
{
    public string WindowsUser { get; set; } = "";
    public string? Email { get; set; }
}
