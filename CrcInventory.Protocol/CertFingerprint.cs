using System.Security.Cryptography;
using System.Security.Cryptography.X509Certificates;
using System.Text;

namespace CrcInventory.Protocol;

/// <summary>
/// SHA-256 pin of a TLS certificate. Clients store this so a later host
/// cannot swap in a different cert without changing the pin.
/// </summary>
public static class CertFingerprint
{
    /// <summary>Lowercase hex SHA-256 of the certificate's raw DER bytes.</summary>
    public static string From(X509Certificate certificate)
    {
        byte[] raw = certificate.GetRawCertData();
        byte[] hash = SHA256.HashData(raw);
        return Convert.ToHexString(hash).ToLowerInvariant();
    }

    /// <summary>Strips separators and casing so stored pins compare the same way.</summary>
    public static string Normalize(string? value)
    {
        // Empty input is not a pin; callers treat "" as "no expected fingerprint".
        if (string.IsNullOrWhiteSpace(value))
            return "";

        var builder = new StringBuilder(value.Length);
        foreach (char c in value.Trim())
        {
            // Colons, spaces, and dashes are display separators, not part of the hash.
            if (c is ':' or ' ' or '-')
                continue;
            builder.Append(char.ToLowerInvariant(c));
        }

        return builder.ToString();
    }

    /// <summary>Constant-time compare so pin checks do not leak which bytes differ.</summary>
    public static bool Matches(string expected, string actual) =>
        CryptographicOperations.FixedTimeEquals(
            Encoding.ASCII.GetBytes(Normalize(expected)),
            Encoding.ASCII.GetBytes(Normalize(actual)));
}
