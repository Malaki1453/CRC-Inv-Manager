using System.Text.Json;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

/// <summary>Routes named ops to <see cref="InventoryStore"/>, enforcing sign-in and IT/admin checks.</summary>
internal sealed class ServerDispatch
{
    private readonly InventoryStore _store;
    private readonly string _fingerprint;

    /// <summary>Binds this dispatcher to a store and the TLS pin advertised in session.hello.</summary>
    public ServerDispatch(InventoryStore store, string fingerprint)
    {
        _store = store;
        _fingerprint = fingerprint;
    }

    /// <summary>Runs one named op. Public ops skip sign-in; everything else requires a session.</summary>
    public object? Handle(string op, JsonElement payload, ClientSession session)
    {
        // Hello, login, and resume must work before a session exists.
        if (IsPublic(op))
            return HandlePublic(op, payload, session);

        // All other ops need a signed-in session on this TLS connection.
        if (!session.SignedIn)
            throw new InvalidOperationException("Sign in first.");

        return op switch
        {
            // Keep-alive so an idle TLS session is not torn down.
            ServerOps.SessionPing => new { ok = true },
            // Clear in-memory identity on this connection.
            ServerOps.AuthLogout => Logout(session),
            // Change password for self or, for IT, another user.
            ServerOps.AuthChangePassword => ChangePassword(payload, session),
            // Create missing tables on first use.
            ServerOps.TableEnsure => EnsureTables(),
            // Header names for a table.
            ServerOps.TableHeaders => Headers(payload),
            // Rows filtered by the caller's table-access policy.
            ServerOps.TableRead => Read(payload, session),
            // Rows with ids, same policy filter.
            ServerOps.TableReadIds => ReadIds(payload, session),
            // Insert one row if the caller may write it.
            ServerOps.TableInsert => Insert(payload, session),
            // Bulk insert if the caller may write each row.
            ServerOps.TableInsertMany => InsertMany(payload, session),
            // Update by id if the caller may write the row.
            ServerOps.TableUpdate => Update(payload, session),
            // Add missing TEXT columns.
            ServerOps.TableEnsureColumns => EnsureColumns(payload),
            // Live (+ archive when viewing old) row count.
            ServerOps.TableCount => Count(payload),
            // Move completed process rows to archive.
            ServerOps.TableArchive => Archive(payload),
            // Latest term_start date.
            ServerOps.TableLatestTerm => LatestTerm(),
            // Admin-only settings with secrets revealed.
            ServerOps.SettingsRead => ReadAdminSettings(session),
            // Settings with secret keys omitted.
            ServerOps.SettingsReadPublic => _store.ReadPublicSettings(),
            // Admin-only settings write.
            ServerOps.SettingsWrite => WriteSettings(payload, session),
            // Signed-in user's UI preferences.
            ServerOps.PrefsRead => _store.ReadPrefs(session.Username),
            // Write UI preferences for the signed-in user.
            ServerOps.PrefsWrite => WritePrefs(payload, session),
            // Windows-user email lookup.
            ServerOps.UserEmailRead => ReadUserEmail(payload),
            // Windows-user email write.
            ServerOps.UserEmailWrite => WriteUserEmail(payload),
            // IT-only account count.
            ServerOps.AccountsCount => RequireIt(session, () => _store.CountAccounts()),
            // Self or IT: one account.
            ServerOps.AccountsGet => GetAccount(payload, session),
            // IT-only create.
            ServerOps.AccountsInsert => InsertAccount(payload, session),
            // IT-only list.
            ServerOps.AccountsList => RequireIt(session, () => _store.ListAccounts()),
            // Self or IT: display name/email.
            ServerOps.AccountsUpdate => UpdateAccount(payload, session),
            // IT password reset.
            ServerOps.AccountsPassword => ResetPassword(payload, session),
            // IT must-change flag.
            ServerOps.AccountsMustChange => MustChange(payload, session),
            // IT rename.
            ServerOps.AccountsRename => RenameAccount(payload, session),
            // Self or IT: email only.
            ServerOps.AccountsEmail => UpdateEmail(payload, session),
            // IT delete (not self).
            ServerOps.AccountsDelete => DeleteAccount(payload, session),
            // IT unlock after lockout.
            ServerOps.AccountsUnlock => UnlockLogin(payload, session),
            // Self or IT: stay-signed-in get.
            ServerOps.AccountsStayGet => StayGet(payload, session),
            // Self or IT: stay-signed-in set.
            ServerOps.AccountsStaySet => StaySet(payload, session),
            // IT role flags.
            ServerOps.AccountsRoles => SetRoles(payload, session),
            // Self, IT, or admin: stored overlay.
            ServerOps.AccountsAccessGet => AccessGet(payload, session),
            // Admin or IT: write overlay.
            ServerOps.AccountsAccessSet => AccessSet(payload, session),
            // Bank list without Plaid secrets.
            ServerOps.BankList => _store.ListBankAccounts(),
            // Insert bank row.
            ServerOps.BankInsert => BankInsert(payload),
            // Update bank row.
            ServerOps.BankUpdate => BankUpdate(payload),
            // Delete bank row.
            ServerOps.BankDelete => BankDelete(payload),
            // Admin: decrypted Plaid link.
            ServerOps.BankLinkGet => BankLinkGet(payload, session),
            // Admin: write Plaid link.
            ServerOps.BankLinkSet => BankLinkSet(payload, session),
            // Admin: Plaid cursor only.
            ServerOps.BankCursor => BankCursor(payload, session),
            // Store a PDF.
            ServerOps.PdfSave => PdfSave(payload),
            // PDF exists?
            ServerOps.PdfHas => PdfHas(payload),
            // Load a PDF.
            ServerOps.PdfGet => PdfGet(payload),
            // Delete a PDF.
            ServerOps.PdfDelete => PdfDelete(payload),
            // IT: admins.json lists.
            ServerOps.RolesRead => RequireIt(session, () => new RolesDto
            {
                Admins = _store.Roles.Admins.ToList(),
                It = _store.Roles.It.ToList()
            }),
            // IT: replace admins.json lists.
            ServerOps.RolesWrite => RolesWrite(payload, session),
            // Unknown names must not be treated as success.
            _ => throw new InvalidOperationException("Unknown operation: " + op)
        };
    }

