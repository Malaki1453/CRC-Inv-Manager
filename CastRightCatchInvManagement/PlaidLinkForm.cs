using System.Text;
using System.Text.Json;
using CrcInventory.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CastRightCatchInvManagement
{
    /// <summary>
    /// Hosts Plaid Link in WebView2 using the JavaScript SDK. The old hosted
    /// link.html?isWebview=true page talks over plaidlink://, which WebView2
    /// often treats as a failed navigation, so Link exits and this window closes.
    /// </summary>
    internal sealed class PlaidLinkForm : Form
    {
        private const string HostName = "crc-plaid.localhost";

        private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
        private readonly Label _status = new();
        private TaskCompletionSource<LinkOutcome>? _linkWait;
        private bool _closing;

        public BankConnectResult? Result { get; private set; }

        /// <summary>Open Connect bank, run link-token through first import, and return the host result.</summary>
        public static BankConnectResult? Run(IWin32Window owner)
        {
            using var form = new PlaidLinkForm();
            form.Shown += async (_, _) => await form.RunPipeline();
            form.ShowDialog(owner);
            return form.Result;
        }

        private PlaidLinkForm()
        {
            Text = "Connect bank";
            StartPosition = FormStartPosition.CenterParent;
            Size = new Size(480, 720);
            MinimumSize = new Size(400, 560);
            BackColor = Theme.NavyDark;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Theme.NavyDark,
                Padding = new Padding(20, 12, 20, 8)
            };
            var title = new Label
            {
                Text = "Opening your bank…",
                Dock = DockStyle.Top,
                Height = 28,
                Font = Theme.BodyBold,
                ForeColor = Color.White,
                AutoSize = false
            };
            _status.Dock = DockStyle.Fill;
            _status.Font = Theme.Small;
            _status.ForeColor = Color.FromArgb(160, 174, 188);
            _status.Text = "Starting";
            header.Controls.Add(_status);
            header.Controls.Add(title);

            Controls.Add(_web);
            Controls.Add(header);
            FormClosed += (_, _) =>
                _linkWait?.TrySetResult(new LinkOutcome { Exit = true });
        }

        /// <summary>Mint a link token, open Plaid Link, then finish connect on the host.</summary>
        private async Task RunPipeline()
        {
            try
            {
                SetStatus("Creating web link to bank");
                var token = await PlaidClient.CreateLinkTokenAsync();
                if (!token.Ok)
                {
                    Fail(token.Error);
                    return;
                }

                SetStatus("Starting WebView2");
                await StartWebViewAsync();

                SetStatus("Opening bank login");
                _linkWait = new TaskCompletionSource<LinkOutcome>(TaskCreationOptions.RunContinuationsAsynchronously);
                OpenLink(token.Value);
                SetStatus("Waiting for bank login");

                var outcome = await _linkWait.Task;
                if (_closing || IsDisposed)
                    return;
                if (outcome.Exit || string.IsNullOrWhiteSpace(outcome.PublicToken))
                {
                    if (outcome.Error.Length > 0)
                        Fail(outcome.Error);
                    else
                    {
                        Result = null;
                        DialogResult = DialogResult.Cancel;
                        Close();
                    }

                    return;
                }

                SetStatus("Exchanging public token");
                var result = await PlaidClient.CompleteConnectAsync(
                    outcome.PublicToken,
                    outcome.Institution,
                    outcome.AccountId,
                    outcome.AccountName,
                    outcome.AccountMask);
                Result = result;
                if (result.Error.Length > 0 && result.Id == 0)
                {
                    Fail(result.Error);
                    return;
                }

                DialogResult = DialogResult.OK;
                Close();
            }
            catch (Exception ex)
            {
                Fail(ex.Message);
            }
        }

        /// <summary>Create the WebView2 host and listen for Plaid SDK messages.</summary>
        private async Task StartWebViewAsync()
        {
            string folder = LinkFolder();
            Directory.CreateDirectory(folder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.Settings.IsWebMessageEnabled = true;
            _web.CoreWebView2.SetVirtualHostNameToFolderMapping(
                HostName,
                folder,
                CoreWebView2HostResourceAccessKind.Allow);
            _web.CoreWebView2.WebMessageReceived += (_, e) => HandleMessage(e.TryGetWebMessageAsString());
            _web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (TryHandlePlaidLink(e.Uri))
                    e.Cancel = true;
            };
            _web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                // A popup must not replace this Link page; that used to close Connect bank.
                if (TryHandlePlaidLink(e.Uri))
                    e.Handled = true;
            };
            _web.CoreWebView2.ProcessFailed += (_, _) =>
                Fail("The bank login window stopped. Try Connect bank again.");
        }

        /// <summary>Write the Plaid.create page and navigate to it on the mapped host.</summary>
        private void OpenLink(string linkToken)
        {
            string html = LinkPage.Replace("__TOKEN__", JsonSerializer.Serialize(linkToken), StringComparison.Ordinal);
            File.WriteAllText(Path.Combine(LinkFolder(), "link.html"), html, Encoding.UTF8);
            _web.CoreWebView2.Navigate("https://" + HostName + "/link.html");
        }

        /// <summary>Plaid SDK posts JSON { type, ... } through chrome.webview.</summary>
        private void HandleMessage(string? json)
        {
            if (string.IsNullOrWhiteSpace(json) || _closing)
                return;

            JsonDocument doc;
            try
            {
                doc = JsonDocument.Parse(json);
            }
            catch (JsonException)
            {
                return;
            }

            using (doc)
            {
                var root = doc.RootElement;
                string type = Prop(root, "type");
                if (type.Equals("load", StringComparison.OrdinalIgnoreCase) ||
                    type.Equals("event", StringComparison.OrdinalIgnoreCase))
                {
                    string ev = Prop(root, "event");
                    SetStatus(StatusForEvent(ev));
                    return;
                }

                if (type.Equals("success", StringComparison.OrdinalIgnoreCase))
                {
                    _linkWait?.TrySetResult(new LinkOutcome
                    {
                        PublicToken = Prop(root, "public_token"),
                        Institution = Prop(root, "institution"),
                        AccountId = Prop(root, "account_id"),
                        AccountName = Prop(root, "account_name"),
                        AccountMask = Prop(root, "account_mask")
                    });
                    return;
                }

                if (type.Equals("exit", StringComparison.OrdinalIgnoreCase))
                {
                    _linkWait?.TrySetResult(new LinkOutcome
                    {
                        Exit = true,
                        Error = Prop(root, "error")
                    });
                }
            }
        }

        /// <summary>Parse leftover plaidlink:// URLs if the SDK still emits them.</summary>
        private bool TryHandlePlaidLink(string? raw)
        {
            if (string.IsNullOrWhiteSpace(raw) ||
                !Uri.TryCreate(raw, UriKind.Absolute, out var uri) ||
                !uri.Scheme.Equals("plaidlink", StringComparison.OrdinalIgnoreCase))
                return false;

            string action = (uri.Host + uri.AbsolutePath).Trim('/');
            var query = ParseQuery(uri.Query);
            if (action.StartsWith("exit", StringComparison.OrdinalIgnoreCase))
            {
                query.TryGetValue("error_message", out string? message);
                if (string.IsNullOrWhiteSpace(message))
                    query.TryGetValue("display_message", out message);
                if (string.IsNullOrWhiteSpace(message))
                    query.TryGetValue("error_code", out message);
                _linkWait?.TrySetResult(new LinkOutcome { Exit = true, Error = message ?? "" });
                return true;
            }

            if (action.StartsWith("event", StringComparison.OrdinalIgnoreCase))
            {
                query.TryGetValue("event_name", out string? ev);
                SetStatus(StatusForEvent(ev ?? ""));
                return true;
            }

            if (action.StartsWith("connected", StringComparison.OrdinalIgnoreCase))
            {
                query.TryGetValue("public_token", out string? token);
                query.TryGetValue("institution_name", out string? inst);
                query.TryGetValue("account_id", out string? accountId);
                query.TryGetValue("account_name", out string? accountName);
                query.TryGetValue("account_mask", out string? mask);
                _linkWait?.TrySetResult(new LinkOutcome
                {
                    PublicToken = token ?? "",
                    Institution = inst ?? "",
                    AccountId = accountId ?? "",
                    AccountName = accountName ?? "",
                    AccountMask = mask ?? ""
                });
                return true;
            }

            return true;
        }

        private static Dictionary<string, string> ParseQuery(string query)
        {
            var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (query.StartsWith('?'))
                query = query[1..];
            foreach (var part in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
            {
                int eq = part.IndexOf('=');
                if (eq < 0)
                    continue;
                string key = Uri.UnescapeDataString(part[..eq].Replace("+", " "));
                string value = Uri.UnescapeDataString(part[(eq + 1)..].Replace("+", " "));
                map[key] = value;
            }

            return map;
        }

        private static string StatusForEvent(string ev)
        {
            ev = (ev ?? "").Trim();
            if (ev.Equals("OPEN", StringComparison.OrdinalIgnoreCase) ||
                ev.Equals("load", StringComparison.OrdinalIgnoreCase))
                return "Choose your bank";
            if (ev.Equals("SELECT_INSTITUTION", StringComparison.OrdinalIgnoreCase))
                return "Opening bank sign-in";
            if (ev.Equals("SUBMIT_CREDENTIALS", StringComparison.OrdinalIgnoreCase))
                return "Signing in";
            if (ev.Equals("TRANSITION_VIEW", StringComparison.OrdinalIgnoreCase))
                return "Waiting for bank login";
            return ev.Length > 0 ? ev : "Waiting for bank login";
        }

        private static string Prop(JsonElement root, string name) =>
            root.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
                ? value.GetString() ?? ""
                : "";

        private static string LinkFolder() =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CastRightCatchInvManagement",
                "WebView2-Plaid");

        private void SetStatus(string text)
        {
            if (IsDisposed)
                return;
            if (InvokeRequired)
            {
                BeginInvoke(() => SetStatus(text));
                return;
            }

            _status.Text = text;
        }

        private void Fail(string error)
        {
            if (_closing || IsDisposed)
                return;
            _closing = true;
            Result = new BankConnectResult { Error = error };
            SetStatus(error);
            MessageBox.Show(this, error, "Connect bank", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private sealed class LinkOutcome
        {
            public bool Exit { get; set; }
            public string Error { get; set; } = "";
            public string PublicToken { get; set; } = "";
            public string Institution { get; set; } = "";
            public string AccountId { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string AccountMask { get; set; } = "";
        }

        private const string LinkPage =
            """
            <!DOCTYPE html>
            <html>
            <head>
              <meta charset="utf-8">
              <meta name="viewport" content="width=device-width, initial-scale=1">
              <title>Connect bank</title>
              <style>
                html, body { margin: 0; height: 100%; background: #0f2744; }
              </style>
              <script src="https://cdn.plaid.com/link/v2/stable/link-initialize.js"></script>
            </head>
            <body>
            <script>
              function send(msg) {
                try { window.chrome.webview.postMessage(JSON.stringify(msg)); } catch (e) {}
              }
              const handler = Plaid.create({
                token: __TOKEN__,
                onLoad: function () { send({ type: 'load', event: 'OPEN' }); },
                onSuccess: function (public_token, metadata) {
                  const account = (metadata && metadata.accounts && metadata.accounts[0]) || {};
                  const institution = (metadata && metadata.institution) || {};
                  send({
                    type: 'success',
                    public_token: public_token,
                    institution: institution.name || '',
                    account_id: account.id || (metadata && metadata.account_id) || '',
                    account_name: account.name || '',
                    account_mask: account.mask || ''
                  });
                },
                onExit: function (err) {
                  send({
                    type: 'exit',
                    error: err ? (err.display_message || err.error_message || err.error_code || '') : ''
                  });
                },
                onEvent: function (eventName) {
                  send({ type: 'event', event: eventName || '' });
                }
              });
              handler.open();
            </script>
            </body>
            </html>
            """;
    }
}
