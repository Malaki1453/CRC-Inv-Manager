using System.Security.Cryptography;
using System.Text;
using Konscious.Security.Cryptography;

namespace CrcInventory.Server;

/// <summary>Argon2id hashes, matching the desktop app so an existing database can be hosted as-is.</summary>
internal static class Passwords
{
    public const int MinimumLength = 8;
    private const int ArgonMemoryKb = 19456;
    private const int ArgonIterations = 2;
    private const int ArgonParallelism = 1;
    private const int ArgonHashLength = 32;

    public static bool MeetsPolicy(string password, out string error)
    {
        error = "";
        if (string.IsNullOrEmpty(password) || password.Length < MinimumLength)
        {
            error = "Password must be at least " + MinimumLength + " characters.";
            return false;
        }

        return true;
    }

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

    public static bool Verify(string password, string hash, string salt)
    {
        try
        {
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
        catch
        {
            return false;
        }
    }

    public static string HashSessionToken(string token)
    {
        byte[] bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token ?? ""));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    public static string NewSessionToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(32));

    public static string NormalizeAnswer(string? answer)
    {
        answer = (answer ?? "").Trim().ToLowerInvariant();
        while (answer.Contains("  ", StringComparison.Ordinal))
            answer = answer.Replace("  ", " ", StringComparison.Ordinal);
        return answer;
    }

    private static bool VerifyArgon2(string password, string encoded)
    {
        string[] parts = encoded.Split('$');
        if (parts.Length != 6)
            return false;

        int memory = ArgonMemoryKb;
        int iterations = ArgonIterations;
        int parallelism = ArgonParallelism;
        foreach (var piece in parts[3].Split(','))
        {
            if (piece.StartsWith("m=", StringComparison.Ordinal) &&
                int.TryParse(piece[2..], out int m))
                memory = m;
            else if (piece.StartsWith("t=", StringComparison.Ordinal) &&
                     int.TryParse(piece[2..], out int t))
                iterations = t;
            else if (piece.StartsWith("p=", StringComparison.Ordinal) &&
                     int.TryParse(piece[2..], out int p))
                parallelism = p;
        }

        byte[] saltBytes = Convert.FromBase64String(parts[4]);
        byte[] expected = Convert.FromBase64String(parts[5]);
        byte[] actual = Argon2(password, saltBytes, memory, iterations, parallelism);
        return CryptographicOperations.FixedTimeEquals(actual, expected);
    }

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
