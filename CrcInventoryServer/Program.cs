using System.Net;
using CrcInventory.Server;

/// <summary>Command-line entry for the inventory TLS host, bootstrap, and fingerprint tools.</summary>
internal static class Program
{
    private const int DefaultPort = 7443;

    /// <summary>Parses flags and either prints help, bootstraps IT, prints the TLS pin, or runs the listener.</summary>
    public static async Task<int> Main(string[] args)
    {
        // Help is a tool, not a server start.
        if (HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintHelp();
            return 0;
        }

        string data = Arg(args, "--data") ?? Path.Combine(AppContext.BaseDirectory, "data");
        data = Path.GetFullPath(data);
        Directory.CreateDirectory(data);
        string? postgres = PostgresArg(args);

        // First IT user must be created on the host; clients cannot do it.
        if (HasFlag(args, "--bootstrap"))
            return Bootstrap(data, Arg(args, "--user"), postgres);

        // Operators need the pin to give clients without starting the listener.
        if (HasFlag(args, "--fingerprint"))
        {
            using var cert = ServerCert.LoadOrCreate(data, out string fingerprint);
            Console.WriteLine(fingerprint);
            return 0;
        }

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
            await ServerHost.RunAsync(data, bind, port, postgres, stop.Token);
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

    /// <summary>Creates the first IT administrator when none exists yet.</summary>
    private static int Bootstrap(string data, string? username, string? postgres)
    {
        var store = new InventoryStore(data, postgres);
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
        Console.WriteLine(
            store.UsesPostgres
                ? "  CrcInventoryServer --data \"" + store.Folder + "\" --postgres \"" + (postgres ?? "") + "\""
                : "  CrcInventoryServer --data \"" + store.Folder + "\"");
        return 0;
    }

    /// <summary>Writes usage, SQLite vs Postgres notes, and flag descriptions to stdout.</summary>
    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Cast Right Catch inventory server

            Default database is local SQLite (crc_inventory.db and old_inventory.db).
            Clients talk over a TLS named-op stream. They never receive the files.

            Postgres (Digital Ocean) is optional. Leave it unset while you test locally.
            When the managed database exists, pass the connection string:

              CrcInventoryServer --data FOLDER --postgres "Host=...;Port=25060;Database=defaultdb;Username=doadmin;Password=...;SSL Mode=Require"
              set CRC_POSTGRES=...   (or DATABASE_URL=postgresql://...)

            Digital Ocean: create a PostgreSQL cluster, copy the connection string,
            allow this server's IP, and use SSL. Live data is schema live; Old is schema archive.
            TLS cert and admins.json still live in --data.

            Usage:
              CrcInventoryServer [--data FOLDER] [--port 7443] [--bind 0.0.0.0]
              CrcInventoryServer --data FOLDER --bootstrap [--user NAME]
              CrcInventoryServer --data FOLDER --fingerprint
              CrcInventoryServer --data FOLDER --postgres CONNECTION

            --data         Folder for SQLite files (default), admins.json, and crc-server.pfx
                           Default: ./data next to this executable
            --postgres     Postgres connection string or URI. Omit to keep local SQLite.
            --port         Listen port (default 7443)
            --bind         Listen address (default 0.0.0.0)
            --bootstrap    Create the first IT administrator
            --fingerprint  Print the TLS certificate SHA-256 pin
            --help         This text

            The first IT user must be created on the host. Clients cannot create it.
            Give each client the host, port, and fingerprint.
            """);
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

    /// <summary>Postgres connection from --postgres, else CRC_POSTGRES, else DATABASE_URL.</summary>
    private static string? PostgresArg(string[] args)
    {
        string? value = Arg(args, "--postgres");
        // Explicit flag wins over environment.
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
        value = Environment.GetEnvironmentVariable("CRC_POSTGRES");
        // Project-specific env var next.
        if (!string.IsNullOrWhiteSpace(value))
            return value.Trim();
        value = Environment.GetEnvironmentVariable("DATABASE_URL");
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

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
