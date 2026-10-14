using System.Net;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

/// <summary>
/// Start/stop the inventory host and list signed-in clients.
/// Double-click the exe or <c>dotnet run</c> with no flags opens this window.
/// </summary>
internal sealed class ServerForm : Form
{
    private static readonly Color Navy = Color.FromArgb(15, 42, 68);
    private static readonly Color Gold = Color.FromArgb(201, 154, 39);
    private static readonly Color Cream = Color.FromArgb(246, 242, 234);
    private static readonly Color Paper = Color.White;
    private static readonly Color Muted = Color.FromArgb(96, 110, 126);

    private readonly TextBox _data;
    private readonly TextBox _live;
    private readonly TextBox _archive;
    private readonly TextBox _port;
    private readonly TextBox _bind;
    private readonly TextBox _fingerprint;
    private readonly ListView _oldFiles;
    private readonly Label _status;
    private readonly Label _itStatus;
    private readonly Button _start;
    private readonly Button _stop;
    private readonly Button _bootstrap;
    private readonly ListView _users;
    private readonly ConnectionHub _hub = new();

    private CancellationTokenSource? _stopSource;
    private Task? _runTask;
    private bool _running;
    private bool _allowClose;

    /// <summary>Prefill from CLI <c>--data</c>, <c>--port</c>, and <c>--bind</c>.</summary>
    public ServerForm(string dataFolder, string bind, int port)
    {
        Text = "Cast Right Catch Inventory Server";
        StartPosition = FormStartPosition.CenterScreen;
        MinimumSize = new Size(720, 720);
        Size = new Size(780, 780);
        BackColor = Cream;
        Font = new Font("Segoe UI", 10f);
        FormClosing += OnFormClosing;
        try
        {
            string exe = Application.ExecutablePath;
            if (!string.IsNullOrEmpty(exe) && File.Exists(exe))
            {
                Icon? extracted = Icon.ExtractAssociatedIcon(exe);
                if (extracted != null)
                    Icon = extracted;
            }
        }
        catch (Exception)
        {
            // Missing icon is cosmetic; keep the default form icon.
        }

        var title = new Label
        {
            Text = "Inventory server",
            Font = new Font("Georgia", 18f, FontStyle.Bold),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(24, 18)
        };
        var hint = new Label
        {
            Text = "Start the host. Clients connect to " + InventoryHost.DnsName + ":" + InventoryHost.Port + " (no IP to type).",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(26, 52)
        };

        var settings = HostSettings.Load(dataFolder);
        _data = Field(24, 92, 520, "Server files folder");
        _data.Text = dataFolder;
        var browse = SmallButton("Browse", 556, 114);
        browse.Click += (_, _) => BrowseData();

        _live = Field(24, 156, 520, "Current database folder");
        _live.Text = string.IsNullOrWhiteSpace(settings.LiveFolder) ? dataFolder : settings.LiveFolder;
        var browseLive = SmallButton("Browse", 556, 178);
        browseLive.Click += (_, _) => BrowseFolder(_live, "Folder for crc_inventory.db (current data)");

        _archive = Field(24, 220, 520, "Old database folder");
        _archive.Text = string.IsNullOrWhiteSpace(settings.ArchiveFolder) ? dataFolder : settings.ArchiveFolder;
        var browseOld = SmallButton("Browse", 556, 242);
        browseOld.Click += (_, _) => BrowseFolder(_archive, "Folder for dated old_inventory files");

        _port = Field(24, 284, 120, "Port");
        _port.Text = port.ToString();
        _bind = Field(160, 284, 200, "Listen address");
        _bind.Text = bind;

        _start = GoldButton("Start server", 24, 348);
        _start.Click += async (_, _) => await StartServer();
        _stop = OutlineButton("Stop", 180, 348);
        _stop.Enabled = false;
        _stop.Click += async (_, _) => await StopServer();
        _bootstrap = OutlineButton("Create IT user", 280, 348);
        _bootstrap.Click += (_, _) => CreateItUser();

        _status = new Label
        {
            Text = "Stopped",
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(24, 394)
        };
        _itStatus = new Label
        {
            Text = "",
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(24, 418)
        };

        _fingerprint = Field(24, 442, 620, "Certificate fingerprint (clients trust this pin)");
        _fingerprint.ReadOnly = true;
        var copy = SmallButton("Copy", 656, 464);
        copy.Click += (_, _) =>
        {
            // Empty fingerprint: nothing to put on the clipboard.
            if (_fingerprint.Text.Length == 0)
                return;
            Clipboard.SetText(_fingerprint.Text);
        };

        var usersLabel = new Label
        {
            Text = "Connected users",
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(24, 518)
        };
        _users = new ListView
        {
            Location = new Point(24, 542),
            Size = new Size(710, 80),
            Anchor = AnchorStyles.Top | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Paper
        };
        _users.Columns.Add("User", 160);
        _users.Columns.Add("Role", 90);
        _users.Columns.Add("Address", 220);
        _users.Columns.Add("Connected", 200);

        var oldLabel = new Label
        {
            Text = "Old database files (named by dates so they sort when copied off this PC)",
            Font = new Font("Segoe UI Semibold", 10f),
            ForeColor = Navy,
            AutoSize = true,
            Location = new Point(24, 632)
        };
        _oldFiles = new ListView
        {
            Location = new Point(24, 656),
            Size = new Size(710, 80),
            Anchor = AnchorStyles.Top | AnchorStyles.Bottom | AnchorStyles.Left | AnchorStyles.Right,
            View = View.Details,
            FullRowSelect = true,
            GridLines = true,
            BackColor = Paper
        };
        _oldFiles.Columns.Add("File", 280);
        _oldFiles.Columns.Add("Dates", 180);
        _oldFiles.Columns.Add("Size", 90);
        _oldFiles.Columns.Add("Modified", 150);

        Controls.Add(title);
        Controls.Add(hint);
        Controls.Add(_data);
        Controls.Add(browse);
        Controls.Add(_live);
        Controls.Add(browseLive);
        Controls.Add(_archive);
        Controls.Add(browseOld);
        Controls.Add(_port);
        Controls.Add(_bind);
        Controls.Add(_start);
        Controls.Add(_stop);
        Controls.Add(_bootstrap);
        Controls.Add(_status);
        Controls.Add(_itStatus);
        Controls.Add(_fingerprint);
        Controls.Add(copy);
        Controls.Add(usersLabel);
        Controls.Add(_users);
        Controls.Add(oldLabel);
        Controls.Add(_oldFiles);

        _hub.Changed += () =>
        {
            // ConnectionHub fires from the accept thread; the list lives on the UI thread.
            if (IsHandleCreated)
                BeginInvoke(RefreshUsers);
        };

        LoadFingerprint();
        RefreshItStatus();
        Shown += (_, _) => LayoutLists();
        Resize += (_, _) => LayoutLists();
        RefreshOldFiles();
    }

