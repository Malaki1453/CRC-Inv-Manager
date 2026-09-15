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
    public static async Task RunAsync(
        string dataFolder,
        string bind,
        int port,
        string? postgres,
        CancellationToken cancel)
    {
        var store = new InventoryStore(dataFolder, postgres);
        using var cert = ServerCert.LoadOrCreate(dataFolder, out string fingerprint);
        var dispatch = new ServerDispatch(store, fingerprint);

        // A non-IP bind string would throw later with a poorer message.
        if (!IPAddress.TryParse(bind, out var address))
            throw new InvalidOperationException("Invalid bind address: " + bind);

        var listener = new TcpListener(address, port);
        listener.Start();
        Console.WriteLine("Cast Right Catch inventory server");
        Console.WriteLine("  data         " + store.Folder);
        Console.WriteLine("  database     " + store.EngineName);
        Console.WriteLine("  listen       " + address + ":" + port);
        Console.WriteLine("  fingerprint  " + fingerprint);
        Console.WriteLine("  first IT     " + (store.HasItUser() ? "yes" : "no — run with --bootstrap on this PC"));
        Console.WriteLine(
            store.UsesPostgres
                ? "Clients connect over TLS. Inventory data is Postgres (live / archive schemas)."
                : "Clients connect over TLS. Database files stay on this machine.");
        Console.WriteLine("Press Ctrl+C to stop.");

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

                _ = Task.Run(() => HandleClientAsync(client, cert, dispatch, cancel), cancel);
            }
        }
        // Always release the listen port, including after Ctrl+C.
        finally
        {
            listener.Stop();
        }
    }

    /// <summary>TLS handshake then request loop for one TCP client; swallows drop/auth cancel, logs other errors.</summary>
    private static async Task HandleClientAsync(
        TcpClient tcp,
        X509Certificate2 cert,
        ServerDispatch dispatch,
        CancellationToken cancel)
    {
        tcp.NoDelay = true;
        var session = new ClientSession();
        string remote = tcp.Client.RemoteEndPoint?.ToString() ?? "client";
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
                WireRequest? request;
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
                    object? payload = dispatch.Handle(request.Op, request.Payload, session);
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
            tcp.Dispose();
        }
    }

    /// <summary>Returns the exception message for expected client errors; hides internals otherwise.</summary>
    private static string PublicError(Exception ex) =>
        ex is InvalidOperationException or ArgumentException
            ? ex.Message
            : "The server could not complete that request.";
}
