using System.Net;
using System.Net.Mail;

namespace CastRightCatchInvManagement
{
    internal static class Mailer
    {
        public static bool TrySendNewUserDetails(
            string toEmail,
            string username,
            string password,
            out string error)
        {
            string company = string.IsNullOrWhiteSpace(AppState.BusinessName)
                ? "Cast Right Catch"
                : AppState.BusinessName.Trim();
            string subject = company + " inventory login";
            string body =
                "An inventory login was created for you.\n\n" +
                "Username: " + username + "\n" +
                "Temporary password: " + password + "\n\n" +
                "Sign in and choose a new password. You will be asked to change it on first login.\n";
            return TrySend(toEmail, subject, body, out error);
        }

        public static bool TrySend(string toEmail, string subject, string body, out string error)
        {
            error = "";
            toEmail = (toEmail ?? "").Trim();
            if (toEmail.Length == 0 || !toEmail.Contains('@'))
            {
                error = "That user needs an email address.";
                return false;
            }

            string host = (AppState.SmtpHost ?? "").Trim();
            if (host.Length == 0)
            {
                error = "Set the SMTP host in Settings before sending login email.";
                return false;
            }

            int port = AppState.SmtpPort > 0 ? AppState.SmtpPort : 587;
            string from = (AppState.CompanyEmail ?? "").Trim();
            if (from.Length == 0 || !from.Contains('@'))
                from = (AppState.SmtpUser ?? "").Trim();
            if (from.Length == 0 || !from.Contains('@'))
            {
                error = "Set a company email or SMTP username to send from.";
                return false;
            }

            try
            {
                using var message = new MailMessage(from, toEmail, subject, body);
                using var client = new SmtpClient(host, port)
                {
                    EnableSsl = AppState.SmtpSsl,
                    DeliveryMethod = SmtpDeliveryMethod.Network
                };
                string user = (AppState.SmtpUser ?? "").Trim();
                if (user.Length > 0)
                    client.Credentials = new NetworkCredential(user, AppState.SmtpPassword ?? "");

                client.Send(message);
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }
    }
}