    /// <summary>Hello, login, and resume — the only ops allowed before sign-in.</summary>
    private object HandlePublic(string op, JsonElement payload, ClientSession session) => op switch
    {
        // Advertise protocol, product name, bootstrap state, and TLS pin.
        ServerOps.SessionHello => new HelloResponse
        {
            Protocol = ServerOps.ProtocolVersion,
            Name = "CrcInventory",
            HasItUser = _store.HasItUser(),
            Fingerprint = _fingerprint
        },
        // Password sign-in.
        ServerOps.AuthLogin => Login(payload, session),
        // Restore a stay-signed-in token.
        ServerOps.AuthResume => Resume(payload, session),
        // Anything else in this path is a programming error.
        _ => throw new InvalidOperationException("Unknown operation: " + op)
    };

    /// <summary>True for ops that must work without a signed-in session.</summary>
    private static bool IsPublic(string op) =>
        op is ServerOps.SessionHello
            or ServerOps.AuthLogin
            or ServerOps.AuthResume;

    /// <summary>Verifies password, upgrades legacy hashes, optionally issues a stay-signed-in token.</summary>
    private AuthResponse Login(JsonElement payload, ClientSession session)
    {
        var request = Read<LoginRequest>(payload);
        // Locked accounts must not proceed to password verify (which would still increment fails).
        if (!_store.AllowLogin(request.Username, out string error))
            throw new InvalidOperationException(error);

        // Same wording as a wrong password so missing users are not revealed.
        if (!_store.TryGetAccountRecord(request.Username, out var record))
            throw new InvalidOperationException("That username or password is not right.");

        // Wrong password records a failure and may lock the account.
        if (!Passwords.Verify(request.Password, record.PasswordHash, record.PasswordSalt))
            throw new InvalidOperationException(_store.NoteLoginFailure(request.Username));

        _store.ClearLoginFails(record.Username);

        // Rehash PBKDF2 accounts to Argon2id on successful sign-in.
        if (!record.PasswordHash.StartsWith("$argon2id$", StringComparison.Ordinal))
            _store.UpdateAccountPassword(record.Username, request.Password);

        string? token = null;
        // Stay-signed-in is skipped when the host disabled it or the user must change password.
        if (request.StaySignedIn && _store.StaySignedInEnabled() && !record.MustChangePassword)
        {
            _store.SetStaySignedIn(record.Username, true);
            token = _store.InsertSession(record.Username, DateTime.Now.AddDays(_store.StaySignedInDays()));
        }

        var auth = _store.ToAuth(record, token);
        session.SignIn(auth);
        return auth;
    }

