using System.Net.Security;
using System.Net.Sockets;
using System.Security.Authentication;
using System.Security.Cryptography.X509Certificates;
using System.Text.Json;

namespace CrcInventory.Protocol;

/// <summary>
/// TLS client for the named-op stream. Pins the host certificate by SHA-256
/// fingerprint so a later transport can keep the same pin check.
/// </summary>
public sealed class ServerClient : IDataChannel
{
    private readonly object _gate = new();
    private TcpClient? _tcp;
    private SslStream? _ssl;

    /// <summary>True while both the TCP socket and TLS stream are present and connected.</summary>
    public bool IsConnected
    {
        get
        {
            lock (_gate)
                return _ssl != null && _tcp is { Connected: true };
        }
    }

    /// <summary>SHA-256 pin of the certificate seen on the last successful connect.</summary>
    public string Fingerprint { get; private set; } = "";

    /// <summary>Opens a TLS session to <paramref name="host"/>:<paramref name="port"/>, pinning the cert when a fingerprint is given.</summary>
    public void Connect(string host, int port, string? fingerprint = null, int timeoutMs = 15000)
    {
        Disconnect();
        string expected = CertFingerprint.Normalize(fingerprint);

        var tcp = new TcpClient();
        using var timeout = new CancellationTokenSource(timeoutMs);
        tcp.ConnectAsync(host, port, timeout.Token).AsTask().GetAwaiter().GetResult();
        tcp.NoDelay = true;

        var ssl = new SslStream(tcp.GetStream(), leaveInnerStreamOpen: false, (_, cert, _, _) =>
        {
            // No server cert means the pin cannot be checked; refuse the handshake.
            if (cert == null)
                return false;
            string actual = CertFingerprint.From(cert);
            return expected.Length == 0 || CertFingerprint.Matches(expected, actual);
        });

        var options = new SslClientAuthenticationOptions
        {
            TargetHost = host,
            EnabledSslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13,
            CertificateRevocationCheckMode = X509RevocationMode.NoCheck
        };
        ssl.AuthenticateAsClient(options);

        string seen = CertFingerprint.From(ssl.RemoteCertificate
            ?? throw new InvalidOperationException("The server did not present a certificate."));
        // A stored pin that does not match means a different host or a MITM; drop the sockets.
        if (expected.Length > 0 && !CertFingerprint.Matches(expected, seen))
        {
            ssl.Dispose();
            tcp.Dispose();
            throw new InvalidOperationException(
                "The server certificate fingerprint does not match. Seen: " + seen);
        }

        lock (_gate)
        {
            _tcp = tcp;
            _ssl = ssl;
            Fingerprint = seen;
        }
    }

    /// <summary>Sends <paramref name="op"/> and deserializes the success payload as <typeparamref name="T"/>.</summary>
    public T Call<T>(string op, object? payload = null)
    {
        SslStream ssl = Require(); // TLS stream for this connection
        var request = Wire.Request(op, payload); // op is the ServerOps name; request is the framed envelope
        lock (_gate)
        {
            Wire.WriteAsync(ssl, request).GetAwaiter().GetResult();
            var response = Wire.ReadAsync<WireResponse>(ssl).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Empty response from server.");
            // Host-reported failures become exceptions so callers do not inspect Ok flags.
            if (!response.Ok)
                throw new InvalidOperationException(response.Error ?? "Server rejected " + op + ".");
            // Some ops return no payload; map that to default/null rather than failing deserialize.
            if (response.Payload is not JsonElement element)
            {
                // object/JsonElement callers accept a missing body as default.
                if (typeof(T) == typeof(object) || typeof(T) == typeof(JsonElement))
                    return default!;
                return JsonSerializer.Deserialize<T>("null", JsonWire.Options)!;
            }

            return element.Deserialize<T>(JsonWire.Options)!;
        }
    }

    /// <summary>Calls <see cref="Call{T}"/> and returns false when disconnected or the call throws.</summary>
    public bool Try<T>(string op, object? payload, out T? result)
    {
        result = default;
        // A disconnected client cannot send; fail quietly for UI retry paths.
        if (!IsConnected)
            return false;

        // Call throws on transport and server errors; this path is best-effort.
        try
        {
            result = Call<T>(op, payload);
            return true;
        }
        // Any transport or server error is treated as a failed attempt, not a crash.
        catch
        {
            return false;
        }
    }

    /// <summary>Disposes the TLS stream and TCP socket and clears the stored pin.</summary>
    public void Disconnect()
    {
        lock (_gate)
        {
            _ssl?.Dispose();
            _tcp?.Dispose();
            _ssl = null;
            _tcp = null;
            Fingerprint = "";
        }
    }

    /// <summary>Closes the connection; same as <see cref="Disconnect"/>.</summary>
    public void Dispose() => Disconnect();

    /// <summary>Returns the open TLS stream, or throws if <see cref="Connect"/> has not succeeded.</summary>
    private SslStream Require()
    {
        lock (_gate)
        {
            // Callers must Connect first; fail here instead of NullReferenceException later.
            if (_ssl == null)
                throw new InvalidOperationException("Not connected to the inventory server.");
            return _ssl;
        }
    }
}
