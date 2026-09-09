using System.Security.Cryptography;
using System.Text;

namespace CrcInventory.Protocol;

/// <summary>
/// AES-GCM for secrets at rest. Key is crc.key in the data folder.
/// Values already prefixed with enc1. are left alone; older plaintext is still readable.
/// </summary>
public static class SecretProtect
{
    public const string Prefix = "enc1.";
    private const string KeyFileName = "crc.key";
    private static readonly object Gate = new();
    private static string _folder = "";
    private static byte[]? _key;

    public static void UseFolder(string? folder)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder);
        lock (Gate)
        {
            if (_key != null && _folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
                return;
            _folder = folder;
            _key = folder.Length == 0 ? null : LoadOrCreateKey(folder);
        }
    }

    public static bool IsSecretSetting(string key) =>
        key.Equals("smtp_password", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("plaid_secret", StringComparison.OrdinalIgnoreCase);

    public static bool IsSecretColumn(string table, string column)
    {
        if (column.Equals("Routing Number", StringComparison.OrdinalIgnoreCase) ||
            column.Equals("Account Number", StringComparison.OrdinalIgnoreCase))
            return true;
        return table.Equals("bank_accounts", StringComparison.OrdinalIgnoreCase) &&
               column.Equals("plaid_access_token", StringComparison.OrdinalIgnoreCase);
    }

    public static bool IsSealed(string? value) =>
        (value ?? "").StartsWith(Prefix, StringComparison.Ordinal);

    public static string Seal(string? plain)
    {
        if (string.IsNullOrEmpty(plain) || IsSealed(plain))
            return plain ?? "";
        byte[]? key = Key();
        if (key == null)
            return plain;

        byte[] nonce = RandomNumberGenerator.GetBytes(12);
        byte[] data = Encoding.UTF8.GetBytes(plain);
        byte[] cipher = new byte[data.Length];
        byte[] tag = new byte[16];
        using var gcm = new AesGcm(key, 16);
        gcm.Encrypt(nonce, data, cipher, tag);
        var blob = new byte[nonce.Length + cipher.Length + tag.Length];
        Buffer.BlockCopy(nonce, 0, blob, 0, nonce.Length);
        Buffer.BlockCopy(cipher, 0, blob, nonce.Length, cipher.Length);
        Buffer.BlockCopy(tag, 0, blob, nonce.Length + cipher.Length, tag.Length);
        return Prefix + Convert.ToBase64String(blob);
    }

    public static string Open(string? stored)
    {
        if (string.IsNullOrEmpty(stored) || !IsSealed(stored))
            return stored ?? "";
        byte[]? key = Key();
        if (key == null)
            return stored;
        try
        {
            byte[] blob = Convert.FromBase64String(stored[Prefix.Length..]);
            if (blob.Length < 12 + 16)
                return stored;
            byte[] nonce = blob[..12];
            byte[] tag = blob[^16..];
            byte[] cipher = blob[12..^16];
            byte[] plain = new byte[cipher.Length];
            using var gcm = new AesGcm(key, 16);
            gcm.Decrypt(nonce, cipher, tag, plain);
            return Encoding.UTF8.GetString(plain);
        }
        catch
        {
            return stored;
        }
    }

    public static string StoreSetting(string key, string? value)
    {
        string text = value ?? "";
        if (text.Length == 0 || !IsSecretSetting(key))
            return text;
        return Seal(text);
    }

    public static string RevealSetting(string key, string? value) =>
        IsSecretSetting(key) ? Open(value) : (value ?? "");

    public static string StoreField(string table, string column, string? value)
    {
        string text = value ?? "";
        if (text.Length == 0 || !IsSecretColumn(table, column))
            return text;
        return Seal(text);
    }

    public static string RevealField(string table, string column, string? value) =>
        IsSecretColumn(table, column) ? Open(value) : (value ?? "");

    public static Dictionary<string, string> WithoutSecrets(Dictionary<string, string> map)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in map)
        {
            if (IsSecretSetting(pair.Key))
                continue;
            copy[pair.Key] = pair.Value ?? "";
        }

        return copy;
    }

    public static Dictionary<string, string> RevealSettings(Dictionary<string, string> map)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in map)
            copy[pair.Key] = RevealSetting(pair.Key, pair.Value);
        return copy;
    }

    private static byte[]? Key()
    {
        lock (Gate)
            return _key;
    }

    private static byte[] LoadOrCreateKey(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, KeyFileName);
        if (File.Exists(path))
        {
            byte[] existing = File.ReadAllBytes(path);
            if (existing.Length == 32)
                return existing;
        }

        byte[] created = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, created);
        return created;
    }
}
