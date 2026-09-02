using System.Text.Json;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

internal sealed class ServerDispatch
{
    private readonly InventoryStore _store;
    private readonly string _fingerprint;

    public ServerDispatch(InventoryStore store, string fingerprint)
    {
        _store = store;
        _fingerprint = fingerprint;
    }

    public object? Handle(string op, JsonElement payload, ClientSession session)
    {
        if (IsPublic(op))
            return HandlePublic(op, payload, session);

        if (!session.SignedIn)
            throw new InvalidOperationException("Sign in first.");

        return op switch
        {
            ServerOps.SessionPing => new { ok = true },
            ServerOps.AuthLogout => Logout(session),
            ServerOps.AuthChangePassword => ChangePassword(payload, session),
            ServerOps.TableEnsure => EnsureTables(),
            ServerOps.TableHeaders => Headers(payload),
            ServerOps.TableRead => Read(payload),
            ServerOps.TableReadIds => ReadIds(payload),
            ServerOps.TableInsert => Insert(payload),
            ServerOps.TableInsertMany => InsertMany(payload),
            ServerOps.TableUpdate => Update(payload),
            ServerOps.TableEnsureColumns => EnsureColumns(payload),
            ServerOps.TableCount => Count(payload),
            ServerOps.TableArchive => Archive(payload),
            ServerOps.TableLatestTerm => LatestTerm(),
            ServerOps.SettingsRead => _store.ReadSettings(),
            ServerOps.SettingsWrite => WriteSettings(payload, session),
            ServerOps.UserEmailRead => ReadUserEmail(payload),
            ServerOps.UserEmailWrite => WriteUserEmail(payload),
            ServerOps.AccountsCount => RequireIt(session, () => _store.CountAccounts()),
            ServerOps.AccountsGet => GetAccount(payload, session),
            ServerOps.AccountsInsert => InsertAccount(payload, session),
            ServerOps.AccountsList => RequireIt(session, () => _store.ListAccounts()),
            ServerOps.AccountsUpdate => UpdateAccount(payload, session),
            ServerOps.AccountsPassword => ResetPassword(payload, session),
            ServerOps.AccountsMustChange => MustChange(payload, session),
            ServerOps.AccountsRename => RenameAccount(payload, session),
            ServerOps.AccountsEmail => UpdateEmail(payload, session),
            ServerOps.AccountsDelete => DeleteAccount(payload, session),
            ServerOps.AccountsStayGet => StayGet(payload, session),
            ServerOps.AccountsStaySet => StaySet(payload, session),
            ServerOps.AccountsRoles => SetRoles(payload, session),
            ServerOps.AccountsAccessGet => AccessGet(payload, session),
            ServerOps.AccountsAccessSet => AccessSet(payload, session),
            ServerOps.SecurityQuestions => SecurityQuestions(payload, session),
            ServerOps.SecuritySet => SecuritySet(payload, session),
            ServerOps.BankList => _store.ListBankAccounts(),
            ServerOps.BankInsert => BankInsert(payload),
            ServerOps.BankUpdate => BankUpdate(payload),
            ServerOps.BankDelete => BankDelete(payload),
            ServerOps.BankLinkGet => BankLinkGet(payload, session),
            ServerOps.BankLinkSet => BankLinkSet(payload, session),
            ServerOps.BankCursor => BankCursor(payload, session),
            ServerOps.PdfSave => PdfSave(payload),
            ServerOps.PdfHas => PdfHas(payload),
            ServerOps.PdfGet => PdfGet(payload),
            ServerOps.RolesRead => RequireIt(session, () => new RolesDto
            {
                Admins = _store.Roles.Admins.ToList(),
                It = _store.Roles.It.ToList()
            }),
            ServerOps.RolesWrite => RolesWrite(payload, session),
            _ => throw new InvalidOperationException("Unknown operation: " + op)
        };
    }

    private object HandlePublic(string op, JsonElement payload, ClientSession session) => op switch
    {
        ServerOps.SessionHello => new HelloResponse
        {
            Protocol = ServerOps.ProtocolVersion,
            Name = "CrcInventory",
            HasItUser = _store.HasItUser(),
            Fingerprint = _fingerprint
        },
        ServerOps.AuthLogin => Login(payload, session),
        ServerOps.AuthResume => Resume(payload, session),
        ServerOps.AuthRecoverQuestions => RecoverQuestions(payload),
        ServerOps.AuthRecover => Recover(payload),
        _ => throw new InvalidOperationException("Unknown operation: " + op)
    };

