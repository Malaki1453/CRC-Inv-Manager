using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CastRightCatchInvManagement
{
    /// <summary>Hosts Plaid Link in WebView2 and returns the public_token on success.</summary>
    internal sealed class PlaidLinkForm : Form
    {
        private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
        private readonly string _linkToken;

        public string? PublicToken { get; private set; }
        public string InstitutionName { get; private set; } = "";
        public string AccountName { get; private set; } = "";
        public string AccountMask { get; private set; } = "";
        public string AccountId { get; private set; } = "";

        public PlaidLinkForm(string linkToken)
        {
            _linkToken = linkToken;
            Text = "Connect bank";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(480, 720);
            MinimumSize = new Size(400, 560);
            BackColor = Theme.NavyDark;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;
            Controls.Add(_web);
            Shown += async (_, _) => await StartAsync();
        }

        private async Task StartAsync()
        {
            try
            {
                string folder = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "CastRightCatchInvManagement",
                    "WebView2-Plaid");
                Directory.CreateDirectory(folder);
                var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.WebMessageReceived += (_, e) =>
                {
                    HandleMessage(e.TryGetWebMessageAsString());
                };
                _web.NavigateToString(BuildHtml(_linkToken));
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not open the bank login window.\n\n" + ex.Message,
                    "Connect bank",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Warning);
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private void HandleMessage(string? json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return;
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                var root = doc.RootElement;
                if (root.TryGetProperty("exit", out var exit) && exit.GetBoolean())
                {
                    DialogResult = DialogResult.Cancel;
                    Close();
                    return;
                }

                PublicToken = root.TryGetProperty("public_token", out var token)
                    ? token.GetString() ?? "" : "";
                if (root.TryGetProperty("institution", out var inst) &&
                    inst.TryGetProperty("name", out var iname))
                    InstitutionName = iname.GetString() ?? "";
                if (root.TryGetProperty("account", out var account))
                {
                    AccountId = account.TryGetProperty("id", out var id) ? id.GetString() ?? "" : "";
                    AccountName = account.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "";
                    AccountMask = account.TryGetProperty("mask", out var m) ? m.GetString() ?? "" : "";
                }

                DialogResult = PublicToken.Length > 0 ? DialogResult.OK : DialogResult.Cancel;
                Close();
            }
            catch
            {
                DialogResult = DialogResult.Cancel;
                Close();
            }
        }

        private static string BuildHtml(string linkToken)
        {
            string token = System.Text.Json.JsonSerializer.Serialize(linkToken);
            return
                """
                <!DOCTYPE html>
                <html>
                <head>
                  <meta charset="utf-8" />
                  <script src="https://cdn.plaid.com/link/v2/stable/link-initialize.js"></script>
                </head>
                <body style="margin:0;background:#0c1624;color:#fff;font-family:Segoe UI,sans-serif;">
                  <p style="padding:24px;">Opening your bank…</p>
                  <script>
                    const handler = Plaid.create({
                      token: TOKEN,
                      onSuccess: (public_token, metadata) => {
                        const account = (metadata.accounts && metadata.accounts[0]) || {};
                        chrome.webview.postMessage(JSON.stringify({
                          public_token,
                          institution: metadata.institution || {},
                          account: { id: account.id, name: account.name, mask: account.mask }
                        }));
                      },
                      onExit: () => chrome.webview.postMessage(JSON.stringify({ exit: true }))
                    });
                    handler.open();
                  </script>
                </body>
                </html>
                """.Replace("TOKEN", token);
        }
    }
}
