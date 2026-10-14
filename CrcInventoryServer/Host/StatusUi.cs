using System.Net.Http;
using System.Text.Json;

namespace CrcInventory.Server;

/// <summary>Console dashboard for a running host. Does not start or stop the service.</summary>
internal static class StatusUi
{
    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        PropertyNameCaseInsensitive = true
    };

    /// <summary>Polls 127.0.0.1:7444 and redraws until Ctrl+C.</summary>
    public static async Task<int> RunAsync(CancellationToken cancel)
    {
        using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(3) };
        using var timer = new PeriodicTimer(TimeSpan.FromSeconds(2));

        Draw(await TryRead(http, cancel).ConfigureAwait(false));
        try
        {
            while (await timer.WaitForNextTickAsync(cancel).ConfigureAwait(false))
                Draw(await TryRead(http, cancel).ConfigureAwait(false));
        }
        catch (OperationCanceledException)
        {
        }

        return 0;
    }

    /// <summary>GET the snapshot, or null when the service is down.</summary>
    private static async Task<StatusSnapshot?> TryRead(HttpClient http, CancellationToken cancel)
    {
        try
        {
            string body = await http.GetStringAsync(new Uri(StatusBoard.Url), cancel).ConfigureAwait(false);
            return JsonSerializer.Deserialize<StatusSnapshot>(body, Json);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>Full-screen console view of the snapshot.</summary>
    private static void Draw(StatusSnapshot? snap)
    {
        try { Console.Clear(); } catch (IOException) { }

        Console.WriteLine("================================================");
        Console.WriteLine(" Cast Right Catch Inventory Server");
        if (snap == null)
        {
            Console.WriteLine(" NOT RUNNING");
            Console.WriteLine("================================================");
            Console.WriteLine();
            Console.WriteLine(" The service is not up, so there is nothing to show.");
            Console.WriteLine(" Start it with:");
            Console.WriteLine("   sudo serverStart");
            Console.WriteLine(" Stop it with:");
            Console.WriteLine("   sudo serverStop");
            Console.WriteLine(" Restart it with:");
            Console.WriteLine("   sudo serverRestart");
            Console.WriteLine(" Update from GitHub with:");
            Console.WriteLine("   sudo serverUpdate");
            Console.WriteLine(" List commands with:");
            Console.WriteLine("   sudo serverHelp");
            Console.WriteLine();
            Console.WriteLine(" Ctrl+C leaves this view.");
            return;
        }

        Console.WriteLine(" RUNNING  |  " + snap.Clients);
        Console.WriteLine("================================================");
        Console.WriteLine(" Listen       " + snap.Listen);
        Console.WriteLine(" Data         " + snap.Data);
        Console.WriteLine(" Current db   " + snap.Live);
        Console.WriteLine(" Old db       " + snap.Archive);
        Console.WriteLine(" IT user      " + (snap.HasIt ? "yes" : "no — create one with --bootstrap"));
        Console.WriteLine(" Fingerprint");
        Console.WriteLine("   " + snap.Fingerprint);
        Console.WriteLine("------------------------------------------------");
        Console.WriteLine(" Connected users (" + snap.Users.Count + ")");
        if (snap.Users.Count == 0)
            Console.WriteLine("   (none)");
        else
        {
            foreach (var user in snap.Users)
                Console.WriteLine("   " + Pad(user.Username, 16) + " " + Pad(user.Role, 6) + " " + Pad(user.Remote, 22) + " " + user.ConnectedAt);
        }

        Console.WriteLine("------------------------------------------------");
        Console.WriteLine(" Old databases (" + snap.OldFiles.Count + ")");
        if (snap.OldFiles.Count == 0)
            Console.WriteLine("   (none)");
        else
        {
            foreach (var file in snap.OldFiles)
                Console.WriteLine("   " + file.Name + "   " + file.Modified);
        }

        Console.WriteLine("------------------------------------------------");
        Console.WriteLine(" Refreshing every 2s. Ctrl+C leaves this view (service keeps running).");
    }

    /// <summary>Left-align <paramref name="text"/> in <paramref name="width"/> characters.</summary>
    private static string Pad(string text, int width)
    {
        text ??= "";
        if (text.Length >= width)
            return text[..width];
        return text.PadRight(width);
    }
}
