using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CrcInventory.Protocol;

public static class CertFingerprint
{
    public static string From(X509Certificate certificate)
    {
        byte[] raw = certificate.GetRawCertData();
        byte[] hash = SHA256.HashData(raw);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
        {
            if (c is ':' or ' ' or '-')
                continue;
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    public static bool Matches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Normalize(expected)),
            Encoding.ASCII.GetBytes(Normalize(actual)));
}
