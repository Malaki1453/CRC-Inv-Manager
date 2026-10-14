using System.Net;
#if WINDOWS
using System.Runtime.InteropServices;
#endif
using CrcInventory.Server;

/// <summary>Command-line entry for the inventory TLS host, bootstrap, and fingerprint tools.</summary>
internal static class Program
{
    private const int DefaultPort = 7443;

    /// <summary>
    /// Windows GUI build with no flags: open the start/stop window.
    /// Console / Linux, or --help / --bootstrap / --fingerprint / --console: prompt tools and listen.
    /// </summary>
#if WINDOWS
    [STAThread]
#endif
    public static int Main(string[] args)
    {
#if WINDOWS
        bool cli = HasFlag(args, "--help") || HasFlag(args, "-h") ||
            HasFlag(args, "--bootstrap") || HasFlag(args, "--fingerprint") ||
            HasFlag(args, "--console") || HasFlag(args, "--ui") ||
            HasFlag(args, "--version") || HasFlag(args, "-v");

        // Help, bootstrap, fingerprint, and headless listen stay on the console.
        if (cli)
        {
            // WinExe has no console of its own; attach the parent terminal so these flags can print.
            EnsureConsole();
            return RunCli(args).GetAwaiter().GetResult();
        }

        string data = Arg(args, "--data") ?? DefaultDataFolder();
        data = Path.GetFullPath(data);
        Directory.CreateDirectory(data);
        string bind = Arg(args, "--bind") ?? IPAddress.Any.ToString();
        int port = DefaultPort;
        if (Arg(args, "--port") is string portText &&
            int.TryParse(portText, out int parsed) && parsed > 0 && parsed <= 65535)
            port = parsed;

        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);
        Application.SetHighDpiMode(HighDpiMode.SystemAware);
        Application.Run(new ServerForm(data, bind, port));
        return 0;
#else
        // Linux / console build: no flags starts the TLS listener.
        return RunCli(args).GetAwaiter().GetResult();
#endif
    }

    /// <summary>
    /// Prefer a repo <c>server-data</c> folder when this exe lives under the git tree.
    /// Walks up from the working directory and from the exe so publish, bin, and a copied binary still match.
    /// Otherwise <c>data</c> next to the executable (a host machine with no repo).
    /// </summary>
    private static string DefaultDataFolder()
    {
        foreach (string start in new[] { Directory.GetCurrentDirectory(), AppContext.BaseDirectory })
        {
            string? dir = Path.GetFullPath(start);
            for (int i = 0; i < 8 && !string.IsNullOrEmpty(dir); i++)
            {
                string candidate = Path.Combine(dir, "server-data");
                if (Directory.Exists(candidate))
                    return candidate;
                dir = Directory.GetParent(dir)?.FullName;
            }
        }

        return Path.Combine(AppContext.BaseDirectory, "data");
    }

#if WINDOWS
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AttachConsole(int dwProcessId);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool AllocConsole();

    /// <summary>Attach the calling terminal, or open a new one, so WinExe CLI flags can read and write.</summary>
    private static void EnsureConsole()
    {
        const int attachParentProcess = -1;
        if (!AttachConsole(attachParentProcess))
            AllocConsole();
        try
        {
            Console.SetOut(new StreamWriter(Console.OpenStandardOutput()) { AutoFlush = true });
            Console.SetError(new StreamWriter(Console.OpenStandardError()) { AutoFlush = true });
            Console.SetIn(new StreamReader(Console.OpenStandardInput()));
        }
        catch (Exception)
        {
            // dotnet run already owns these streams; keep them.
        }
    }
