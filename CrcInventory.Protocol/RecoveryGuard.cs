namespace CrcInventory.Protocol;

/// <summary>Shared wording for sign-in lockout (5 tries, 15 minutes; 30 tries, IT unlock).</summary>
public static class RecoveryGuard
{
    /// <summary>Failed attempts that trigger a timed lock.</summary>
    public const int MaxTries = 5;

    /// <summary>Minutes a timed lock lasts after every <see cref="MaxTries"/> failures.</summary>
    public const int LockMinutes = 15;

    /// <summary>User-facing text for a timed lock that expires at <paramref name="until"/>.</summary>
    public static string LockedMessage(DateTime until) =>
        "Too many tries. Try again at " + until.ToLocalTime().ToString("h:mm tt") + ".";

    /// <summary>Failed attempts after which only IT can unlock the account.</summary>
    public const int LoginMaxTries = 30;

    /// <summary>Sentinel stored in login_lock_until when IT must unlock.</summary>
    public const string ItLockValue = "IT";

    /// <summary>True when the stored lock value is the IT-unlock sentinel rather than a timestamp.</summary>
    public static bool IsItLock(string? value) =>
        (value ?? "").Trim().Equals(ItLockValue, StringComparison.OrdinalIgnoreCase);

    /// <summary>User-facing text when the account needs an IT unlock.</summary>
    public static string ItLockMessage =>
        "This account is locked. Contact IT.";

    /// <summary>Wrong-password text that still reports how many tries remain in this window.</summary>
    public static string LoginWrongMessage(int left) =>
        "That username or password is not right. " + left + (left == 1 ? " try" : " tries") + " left.";

    /// <summary>
    /// After every 5 fails: 15-minute lock. At 30 fails: IT must unlock.
    /// Fails is the new total (already incremented).
    /// </summary>
    public static (string Until, string Message) NextLoginPenalty(int fails)
    {
        // Thirty failures is the hard cap; only IT can clear this lock.
        if (fails >= LoginMaxTries)
            return (ItLockValue, ItLockMessage);
        // Every fifth failure starts a timed lock so brute force is slowed.
        if (fails > 0 && fails % MaxTries == 0)
        {
            var until = DateTime.Now.AddMinutes(LockMinutes);
            return (until.ToString("o"), LockedMessage(until));
        }

        int left = MaxTries - (fails % MaxTries);
        return ("", LoginWrongMessage(left));
    }
}
