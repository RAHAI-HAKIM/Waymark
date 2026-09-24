using Waymark.Domain.Organisation;

namespace Waymark.Domain.Tests;

/// <summary>
/// Five wrong PINs in a row lock a person out for five minutes (session A5, D-083).
///
/// <para>
/// This is what actually protects a PIN of four digits: Argon2 slows each guess, the lockout
/// stops the ten-thousandth. Every failure here is silent — sign-ins keep working, and a lock that
/// never starts or ends early looks exactly like a till with honest cashiers.
/// </para>
/// </summary>
public sealed class SignInLockoutTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    private static SignInLockout WrongTimes(int times, DateTimeOffset at)
    {
        var state = SignInLockout.Clear;
        for (var i = 0; i < times; i++)
        {
            state = state.AfterWrong(at);
        }

        return state;
    }

    [Fact]
    public void Nobody_starts_locked()
    {
        Assert.False(SignInLockout.Clear.IsLocked(T0));
    }

    [Fact]
    public void Four_wrong_in_a_row_do_not_lock()
    {
        var state = WrongTimes(4, T0);

        Assert.False(state.IsLocked(T0));
        Assert.Equal(4, state.WrongInARow);
    }

    [Fact]
    public void The_fifth_wrong_in_a_row_locks_for_five_minutes_from_that_moment()
    {
        var fifth = T0.AddSeconds(40);
        var state = WrongTimes(4, T0).AfterWrong(fifth);

        Assert.True(state.IsLocked(fifth));
        Assert.Equal(fifth.AddMinutes(5), state.LockedUntil);
    }

    [Fact]
    public void Still_locked_a_second_before_the_end()
    {
        var state = WrongTimes(5, T0);

        Assert.True(state.IsLocked(T0.AddMinutes(5).AddSeconds(-1)));
    }

    [Fact]
    public void Free_again_exactly_when_the_lock_ends()
    {
        var state = WrongTimes(5, T0);

        Assert.False(state.IsLocked(T0.AddMinutes(5)));
    }

    [Fact]
    public void A_right_pin_clears_the_count()
    {
        var state = WrongTimes(4, T0).AfterRight().AfterWrong(T0);

        Assert.Equal(1, state.WrongInARow);
        Assert.False(state.IsLocked(T0));
    }

    [Fact]
    public void After_a_lock_ends_the_next_wrong_pin_is_the_first_not_the_sixth()
    {
        // Carrying the count across the lock would re-lock on a single slip, and a person who
        // waited out five minutes would be locked out again by their first typo.
        var afterLock = T0.AddMinutes(5);
        var state = WrongTimes(5, T0).AfterWrong(afterLock);

        Assert.False(state.IsLocked(afterLock));
        Assert.Equal(1, state.WrongInARow);
    }

    [Fact]
    public void Five_more_wrong_after_a_lock_lock_again()
    {
        var afterLock = T0.AddMinutes(5);
        var state = WrongTimes(5, T0);
        for (var i = 0; i < 5; i++)
        {
            state = state.AfterWrong(afterLock);
        }

        Assert.True(state.IsLocked(afterLock));
        Assert.Equal(afterLock.AddMinutes(5), state.LockedUntil);
    }

    [Fact]
    public void Clear_is_what_a_right_pin_leaves()
    {
        Assert.Equal(SignInLockout.Clear, WrongTimes(3, T0).AfterRight());
    }

    [Fact]
    public void The_rule_is_five_and_five_minutes()
    {
        // D-083's numbers, pinned so a change is a decision and not an edit.
        Assert.Equal(5, SignInLockout.WrongBeforeLock);
        Assert.Equal(TimeSpan.FromMinutes(5), SignInLockout.LockFor);
    }
}