#endif

    /// <summary>Parses flags and either prints help, bootstraps IT, prints the TLS pin, or runs the listener.</summary>
    private static async Task<int> RunCli(string[] args)
    {
        // Help is a tool, not a server start.
        if (HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintHelp();
            return 0;
        }

        if (HasFlag(args, "--version") || HasFlag(args, "-v"))
        {
            Console.WriteLine(ServerVersion());
            return 0;
        }

        string data = Arg(args, "--data") ?? DefaultDataFolder();
        data = Path.GetFullPath(data);
        Directory.CreateDirectory(data);
        (string? live, string? archive) = ResolveFolders(data, args);

        // First IT user must be created on the host; clients cannot do it.
        if (HasFlag(args, "--bootstrap"))
            return Bootstrap(data, Arg(args, "--user"), live, archive);

        // Operators need the pin to give clients without starting the listener.
        if (HasFlag(args, "--fingerprint"))
        {
            using var cert = ServerCert.LoadOrCreate(data, out string fingerprint);
            Console.WriteLine(fingerprint);
            return 0;
        }

        // Console dashboard against a running service; does not start or stop TLS.
        if (HasFlag(args, "--ui"))
            return await ShowUi();

        string bind = Arg(args, "--bind") ?? IPAddress.Any.ToString();
        int port = DefaultPort;
        // A bad --port would bind the wrong service or throw later with a poorer message.
        if (Arg(args, "--port") is string portText &&
            (!int.TryParse(portText, out port) || port <= 0 || port > 65535))
        {
            Console.Error.WriteLine("Port must be between 1 and 65535.");
            return 1;
        }

        using var stop = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            stop.Cancel();
        };

        // Run until Ctrl+C or a startup failure.
        try
        {
            await ServerHost.RunAsync(data, bind, port, stop.Token, live, archive);
            return 0;
        }
        // Ctrl+C is a clean shutdown.
        catch (OperationCanceledException)
        {
            return 0;
        }
        // Bind failures and startup errors go to stderr with a non-zero exit.
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    /// <summary>
    /// --live / --archive override host-settings.json; missing flags use the saved folders;
    /// missing settings keep both databases in --data.
    /// </summary>
    private static (string? live, string? archive) ResolveFolders(string data, string[] args)
    {
        var settings = HostSettings.Load(data);
        string? live = FolderArg(args, "--live") ?? BlankToNull(settings.LiveFolder);
        string? archive = FolderArg(args, "--archive") ?? BlankToNull(settings.ArchiveFolder);
        return (live, archive);
    }

    /// <summary>Opens the console dashboard until Ctrl+C. The systemd service keeps running.</summary>
    private static async Task<int> ShowUi()
    {
        using var view = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) =>
        {
            e.Cancel = true;
            view.Cancel();
        };
        return await StatusUi.RunAsync(view.Token);
    }

    /// <summary>Creates the first IT administrator when none exists yet.</summary>
    private static int Bootstrap(string data, string? username, string? live, string? archive)
    {
        var store = new InventoryStore(data, live, archive);
        // Bootstrap is a one-time host action; do not add a second IT user this way.
        if (store.HasItUser())
        {
            Console.WriteLine("An IT user already exists. Nothing to do.");
            return 0;
        }

        username = (username ?? ReadLine("IT username: ") ?? "").Trim();
        // An empty name cannot be a sign-in identity.
        if (username.Length == 0)
        {
            Console.Error.WriteLine("Username is required.");
            return 1;
        }

        string password = Environment.GetEnvironmentVariable("CRC_BOOTSTRAP_PASSWORD")
            ?? ReadSecret("Password: ");
        string confirm = Environment.GetEnvironmentVariable("CRC_BOOTSTRAP_PASSWORD") != null
            ? password
            : ReadSecret("Confirm password: ");
        // Env-supplied passwords skip the confirm prompt; typed ones must match.
        if (password != confirm)
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 1;
        }

        // Shared policy so the new IT user can sign in from the desktop app.
        if (!Passwords.MeetsPolicy(password, out string error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        // Duplicate or other insert failure is reported instead of starting a half-set-up host.
        if (!store.InsertAccount(username, username, password, email: "", isAdmin: true, isIt: true, mustChange: false))
        {
            Console.Error.WriteLine("Could not create that user.");
            return 1;
        }

        Console.WriteLine("Created IT administrator '" + username + "'.");
        Console.WriteLine("Database: " + store.EngineName);
        Console.WriteLine("Data folder: " + store.Folder);
        Console.WriteLine("Start the server with:");
        Console.WriteLine("  CrcInventoryServer --data \"" + store.Folder + "\"");
        return 0;
    }

    /// <summary>Writes usage and flag descriptions to stdout.</summary>
    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Cast Right Catch inventory server

            Database is local SQLite (crc_inventory.db and old_inventory.db) in --data.
            Clients talk over a TLS named-op stream. They never receive the files.
            TLS cert and admins.json live in --data.

            Usage:
              CrcInventoryServer
              CrcInventoryServer [--data FOLDER] [--live FOLDER] [--archive FOLDER] [--port 7443] [--bind 0.0.0.0]
              CrcInventoryServer --data FOLDER --bootstrap [--user NAME]
              CrcInventoryServer --data FOLDER --fingerprint
              CrcInventoryServer --ui
              CrcInventoryServer --version

            Windows GUI build with no flags opens the start/stop window.
            Linux / console build with no flags starts listening. Ctrl+C stops.

            Publish Linux (from the Windows repo):
              dotnet publish CrcInventoryServer -c Release -f net8.0 -p:PublishProfile=LinuxProfile

            On the host:
              ./CrcInventoryServer --data /var/lib/crc-inventory
              ./CrcInventoryServer --data /var/lib/crc-inventory --bootstrap --user NAME
              ./CrcInventoryServer --data /var/lib/crc-inventory --fingerprint
              sudo serverui
              sudo serverStart
              sudo serverStop
              sudo serverRestart
              sudo serverHelp
              sudo serverUpdate

            --data         Folder for admins.json, crc-server.pfx, crc.key, host-settings.json
                           Default: repo server-data when found, else ./data next to this binary
            --live         Folder for crc_inventory.db (overrides host-settings.json)
            --archive      Folder for dated old_inventory files (overrides host-settings.json)
            --port         Listen port (default 7443)
            --bind         Listen address (default 0.0.0.0)
            --console      Listen in this terminal (Windows GUI build; default on Linux)
            --ui           Console dashboard for a running host (service keeps running)
            --bootstrap    Create the first IT administrator
            --fingerprint  Print the TLS certificate SHA-256 pin
            --version      Print this build's version
            --help         This text

            The first IT user must be created on the host. Clients cannot create it.
            Give each client the host, port, and fingerprint.
            """);
    }

    /// <summary>Assembly informational version, or 0.0.0 when unset.</summary>
    private static string ServerVersion()
    {
        var assembly = typeof(Program).Assembly;
        string? info = assembly
            .GetCustomAttributes(typeof(System.Reflection.AssemblyInformationalVersionAttribute), false)
            .OfType<System.Reflection.AssemblyInformationalVersionAttribute>()
            .FirstOrDefault()
            ?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(info))
        {
            int plus = info.IndexOf('+');
            return plus > 0 ? info[..plus] : info;
        }

        return assembly.GetName().Version?.ToString() ?? "0.0.0";
    }

    /// <summary>True when <paramref name="name"/> appears as a case-insensitive flag.</summary>
    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));

    /// <summary>Value after <paramref name="name"/>, empty string if the flag has no value, or null if absent.</summary>
    private static string? Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            // Not this flag; keep scanning.
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            // Next token is the value unless it looks like another flag.
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return args[i + 1];
            // Flag was present but had no value (or the next token was another flag).
            return "";
        }

        return null;
    }

    /// <summary>Non-empty --live / --archive value, or null when the flag is absent or blank.</summary>
    private static string? FolderArg(string[] args, string name)
    {
        string? value = Arg(args, name);
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    /// <summary>Null when <paramref name="value"/> is missing or whitespace so callers fall through to --data.</summary>
    private static string? BlankToNull(string? value) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    /// <summary>Writes <paramref name="prompt"/> and reads a visible line from the console.</summary>
    private static string? ReadLine(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine();
    }

    /// <summary>Reads a password without echoing, supporting backspace.</summary>
    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true); // one typed keystroke, not echoed
            // Enter ends the secret.
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            // Backspace removes the last typed character when there is one.
            if (key.Key == ConsoleKey.Backspace)
            {
                // Do not underflow an empty buffer.
                if (chars.Count > 0)
                    chars.RemoveAt(chars.Count - 1);
                continue;
            }

            // Ignore other control keys so they are not stored in the password.
            if (!char.IsControl(key.KeyChar))
                chars.Add(key.KeyChar);
        }

        return new string(chars.ToArray());
    }
}
