using System.Security.Cryptography;
using System.Text;

namespace CrcInventory.Protocol;

/// <summary>
/// AES-GCM for secrets at rest. Key is crc.key in the data folder.
/// Values already prefixed with enc1. are left alone; older plaintext is still readable.
/// </summary>
public static class SecretProtect
{
    /// <summary>Prefix written in front of sealed ciphertext so Open can skip plaintext.</summary>
    public const string Prefix = "enc1.";
    private const string KeyFileName = "crc.key";
    private static readonly object Gate = new();
    private static string _folder = "";
    private static byte[]? _key;

    /// <summary>Points the key file at <paramref name="folder"/>, loading or creating crc.key once per path.</summary>
    public static void UseFolder(string? folder)
    {
        folder = string.IsNullOrWhiteSpace(folder) ? "" : Path.GetFullPath(folder);
        lock (Gate)
        {
            // Same folder already loaded; skip disk I/O and keep the existing key.
            if (_key != null && _folder.Equals(folder, StringComparison.OrdinalIgnoreCase))
                return;
            _folder = folder;
            _key = folder.Length == 0 ? null : LoadOrCreateKey(folder);
        }
    }

    /// <summary>True for setting keys that must be sealed before they are stored.</summary>
    public static bool IsSecretSetting(string key) =>
        key.Equals("smtp_password", StringComparison.OrdinalIgnoreCase) ||
        key.Equals("plaid_secret", StringComparison.OrdinalIgnoreCase);

    /// <summary>True for table columns that hold account numbers or Plaid tokens.</summary>
    public static bool IsSecretColumn(string table, string column)
    {
        // Routing and account numbers are secrets on every table that has them.
        if (column.Equals("Routing Number", StringComparison.OrdinalIgnoreCase) ||
            column.Equals("Account Number", StringComparison.OrdinalIgnoreCase))
            return true;
        return table.Equals("bank_accounts", StringComparison.OrdinalIgnoreCase) &&
               column.Equals("plaid_access_token", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>True when <paramref name="value"/> is already AES-GCM ciphertext with <see cref="Prefix"/>.</summary>
    public static bool IsSealed(string? value) =>
        (value ?? "").StartsWith(Prefix, StringComparison.Ordinal);

    /// <summary>Encrypts plaintext with AES-GCM, or returns it unchanged when already sealed or no key is loaded.</summary>
    public static string Seal(string? plain)
    {
        // Empty or already-sealed values must stay as-is so Open can still read them.
        if (string.IsNullOrEmpty(plain) || IsSealed(plain))
            return plain ?? "";
        byte[]? key = Key();
        // Without a data-folder key, leave plaintext so older hosts remain readable.
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

    /// <summary>Decrypts an enc1. blob, or returns the stored text when it is plaintext or decrypt fails.</summary>
    public static string Open(string? stored)
    {
        // Plaintext (including empty) is returned as-is so pre-encryption rows still work.
        if (string.IsNullOrEmpty(stored) || !IsSealed(stored))
            return stored ?? "";
        byte[]? key = Key();
        // No key means we cannot decrypt; return the sealed blob rather than throwing.
        if (key == null)
            return stored;
        // Decrypt can fail on a wrong key or truncated blob; keep the stored text.
        try
        {
            byte[] blob = Convert.FromBase64String(stored[Prefix.Length..]);
            // Too short to hold nonce + tag; treat as corrupt and keep the original text.
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
        // Wrong key or damaged blob: keep the stored value so the row is not wiped.
        catch
        {
            return stored;
        }
    }

    /// <summary>Seals a setting value when the key is a secret; otherwise stores it as plaintext.</summary>
    public static string StoreSetting(string key, string? value)
    {
        string text = value ?? "";
        // Non-secrets and empty values are stored verbatim.
        if (text.Length == 0 || !IsSecretSetting(key))
            return text;
        return Seal(text);
    }

    /// <summary>Opens a setting value when the key is a secret; otherwise returns it unchanged.</summary>
    public static string RevealSetting(string key, string? value) =>
        IsSecretSetting(key) ? Open(value) : (value ?? "");

    /// <summary>Seals a cell when the table/column pair is a secret; otherwise stores it as plaintext.</summary>
    public static string StoreField(string table, string column, string? value)
    {
        string text = value ?? "";
        // Non-secret columns and empty cells stay plaintext.
        if (text.Length == 0 || !IsSecretColumn(table, column))
            return text;
        return Seal(text);
    }

    /// <summary>Opens a cell when the table/column pair is a secret; otherwise returns it unchanged.</summary>
    public static string RevealField(string table, string column, string? value) =>
        IsSecretColumn(table, column) ? Open(value) : (value ?? "");

    /// <summary>Copies <paramref name="map"/> without secret setting keys so public settings never leak them.</summary>
    public static Dictionary<string, string> WithoutSecrets(Dictionary<string, string> map)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in map)
        {
            // Drop secret keys entirely rather than sending sealed or plaintext secrets to clients.
            if (IsSecretSetting(pair.Key))
                continue;
            copy[pair.Key] = pair.Value ?? "";
        }

        return copy;
    }

    /// <summary>Copies <paramref name="map"/> with secret setting values decrypted for administrators.</summary>
    public static Dictionary<string, string> RevealSettings(Dictionary<string, string> map)
    {
        var copy = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in map)
            copy[pair.Key] = RevealSetting(pair.Key, pair.Value);
        return copy;
    }

    /// <summary>Returns the loaded AES key, or null when no data folder is configured.</summary>
    private static byte[]? Key()
    {
        lock (Gate)
            return _key;
    }

    /// <summary>Loads a 32-byte key from crc.key, or writes a new one if the file is missing or the wrong size.</summary>
    private static byte[] LoadOrCreateKey(string folder)
    {
        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, KeyFileName);
        // Reuse an existing 32-byte key so previously sealed values still open.
        if (File.Exists(path))
        {
            byte[] existing = File.ReadAllBytes(path);
            // Ignore truncated or oversized files and replace them with a fresh key.
            if (existing.Length == 32)
                return existing;
        }

        byte[] created = RandomNumberGenerator.GetBytes(32);
        File.WriteAllBytes(path, created);
        return created;
    }
}
