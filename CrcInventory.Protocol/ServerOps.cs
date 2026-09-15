namespace CrcInventory.Protocol;

/// <summary>
/// Named operations on the encrypted stream. New access methods (HTTPS, gRPC, …)
/// keep these names and add another <see cref="IDataChannel"/> — clients never
/// receive database files.
/// </summary>
public static class ServerOps
{
    /// <summary>Current framed-protocol version; bump when request/response shapes change incompatibly.</summary>
    public const int ProtocolVersion = 1;

    /// <summary>Unauthenticated hello: protocol, product name, IT bootstrap state, TLS pin.</summary>
    public const string SessionHello = "session.hello";
    /// <summary>Authenticated keep-alive so an idle TLS session is not torn down.</summary>
    public const string SessionPing = "session.ping";

    /// <summary>Password sign-in; may issue a stay-signed-in token.</summary>
    public const string AuthLogin = "auth.login";
    /// <summary>Restore a session from a stay-signed-in token.</summary>
    public const string AuthResume = "auth.resume";
    /// <summary>Clear the in-memory session on this connection.</summary>
    public const string AuthLogout = "auth.logout";
    /// <summary>Change password for self or, for IT, another user (requires current password).</summary>
    public const string AuthChangePassword = "auth.changePassword";

    /// <summary>Create missing live/archive tables and app tables.</summary>
    public const string TableEnsure = "table.ensure";
    /// <summary>Return header names for a table (optionally merging archive columns).</summary>
    public const string TableHeaders = "table.headers";
    /// <summary>Read rows, filtered by the caller's table-access policy.</summary>
    public const string TableRead = "table.read";
    /// <summary>Read rows with ids, filtered by the caller's table-access policy.</summary>
    public const string TableReadIds = "table.readIds";
    /// <summary>Insert one row if the caller may write it.</summary>
    public const string TableInsert = "table.insert";
    /// <summary>Insert many rows if the caller may write each of them.</summary>
    public const string TableInsertMany = "table.insertMany";
    /// <summary>Update a row by id if the caller may write it.</summary>
    public const string TableUpdate = "table.update";
    /// <summary>Add missing TEXT columns on live (and archive when viewing old).</summary>
    public const string TableEnsureColumns = "table.ensureColumns";
    /// <summary>Count rows in live, plus archive when viewing old process tables.</summary>
    public const string TableCount = "table.count";
    /// <summary>Move completed process rows from live into archive.</summary>
    public const string TableArchive = "table.archive";
    /// <summary>Latest term_start date across live tables.</summary>
    public const string TableLatestTerm = "table.latestTerm";

    /// <summary>Read all settings with secrets revealed (admin only).</summary>
    public const string SettingsRead = "settings.read";
    /// <summary>Read settings with secret keys omitted.</summary>
    public const string SettingsReadPublic = "settings.readPublic";
    /// <summary>Write settings (admin only); secret keys are sealed.</summary>
    public const string SettingsWrite = "settings.write";
    /// <summary>Read UI preferences for the signed-in user.</summary>
    public const string PrefsRead = "prefs.read";
    /// <summary>Write UI preferences for the signed-in user.</summary>
    public const string PrefsWrite = "prefs.write";
    /// <summary>Read the email stored for a Windows user name.</summary>
    public const string UserEmailRead = "userEmail.read";
    /// <summary>Write the email stored for a Windows user name.</summary>
    public const string UserEmailWrite = "userEmail.write";

    /// <summary>Count app_accounts rows (IT only).</summary>
    public const string AccountsCount = "accounts.count";
    /// <summary>Get one account; self or IT.</summary>
    public const string AccountsGet = "accounts.get";
    /// <summary>Create an account (IT only).</summary>
    public const string AccountsInsert = "accounts.insert";
    /// <summary>List accounts (IT only).</summary>
    public const string AccountsList = "accounts.list";
    /// <summary>Update display name and email; self or IT.</summary>
    public const string AccountsUpdate = "accounts.update";
    /// <summary>IT password reset; may set must-change.</summary>
    public const string AccountsPassword = "accounts.password";
    /// <summary>IT sets or clears the must-change-password flag.</summary>
    public const string AccountsMustChange = "accounts.mustChange";
    /// <summary>IT renames an account.</summary>
    public const string AccountsRename = "accounts.rename";
    /// <summary>Update email; self or IT.</summary>
    public const string AccountsEmail = "accounts.email";
    /// <summary>IT deletes an account (not the signed-in user).</summary>
    public const string AccountsDelete = "accounts.delete";
    /// <summary>IT clears login-failure lockout.</summary>
    public const string AccountsUnlock = "accounts.unlock";
    /// <summary>Read stay-signed-in flag; self or IT.</summary>
    public const string AccountsStayGet = "accounts.stayGet";
    /// <summary>Set stay-signed-in flag; self or IT.</summary>
    public const string AccountsStaySet = "accounts.staySet";
    /// <summary>IT sets admin/IT flags on an account.</summary>
    public const string AccountsRoles = "accounts.roles";
    /// <summary>Read stored table-access overlay; self, IT, or admin.</summary>
    public const string AccountsAccessGet = "accounts.accessGet";
    /// <summary>Write table-access overlay; admin or IT.</summary>
    public const string AccountsAccessSet = "accounts.accessSet";

    /// <summary>List bank_accounts rows (no Plaid secrets).</summary>
    public const string BankList = "bank.list";
    /// <summary>Insert a bank_accounts row.</summary>
    public const string BankInsert = "bank.insert";
    /// <summary>Update a bank_accounts row.</summary>
    public const string BankUpdate = "bank.update";
    /// <summary>Delete a bank_accounts row.</summary>
    public const string BankDelete = "bank.delete";
    /// <summary>Read decrypted Plaid link fields (admin only).</summary>
    public const string BankLinkGet = "bank.linkGet";
    /// <summary>Write Plaid link fields (admin only).</summary>
    public const string BankLinkSet = "bank.linkSet";
    /// <summary>Update only the Plaid transactions cursor (admin only).</summary>
    public const string BankCursor = "bank.cursor";

    /// <summary>Store a PDF under kind+key.</summary>
    public const string PdfSave = "pdf.save";
    /// <summary>True when a PDF exists for kind+key.</summary>
    public const string PdfHas = "pdf.has";
    /// <summary>Load a stored PDF by kind+key.</summary>
    public const string PdfGet = "pdf.get";
    /// <summary>Delete a stored PDF by kind+key.</summary>
    public const string PdfDelete = "pdf.delete";

    /// <summary>Read admin and IT username lists (IT only).</summary>
    public const string RolesRead = "roles.read";
    /// <summary>Replace admin and IT username lists (IT only; both lists must stay non-empty).</summary>
    public const string RolesWrite = "roles.write";
}
