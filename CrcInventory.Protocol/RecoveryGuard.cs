namespace CrcInventory.Protocol;

/// <summary>Shared wording for sign-in lockout (5 tries, 15 minutes; 30 tries, IT unlock).</summary>
public static class RecoveryGuard
{
    public const int MaxTries = 5;
    public const int LockMinutes = 15;

    public static string LockedMessage(DateTime until) =>
        "Too many tries. Try again at " + until.ToLocalTime().ToString("h:mm tt") + ".";

    public const int LoginMaxTries = 30;
    public const string ItLockValue = "IT";

    public static bool IsItLock(string? value) =>
        (value ?? "").Trim().Equals(ItLockValue, StringComparison.OrdinalIgnoreCase);

    public static string ItLockMessage =>
        "This account is locked. Contact IT.";

    public static string LoginWrongMessage(int left) =>
        "That username or password is not right. " + left + (left == 1 ? " try" : " tries") + " left.";

    /// <summary>
    /// After every 5 fails: 15-minute lock. At 30 fails: IT must unlock.
    /// Fails is the new total (already incremented).
    /// </summary>
    public static (string Until, string Message) NextLoginPenalty(int fails)
    {
        if (fails >= LoginMaxTries)
            return (ItLockValue, ItLockMessage);
        if (fails > 0 && fails % MaxTries == 0)
        {
            var until = DateTime.Now.AddMinutes(LockMinutes);
            return (until.ToString("o"), LockedMessage(until));
        }

        int left = MaxTries - (fails % MaxTries);
        return ("", LoginWrongMessage(left));
    }
}