    /// <summary>Restores a session from a stay-signed-in token if the account is still allowed to sign in.</summary>
    private AuthResponse Resume(JsonElement payload, ClientSession session)
    {
        var request = Read<ResumeRequest>(payload);
        // Tokens are useless when the host turned the feature off.
        if (!_store.StaySignedInEnabled())
            throw new InvalidOperationException("Stay signed in is off.");

        string? username = _store.FindSessionUsername(request.Token);
        // Expired, unknown, or deleted tokens must not sign anyone in.
        if (string.IsNullOrWhiteSpace(username) || !_store.TryGetAccountRecord(username, out var record))
            throw new InvalidOperationException("That session is no longer valid.");
        // IT-locked or timed-out accounts cannot resume either.
        if (!_store.AllowLogin(username, out string error))
            throw new InvalidOperationException(error);

        var auth = _store.ToAuth(record, request.Token);
        session.SignIn(auth);
        return auth;
    }

    /// <summary>Clears the in-memory session on this connection.</summary>
    private static object Logout(ClientSession session)
    {
        session.SignOut();
        return true;
    }

    /// <summary>Changes password for self or, for IT, another user; requires the current password.</summary>
    private bool ChangePassword(JsonElement payload, ClientSession session)
    {
        var request = Read<ChangePasswordRequest>(payload);
        string username = string.IsNullOrWhiteSpace(request.Username) ? session.Username : request.Username;
        // Non-IT users may only change their own password.
        if (!SelfOrIt(session, username))
            throw new InvalidOperationException("Not allowed.");
        // Current password must match so a stolen session still needs the old secret to rotate it.
        if (!_store.TryGetAccountRecord(username, out var record) ||
            !Passwords.Verify(request.CurrentPassword, record.PasswordHash, record.PasswordSalt))
            throw new InvalidOperationException("That username or password is not right.");
        // Policy failure is reported with the same wording as the desktop app.
        if (!_store.UpdateAccountPassword(username, request.NewPassword))
            throw new InvalidOperationException(
                "Password must be at least " + Passwords.MinimumLength +
                " characters, with a capital letter, a number, and a symbol.");
        _store.SetMustChangePassword(username, false);
        return true;
    }

    /// <summary>Ensures live/archive and app tables exist.</summary>
    private bool EnsureTables()
    {
        _store.EnsureCreated();
        return true;
    }

    /// <summary>Header names for the requested table.</summary>
    private string[] Headers(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.Headers(request.Table, request.ViewOld);
    }

    /// <summary>Reads rows and strips blocked rows/hidden columns for the signed-in user.</summary>
    private List<Dictionary<string, string>> Read(JsonElement payload, ClientSession session)
    {
        var request = Read<TableRequest>(payload);
        var rows = _store.Read(request.Table, request.ViewOld);
        return AccessFilter.Restrict(
            _store,
            request.Table,
            rows,
            session.Username,
            fullAccess: false);
    }

    /// <summary>Reads rows with ids, then applies the same access filter as <see cref="Read"/>.</summary>
    private List<IdFieldsDto> ReadIds(JsonElement payload, ClientSession session)
    {
        var request = Read<TableRequest>(payload);
        return AccessFilter.Restrict(
                _store,
                request.Table,
                _store.ReadWithIds(request.Table, request.ViewOld),
                session.Username,
                fullAccess: false)
            .Select(row => new IdFieldsDto { Id = row.Id, Fields = row.Fields })
            .ToList();
    }

    /// <summary>Inserts one row after the access filter allows the write.</summary>
    private bool Insert(JsonElement payload, ClientSession session)
    {
        var request = Read<TableRequest>(payload);
        var values = request.Values ?? new Dictionary<string, string>();
        RequireRowAccess(request.Table, values, session);
        _store.Insert(request.Table, values);
        return true;
    }

