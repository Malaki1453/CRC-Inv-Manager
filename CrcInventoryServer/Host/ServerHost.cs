using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

/// <summary>TLS TCP listener that accepts framed named-ops and hands them to <see cref="ServerDispatch"/>.</summary>
internal static class ServerHost
{
    /// <summary>Binds <paramref name="bind"/>:<paramref name="port"/> and serves clients until <paramref name="cancel"/>.</summary>
    public static Task RunAsync(
        string dataFolder,
        string bind,
        int port,
        CancellationToken cancel,
        string? liveFolder = null,
        string? archiveFolder = null) =>
        RunAsync(dataFolder, bind, port, connections: null, log: null, cancel, liveFolder, archiveFolder);

    /// <summary>Same as the other RunAsync, with optional connected-user tracking and status lines for the GUI.</summary>
    public static async Task RunAsync(
        string dataFolder,
        string bind,
        int port,
        ConnectionHub? connections,
        Action<string>? log,
        CancellationToken cancel,
        string? liveFolder = null,
        string? archiveFolder = null)
    {
        var store = new InventoryStore(dataFolder, liveFolder, archiveFolder);
        using var cert = ServerCert.LoadOrCreate(dataFolder, out string fingerprint);
        var dispatch = new ServerDispatch(store, fingerprint);
        // Console / systemd has no ServerForm hub; still track sessions for --ui.
        connections ??= new ConnectionHub();

        // A non-IP bind string would throw later with a poorer message.
        if (!IPAddress.TryParse(bind, out var address))
            throw new InvalidOperationException("Invalid bind address: " + bind);

        var listener = new TcpListener(address, port);
        listener.Start();
        string listen = "Listening on " + address + ":" + port;
        WriteLine(log, "Cast Right Catch inventory server");
        WriteLine(log, "  data         " + store.Folder);
        WriteLine(log, "  database     " + store.EngineName);
        WriteLine(log, listen);
        WriteLine(log, "  fingerprint  " + fingerprint);
        WriteLine(log, "  first IT     " + (store.HasItUser() ? "yes" : "no — create an IT user first"));
        WriteLine(log, "Clients connect over TLS. SQLite files stay on this machine.");
        WriteLine(log, "Status board  " + StatusBoard.Url + "  (CrcInventoryServer --ui)");
        // Console-only hosts still print how to stop.
        if (log == null)
            Console.WriteLine("Press Ctrl+C to stop.");

        using var status = new StatusBoard(store, connections, fingerprint, listen);
        Task statusTask = status.RunAsync(cancel);
        Task bankSyncTask = BankSyncWatch.RunAsync(store, cancel);

        // Accept clients until the process is asked to stop.
        try
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client;
                // Accept throws when the cancel token fires while waiting.
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancel).ConfigureAwait(false);
                }
                // Ctrl+C / token cancel: leave the accept loop.
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cert, dispatch, connections, cancel), cancel);
            }
        }
        // Always release the listen port, including after Ctrl+C.
        finally
        {
            listener.Stop();
            try { await statusTask.ConfigureAwait(false); } catch (Exception) { }
            try { await bankSyncTask.ConfigureAwait(false); } catch (Exception) { }
        }
    }

    private static void WriteLine(Action<string>? log, string line)
    {
        if (log != null)
            log(line);
        else
            Console.WriteLine(line);
    }

    /// <summary>TLS handshake then request loop for one TCP client; swallows drop/auth cancel, logs other errors.</summary>
    private static async Task HandleClientAsync(
        TcpClient tcp,
        X509Certificate2 cert,
        ServerDispatch dispatch,
        ConnectionHub? connections,
        CancellationToken cancel)
    {
        tcp.NoDelay = true;
        var session = new ClientSession
        {
            Remote = tcp.Client.RemoteEndPoint?.ToString() ?? "client",
            ConnectedAt = DateTime.Now
        };
        string remote = session.Remote;
        connections?.Add(session);
        // Handshake and request loop; drop/auth failures are handled below.
        try
        {
            await using var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false);
            var options = new SslServerAuthenticationOptions
            {
                ServerCertificate = cert,
                EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
                CertificateRevocationCheckMode = X509RevocationMode.NoCheck,
                ClientCertificateRequired = false
            };
            await ssl.AuthenticateAsServerAsync(options, cancel).ConfigureAwait(false);

            while (!cancel.IsCancellationRequested)
            {
                WireRequest? request; // framed named-op from this TLS client
                // End of stream is a clean disconnect, not a failed op.
                try
                {
                    request = await Wire.ReadAsync<WireRequest>(ssl, cancel).ConfigureAwait(false);
                }
                // Client closed the TLS stream; end this connection.
                catch (EndOfStreamException)
                {
                    break;
                }

                // Empty or nameless ops cannot be dispatched.
                if (request == null || string.IsNullOrWhiteSpace(request.Op))
                {
                    await Wire.WriteAsync(ssl, Wire.Fail("", "Empty request."), cancel).ConfigureAwait(false);
                    continue;
                }

                // Op failures must answer the client without killing the connection.
                try
                {
                    // request.Op is the named ServerOps string; payload is the JSON result to frame.
                    object? payload = dispatch.Handle(request.Op, request.Payload, session);
                    connections?.Notify();
                    await Wire.WriteAsync(ssl, Wire.Ok(request.Id, payload), cancel).ConfigureAwait(false);
                }
                // Per-op failures stay on this connection; only a public message is sent back.
                catch (Exception ex)
                {
                    await Wire.WriteAsync(
                        ssl,
                        Wire.Fail(request.Id, PublicError(ex)),
                        cancel).ConfigureAwait(false);
                }
            }
        }
        // Dropped TLS/TCP and handshake cancel are normal client disconnects.
        catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException)
        {
            // client dropped the stream
        }
        // Unexpected errors are logged with the remote endpoint for the operator.
        catch (Exception ex)
        {
            Console.Error.WriteLine(remote + ": " + ex.Message);
        }
        // Always close the socket so a hung client does not leak connections.
        finally
        {
            connections?.Remove(session);
            tcp.Dispose();
        }
    }

    /// <summary>Returns the exception message for expected client errors; hides internals otherwise.</summary>
    private static string PublicError(Exception ex) =>
        ex is InvalidOperationException or ArgumentException
            ? ex.Message
            : "The server could not complete that request.";
}
