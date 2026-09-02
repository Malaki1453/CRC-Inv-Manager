using System.Net;
using CrcInventory.Server;

internal static class Program
{
    private const int DefaultPort = 7443;

    public static async Task<int> Main(string[] args)
    {
        if (HasFlag(args, "--help") || HasFlag(args, "-h"))
        {
            PrintHelp();
            return 0;
        }

        string data = Arg(args, "--data") ?? Path.Combine(AppContext.BaseDirectory, "data");
        data = Path.GetFullPath(data);
        Directory.CreateDirectory(data);

        if (HasFlag(args, "--bootstrap"))
            return Bootstrap(data, Arg(args, "--user"));

        if (HasFlag(args, "--fingerprint"))
        {
            using var cert = ServerCert.LoadOrCreate(data, out string fingerprint);
            Console.WriteLine(fingerprint);
            return 0;
        }

        string bind = Arg(args, "--bind") ?? IPAddress.Any.ToString();
        int port = DefaultPort;
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

        try
        {
            await ServerHost.RunAsync(data, bind, port, stop.Token);
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }

    private static int Bootstrap(string data, string? username)
    {
        var store = new InventoryStore(data);
        if (store.HasItUser())
        {
            Console.WriteLine("An IT user already exists. Nothing to do.");
            return 0;
        }

        username = (username ?? ReadLine("IT username: ") ?? "").Trim();
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
        if (password != confirm)
        {
            Console.Error.WriteLine("Passwords do not match.");
            return 1;
        }

        if (!Passwords.MeetsPolicy(password, out string error))
        {
            Console.Error.WriteLine(error);
            return 1;
        }

        if (!store.InsertAccount(username, username, password, email: "", isAdmin: true, isIt: true, mustChange: false))
        {
            Console.Error.WriteLine("Could not create that user.");
            return 1;
        }

        Console.WriteLine("Created IT administrator '" + username + "'.");
        Console.WriteLine("Database folder: " + store.Folder);
        Console.WriteLine("Start the server with:");
        Console.WriteLine("  CrcInventoryServer --data \"" + store.Folder + "\"");
        return 0;
    }

    private static void PrintHelp()
    {
        Console.WriteLine(
            """
            Cast Right Catch inventory server

            Hosts crc_inventory.db and old_inventory.db on this PC. Clients talk over
            a TLS named-op stream. They never receive the database files.

            Usage:
              CrcInventoryServer [--data FOLDER] [--port 7443] [--bind 0.0.0.0]
              CrcInventoryServer --data FOLDER --bootstrap [--user NAME]
              CrcInventoryServer --data FOLDER --fingerprint

            --data         Folder for databases, admins.json, and crc-server.pfx
                           Default: ./data next to this executable
            --port         Listen port (default 7443)
            --bind         Listen address (default 0.0.0.0)
            --bootstrap    Create the first IT administrator on this machine
            --fingerprint  Print the TLS certificate SHA-256 pin
            --help         This text

            The first IT user must be created on the host. Clients cannot create it.
            Give each client the host, port, and fingerprint.
            """);
    }

    private static bool HasFlag(string[] args, string name) =>
        args.Any(arg => arg.Equals(name, StringComparison.OrdinalIgnoreCase));

    private static string? Arg(string[] args, string name)
    {
        for (int i = 0; i < args.Length; i++)
        {
            if (!args[i].Equals(name, StringComparison.OrdinalIgnoreCase))
                continue;
            if (i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal))
                return args[i + 1];
            return "";
        }

        return null;
    }

    private static string? ReadLine(string prompt)
    {
        Console.Write(prompt);
        return Console.ReadLine();
    }

    private static string ReadSecret(string prompt)
    {
        Console.Write(prompt);
        var chars = new List<char>();
        while (true)
        {
            var key = Console.ReadKey(intercept: true);
            if (key.Key == ConsoleKey.Enter)
            {
                Console.WriteLine();
                break;
            }

            if (key.Key == ConsoleKey.Backspace)
            {
                if (chars.Count > 0)
                    chars.RemoveAt(chars.Count - 1);
                continue;
            }

            if (!char.IsControl(key.KeyChar))
                chars.Add(key.KeyChar);
        }

        return new string(chars.ToArray());
    }
}
