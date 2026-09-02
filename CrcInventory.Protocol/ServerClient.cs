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

    public bool IsConnected
    {
        get
        {
            lock (_gate)
                return _ssl != null && _tcp is { Connected: true };
        }
    }

    public string Fingerprint { get; private set; } = "";

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

    public T Call<T>(string op, object? payload = null)
    {
        SslStream ssl = Require();
        var request = Wire.Request(op, payload);
        lock (_gate)
        {
            Wire.WriteAsync(ssl, request).GetAwaiter().GetResult();
            var response = Wire.ReadAsync<WireResponse>(ssl).GetAwaiter().GetResult()
                ?? throw new InvalidOperationException("Empty response from server.");
            if (!response.Ok)
                throw new InvalidOperationException(response.Error ?? "Server rejected " + op + ".");
            if (response.Payload is not JsonElement element)
            {
                if (typeof(T) == typeof(object) || typeof(T) == typeof(JsonElement))
                    return default!;
                return JsonSerializer.Deserialize<T>("null", JsonWire.Options)!;
            }

            return element.Deserialize<T>(JsonWire.Options)!;
        }
    }

    public bool Try<T>(string op, object? payload, out T? result)
    {
        result = default;
        if (!IsConnected)
            return false;

        try
        {
            result = Call<T>(op, payload);
            return true;
        }
        catch
        {
            return false;
        }
    }

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

    public void Dispose() => Disconnect();

    private SslStream Require()
    {
        lock (_gate)
        {
            if (_ssl == null)
                throw new InvalidOperationException("Not connected to the inventory server.");
            return _ssl;
        }
    }
}
