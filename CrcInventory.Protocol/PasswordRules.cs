namespace CrcInventory.Protocol;

/// <summary>Shared password rules for the desktop app and the inventory server.</summary>
public static class PasswordRules
{
    public const int MinLength = 8;

    public static bool Meets(string? password, out string error)
    {
        error = "";
        password ??= "";
        if (password.Length < MinLength)
        {
            error = "Password must be at least " + MinLength +
                    " characters, with a capital letter, a number, and a symbol.";
            return false;
        }

        bool upper = false, digit = false, symbol = false;
        foreach (char c in password)
        {
            if (char.IsUpper(c))
                upper = true;
            else if (char.IsDigit(c))
                digit = true;
            else if (!char.IsLetter(c))
                symbol = true;
        }

        if (upper && digit && symbol)
            return true;

        error = "Password needs a capital letter, a number, and a symbol.";
        return false;
    }
}