    private static bool IsPublic(string op) =>
        op is ServerOps.SessionHello
            or ServerOps.AuthLogin
            or ServerOps.AuthResume
            or ServerOps.AuthRecoverQuestions
            or ServerOps.AuthRecover;

    private AuthResponse Login(JsonElement payload, ClientSession session)
    {
        var request = Read<LoginRequest>(payload);
        if (!_store.TryGetAccountRecord(request.Username, out var record) ||
            !Passwords.Verify(request.Password, record.PasswordHash, record.PasswordSalt))
            throw new InvalidOperationException("That username or password is not right.");

        if (!record.PasswordHash.StartsWith("$argon2id$", StringComparison.Ordinal))
            _store.UpdateAccountPassword(record.Username, request.Password);

        string? token = null;
        if (request.StaySignedIn && _store.StaySignedInEnabled() && !record.MustChangePassword)
        {
            _store.SetStaySignedIn(record.Username, true);
            token = _store.InsertSession(record.Username, DateTime.Now.AddDays(_store.StaySignedInDays()));
        }

        var auth = _store.ToAuth(record, token);
        session.SignIn(auth);
        return auth;
    }

    private AuthResponse Resume(JsonElement payload, ClientSession session)
    {
        var request = Read<ResumeRequest>(payload);
        if (!_store.StaySignedInEnabled())
            throw new InvalidOperationException("Stay signed in is off.");

        string? username = _store.FindSessionUsername(request.Token);
        if (string.IsNullOrWhiteSpace(username) || !_store.TryGetAccountRecord(username, out var record))
            throw new InvalidOperationException("That session is no longer valid.");

        var auth = _store.ToAuth(record, request.Token);
        session.SignIn(auth);
        return auth;
    }

    private static object Logout(ClientSession session)
    {
        session.SignOut();
        return true;
    }

    private RecoverQuestionsResponse RecoverQuestions(JsonElement payload)
    {
        var request = Read<RecoverQuestionsRequest>(payload);
        return _store.SecurityQuestions(request.Username);
    }

    private bool Recover(JsonElement payload)
    {
        var request = Read<RecoverRequest>(payload);
        if (!_store.VerifySecurityAnswers(request.Username, request.A1, request.A2, request.A3))
            throw new InvalidOperationException("Those answers are not right.");
        if (!_store.UpdateAccountPassword(request.Username, request.NewPassword))
            throw new InvalidOperationException("Password must be at least " + Passwords.MinimumLength + " characters.");
        _store.SetMustChangePassword(request.Username, false);
        return true;
    }

    private bool ChangePassword(JsonElement payload, ClientSession session)
    {
        var request = Read<ChangePasswordRequest>(payload);
        string username = string.IsNullOrWhiteSpace(request.Username) ? session.Username : request.Username;
        if (!SelfOrIt(session, username))
            throw new InvalidOperationException("Not allowed.");
        if (!_store.TryGetAccountRecord(username, out var record) ||
            !Passwords.Verify(request.CurrentPassword, record.PasswordHash, record.PasswordSalt))
            throw new InvalidOperationException("That username or password is not right.");
        if (!_store.UpdateAccountPassword(username, request.NewPassword))
            throw new InvalidOperationException("Password must be at least " + Passwords.MinimumLength + " characters.");
        _store.SetMustChangePassword(username, false);
        return true;
    }

    private bool EnsureTables()
    {
        _store.EnsureCreated();
        return true;
    }

    private string[] Headers(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.Headers(request.Table, request.ViewOld);
    }

    private List<Dictionary<string, string>> Read(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.Read(request.Table, request.ViewOld);
    }

    private List<IdFieldsDto> ReadIds(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.ReadWithIds(request.Table, request.ViewOld)
            .Select(row => new IdFieldsDto { Id = row.Id, Fields = row.Fields })
            .ToList();
    }

    private bool Insert(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        _store.Insert(request.Table, request.Values ?? new Dictionary<string, string>());
        return true;
    }