    /// <summary>Inserts many rows after each one passes the access filter.</summary>
    private int InsertMany(JsonElement payload, ClientSession session)
    {
        var request = Read<TableRequest>(payload);
        var rows = request.Rows ?? new List<Dictionary<string, string>>();
        foreach (var row in rows)
            RequireRowAccess(request.Table, row, session);
        return _store.InsertMany(request.Table, rows);
    }

    /// <summary>Updates a row by id after the access filter allows the write.</summary>
    private bool Update(JsonElement payload, ClientSession session)
    {
        var request = Read<TableRequest>(payload);
        var values = request.Values ?? new Dictionary<string, string>();
        RequireRowAccess(request.Table, values, session);
        return _store.UpdateById(request.Table, request.Id, values);
    }

    /// <summary>Throws when the signed-in user may not write this table/row.</summary>
    private void RequireRowAccess(string table, Dictionary<string, string> values, ClientSession session)
    {
        // View-only, denied tables, and blocked parties must not be written.
        if (!AccessFilter.CanWriteRow(_store, table, values, session.Username, fullAccess: false))
            throw new InvalidOperationException("You do not have access to that data.");
    }

    /// <summary>Adds missing TEXT columns on live (and archive when viewing old).</summary>
    private bool EnsureColumns(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        _store.EnsureColumns(request.Table, request.Columns ?? Array.Empty<string>(), request.ViewOld);
        return true;
    }

    /// <summary>Row count for the requested table.</summary>
    private int Count(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        return _store.Count(request.Table, request.ViewOld);
    }

    /// <summary>Archives completed process rows for the optional term in the request.</summary>
    private int Archive(JsonElement payload)
    {
        var request = Read<TableRequest>(payload);
        DateTime? term = DateTime.TryParse(request.Term, out var parsed) ? parsed : null;
        return _store.ArchiveCompleted(term);
    }

    /// <summary>Latest term_start as yyyy-MM-dd, or null.</summary>
    private string? LatestTerm() => _store.LatestTerm()?.ToString("yyyy-MM-dd");

    /// <summary>Admin-only settings with secrets revealed.</summary>
    private Dictionary<string, string> ReadAdminSettings(ClientSession session)
    {
        RequireAdmin(session);
        return _store.ReadSettings(revealSecrets: true);
    }

    /// <summary>Admin-only settings write.</summary>
    private bool WriteSettings(JsonElement payload, ClientSession session)
    {
        RequireAdmin(session);
        var request = Read<SettingsWriteRequest>(payload);
        _store.WriteSettings(request.Values);
        return true;
    }

    /// <summary>Writes UI preferences for the signed-in user only.</summary>
    private bool WritePrefs(JsonElement payload, ClientSession session)
    {
        var request = Read<PrefsWriteRequest>(payload);
        _store.WritePrefs(session.Username, request.Values);
        return true;
    }

    /// <summary>Looks up the email stored for a Windows user name.</summary>
    private string? ReadUserEmail(JsonElement payload)
    {
        var request = Read<UserEmailRequest>(payload);
        return _store.ReadUserEmail(request.WindowsUser);
    }

    /// <summary>Stores the email for a Windows user name.</summary>
    private bool WriteUserEmail(JsonElement payload)
    {
        var request = Read<UserEmailRequest>(payload);
        _store.WriteUserEmail(request.WindowsUser, request.Email);
        return true;
    }

    /// <summary>Returns one account for self or IT; Found is false when missing.</summary>
    private AccountGetDto GetAccount(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        // Non-IT users may only read their own account.
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        return _store.GetAccount(request.Username) ?? new AccountGetDto { Found = false };
    }

    /// <summary>IT-only account create; new users must change password on first sign-in.</summary>
    private bool InsertAccount(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        // A missing password would otherwise fail later with a poorer message.
        if (string.IsNullOrWhiteSpace(request.Password))
            throw new InvalidOperationException(
                "Password must be at least " + Passwords.MinimumLength +
                " characters, with a capital letter, a number, and a symbol.");
        bool created = _store.InsertAccount(
            request.Username,
            request.DisplayName ?? "",
            request.Password,
            request.Email ?? "",
            request.IsAdmin,
            request.IsIt,
            mustChange: true);
        // Duplicate username or policy failure is reported to the client.
        if (!created)
            throw new InvalidOperationException("Could not create that user.");
        return true;
    }