    private void LayoutLists()
    {
        int width = Math.Max(400, ClientSize.Width - 48);
        _users.Width = width;
        _oldFiles.Width = width;
        int leftover = ClientSize.Height - _oldFiles.Top - 24;
        _oldFiles.Height = Math.Max(60, leftover);
    }

    private async Task StartServer()
    {
        // Already listening; Start would bind the port twice.
        if (_running)
            return;
        if (!int.TryParse(_port.Text.Trim(), out int port) || port <= 0 || port > 65535)
        {
            MessageBox.Show(this, "Port must be between 1 and 65535.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        string data = Path.GetFullPath(_data.Text.Trim());
        Directory.CreateDirectory(data);
        string bind = _bind.Text.Trim();
        if (bind.Length == 0)
            bind = IPAddress.Any.ToString();
        string live = Path.GetFullPath(_live.Text.Trim());
        string archive = Path.GetFullPath(_archive.Text.Trim());
        Directory.CreateDirectory(live);
        Directory.CreateDirectory(archive);
        new HostSettings { LiveFolder = live, ArchiveFolder = archive }.Save(data);

        _stopSource = new CancellationTokenSource();
        var cancel = _stopSource.Token;
        _running = true;
        SetRunningUi(true);
        _status.Text = "Starting…";
        LoadFingerprint();
        RefreshItStatus();

        _runTask = Task.Run(async () =>
        {
            try
            {
                await ServerHost.RunAsync(data, bind, port, _hub, Log, cancel, live, archive).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                // Stop button / window close.
            }
            catch (Exception ex)
            {
                BeginInvoke(() =>
                {
                    _status.Text = "Failed to start.";
                    MessageBox.Show(this, ex.Message, Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
                });
            }
            finally
            {
                BeginInvoke(() =>
                {
                    _running = false;
                    SetRunningUi(false);
                    // Keep a failure message if Start already set one.
                    if (_status.Text.StartsWith("Listening", StringComparison.Ordinal) ||
                        _status.Text.StartsWith("Starting", StringComparison.Ordinal))
                        _status.Text = "Stopped";
                    RefreshUsers();
                });
            }
        }, cancel);
        await Task.Yield();
    }

    private async Task StopServer()
    {
        // Not running; Stop has nothing to cancel.
        if (!_running || _stopSource == null)
            return;
        _status.Text = "Stopping…";
        _stopSource.Cancel();
        if (_runTask != null)
        {
            try { await _runTask.ConfigureAwait(true); }
            catch (OperationCanceledException) { }
        }
    }

    private void CreateItUser()
    {
        // The running host already opened the store; bootstrap against the same folder while stopped.
        if (_running)
        {
            MessageBox.Show(this, "Stop the server before creating the first IT user.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            return;
        }

        string data = Path.GetFullPath(_data.Text.Trim());
        Directory.CreateDirectory(data);
        var store = new InventoryStore(data, _live.Text.Trim(), _archive.Text.Trim());
        // Bootstrap is one-time; a second IT user is added from the desktop app.
        if (store.HasItUser())
        {
            MessageBox.Show(this, "An IT user already exists. Add more people from the desktop app.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
            RefreshItStatus();
            return;
        }

        using var dialog = new ItBootstrapForm();
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;

        if (!store.InsertAccount(dialog.Username, dialog.Username, dialog.Password, email: "", isAdmin: true, isIt: true, mustChange: false))
        {
            MessageBox.Show(this, "Could not create that user.", Text, MessageBoxButtons.OK, MessageBoxIcon.Error);
            return;
        }

        MessageBox.Show(this, "Created IT administrator '" + dialog.Username + "'.", Text, MessageBoxButtons.OK, MessageBoxIcon.Information);
        RefreshItStatus();
    }

    private void BrowseData()
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = "Folder for SQLite files, admins.json, and the TLS certificate",
            SelectedPath = Directory.Exists(_data.Text) ? _data.Text : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        _data.Text = dialog.SelectedPath;
        var settings = HostSettings.Load(dialog.SelectedPath);
        if (!string.IsNullOrWhiteSpace(settings.LiveFolder))
            _live.Text = settings.LiveFolder;
        if (!string.IsNullOrWhiteSpace(settings.ArchiveFolder))
            _archive.Text = settings.ArchiveFolder;
        LoadFingerprint();
        RefreshItStatus();
        RefreshOldFiles();
    }

    private void BrowseFolder(TextBox box, string description)
    {
        using var dialog = new FolderBrowserDialog
        {
            Description = description,
            SelectedPath = Directory.Exists(box.Text) ? box.Text : ""
        };
        if (dialog.ShowDialog(this) != DialogResult.OK)
            return;
        box.Text = dialog.SelectedPath;
        RefreshOldFiles();
    }

    private void RefreshOldFiles()
    {
        _oldFiles.Items.Clear();
        string folder = _archive.Text.Trim();
        if (!Directory.Exists(folder))
            return;
        foreach (var path in Directory.GetFiles(folder, "old_inventory*.db").OrderByDescending(File.GetLastWriteTimeUtc))
        {
            var info = new FileInfo(path);
            var item = new ListViewItem(info.Name);
            item.SubItems.Add(DatesFromOldFileName(info.Name));
            item.SubItems.Add((info.Length / 1024).ToString("N0") + " KB");
            item.SubItems.Add(info.LastWriteTime.ToString("g"));
            _oldFiles.Items.Add(item);
        }
    }

    /// <summary>Read MM yyyy - MM yyyy from old_inventory 06 2025 - 08 2025.db.</summary>
    private static string DatesFromOldFileName(string name)
    {
        const string prefix = "old_inventory ";
        const string suffix = ".db";
        if (name.Equals(Schema.ArchiveFileName, StringComparison.OrdinalIgnoreCase))
            return "(not stamped yet)";
        if (!name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) ||
            !name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
            return "—";
        string middle = name.Substring(prefix.Length, name.Length - prefix.Length - suffix.Length).Trim();
        return middle.Length > 0 ? middle : "—";
    }

    private void LoadFingerprint()
    {
        try
        {
            string data = Path.GetFullPath(_data.Text.Trim());
            Directory.CreateDirectory(data);
            using var cert = ServerCert.LoadOrCreate(data, out string fingerprint);
            _fingerprint.Text = fingerprint;
        }
        catch (Exception ex)
        {
            _fingerprint.Text = "";
            _status.Text = ex.Message;
        }
    }

    private void RefreshItStatus()
    {
        try
        {
            string data = Path.GetFullPath(_data.Text.Trim());
            Directory.CreateDirectory(data);
            var store = new InventoryStore(data, _live.Text.Trim(), _archive.Text.Trim());
            _itStatus.Text = store.HasItUser()
                ? "IT user is set up. Sign in from the desktop app."
                : "No IT user yet. Click Create IT user before clients can sign in.";
            _bootstrap.Enabled = !_running && !store.HasItUser();
        }
        catch
        {
            _itStatus.Text = "";
        }
    }

    private void RefreshUsers()
    {
        var rows = _hub.Snapshot();
        _users.BeginUpdate();
        _users.Items.Clear();
        foreach (var row in rows)
        {
            string name = row.Username.Length > 0 ? row.Username : "(not signed in)";
            string role = row.Username.Length == 0
                ? "—"
                : row.IsIt ? "IT" : row.IsAdmin ? "Admin" : "User";
            var item = new ListViewItem(name);
            item.SubItems.Add(role);
            item.SubItems.Add(row.Remote);
            item.SubItems.Add(row.ConnectedAt.ToLocalTime().ToString("g"));
            _users.Items.Add(item);
        }

        _users.EndUpdate();
        _users.Columns[0].Text = "User (" + rows.Count + ")";
    }

    private void Log(string line)
    {
        if (!IsHandleCreated)
            return;
        BeginInvoke(() =>
        {
            // RunAsync reports listen/fingerprint on start.
            if (line.StartsWith("listen", StringComparison.OrdinalIgnoreCase) ||
                line.StartsWith("Listening", StringComparison.OrdinalIgnoreCase))
                _status.Text = line;
            else if (_running && _status.Text.StartsWith("Starting", StringComparison.Ordinal))
                _status.Text = "Listening";
        });
    }

    private void SetRunningUi(bool running)
    {
        _start.Enabled = !running;
        _stop.Enabled = running;
        _data.Enabled = !running;
        _live.Enabled = !running;
        _archive.Enabled = !running;
        _port.Enabled = !running;
        _bind.Enabled = !running;
        RefreshItStatus();
        RefreshOldFiles();
    }

    private async void OnFormClosing(object? sender, FormClosingEventArgs e)
    {
        // Stop already finished, or the user closed a stopped window.
        if (_allowClose || !_running)
            return;
        e.Cancel = true;
        await StopServer();
        _allowClose = true;
        Close();
    }

    private static TextBox Field(int x, int y, int width, string caption)
    {
        // Caption is painted as a sibling label; this helper is used from the constructor.
        return new TextBox
        {
            Location = new Point(x, y + 22),
            Width = width,
            BorderStyle = BorderStyle.FixedSingle
        };
    }

    protected override void OnLoad(EventArgs e)
    {
        base.OnLoad(e);
        AddCaption(_data, "Server files (certificate, admins)");
        AddCaption(_live, "Current database folder (crc_inventory.db)");
        AddCaption(_archive, "Old database folder (files named by dates)");
        AddCaption(_port, "Port");
        AddCaption(_bind, "Listen address");
        AddCaption(_fingerprint, "Certificate fingerprint (clients trust this pin)");
    }

    private void AddCaption(Control field, string text)
    {
        var label = new Label
        {
            Text = text,
            ForeColor = Muted,
            AutoSize = true,
            Location = new Point(field.Left, field.Top - 20)
        };
        Controls.Add(label);
        label.BringToFront();
    }

    private Button GoldButton(string text, int x, int y)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(140, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Gold,
            ForeColor = Navy,
            Font = new Font("Segoe UI Semibold", 10f)
        };
        button.FlatAppearance.BorderSize = 0;
        return button;
    }

    private Button OutlineButton(string text, int x, int y)
    {
        var button = new Button
        {
            Text = text,
            Location = new Point(x, y),
            Size = new Size(140, 36),
            FlatStyle = FlatStyle.Flat,
            BackColor = Paper,
            ForeColor = Navy
        };
        button.FlatAppearance.BorderColor = Navy;
        return button;
    }

    private Button SmallButton(string text, int x, int y)
    {
        var button = OutlineButton(text, x, y);
        button.Size = new Size(88, 28);
        return button;
    }
}

/// <summary>Username and password for the first IT administrator.</summary>
internal sealed class ItBootstrapForm : Form
{
    private readonly TextBox _user;
    private readonly TextBox _password;
    private readonly TextBox _confirm;

    public string Username => _user.Text.Trim();
    public string Password => _password.Text;

    public ItBootstrapForm()
    {
        Text = "Create IT user";
        FormBorderStyle = FormBorderStyle.FixedDialog;
        StartPosition = FormStartPosition.CenterParent;
        MinimizeBox = false;
        MaximizeBox = false;
        ClientSize = new Size(400, 250);
        Font = new Font("Segoe UI", 10f);

        var userLabel = new Label { Text = "Username", Location = new Point(20, 16), AutoSize = true };
        _user = new TextBox { Location = new Point(20, 38), Width = 350 };
        var passLabel = new Label { Text = "Password", Location = new Point(20, 74), AutoSize = true };
        _password = new TextBox { Location = new Point(20, 96), Width = 350, UseSystemPasswordChar = true };
        var confirmLabel = new Label { Text = "Confirm password", Location = new Point(20, 132), AutoSize = true };
        _confirm = new TextBox { Location = new Point(20, 154), Width = 350, UseSystemPasswordChar = true };

        var ok = new Button { Text = "Create", DialogResult = DialogResult.None, Location = new Point(186, 200), Size = new Size(90, 32) };
        var cancel = new Button { Text = "Cancel", DialogResult = DialogResult.Cancel, Location = new Point(282, 200), Size = new Size(90, 32) };
        ok.Click += (_, _) => TryAccept();
        AcceptButton = ok;
        CancelButton = cancel;

        Controls.AddRange(new Control[] { userLabel, _user, passLabel, _password, confirmLabel, _confirm, ok, cancel });
    }

    private void TryAccept()
    {
        if (Username.Length == 0)
        {
            MessageBox.Show(this, "Username is required.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (_password.Text != _confirm.Text)
        {
            MessageBox.Show(this, "Passwords do not match.", Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        if (!Passwords.MeetsPolicy(_password.Text, out string error))
        {
            MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
            return;
        }

        DialogResult = DialogResult.OK;
        Close();
    }
}
