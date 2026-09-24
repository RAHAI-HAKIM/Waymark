namespace Waymark.Domain.Organisation;

/// <summary>
/// <b>Session A5.</b> One person's run of wrong PINs, and whether they may try
/// again (D-083): <b>five wrong in a row lock them out for five minutes</b>.
///
/// <para>
/// Pure and immutable, like <see cref="Engine.CardAudience"/>: every attempt returns the next
/// state, and the clock is passed in, so the rule is argued with in a test that waits for nothing.
/// Holding one per person and asking it before checking a PIN is StoreServer's job.
/// </para>
///
/// <para>
/// <b>Why this is the rule that matters.</b> A PIN of 4 to 8 digits falls to guessing under any
/// hash; Argon2 only slows the guess. What stops ten thousand tries at the till is this. Written
/// one step too loose — a lock that never starts, a count that a correct PIN from somebody else
/// resets, a lock that ends early — and nothing reports it: every sign-in still works, and the
/// PIN protects nothing.
/// </para>
///
/// <para><b>The rules the tests hold:</b></para>
/// <list type="number">
///   <item><description>Wrong PINs are counted <b>in a row</b>; a right one clears the count.</description></item>
///   <item><description>The <b>fifth</b> wrong PIN in a row locks, for <see cref="LockFor"/> from that moment.</description></item>
///   <item><description>While locked, <see cref="IsLocked"/> is true, and an attempt is refused before any PIN is checked — so a locked attempt changes nothing, neither extending the lock nor counting.</description></item>
///   <item><description>When the lock ends, the count starts again from zero: the next wrong PIN is the first, not the sixth.</description></item>
///   <item><description>The lock ends exactly at <see cref="LockedUntil"/>: at that instant the person may try again.</description></item>
/// </list>
/// </summary>
/// <param name="WrongInARow">Wrong PINs since the last right one, or since the last lock ended.</param>
/// <param name="LockedUntil">When the current lock ends, or null when there is none.</param>
public sealed record SignInLockout(int WrongInARow, DateTimeOffset? LockedUntil)
{
    /// <summary>Wrong PINs in a row that lock (D-083).</summary>
    public const int WrongBeforeLock = 5;

    /// <summary>How long a lock lasts (D-083).</summary>
    public static readonly TimeSpan LockFor = TimeSpan.FromMinutes(5);

    /// <summary>Nobody has got it wrong.</summary>
    public static SignInLockout Clear { get; } = new(0, null);

    /// <summary>Whether an attempt at <paramref name="now"/> is refused without checking the PIN.</summary>
    public bool IsLocked(DateTimeOffset now)
    {
        return LockedUntil.HasValue && now < LockedUntil;
    }

    /// <summary>The state after a wrong PIN at <paramref name="at"/>. Called only when not locked.</summary>
    public SignInLockout AfterWrong(DateTimeOffset at)
    {
        if(IsLocked(at))
        {
            return this;
        }
        int NewWrongInARow = WrongInARow + 1;
        if (NewWrongInARow > WrongBeforeLock)
        {
            NewWrongInARow %= WrongBeforeLock;
        }
        DateTimeOffset? NewLockedUntil = null;
        if(NewWrongInARow >= WrongBeforeLock)
        {
            NewLockedUntil = at.Add(LockFor);
        }
        return new SignInLockout(NewWrongInARow, NewLockedUntil);
    }

    /// <summary>The state after a right PIN.</summary>
    public SignInLockout AfterRight()
    {
        return WrongInARow == 0 ? this : Clear;
    }

}