    /// <summary>Updates display name and email for self or IT.</summary>
    private bool UpdateAccount(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        // Non-IT users may only update their own profile.
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        return _store.UpdateAccount(request.Username, request.DisplayName ?? "", request.Email ?? "");
    }

    /// <summary>IT password reset; also sets the must-change flag from the request.</summary>
    private bool ResetPassword(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        // Blank or policy-failing passwords must not overwrite the hash.
        if (string.IsNullOrWhiteSpace(request.Password) ||
            !_store.UpdateAccountPassword(request.Username, request.Password))
            throw new InvalidOperationException(
                "Password must be at least " + Passwords.MinimumLength +
                " characters, with a capital letter, a number, and a symbol.");
        _store.SetMustChangePassword(request.Username, request.MustChange);
        return true;
    }

    /// <summary>IT sets or clears must-change-password.</summary>
    private bool MustChange(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        _store.SetMustChangePassword(request.Username, request.MustChange);
        return true;
    }

    /// <summary>IT renames an account.</summary>
    private bool RenameAccount(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        return _store.RenameAccount(request.OldUsername ?? "", request.NewUsername ?? "");
    }

    /// <summary>Updates email for self or IT.</summary>
    private bool UpdateEmail(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        // Non-IT users may only change their own email.
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        _store.UpdateAccountEmail(request.Username, request.Email ?? "");
        return true;
    }

