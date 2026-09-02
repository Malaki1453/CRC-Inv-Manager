using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CastRightCatchInvManagement
{
    /// <summary>Dedicated PDF window (WebView2). Save to database, print, replace, or edit.</summary>
    internal sealed class PdfViewForm : Form
    {
        private static readonly Dictionary<string, PdfViewForm> OpenDocs =
            new(StringComparer.OrdinalIgnoreCase);

        private static string UserDataFolder =>
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "CastRightCatchInvManagement",
                "WebView2");

        private readonly WebView2 _web;
        private readonly Panel _fallback;
        private readonly Label _title;
        private readonly Label _subtitle;
        private readonly Button _save;
        private readonly Button _saveAs;
        private readonly Button _print;
        private readonly Button _replace;
        private readonly Button _edit;
        private string _path;
        private string _id;
        private string? _kind;
        private string? _key;

        public static void ShowDocument(
            string path,
            string? title = null,
            string? kind = null,
            string? key = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(
                    "The PDF could not be found.",
                    "PDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string id = Path.GetFullPath(path);
            if (OpenDocs.TryGetValue(id, out var existing) &&
                existing != null &&
                !existing.IsDisposed)
            {
                existing._kind ??= kind;
                existing._key ??= key;
                existing.ApplyChrome(title);
                existing.BringToFront();
                existing.WindowState = FormWindowState.Normal;
                existing.Activate();
                return;
            }

            var form = new PdfViewForm(path, title, kind, key);
            OpenDocs[form._id] = form;
            form.FormClosed += (_, _) =>
            {
                if (OpenDocs.TryGetValue(form._id, out var mapped) && mapped == form)
                    OpenDocs.Remove(form._id);
            };
            form.Show();
            form.Activate();
        }

        private PdfViewForm(string path, string? title, string? kind, string? key)
        {
            _path = Path.GetFullPath(path);
            _id = _path;
            _kind = kind;
            _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();

            Text = "PDF";
            StartPosition = FormStartPosition.CenterScreen;
            FormBorderStyle = FormBorderStyle.Sizable;
            MinimizeBox = true;
            MaximizeBox = true;
            ShowInTaskbar = true;
            AutoScaleMode = AutoScaleMode.Font;
            AutoScaleDimensions = new SizeF(7F, 15F);
            ClientSize = new Size(980, 780);
            MinimumSize = new Size(720, 520);
            BackColor = Theme.Cream;
            Font = Theme.Body;
            ForeColor = Theme.Ink;
            if (BrandAssets.AppIcon != null)
                Icon = BrandAssets.AppIcon;

            var header = new Panel
            {
                Dock = DockStyle.Top,
                Height = 72,
                BackColor = Theme.Paper,
                Padding = new Padding(20, 8, 20, 0)
            };
            var gold = new Panel
            {
                Dock = DockStyle.Bottom,
                Height = 3,
                BackColor = Theme.Gold
            };
            _title = new Label
            {
                Dock = DockStyle.Top,
                Height = 36,
                Font = Theme.PageTitle,
                ForeColor = Theme.Navy,
                TextAlign = ContentAlignment.BottomLeft
            };
            _subtitle = new Label
            {
                Dock = DockStyle.Top,
                Height = 22,
                Font = Theme.Small,
                ForeColor = Theme.Muted,
                TextAlign = ContentAlignment.TopLeft
            };
            header.Controls.Add(_subtitle);
            header.Controls.Add(_title);
            header.Controls.Add(gold);

            var toolbar = new Panel
            {
                Dock = DockStyle.Top,
                Height = 52,
                BackColor = Theme.Paper,
                Padding = new Padding(16, 8, 16, 8)
            };
            Theme.EnableDoubleBuffer(toolbar);
            toolbar.Paint += (_, e) =>
            {
                using var line = new SolidBrush(Theme.Gold);
                e.Graphics.FillRectangle(line, 0, toolbar.Height - 2, toolbar.Width, 2);
            };

            _save = ToolButton("Save to database", 158);
            Theme.StyleNavyButton(_save);
            _save.Click += (_, _) => SaveToDatabase();

            _saveAs = ToolButton("Save as", 96);
            Theme.StyleOutlineButton(_saveAs);
            _saveAs.Click += (_, _) => SaveAs();

            _print = ToolButton("Print", 80);
            Theme.StyleOutlineButton(_print);
            _print.Click += async (_, _) => await PrintPdf();

            _replace = ToolButton("Replace", 96);
            Theme.StyleOutlineButton(_replace);
            _replace.Click += (_, _) => ReplacePdf();

            _edit = ToolButton("Edit", 150);
            Theme.StyleGoldButton(_edit);
            _edit.Click += (_, _) => EditSource();

            toolbar.Controls.Add(_save);
            toolbar.Controls.Add(_saveAs);
            toolbar.Controls.Add(_print);
            toolbar.Controls.Add(_replace);
            toolbar.Controls.Add(_edit);
            toolbar.Resize += (_, _) => LayoutToolbar();

            _web = new WebView2
            {
                Dock = DockStyle.Fill,
                DefaultBackgroundColor = Theme.Cream
            };

            _fallback = new Panel
            {
                Dock = DockStyle.Fill,
                BackColor = Theme.Cream,
                Visible = false,
                Padding = new Padding(40)
            };
            var fallbackText = new Label
            {
                Name = "fallbackText",
                Dock = DockStyle.Top,
                Height = 80,
                Font = Theme.Body,
                ForeColor = Theme.Muted
            };
            var openDefault = new Button
            {
                Text = "Open in default app",
                Size = new Size(180, 34),
                Location = new Point(40, 130)
            };
            Theme.StyleNavyButton(openDefault);
            openDefault.Click += (_, _) => OpenInDefaultApp();
            _fallback.Controls.Add(openDefault);
            _fallback.Controls.Add(fallbackText);

            Controls.Add(_web);
            Controls.Add(_fallback);
            Controls.Add(toolbar);
            Controls.Add(header);

            ApplyChrome(title);
            Shown += async (_, _) => await InitViewer();
        }

        private static Button ToolButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Size = new Size(width, 34),
                TabStop = true
            };
        }

        private void LayoutToolbar()
        {
            int x = 16;
            int y = 8;
            foreach (var button in new[] { _save, _saveAs, _print, _replace, _edit })
            {
                if (!button.Visible)
                    continue;
                button.Location = new Point(x, y);
                x += button.Width + 8;
            }
        }

        private void ApplyChrome(string? title)
        {
            string heading = string.IsNullOrWhiteSpace(title)
                ? Path.GetFileNameWithoutExtension(_path)
                : title.Trim();
            _title.Text = heading;
            Text = heading;

            bool stored = !string.IsNullOrWhiteSpace(_kind) && !string.IsNullOrWhiteSpace(_key);
            _save.Visible = stored;
            _save.Enabled = stored;

            if (_kind == DataFiles.PdfKindInvoice)
            {
                _edit.Text = "Edit invoice";
                _edit.Visible = true;
                _subtitle.Text = stored
                    ? "Mark up in this window, then Save to database. Edit invoice opens Create Invoice."
                    : "Mark up, print, or replace this PDF.";
            }
            else if (_kind == DataFiles.PdfKindSalesOrder)
            {
                _edit.Text = "Edit sales order";
                _edit.Visible = true;
                _subtitle.Text = stored
                    ? "Mark up in this window, then Save to database. Edit sales order opens Create Sales Order."
                    : "Mark up, print, or replace this PDF.";
            }
            else
            {
                _edit.Visible = false;
                _subtitle.Text = "Mark up, print, or replace this PDF.";
            }

            LayoutToolbar();
        }

        private async Task InitViewer()
        {
            try
            {
                Directory.CreateDirectory(UserDataFolder);
                var env = await CoreWebView2Environment.CreateAsync(null, UserDataFolder);
                await _web.EnsureCoreWebView2Async(env);
                _web.CoreWebView2.Settings.AreDefaultContextMenusEnabled = true;
                _web.CoreWebView2.Settings.AreBrowserAcceleratorKeysEnabled = true;
                NavigatePdf();
            }
            catch (Exception ex)
            {
                ShowFallback(
                    "This computer needs the Microsoft Edge WebView2 Runtime to show PDFs in the app.\n" +
                    ex.Message);
            }
        }

        private void NavigatePdf()
        {
            if (_web.CoreWebView2 == null)
                return;

            string uri = new Uri(_path).AbsoluteUri;
            _web.CoreWebView2.Navigate(uri);
        }

        private void ShowFallback(string message)
        {
            _web.Visible = false;
            _fallback.Visible = true;
            _fallback.BringToFront();
            if (_fallback.Controls["fallbackText"] is Label label)
                label.Text = message;
            _print.Enabled = false;
        }

        private void SaveToDatabase()
        {
            if (string.IsNullOrWhiteSpace(_kind) || string.IsNullOrWhiteSpace(_key))
                return;
            if (!File.Exists(_path))
            {
                ToastAlert.Error(this, "The PDF file is missing.");
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(_path);
                _path = DataFiles.SaveStoredPdf(_kind, _key, Path.GetFileName(_path), bytes);
                ToastAlert.Success(this, "Saved to the database.");
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        private void SaveAs()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save PDF as",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                FileName = Path.GetFileName(_path),
                OverwritePrompt = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                File.Copy(_path, dialog.FileName, overwrite: true);
                ToastAlert.Success(this, "PDF saved.");
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        private async Task PrintPdf()
        {
            if (_web.CoreWebView2 == null)
            {
                OpenInDefaultApp();
                return;
            }

            try
            {
                await _web.ExecuteScriptAsync("window.print();");
            }
            catch
            {
                OpenInDefaultApp();
            }
        }

        private void ReplacePdf()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Replace this PDF",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                CheckFileExists = true
            };
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                string name = Path.GetFileName(dialog.FileName);
                if (!string.IsNullOrWhiteSpace(_kind) && !string.IsNullOrWhiteSpace(_key))
                {
                    _path = Path.GetFullPath(DataFiles.SaveStoredPdf(_kind, _key, name, bytes));
                    if (OpenDocs.TryGetValue(_id, out var mapped) && mapped == this)
                        OpenDocs.Remove(_id);
                    _id = _path;
                    OpenDocs[_id] = this;
                }
                else
                {
                    File.WriteAllBytes(_path, bytes);
                }

                ApplyChrome(Path.GetFileNameWithoutExtension(_path));
                NavigatePdf();
                ToastAlert.Success(this, "PDF replaced.");
            }
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        private void EditSource()
        {
            if (_kind == DataFiles.PdfKindInvoice)
                Navigator.GoTo(AppPage.InvoicePdf);
            else if (_kind == DataFiles.PdfKindSalesOrder)
                Navigator.GoTo(AppPage.SalesOrder);
        }

        private void OpenInDefaultApp()
        {
            if (!File.Exists(_path))
                return;

            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = _path,
                UseShellExecute = true
            });
        }
    }
}
