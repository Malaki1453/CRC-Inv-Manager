using System.Security.Cryptography;
using System.Text;
using CrcInventory.Protocol;
using Konscious.Security.Cryptography;

namespace CrcInventory.Server;

/// <summary>Argon2id hashes, matching the desktop app so an existing database can be hosted as-is.</summary>
internal static class Passwords
{
    /// <summary>Same minimum length as <see cref="PasswordRules.MinLength"/>.</summary>
    public const int MinimumLength = PasswordRules.MinLength;
    private const int ArgonMemoryKb = 19456;
    private const int ArgonIterations = 2;
    private const int ArgonParallelism = 1;
    private const int ArgonHashLength = 32;

    /// <summary>Delegates to the shared password policy used by the desktop app.</summary>
    public static bool MeetsPolicy(string password, out string error) =>
        PasswordRules.Meets(password, out error);

    /// <summary>Hashes <paramref name="password"/> with Argon2id and returns the PHC string plus a salt marker.</summary>
    public static void Hash(string password, out string hash, out string salt)
    {
        byte[] saltBytes = RandomNumberGenerator.GetBytes(16);
        byte[] hashBytes = Argon2(password, saltBytes, ArgonMemoryKb, ArgonIterations, ArgonParallelism);
        salt = "argon2id";
        hash = "$argon2id$v=19$m=" + ArgonMemoryKb +
               ",t=" + ArgonIterations +
               ",p=" + ArgonParallelism +
               "$" + Convert.ToBase64String(saltBytes) +
               "$" + Convert.ToBase64String(hashBytes);
    }

    /// <summary>Verifies against an Argon2id PHC string, or falls back to the older PBKDF2 format.</summary>
    public static bool Verify(string password, string hash, string salt)
    {
        // Corrupt hashes must fail closed, not throw to the login path.
        try
        {
            // Current accounts store a PHC Argon2id string; verify with the encoded parameters.
            if (hash.StartsWith("$argon2id$", StringComparison.Ordinal))
                return VerifyArgon2(password, hash);

            byte[] saltBytes = Convert.FromBase64String(salt);
            byte[] expected = Convert.FromBase64String(hash);
            byte[] actual = Rfc2898DeriveBytes.Pbkdf2(
                password,
                saltBytes,
                100_000,
                HashAlgorithmName.SHA256,
                expected.Length);
            return CryptographicOperations.FixedTimeEquals(actual, expected);
        }
        // Corrupt hash/salt or bad encoding is treated as a failed password, not a crash.
        catch
        {
            return false;
        }
    }

    /// <summary>SHA-256 hex of a session token so the raw token is never stored.</summary>
    public static string HashSessionToken(string token)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    /// <summary>Cryptographically random 32-byte token, base64-encoded for the client.</summary>
    public static string NewSessionToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    /// <summary>Parses a PHC Argon2id string and compares the derived hash in constant time.</summary>
    private static bool VerifyArgon2(string password, string encoded)
    {
        string[] parts = encoded.Split('$');
        // A well-formed PHC string has empty, algorithm, version, params, salt, hash.
        if (parts.Length != 6)
            return false;

        int memory = ArgonMemoryKb;
        int iterations = ArgonIterations;
        int parallelism = ArgonParallelism;
        foreach (var piece in parts[3].Split(','))
        {
            // Memory parameter from the stored hash so older parameter sets still verify.
            if (piece.StartsWith("m=", StringComparison.Ordinal) &&
                int.TryParse(piece[2..], out int m))
                memory = m;
            // Iteration count from the stored hash.
            else if (piece.StartsWith("t=", StringComparison.Ordinal) &&
                     int.TryParse(piece[2..], out int t))
                iterations = t;
            // Parallelism from the stored hash.
            else if (piece.StartsWith("p=", StringComparison.Ordinal) &&
                     int.TryParse(piece[2..], out int p))
                parallelism = p;
        }

        byte[] saltBytes = Convert.FromBase64String(parts[4]);
        byte[] expected = Convert.FromBase64String(parts[5]);
        byte[] actual = Argon2(password, saltBytes, memory, iterations, parallelism);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

    /// <summary>Runs Argon2id with the given parameters and returns <see cref="ArgonHashLength"/> bytes.</summary>
    private static byte[] Argon2(string password, byte[] salt, int memoryKb, int iterations, int parallelism)
    {
        using var argon = new Argon2id(Encoding.UTF8.GetBytes(password))
        {
            Salt = salt,
            DegreeOfParallelism = Math.Max(1, parallelism),
            Iterations = Math.Max(1, iterations),
            MemorySize = Math.Max(8, memoryKb)
        };
        return argon.GetBytes(ArgonHashLength);
    }
}