    /// <summary>IT deletes an account, but not the currently signed-in user.</summary>
    private bool DeleteAccount(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        // Deleting the active session's user would lock this connection out of IT.
        if (request.Username.Equals(session.Username, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("You cannot delete the signed-in user.");
        return _store.DeleteAccount(request.Username);
    }

    /// <summary>IT clears login-failure lockout.</summary>
    private bool UnlockLogin(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        _store.ClearLoginFails(request.Username);
        return true;
    }

    /// <summary>Stay-signed-in flag for self or IT.</summary>
    private bool StayGet(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        // Non-IT users may only read their own preference.
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        return _store.GetStaySignedIn(request.Username);
    }

    /// <summary>Sets stay-signed-in for self or IT.</summary>
    private bool StaySet(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        // Non-IT users may only change their own preference.
        if (!SelfOrIt(session, request.Username))
            throw new InvalidOperationException("Not allowed.");
        _store.SetStaySignedIn(request.Username, request.Enabled);
        return true;
    }

    /// <summary>IT writes admin/IT flags on an account.</summary>
    private bool SetRoles(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<AccountWriteRequest>(payload);
        _store.SetAccountRoles(request.Username, request.IsAdmin, request.IsIt);
        return true;
    }

    /// <summary>Stored table-access overlay for self, IT, or admin.</summary>
    private string AccessGet(JsonElement payload, ClientSession session)
    {
        var request = Read<AccountWriteRequest>(payload);
        // Admins may inspect any overlay; others only self unless IT.
        if (!SelfOrIt(session, request.Username) && !session.IsAdmin)
            throw new InvalidOperationException("Not allowed.");
        return _store.GetStoredTableAccess(request.Username);
    }

    /// <summary>Admin or IT writes the table-access overlay for a user.</summary>
    private bool AccessSet(JsonElement payload, ClientSession session)
    {
        // Ordinary users must not change anyone's access policy.
        if (!session.IsAdmin && !session.IsIt)
            throw new InvalidOperationException("Not allowed.");
        var request = Read<AccountWriteRequest>(payload);
        _store.SetTableAccess(request.Username, request.Json ?? "");
        return true;
    }

    /// <summary>Inserts a bank_accounts row and returns its id.</summary>
    private long BankInsert(JsonElement payload)
    {
        var request = Read<BankWriteRequest>(payload);
        return _store.InsertBankAccount(
            request.Name ?? "",
            request.Bank ?? "",
            request.Last4 ?? "",
            request.Notes ?? "");
    }

    /// <summary>Updates non-secret fields on a bank_accounts row.</summary>
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

    /// <summary>Deletes a bank_accounts row.</summary>
    private bool BankDelete(JsonElement payload)
    {
        var request = Read<BankWriteRequest>(payload);
        _store.DeleteBankAccount(request.Id);
        return true;
    }

    /// <summary>Admin-only decrypted Plaid link fields.</summary>
    private BankLinkDto BankLinkGet(JsonElement payload, ClientSession session)
    {
        RequireAdmin(session);
        var request = Read<BankWriteRequest>(payload);
        return _store.GetBankLiveLink(request.Id);
    }

    /// <summary>Admin-only write of Plaid link fields.</summary>
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

    /// <summary>Admin-only Plaid transactions-cursor update.</summary>
    private bool BankCursor(JsonElement payload, ClientSession session)
    {
        RequireAdmin(session);
        var request = Read<BankWriteRequest>(payload);
        _store.SetBankLiveCursor(request.Id, request.Cursor ?? "");
        return true;
    }

    /// <summary>Stores a PDF under kind+key.</summary>
    private bool PdfSave(JsonElement payload)
    {
        var request = Read<PdfRequest>(payload);
        _store.SavePdf(request.Kind, request.Key, request.FileName ?? "", request.Content ?? Array.Empty<byte>());
        return true;
    }

    /// <summary>True when a PDF exists for kind+key.</summary>
    private bool PdfHas(JsonElement payload)
    {
        var request = Read<PdfRequest>(payload);
        return _store.HasPdf(request.Kind, request.Key);
    }

    /// <summary>Loads a stored PDF, or null when missing.</summary>
    private PdfDto? PdfGet(JsonElement payload)
    {
        var request = Read<PdfRequest>(payload);
        return _store.TryGetPdf(request.Kind, request.Key);
    }

    /// <summary>Deletes a stored PDF by kind+key.</summary>
    private bool PdfDelete(JsonElement payload)
    {
        var request = Read<PdfRequest>(payload);
        _store.DeletePdf(request.Kind, request.Key);
        return true;
    }

    /// <summary>IT replaces admin/IT lists; both must stay non-empty, then account flags are synced.</summary>
    private bool RolesWrite(JsonElement payload, ClientSession session)
    {
        RequireIt(session);
        var request = Read<RolesDto>(payload);
        // An empty IT list would make the host unmanageable.
        if (request.It.Count == 0)
            throw new InvalidOperationException("There must be at least one IT user.");
        // An empty admin list would lock out settings.
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

    /// <summary>Deserializes the JSON payload, or a default instance when it is null/undefined.</summary>
    private static T Read<T>(JsonElement payload)
    {
        var value = payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? default
            : payload.Deserialize<T>(JsonWire.Options);
        return value ?? Activator.CreateInstance<T>();
    }

    /// <summary>Runs <paramref name="action"/> after confirming the session is IT.</summary>
    private static T RequireIt<T>(ClientSession session, Func<T> action)
    {
        RequireIt(session);
        return action();
    }

    /// <summary>Throws when the session is not an IT user.</summary>
    private static void RequireIt(ClientSession session)
    {
        // Account and role ops are IT-only.
        if (!session.IsIt)
            throw new InvalidOperationException("IT access is required.");
    }

    /// <summary>Throws when the session is not an administrator.</summary>
    private static void RequireAdmin(ClientSession session)
    {
        // Settings and Plaid secrets are administrator-only.
        if (!session.IsAdmin)
            throw new InvalidOperationException("Administrator access is required.");
    }

    /// <summary>True when the session is IT or the username is the signed-in user.</summary>
    private static bool SelfOrIt(ClientSession session, string username) =>
        session.IsIt ||
        username.Equals(session.Username, StringComparison.OrdinalIgnoreCase);
}

/// <summary>Per-connection sign-in state: username and admin/IT flags from the last AuthResponse.</summary>
internal sealed class ClientSession
{
    /// <summary>Canonical account name, or empty when signed out.</summary>
    public string Username { get; private set; } = "";
    /// <summary>Administrator flag from the last successful sign-in.</summary>
    public bool IsAdmin { get; private set; }
    /// <summary>IT flag from the last successful sign-in.</summary>
    public bool IsIt { get; private set; }
    /// <summary>True when <see cref="Username"/> is non-empty.</summary>
    public bool SignedIn => Username.Length > 0;

    /// <summary>Copies identity and role flags from a successful login or resume.</summary>
    public void SignIn(AuthResponse auth)
    {
        Username = auth.Username;
        IsAdmin = auth.IsAdmin;
        IsIt = auth.IsIt;
    }

    /// <summary>Clears identity so later ops on this connection require sign-in again.</summary>
    public void SignOut()
    {
        Username = "";
        IsAdmin = false;
        IsIt = false;
    }
}
