using System.Net;
using System.Net.Mail;
using System.Text;

namespace CastRightCatchInvManagement
{
    /// <summary>Sends new-user emails through the admin SMTP settings in the shared database.</summary>
    internal static class Mailer
    {
        public const string GmailHost = "smtp.gmail.com";
        public const string OfficeHost = "smtp.office365.com";
        public const string DefaultHost = GmailHost;
        public const int DefaultPort = 587;

        public static string NewUserSubject()
        {
            string company = string.IsNullOrWhiteSpace(AppState.BusinessName)
                ? "Cast Right Catch"
                : AppState.BusinessName.Trim();
            return company + " inventory login";
        }

        public static string NewUserBody(string username, string password)
        {
            return
                "An inventory login was created for you.\n\n" +
                "Username: " + username + "\n" +
                "Temporary password: " + password + "\n\n" +
                "Sign in and choose a new password. You will be asked to change it on first login.\n";
        }

        public static string ResolveHost()
        {
            string host = (AppState.SmtpHost ?? "").Trim();
            string user = ResolveUser();
            bool auto = host.Length == 0 ||
                        host.Equals(GmailHost, StringComparison.OrdinalIgnoreCase) ||
                        host.Equals(OfficeHost, StringComparison.OrdinalIgnoreCase);
            if (auto && IsMicrosoftAddress(user))
                return OfficeHost;
            if (auto)
                return GmailHost;
            return host;
        }

        public static int ResolvePort() =>
            AppState.SmtpPort > 0 ? AppState.SmtpPort : DefaultPort;

        public static string ResolveUser()
        {
            string user = (AppState.SmtpUser ?? "").Trim();
            if (user.Length > 0)
                return user;
            string company = (AppState.CompanyEmail ?? "").Trim();
            return company.Contains('@') ? company : "";
        }

        public static bool TrySendNewUserDetails(
            string toEmail,
            string username,
            string password,
            out string error)
        {
            return TrySend(toEmail, NewUserSubject(), NewUserBody(username, password), out error);
        }

        public static bool TrySend(string toEmail, string subject, string body, out string error)
        {
            error = "";
            try
            {
                SqliteInventory.ApplyAdminSmtp();
            }
            catch
            {
                // send with whatever is already in memory
            }
            toEmail = (toEmail ?? "").Trim();
            if (toEmail.Length == 0 || !toEmail.Contains('@'))
            {
                error = "That user needs an email address.";
                return false;
            }

            string host = ResolveHost();
            int port = ResolvePort();
            string user = ResolveUser();
            string from = user.Contains('@')
                ? user
                : (AppState.CompanyEmail ?? "").Trim();
            if (from.Length == 0 || !from.Contains('@'))
            {
                error = "Set the login email on Admin → Admin management to the mailbox you send from, for example you@gmail.com.";
                return false;
            }

            if (user.Length == 0)
                user = from;

            string password = AppState.SmtpPassword ?? "";
            if (IsGmailAddress(user))
                password = password.Replace(" ", "");
            if (password.Length == 0)
            {
                error = "Set the login email password on Admin → Admin management. For Gmail this must be a 16-character app password, not the normal Gmail password.";
                return false;
            }

            try
            {
                ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12;

                using var message = new MailMessage
                {
                    From = new MailAddress(from),
                    Sender = new MailAddress(from),
                    Subject = subject ?? "",
                    Body = body ?? "",
                    BodyEncoding = Encoding.UTF8,
                    SubjectEncoding = Encoding.UTF8
                };
                message.To.Add(new MailAddress(toEmail));

                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = AppState.SmtpSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network,
                    UseDefaultCredentials = false,
                    Timeout = 30000,
                    Credentials = new NetworkCredential(user, password)
                };
                client.Send(message);
                return true;
            }
            catch (Exception ex)
            {
                error = Explain(ex, host, port, user, from);
                return false;
            }
        }

        private static string Explain(
            Exception ex,
            string host,
            int port,
            string user,
            string from)
        {
            string raw = Flatten(ex);
            string lower = raw.ToLowerInvariant();

            bool gmail = IsGmailAddress(user) ||
                         host.Equals(GmailHost, StringComparison.OrdinalIgnoreCase);

            if (gmail &&
                (lower.Contains("not authenticated") ||
                 lower.Contains("username and password not accepted") ||
                 lower.Contains("5.7.8") ||
                 lower.Contains("5.7.9") ||
                 lower.Contains("application-specific password") ||
                 lower.Contains("logon failure")))
            {
                return
                    "Gmail blocked the login for " + user +
                    ". Use smtp.gmail.com on port 587. The password must be a Gmail app password " +
                    "(Google Account → Security → 2-Step Verification → App passwords), not the normal Gmail password.";
            }

            if (lower.Contains("smtpclientauthentication is disabled") ||
                lower.Contains("5.7.139") ||
                lower.Contains("5.7.57"))
            {
                return
                    "Microsoft 365 blocked the login. If you are using Gmail, set SMTP host to smtp.gmail.com " +
                    "and use a Gmail app password. For Microsoft 365, turn on Authenticated SMTP for " + user + ".";
            }

            if (lower.Contains("not authenticated") ||
                lower.Contains("authentication unsuccessful") ||
                lower.Contains("5.7.3") ||
                lower.Contains("logon failure") ||
                lower.Contains("username and password not accepted"))
            {
                return
                    "The mail server did not accept the username/password for " + user +
                    " (host " + host + "). For Gmail, use smtp.gmail.com and a 16-character app password.";
            }

            if (lower.Contains("send as denied") ||
                lower.Contains("not allowed to send") ||
                lower.Contains("5.7.60") ||
                lower.Contains("5.7.64") ||
                lower.Contains("sender address rejected"))
            {
                return
                    "The From address (" + from + ") must be the same mailbox you sign into SMTP with (" +
                    user + "). Put that address in Login email on Admin.";
            }

            if (lower.Contains("timed out") || lower.Contains("timeout"))
            {
                return
                    "The mail server " + host + ":" + port +
                    " did not answer in time. Check the host and that this PC can reach the internet.";
            }

            if (raw.Length == 0)
                raw = "The mail server refused the message.";
            return raw + " (host " + host + ":" + port + ", user " + user + ")";
        }

        private static string Flatten(Exception ex)
        {
            var parts = new List<string>();
            for (var current = ex; current != null; current = current.InnerException)
            {
                string text = (current.Message ?? "").Trim();
                if (text.Length > 0 &&
                    !parts.Any(p => p.Equals(text, StringComparison.OrdinalIgnoreCase)))
                    parts.Add(text);
            }

            return string.Join(" ", parts);
        }

        public static bool IsGmailAddress(string email)
        {
            string domain = DomainOf(email);
            return domain.Equals("gmail.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.Equals("googlemail.com", StringComparison.OrdinalIgnoreCase);
        }

        public static bool IsMicrosoftAddress(string email)
        {
            string domain = DomainOf(email);
            return domain.Equals("outlook.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.Equals("hotmail.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.Equals("live.com", StringComparison.OrdinalIgnoreCase) ||
                   domain.Equals("office365.com", StringComparison.OrdinalIgnoreCase);
        }

        private static string DomainOf(string email)
        {
            email = (email ?? "").Trim();
            int at = email.LastIndexOf('@');
            return at >= 0 && at < email.Length - 1 ? email[(at + 1)..] : "";
        }
    }
}