    private int InsertMany(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.InsertMany(request.Table, request.Rows ?? new List<Dictionary<string, string>>());
    }

    private bool Update(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.UpdateById(
            request.Table,
            request.Id,
            request.Values ?? new Dictionary<string, string>());
    }

    private bool EnsureColumns(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        _store.EnsureColumns(request.Table, request.Columns ?? Array.Empty<string>(), request.ViewOld);
        return true;
    }

    private int Count(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.Count(request.Table, request.ViewOld);
    }

    private int Archive(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        DateTime? term = DateTime.TryParse(request.Term, out var parsed) ? parsed : null;
        return _store.ArchiveCompleted(term);
    }

    private string? LatestTerm() => _store.LatestTerm()?.ToString("yyyy-MM-dd");

    private bool WriteSettings(JsonElement payload, ClientSession session)
    {
        RequireAdmin(session);
        var request = Read<SettingsWriteRequest>(payload);
        _store.WriteSettings(request.Values);
        return true;
    }

    private string? ReadUserEmail(JsonElement payload)
    {
        var request = Read<UserEmailRequest>(payload);
        return _store.ReadUserEmail(request.WindowsUser);
    }

    private bool WriteUserEmail(JsonElement payload)
    {
        var request = Read<UserEmailRequest>(payload);
        _store.WriteUserEmail(request.WindowsUser, request.Email);
        return true;
    }

    private AccountGetDto GetAccount(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        return _store.GetAccount(request.Username) ?? new AccountGetDto { Found = false };
    }

    private bool InsertAccount(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException("Password must be at least " + Passwords.MinimumLength + " characters.");
        bool created = _store.InsertAccount(
            request.Username,
            request.DisplayName ?? "",
            request.Password,
            request.Email ?? "",
            request.IsAdmin,
            request.IsIt,
            mustChange: true);
        if (!created)
            throw new InvalidOperationException("Could not create that user.");
        return true;
    }

    private bool UpdateAccount(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        return _store.UpdateAccount(request.Username, request.DisplayName ?? "", request.Email ?? "");
    }

    private bool ResetPassword(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        if (string.IsNullOrWhiteSpace(request.Password) ||
            !_store.UpdateAccountPassword(request.Username, request.Password))
            throw new InvalidOperationException("Password must be at least " + Passwords.MinimumLength + " characters.");
        _store.SetMustChangePassword(request.Username, request.MustChange);
        return true;
    }

    private bool MustChange(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        _store.SetMustChangePassword(request.Username, request.MustChange);
        return true;
    }

    private bool RenameAccount(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        return _store.RenameAccount(request.OldUsername ?? "", request.NewUsername ?? "");
    }

    private bool UpdateEmail(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        _store.UpdateAccountEmail(request.Username, request.Email ?? "");
        return true;
    }

    private bool DeleteAccount(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        if (request.Username.Equals(session.Username, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("You cannot delete the signed-in user.");
        return _store.DeleteAccount(request.Username);
    }

    private bool StayGet(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        return _store.GetStaySignedIn(request.Username);
    }

    private bool StaySet(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        _store.SetStaySignedIn(request.Username, request.Enabled);
        return true;
    }

    private bool SetRoles(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        _store.SetAccountRoles(request.Username, request.IsAdmin, request.IsIt);
        return true;
    }

    private string AccessGet(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username) && !session.IsAdmin)
            throw new InvalidOperationException("Not allowed.");
        return _store.GetTableAccess(request.Username);
    }

    private bool AccessSet(JsonElement payload, ClientSession session)
    {
        if (!session.IsAdmin && !session.IsIt)
            throw new InvalidOperationException("Not allowed.");
        var request = Read<AccountWriteRequest>(payload);
        _store.SetTableAccess(request.Username, request.Json ?? "");
        return true;
    }

    private RecoverQuestionsResponse SecurityQuestions(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        return _store.SecurityQuestions(request.Username);
    }

    private bool SecuritySet(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        _store.SetSecurityQuestions(
            request.Username,
            request.Q1 ?? "", request.A1 ?? "",
            request.Q2 ?? "", request.A2 ?? "",
            request.Q3 ?? "", request.A3 ?? "");
        return true;
    }

