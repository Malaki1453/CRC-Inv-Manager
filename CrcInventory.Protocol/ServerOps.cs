namespace CrcInventory.Protocol;

/// <summary>
/// Named operations on the encrypted stream. New access methods (HTTPS, gRPC, …)
/// keep these names and add another <see cref="IDataChannel"/> — clients never
/// receive database files.
/// </summary>
public static class ServerOps
{
    public const int ProtocolVersion = 1;

    public const string SessionHello = "session.hello";
    public const string SessionPing = "session.ping";

    public const string AuthLogin = "auth.login";
    public const string AuthResume = "auth.resume";
    public const string AuthLogout = "auth.logout";
    public const string AuthRecoverQuestions = "auth.recoverQuestions";
    public const string AuthRecover = "auth.recover";
    public const string AuthChangePassword = "auth.changePassword";

    public const string TableEnsure = "table.ensure";
    public const string TableHeaders = "table.headers";
    public const string TableRead = "table.read";
    public const string TableReadIds = "table.readIds";
    public const string TableInsert = "table.insert";
    public const string TableInsertMany = "table.insertMany";
    public const string TableUpdate = "table.update";
    public const string TableEnsureColumns = "table.ensureColumns";
    public const string TableCount = "table.count";
    public const string TableArchive = "table.archive";
    public const string TableLatestTerm = "table.latestTerm";

    public const string SettingsRead = "settings.read";
    public const string SettingsWrite = "settings.write";
    public const string UserEmailRead = "userEmail.read";
    public const string UserEmailWrite = "userEmail.write";

    public const string AccountsCount = "accounts.count";
    public const string AccountsGet = "accounts.get";
    public const string AccountsInsert = "accounts.insert";
    public const string AccountsList = "accounts.list";
    public const string AccountsUpdate = "accounts.update";
    public const string AccountsPassword = "accounts.password";
    public const string AccountsMustChange = "accounts.mustChange";
    public const string AccountsRename = "accounts.rename";
    public const string AccountsEmail = "accounts.email";
    public const string AccountsDelete = "accounts.delete";
    public const string AccountsStayGet = "accounts.stayGet";
    public const string AccountsStaySet = "accounts.staySet";
    public const string AccountsRoles = "accounts.roles";
    public const string AccountsAccessGet = "accounts.accessGet";
    public const string AccountsAccessSet = "accounts.accessSet";

    public const string SecurityQuestions = "security.questions";
    public const string SecuritySet = "security.set";

    public const string BankList = "bank.list";
    public const string BankInsert = "bank.insert";
    public const string BankUpdate = "bank.update";
    public const string BankDelete = "bank.delete";
    public const string BankLinkGet = "bank.linkGet";
    public const string BankLinkSet = "bank.linkSet";
    public const string BankCursor = "bank.cursor";

    public const string PdfSave = "pdf.save";
    public const string PdfHas = "pdf.has";
    public const string PdfGet = "pdf.get";

    public const string RolesRead = "roles.read";
    public const string RolesWrite = "roles.write";
}
