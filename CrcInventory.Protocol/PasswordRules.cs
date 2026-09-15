namespace CrcInventory.Protocol;

/// <summary>Shared password rules for the desktop app and the inventory server.</summary>
public static class PasswordRules
{
    /// <summary>Shortest allowed password; both sides reject anything shorter.</summary>
    public const int MinLength = 8;

    /// <summary>Returns true when the password meets length and character-class rules; otherwise sets <paramref name="error"/>.</summary>
    public static bool Meets(string? password, out string error)
    {
        error = "";
        password ??= "";
        // Reject passwords that cannot meet the length floor of the shared policy.
        if (password.Length < MinLength)
        {
            error = "Password must be at least " + MinLength +
                    " characters, with a capital letter, a number, and a symbol.";
            return false;
        }

        bool upper = false, digit = false, symbol = false;
        foreach (char c in password)
        {
            // Uppercase letters satisfy the capital-letter rule.
            if (char.IsUpper(c))
                upper = true;
            // Digits satisfy the number rule and are not treated as symbols.
            else if (char.IsDigit(c))
                digit = true;
            // Any remaining non-letter is a symbol (punctuation, space, etc.).
            else if (!char.IsLetter(c))
                symbol = true;
        }

        // All three required character classes are present.
        if (upper && digit && symbol)
            return true;

        error = "Password needs a capital letter, a number, and a symbol.";
        return false;
    }
}