    private long BankInsert(JsonElement payload)
    {
        var request = Read<BankWriteRequest>(payload);
        return _store.InsertBankAccount(
            request.Name ?? "",
            request.Bank ?? "",
            request.Last4 ?? "",
            request.Notes ?? "");
    }

    private bool BankUpdate(JsonElement payload)
    {
        var request = Read<BankWriteRequest>(payload);
        _store.UpdateBankAccount(
            request.Id,
            request.Name ?? "",
            request.Bank ?? "",
            request.Last4 ?? "",
            request.Notes ?? "");
        return true;
    }

    private bool BankDelete(JsonElement payload)
    {
        var request = Read<BankWriteRequest>(payload);
        _store.DeleteBankAccount(request.Id);
        return true;
    }

    private BankLinkDto BankLinkGet(JsonElement payload, ClientSession session)
    {
        RequireAdmin(session);
        var request = Read<BankWriteRequest>(payload);
        return _store.GetBankLiveLink(request.Id);
    }

    private bool BankLinkSet(JsonElement payload, ClientSession session)
    {
        RequireAdmin(session);
        var request = Read<BankWriteRequest>(payload);
        _store.SetBankLiveLink(
            request.Id,
            request.AccessToken ?? "",
            request.ItemId ?? "",
            request.AccountId ?? "",
            request.Cursor ?? "");
        return true;
    }

    private bool BankCursor(JsonElement payload, ClientSession session)
    {
        RequireAdmin(session);
        var request = Read<BankWriteRequest>(payload);
        _store.SetBankLiveCursor(request.Id, request.Cursor ?? "");
        return true;
    }

    private bool PdfSave(JsonElement payload)
    {
        var request = Read<PdfRequest>(payload);
        _store.SavePdf(request.Kind, request.Key, request.FileName ?? "", request.Content ?? Array.Empty<byte>());
        return true;
    }

    private bool PdfHas(JsonElement payload)
    {
        var request = Read<PdfRequest>(payload);
        return _store.HasPdf(request.Kind, request.Key);
    }

    private PdfDto? PdfGet(JsonElement payload)
    {
        var request = Read<PdfRequest>(payload);
        return _store.TryGetPdf(request.Kind, request.Key);
    }

    private bool RolesWrite(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<RolesDto>(payload);
        if (request.It.Count == 0)
            throw new InvalidOperationException("There must be at least one IT user.");
        if (request.Admins.Count == 0)
            throw new InvalidOperationException("There must be at least one administrator.");
        _store.Roles.Replace(request.Admins, request.It);
        foreach (var account in _store.ListAccounts())
        {
            bool admin = request.Admins.Any(name => name.Equals(account.Username, StringComparison.OrdinalIgnoreCase));
            bool it = request.It.Any(name => name.Equals(account.Username, StringComparison.OrdinalIgnoreCase));
            _store.SetAccountRoles(account.Username, admin, it);
        }

        return true;
    }

    private static T Read<T>(JsonElement payload)
    {
        var value = payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? default
            : payload.Deserialize<T>(JsonWire.Options);
        return value ?? Activator.CreateInstance<T>();
    }

    private static T RequireIt<T>(ClientSession session, Func<T> action)
    {
        RequireIt(session);
        return action();
    }

    private static void RequireIt(ClientSession session)
    {
        if (!session.IsIt)
            throw new InvalidOperationException("IT access is required.");
    }

    private static void RequireAdmin(ClientSession session)
    {
        if (!session.IsAdmin)
            throw new InvalidOperationException("Administrator access is required.");
    }

    private static bool SelfOrIt(ClientSession session, string username) =>
        session.IsIt ||
        username.Equals(session.Username, StringComparison.OrdinalIgnoreCase);
}

internal sealed class ClientSession
{
    public string Username { get; private set; } = "";
    public bool IsAdmin { get; private set; }
    public bool IsIt { get; private set; }
    public bool SignedIn => Username.Length > 0;

    public void SignIn(AuthResponse auth)
    {
        Username = auth.Username;
        IsAdmin = auth.IsAdmin;
        IsIt = auth.IsIt;
    }

    public void SignOut()
    {
        Username = "";
        IsAdmin = false;
        IsIt = false;
    }
}
