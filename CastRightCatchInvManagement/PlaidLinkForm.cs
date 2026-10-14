using CrcInventory.Protocol;
using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CastRightCatchInvManagement
{
    /// <summary>Hosts Plaid Link in WebView2, with gray debug status under the loader.</summary>
    internal sealed class PlaidLinkForm : Form
    {
        private readonly WebView2 _web = new() { Dock = DockStyle.Fill };
        private readonly Label _status = new();
        private TaskCompletionSource<LinkOutcome>? _linkWait;

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
        }

        /// <summary>Mint a link token, open hosted Link, then finish connect on the host.</summary>
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
                string url =
                    "https://cdn.plaid.com/link/v2/stable/link.html?isWebview=true&token=" +
                    Uri.EscapeDataString(token.Value);
                _web.CoreWebView2.Navigate(url);
                SetStatus("Waiting for bank login");

                var outcome = await _linkWait.Task;
                if (outcome.Exit || string.IsNullOrWhiteSpace(outcome.PublicToken))
                {
                    Result = null;
                    DialogResult = DialogResult.Cancel;
                    Close();
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

        /// <summary>Create the WebView2 host and wire Plaid redirects plus popups.</summary>
        private async Task StartWebViewAsync()
        {
            string folder = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CastRightCatchInvManagement",
                "WebView2-Plaid");
            Directory.CreateDirectory(folder);
            var env = await CoreWebView2Environment.CreateAsync(userDataFolder: folder);
            await _web.EnsureCoreWebView2Async(env);
            _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = false;
            _web.CoreWebView2.NavigationStarting += (_, e) =>
            {
                if (TryHandlePlaidLink(e.Uri))
                    e.Cancel = true;
            };
            _web.CoreWebView2.NewWindowRequested += (_, e) =>
            {
                e.Handled = true;
                if (!string.IsNullOrWhiteSpace(e.Uri))
                    _web.CoreWebView2.Navigate(e.Uri);
            };
        }

        /// <summary>Parse plaidlink://connected and plaidlink://exit from the webview.</summary>
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
                _linkWait?.TrySetResult(new LinkOutcome { Exit = true });
                return true;
            }

            if (action.StartsWith("event", StringComparison.OrdinalIgnoreCase))
                return true;

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
            Result = new BankConnectResult { Error = error };
            SetStatus(error);
            MessageBox.Show(this, error, "Connect bank", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            DialogResult = DialogResult.Cancel;
            Close();
        }

        private sealed class LinkOutcome
        {
            public bool Exit { get; set; }
            public string PublicToken { get; set; } = "";
            public string Institution { get; set; } = "";
            public string AccountId { get; set; } = "";
            public string AccountName { get; set; } = "";
            public string AccountMask { get; set; } = "";
        }
    }
}
