using Microsoft.Web.WebView2.Core;
using Microsoft.Web.WebView2.WinForms;

namespace CastRightCatchInvManagement
{
    /// <summary>Dedicated PDF window (WebView2). Save, print, replace, or edit.</summary>
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

        /// <summary>Open the PDF, reusing an existing window for the same stored document.</summary>
        public static void ShowDocument(
            string path,
            string? title = null,
            string? kind = null,
            string? key = null)
        {
            // Temp files can vanish after a failed save; do not open an empty viewer.
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path))
            {
                MessageBox.Show(
                    "The PDF could not be found.",
                    "PDF",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information);
                return;
            }

            string id = DocumentId(path, kind, key);
            // One window per invoice/SO so Save/Replace does not fork copies.
            if (OpenDocs.TryGetValue(id, out var existing) &&
                existing != null &&
                !existing.IsDisposed)
            {
                existing._kind ??= kind;
                existing._key ??= key;
                existing._path = Path.GetFullPath(path);
                existing.ApplyChrome(title);
                existing.NavigatePdf();
                existing.RaiseAboveOthers();
                return;
            }

            var form = new PdfViewForm(path, title, kind, key);
            OpenDocs[form._id] = form;
            form.FormClosed += (_, _) =>
            {
                // Drop the map entry only if this instance still owns it.
                if (OpenDocs.TryGetValue(form._id, out var mapped) && mapped == form)
                    OpenDocs.Remove(form._id);
            };
            form.Show();
            form.RaiseAboveOthers();
        }

        /// <summary>Stable id: stored kind+key when present, otherwise the full file path.</summary>
        private static string DocumentId(string path, string? kind, string? key)
        {
            // Database-backed PDFs keep one window even if the temp path changes after Save.
            if (!string.IsNullOrWhiteSpace(kind) && !string.IsNullOrWhiteSpace(key))
                return kind.Trim() + ":" + key.Trim();
            return Path.GetFullPath(path);
        }

        /// <summary>Build the PDF chrome, toolbar, WebView2 host, and fallback panel.</summary>
        private PdfViewForm(string path, string? title, string? kind, string? key)
        {
            _path = Path.GetFullPath(path);
            _kind = kind;
            _key = string.IsNullOrWhiteSpace(key) ? null : key.Trim();
            _id = DocumentId(_path, _kind, _key);

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
            // Match other CRC windows when the brand icon is present.
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
            Shown += async (_, _) =>
            {
                RaiseAboveOthers();
                await InitViewer();
                // WebView init can dispose the form if the runtime is missing.
                if (!IsDisposed)
                    RaiseAboveOthers();
            };
        }

        /// <summary>
        /// Come to the front of every window for this first appearance, then drop TopMost
        /// so other CRC windows can be used normally.
        /// </summary>
        private void RaiseAboveOthers()
        {
            // Closed during init should not try to activate.
            if (IsDisposed)
                return;

            // A minimized viewer would stay in the taskbar after Show PDF.
            if (WindowState == FormWindowState.Minimized)
                WindowState = FormWindowState.Normal;

            TopMost = true;
            Show();
            BringToFront();
            Activate();
            TopMost = false;
        }

        /// <summary>Toolbar button with a fixed width so LayoutToolbar can pack them.</summary>
        private static Button ToolButton(string text, int width)
        {
            return new Button
            {
                Text = text,
                Size = new Size(width, 34),
                TabStop = true
            };
        }

        /// <summary>Pack visible toolbar buttons left to right after chrome changes.</summary>
        private void LayoutToolbar()
        {
            int x = 16;
            int y = 8;
            foreach (var button in new[] { _save, _saveAs, _print, _replace, _edit })
            {
                // Hidden actions (Save/Edit on ad-hoc files) should not leave a gap.
                if (!button.Visible)
                    continue;
                button.Location = new Point(x, y);
                x += button.Width + 8;
            }
        }

        /// <summary>Title, subtitle, and Edit/Save labels based on stored PDF kind.</summary>
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

            // Issued/received invoices open Create Invoice for edits.
            if (_kind == DataFiles.PdfKindInvoice)
            {
                _edit.Text = "Edit invoice";
                _edit.Visible = true;
                _save.Text = "Save to database";
                _subtitle.Text = stored
                    ? "Mark up in this window, then Save to database. Edit invoice opens Create Invoice."
                    : "Mark up, print, or replace this PDF.";
            }
            // Sales-order PDFs edit on Create Sales Order, not New Sale.
            else if (_kind == DataFiles.PdfKindSalesOrder)
            {
                _edit.Text = "Edit sales order";
                _edit.Visible = true;
                _save.Text = "Save to database";
                _subtitle.Text = stored
                    ? "Mark up in this window, then Save to database. Edit sales order opens Create Sales Order."
                    : "Mark up, print, or replace this PDF.";
            }
            // Both CRC purchase PDFs and stored vendor invoices edit on New Purchase.
            else if (_kind == DataFiles.PdfKindPurchase ||
                     _kind == DataFiles.PdfKindPurchaseInvoice)
            {
                _edit.Text = "Edit purchase";
                _edit.Visible = true;
                _save.Text = "Save to database";
                _subtitle.Text = stored
                    ? _kind == DataFiles.PdfKindPurchaseInvoice
                        ? "Vendor invoice stored with this PO. Mark up, then Save to database. Edit purchase opens New Purchase."
                        : "Mark up in this window, then Save to database. Edit purchase opens New Purchase."
                    : "Mark up, print, or replace this PDF.";
            }
            // Sale PDFs are per-PO product lines, edited as a sales order.
            else if (_kind == DataFiles.PdfKindSale)
            {
                _edit.Text = "Edit sale";
                _edit.Visible = true;
                _save.Text = "Save to database";
                _subtitle.Text = stored
                    ? "Mark up in this window, then Save to database. Edit sale opens New Sale."
                    : "Mark up, print, or replace this PDF.";
            }
            // Ad-hoc files have no source form to jump to.
            else
            {
                _edit.Visible = false;
                _save.Text = "Save to database";
                _subtitle.Text = "Mark up, print, or replace this PDF.";
            }

            LayoutToolbar();
        }

        /// <summary>Create the WebView2 environment and navigate to the PDF.</summary>
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
            // WebView2 runtime is optional; offer Open in default app instead of crashing.
            catch (Exception ex)
            {
                ShowFallback(
                    "This computer needs the Microsoft Edge WebView2 Runtime to show PDFs in the app.\n" +
                    ex.Message);
            }
        }

        /// <summary>Load the current file path into WebView2 once the core is ready.</summary>
        private void NavigatePdf()
        {
            // InitViewer has not finished yet; Shown will navigate after EnsureCoreWebView2.
            if (_web.CoreWebView2 == null)
                return;

            string uri = new Uri(_path).AbsoluteUri;
            _web.CoreWebView2.Navigate(uri);
        }

        /// <summary>Hide WebView2 and explain how to open the PDF outside the app.</summary>
        private void ShowFallback(string message)
        {
            _web.Visible = false;
            _fallback.Visible = true;
            _fallback.BringToFront();
            // The fallback label is looked up by name so markup can stay in the constructor.
            if (_fallback.Controls["fallbackText"] is Label label)
                label.Text = message;
            _print.Enabled = false;
        }

        /// <summary>Write the current file bytes back into the stored PDF for this kind/key.</summary>
        private void SaveToDatabase()
        {
            // Ad-hoc files are not in crc_inventory; use Save as instead.
            if (string.IsNullOrWhiteSpace(_kind) || string.IsNullOrWhiteSpace(_key))
                return;
            // Markup tools can leave a missing temp path after a failed replace.
            if (!File.Exists(_path))
            {
                ToastAlert.Error(this, "The PDF file is missing.");
                return;
            }

            try
            {
                byte[] bytes = File.ReadAllBytes(_path);
                _path = Path.GetFullPath(DataFiles.SaveStoredPdf(_kind, _key, Path.GetFileName(_path), bytes));
                NavigatePdf();
                ToastAlert.Success(this, "Saved to the database.");
            }
            // Disk/DB write failures should keep the viewer on the last good file.
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        /// <summary>Copy the PDF to a user-chosen path without changing the stored original.</summary>
        private void SaveAs()
        {
            using var dialog = new SaveFileDialog
            {
                Title = "Save PDF as",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                FileName = Path.GetFileName(_path),
                OverwritePrompt = true
            };
            // Cancel leaves the viewer unchanged.
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                File.Copy(_path, dialog.FileName, overwrite: true);
                ToastAlert.Success(this, "PDF saved.");
            }
            // Destination locked in Excel/Adobe is the usual failure.
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        /// <summary>Print via WebView2, or the default app when the runtime is missing.</summary>
        private async Task PrintPdf()
        {
            // Fallback path when InitViewer showed the Edge-runtime message.
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
                // Script print can fail on some runtimes; the OS viewer still prints.
                OpenInDefaultApp();
            }
        }

        /// <summary>Swap in another PDF file, storing it when this document is database-backed.</summary>
        private void ReplacePdf()
        {
            using var dialog = new OpenFileDialog
            {
                Title = "Replace this PDF",
                Filter = "PDF files (*.pdf)|*.pdf|All files (*.*)|*.*",
                CheckFileExists = true
            };
            // Cancel keeps the currently shown PDF.
            if (dialog.ShowDialog(this) != DialogResult.OK)
                return;

            try
            {
                byte[] bytes = File.ReadAllBytes(dialog.FileName);
                string name = Path.GetFileName(dialog.FileName);
                // Stored documents must update the database so other PCs see the replacement.
                if (!string.IsNullOrWhiteSpace(_kind) && !string.IsNullOrWhiteSpace(_key))
                {
                    _path = Path.GetFullPath(DataFiles.SaveStoredPdf(_kind, _key, name, bytes));
                }
                // Loose files are overwritten in place.
                else
                {
                    File.WriteAllBytes(_path, bytes);
                }

                ApplyChrome(Path.GetFileNameWithoutExtension(_path));
                NavigatePdf();
                ToastAlert.Success(this, "PDF replaced.");
            }
            // Leave the previous PDF on screen if the new file cannot be written.
            catch (Exception ex)
            {
                ToastAlert.Error(this, ex.Message);
            }
        }

        /// <summary>Jump to the form that created this stored PDF so they can change the data.</summary>
        private void EditSource()
        {
            // Invoice PDFs are edited on Create Invoice.
            if (_kind == DataFiles.PdfKindInvoice)
                Navigator.GoTo(AppPage.InvoicePdf);
            // Sales-order PDFs open Create Sales Order.
            else if (_kind == DataFiles.PdfKindSalesOrder)
                Navigator.GoTo(AppPage.SalesOrder);
            // Purchase and stored vendor-invoice PDFs open New Purchase.
            else if (_kind == DataFiles.PdfKindPurchase ||
                     _kind == DataFiles.PdfKindPurchaseInvoice)
                Navigator.GoTo(AppPage.AddPurchase);
            // Sale PDFs try to reopen the matching PO lines.
            else if (_kind == DataFiles.PdfKindSale)
            {
                var rows = DataFiles.FindSalesByPo(_key);
                // Prefer editing the existing sale lines when the PO is still live.
                if (rows.Count > 0)
                    SalesOrder.OpenEdit(rows[0]);
                // No live sale lines for that PO: open a blank Create Sales Order.
                else
                    Navigator.GoTo(AppPage.SalesOrder);
            }
        }

        /// <summary>Shell-open the file when WebView2 cannot display or print it.</summary>
        private void OpenInDefaultApp()
        {
            // Missing temp path would only flash a Windows error dialog.
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
