using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using CrcInventory.Protocol;

namespace CrcInventory.Server;

internal static class ServerCert
{
    public static X509Certificate2 LoadOrCreate(string dataFolder, out string fingerprint)
    {
        Directory.CreateDirectory(dataFolder);
        string path = Path.Combine(dataFolder, Schema.CertificateFileName);
        X509Certificate2 cert;
        if (File.Exists(path))
        {
            cert = new X509Certificate2(path, "", X509KeyStorageFlags.Exportable);
        }
        else
        {
            cert = Create();
            File.WriteAllBytes(path, cert.Export(X509ContentType.Pfx));
        }

        fingerprint = CertFingerprint.From(cert);
        return cert;
    }

    private static X509Certificate2 Create()
    {
        using var rsa = RSA.Create(2048);
        var request = new CertificateRequest(
            "CN=Cast Right Catch Inventory Server",
            rsa,
            HashAlgorithmName.SHA256,
            RSASignaturePadding.Pkcs1);
        request.CertificateExtensions.Add(
            new X509KeyUsageExtension(
                X509KeyUsageFlags.DigitalSignature | X509KeyUsageFlags.KeyEncipherment,
                true));
        request.CertificateExtensions.Add(
            new X509EnhancedKeyUsageExtension(
                new OidCollection { new Oid("1.3.6.1.5.5.7.3.1") },
                true));
        request.CertificateExtensions.Add(
            new X509SubjectKeyIdentifierExtension(request.PublicKey, false));

        var san = new SubjectAlternativeNameBuilder();
        san.AddDnsName("localhost");
        san.AddIpAddress(System.Net.IPAddress.Loopback);
        san.AddIpAddress(System.Net.IPAddress.IPv6Loopback);
        request.CertificateExtensions.Add(san.Build());

        using var ephemeral = request.CreateSelfSigned(
            DateTimeOffset.UtcNow.AddDays(-1),
            DateTimeOffset.UtcNow.AddYears(10));
        return new X509Certificate2(
            ephemeral.Export(X509ContentType.Pfx),
            "",
            X509KeyStorageFlags.Exportable);
    }
}
