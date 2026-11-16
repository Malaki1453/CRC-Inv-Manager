using System.Reflection;
using Microsoft.Win32;

namespace CastRightCatchInvManagement
{
    /// <summary>Opens a new Outlook message with a file attached so the user can finish sending it.</summary>
    internal static class OutlookMail
    {
        /// <summary>Open a blank Outlook compose window with <paramref name="pdfPath"/> attached as <paramref name="displayName"/>.</summary>
        public static bool TryCompose(string pdfPath, string? displayName, out string error)
        {
            error = "";
            if (!File.Exists(pdfPath))
            {
                error = "The PDF could not be found.";
                return false;
            }

            string fileName = AttachmentFileName(pdfPath, displayName);
            string attach = CopyForSend(pdfPath, fileName) ?? pdfPath;
            if (TryOutlookCom(attach, fileName, out string comError))
                return true;
            if (TryOutlookExe(attach, out string exeError))
                return true;

            error = comError.Length > 0
                ? comError
                : exeError.Length > 0
                    ? exeError
                    : "Outlook is not installed on this PC.";
            return false;
        }

        /// <summary>Document title as a .pdf file name, without the viewer’s temp hex prefix.</summary>
        private static string AttachmentFileName(string pdfPath, string? displayName)
        {
            string name = SanitizeFileName(displayName);
            if (name.Length == 0)
                name = SanitizeFileName(Path.GetFileNameWithoutExtension(pdfPath));
            if (name.Length == 0)
                name = "document";
            if (!name.EndsWith(".pdf", StringComparison.OrdinalIgnoreCase))
                name += ".pdf";
            return name;
        }

        /// <summary>Replace characters Windows will not allow in a file name.</summary>
        private static string SanitizeFileName(string? name)
        {
            string text = (name ?? "").Trim();
            foreach (char c in Path.GetInvalidFileNameChars())
                text = text.Replace(c, '-');
            return text.Trim(' ', '.');
        }

        /// <summary>Copy into a send folder under the document name so Outlook is not sharing the WebView2 temp file.</summary>
        private static string? CopyForSend(string pdfPath, string fileName)
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CastRightCatchInvManagement",
                    "PdfSend");
                Directory.CreateDirectory(folder);
                SweepOldCopies(folder);

                string dest = Path.Combine(folder, fileName);
                File.Copy(pdfPath, dest, overwrite: true);
                return dest;
            }
            catch
            {
                return null;
            }
        }

        /// <summary>Drop send copies older than a day so the folder does not grow without bound.</summary>
        private static void SweepOldCopies(string folder)
        {
            DateTime cutoff = DateTime.UtcNow.AddDays(-1);
            foreach (string path in Directory.GetFiles(folder, "*.pdf"))
            {
                try
                {
                    if (File.GetCreationTimeUtc(path) < cutoff)
                        File.Delete(path);
                }
                catch
                {
                }
            }
        }

        /// <summary>Late-bound Outlook COM so the build does not need an Office interop reference.</summary>
        private static bool TryOutlookCom(string pdfPath, string fileName, out string error)
        {
            error = "";
            Type? type = Type.GetTypeFromProgID("Outlook.Application");
            if (type == null)
                return false;

            try
            {
                object? app = Activator.CreateInstance(type);
                if (app == null)
                    return false;

                object? mail = type.InvokeMember(
                    "CreateItem",
                    BindingFlags.InvokeMethod,
                    null,
                    app,
                    new object[] { 0 });
                if (mail == null)
                    return false;

                object? attachments = mail.GetType().InvokeMember(
                    "Attachments",
                    BindingFlags.GetProperty,
                    null,
                    mail,
                    null);
                if (attachments == null)
                    return false;
                try
                {
                    // Source, olByValue, Position, DisplayName so the attachment shows the document title.
                    attachments.GetType().InvokeMember(
                        "Add",
                        BindingFlags.InvokeMethod,
                        null,
                        attachments,
                        new object[] { pdfPath, 1, Missing.Value, fileName });
                }
                catch
                {
                    attachments.GetType().InvokeMember(
                        "Add",
                        BindingFlags.InvokeMethod,
                        null,
                        attachments,
                        new object[] { pdfPath });
                }

                mail.GetType().InvokeMember(
                    "Display",
                    BindingFlags.InvokeMethod,
                    null,
                    mail,
                    new object[] { false });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>outlook.exe /a when COM is unavailable (new Outlook, or Outlook not registered).</summary>
        private static bool TryOutlookExe(string pdfPath, out string error)
        {
            error = "";
            string? exe = OutlookExePath();
            if (exe == null)
                return false;

            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exe,
                    Arguments = "/a \"" + pdfPath + "\"",
                    UseShellExecute = false
                });
                return true;
            }
            catch (Exception ex)
            {
                error = ex.Message;
                return false;
            }
        }

        /// <summary>Installed OUTLOOK.EXE from the App Paths registry, if present.</summary>
        private static string? OutlookExePath()
        {
            foreach (string key in new[]
            {
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE",
                @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\App Paths\OUTLOOK.EXE"
            })
            {
                using var hive = Registry.LocalMachine.OpenSubKey(key);
                string? path = hive?.GetValue("") as string;
                if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                    return path;
            }

            return null;
        }
    }
}
