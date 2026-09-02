using System.Net;
using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

internal static class ServerHost
{
    public static async Task RunAsync(
        string dataFolder,
        string bind,
        int port,
        CancellationToken cancel)
    {
        var store = new InventoryStore(dataFolder);
        using var cert = ServerCert.LoadOrCreate(dataFolder, out string fingerprint);
        var dispatch = new ServerDispatch(store, fingerprint);

        if (!IPAddress.TryParse(bind, out var address))
            throw new InvalidOperationException("Invalid bind address: " + bind);

        var listener = new TcpListener(address, port);
        listener.Start();
        Console.WriteLine("Cast Right Catch inventory server");
        Console.WriteLine("  data         " + store.Folder);
        Console.WriteLine("  listen       " + address + ":" + port);
        Console.WriteLine("  fingerprint  " + fingerprint);
        Console.WriteLine("  first IT     " + (store.HasItUser() ? "yes" : "no — run with --bootstrap on this PC"));
        Console.WriteLine("Clients connect over TLS. Database files stay on this machine.");
        Console.WriteLine("Press Ctrl+C to stop.");

        try
        {
            while (!cancel.IsCancellationRequested)
            {
                TcpClient client;
                try
                {
                    client = await listener.AcceptTcpClientAsync(cancel).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    break;
                }

                _ = Task.Run(() => HandleClientAsync(client, cert, dispatch, cancel), cancel);
            }
        }
        finally
        {
            listener.Stop();
        }
    }

    private static async Task HandleClientAsync(
        TcpClient tcp,
        X509Certificate2 cert,
        ServerDispatch dispatch,
        CancellationToken cancel)
    {
        tcp.NoDelay = true;
        var session = new ClientSession();
        string remote = tcp.Client.RemoteEndPoint?.ToString() ?? "client";
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
                try
                {
                    request = await Wire.ReadAsync<WireRequest>(ssl, cancel).ConfigureAwait(false);
                }
                catch (EndOfStreamException)
                {
                    break;
                }

                if (request == null || string.IsNullOrWhiteSpace(request.Op))
                {
                    await Wire.WriteAsync(ssl, Wire.Fail("", "Empty request."), cancel).ConfigureAwait(false);
                    continue;
                }

                try
                {
                    object? payload = dispatch.Handle(request.Op, request.Payload, session);
                    await Wire.WriteAsync(ssl, Wire.Ok(request.Id, payload), cancel).ConfigureAwait(false);
                }
                catch (Exception ex)
                {
                    await Wire.WriteAsync(
                        ssl,
                        Wire.Fail(request.Id, PublicError(ex)),
                        cancel).ConfigureAwait(false);
                }
            }
        }
        catch (Exception ex) when (ex is IOException or AuthenticationException or OperationCanceledException)
        {
            // client dropped the stream
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(remote + ": " + ex.Message);
        }
        finally
        {
            tcp.Dispose();
        }
    }

    private static string PublicError(Exception ex) =>
        ex is InvalidOperationException or ArgumentException
            ? ex.Message
            : "The server could not complete that request.";
}
