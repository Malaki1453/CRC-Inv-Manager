using System.Net;
using System.Text;
using System.Text.Json;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

/// <summary>
/// Local HTTP snapshot of the running host so <c>--ui</c> can show a console dashboard
/// without attaching to the systemd process.
/// </summary>
internal sealed class StatusBoard : IDisposable
{
    /// <summary>Loopback-only port. Not the TLS client port.</summary>
    public const int Port = 7444;

    /// <summary>URL the <c>--ui</c> command polls.</summary>
    public const string Url = "http://127.0.0.1:7444/";

    private static readonly JsonSerializerOptions Json = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    private readonly InventoryStore _store;
    private readonly ConnectionHub _hub;
    private readonly string _fingerprint;
    private readonly string _listen;
    private readonly HttpListener _http = new();

    /// <summary>Serves JSON on 127.0.0.1:7444 while the TLS host is running.</summary>
    public StatusBoard(InventoryStore store, ConnectionHub hub, string fingerprint, string listen)
    {
        _store = store;
        _hub = hub;
        _fingerprint = fingerprint;
        _listen = listen;
        _http.Prefixes.Add(Url);
    }

    /// <summary>Accepts local status requests until <paramref name="cancel"/>.</summary>
    public async Task RunAsync(CancellationToken cancel)
    {
        try
        {
            _http.Start();
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("Status board (for --ui) did not bind " + Url + ": " + ex.Message);
            return;
        }

        try
        {
            while (!cancel.IsCancellationRequested && _http.IsListening)
            {
                HttpListenerContext ctx;
                try
                {
                    ctx = await _http.GetContextAsync().WaitAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (ObjectDisposedException)
                {
                    break;
                }
                catch (HttpListenerException)
                {
                    break;
                }

                _ = Task.Run(() => WriteSnapshot(ctx));
            }
        }
        finally
        {
            if (_http.IsListening)
                _http.Stop();
        }
    }

    /// <summary>One JSON snapshot of folders, fingerprint, IT, users, and old databases.</summary>
    public StatusSnapshot Snapshot()
    {
        var users = _hub.Snapshot();
        var oldFiles = new List<StatusOldFile>();
        foreach (string path in _store.ListOldDatabaseFiles())
        {
            var info = new FileInfo(path);
            oldFiles.Add(new StatusOldFile(
                info.Name,
                info.Exists ? info.Length : 0,
                info.Exists ? info.LastWriteTime.ToString("MM/dd/yy HH:mm") : ""));
        }

        return new StatusSnapshot(
            true,
            _listen,
            InventoryHost.DnsName + ":" + InventoryHost.Port,
            _fingerprint,
            _store.Folder,
            _store.LivePath,
            _store.ArchivePath,
            _store.HasItUser(),
            users.Select(u => new StatusUser(
                string.IsNullOrWhiteSpace(u.Username) ? "(signing in)" : u.Username,
                u.IsIt ? "IT" : u.IsAdmin ? "Admin" : "user",
                u.Remote,
                u.ConnectedAt.ToString("HH:mm:ss"))).ToList(),
            oldFiles);
    }

    /// <inheritdoc />
    public void Dispose()
    {
        try
        {
            if (_http.IsListening)
                _http.Stop();
        }
        catch (ObjectDisposedException)
        {
        }

        _http.Close();
    }

    /// <summary>Writes the snapshot as JSON. Failures on this socket must not stop TLS.</summary>
    private void WriteSnapshot(HttpListenerContext ctx)
    {
        try
        {
            byte[] body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(Snapshot(), Json));
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json; charset=utf-8";
            ctx.Response.ContentLength64 = body.Length;
            ctx.Response.OutputStream.Write(body, 0, body.Length);
        }
        catch (Exception)
        {
            // Viewer disconnected mid-write.
        }
        finally
        {
            try { ctx.Response.OutputStream.Close(); } catch (Exception) { }
        }
    }
}

/// <summary>JSON payload for <c>--ui</c>.</summary>
internal sealed record StatusSnapshot(
    bool Running,
    string Listen,
    string Clients,
    string Fingerprint,
    string Data,
    string Live,
    string Archive,
    bool HasIt,
    List<StatusUser> Users,
    List<StatusOldFile> OldFiles);

/// <summary>One connected TLS client in the dashboard.</summary>
internal sealed record StatusUser(string Username, string Role, string Remote, string ConnectedAt);

/// <summary>One dated archive file in the dashboard.</summary>
internal sealed record StatusOldFile(string Name, long Bytes, string Modified);
