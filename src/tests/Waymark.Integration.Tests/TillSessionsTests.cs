using System.Collections.Concurrent;
using System.Runtime.Versioning;
using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;
using Waymark.StoreServer.Security;

namespace Waymark.Integration.Tests;

/// <summary>
/// Who is signed in at which till (session A5, D-083). The silent failures: a lock that ten
/// guesses sent together walk straight past, a right PIN that unlocks a locked person early, a
/// till that keeps a previous cashier's session alive under the next one's, and a sale whose
/// seller the till simply claims.
///
/// <para>
/// The hasher is a fake, so these argue the session rules and nothing about Argon2; the lockout is
/// Hakim's <see cref="SignInLockout"/>, so they turn green with it.
/// </para>
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class TillSessionsTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    private sealed class HeldClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>"test$" and the PIN: enough to tell a right PIN from a wrong one, and to count checks.</summary>
    private sealed class CountingHasher(TimeSpan? slowness = null) : IPinHasher
    {
        private int _verified;

        public int Verified => _verified;

        public string Hash(string pin) => "test$" + pin;

        public bool Verify(string pin, string? storedHash)
        {
            Interlocked.Increment(ref _verified);
            if (slowness is { } delay)
            {
                Thread.Sleep(delay);
            }

            return storedHash == "test$" + pin;
        }
    }

    private sealed class Credentials : IStaffCredentials
    {
        public ConcurrentDictionary<string, string> Hashes { get; } = new()
        {
            ["nabil"] = "test$4821",
            ["samia"] = "test$1357",
            ["karim"] = StaffPin.NeverUsable,
        };

        public Task<IReadOnlyList<SignInCandidate>> CandidatesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<string?> PinHashAsync(string staffId, CancellationToken cancellationToken = default) =>
            Task.FromResult(Hashes.GetValueOrDefault(staffId));

        public Task<bool> StagePinHashAsync(string staffId, string pinHash, DateTimeOffset at, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private readonly HeldClock _clock = new(T0);
    private readonly Credentials _credentials = new();

    private (TillSessions Sessions, CountingHasher Hasher) Build(TimeSpan? slowness = null)
    {
        var hasher = new CountingHasher(slowness);
        return (new TillSessions(hasher, _clock), hasher);
    }

    private Task<SignInAnswer> SignIn(TillSessions sessions, string staff, string pin, string terminal = "till-1", bool terminalKnown = true) =>
        sessions.SignInAsync(new SignInRequest(terminal, staff, pin), terminalKnown, _credentials);

    // ------------------------------------------------------------- signing in

    [Fact]
    public async Task A_right_pin_opens_a_session_the_token_names()
    {
        var (sessions, _) = Build();

        var answer = await SignIn(sessions, "nabil", "4821");

        Assert.Equal(SignInOutcomes.SignedIn, answer.Outcome);
        Assert.Equal(new SignedInTill("nabil", "till-1", T0), sessions.Resolve(answer.SessionToken));
    }

    [Fact]
    public async Task Every_token_is_new_and_long_enough_not_to_be_guessed()
    {
        var (sessions, _) = Build();

        var first = (await SignIn(sessions, "nabil", "4821")).SessionToken!;
        var second = (await SignIn(sessions, "nabil", "4821", "till-2")).SessionToken!;

        Assert.NotEqual(first, second);
        // 32 random bytes, in unpadded base64url: 43 characters.
        Assert.Equal(43, first.Length);
        Assert.DoesNotContain('=', first);
    }

    [Fact]
    public async Task A_token_the_server_never_gave_names_nobody()
    {
        var (sessions, _) = Build();
        await SignIn(sessions, "nabil", "4821");

        Assert.Null(sessions.Resolve("made-up"));
        Assert.Null(sessions.Resolve(""));
        Assert.Null(sessions.Resolve(null));
    }

    [Fact]
    public async Task A_wrong_pin_says_how_many_attempts_are_left()
    {
        var (sessions, _) = Build();

        var first = await SignIn(sessions, "nabil", "0000");
        var second = await SignIn(sessions, "nabil", "0000");

        Assert.Equal((SignInOutcomes.WrongPin, 4), (first.Outcome, first.AttemptsLeft!.Value));
        Assert.Equal((SignInOutcomes.WrongPin, 3), (second.Outcome, second.AttemptsLeft!.Value));
        Assert.Null(first.SessionToken);
    }

    [Fact]
    public async Task The_fifth_wrong_pin_locks_and_says_until_when()
    {
        var (sessions, _) = Build();
        for (var i = 0; i < 4; i++)
        {
            await SignIn(sessions, "nabil", "0000");
        }

        var fifth = await SignIn(sessions, "nabil", "0000");

        Assert.Equal(SignInOutcomes.Locked, fifth.Outcome);
        Assert.Equal(T0.AddMinutes(5), fifth.LockedUntil);
    }

    [Fact]
    public async Task Locked_the_right_pin_is_refused_without_being_checked()
    {
        // The lock is what makes a four-digit PIN survive: if the right PIN still worked, a
        // guesser would only need to keep going. And checking it at all would say, by timing or by
        // answer, whether it was right.
        var (sessions, hasher) = Build();
        for (var i = 0; i < 5; i++)
        {
            await SignIn(sessions, "nabil", "0000");
        }

        var checkedBefore = hasher.Verified;
        var answer = await SignIn(sessions, "nabil", "4821");

        Assert.Equal(SignInOutcomes.Locked, answer.Outcome);
        Assert.Equal(checkedBefore, hasher.Verified);
    }

    [Fact]
    public async Task When_the_lock_ends_the_right_pin_works()
    {
        var (sessions, _) = Build();
        for (var i = 0; i < 5; i++)
        {
            await SignIn(sessions, "nabil", "0000");
        }

        _clock.Now = T0.AddMinutes(5);

        Assert.Equal(SignInOutcomes.SignedIn, (await SignIn(sessions, "nabil", "4821")).Outcome);
    }

    [Fact]
    public async Task The_lock_is_one_persons_not_the_tills()
    {
        var (sessions, _) = Build();
        for (var i = 0; i < 5; i++)
        {
            await SignIn(sessions, "nabil", "0000");
        }

        Assert.Equal(SignInOutcomes.SignedIn, (await SignIn(sessions, "samia", "1357")).Outcome);
    }

    [Fact]
    public async Task A_right_pin_starts_the_count_again()
    {
        var (sessions, _) = Build();
        for (var i = 0; i < 4; i++)
        {
            await SignIn(sessions, "nabil", "0000");
        }

        await SignIn(sessions, "nabil", "4821");
        var next = await SignIn(sessions, "nabil", "0000");

        Assert.Equal(4, next.AttemptsLeft);
    }

    [Fact]
    public async Task Ten_guesses_sent_together_are_checked_five_times_not_ten()
    {
        // Without the gate every guess is checked before the fifth has locked anything, and the
        // lock counts ten wrong PINs that were all tried.
        var (sessions, hasher) = Build(slowness: TimeSpan.FromMilliseconds(20));

        var answers = await Task.WhenAll(Enumerable.Range(0, 10)
            .Select(i => Task.Run(() => SignIn(sessions, "nabil", $"{1000 + i}"))));

        Assert.Equal(5, hasher.Verified);
        Assert.Equal(6, answers.Count(answer => answer.Outcome == SignInOutcomes.Locked));
    }

    [Fact]
    public async Task Somebody_the_store_does_not_have_is_unknown_and_nothing_is_checked()
    {
        var (sessions, hasher) = Build();

        Assert.Equal(SignInOutcomes.UnknownStaff, (await SignIn(sessions, "nobody", "4821")).Outcome);
        Assert.Equal(SignInOutcomes.UnknownStaff, (await SignIn(sessions, "", "4821")).Outcome);
        Assert.Equal(0, hasher.Verified);
    }

    [Fact]
    public async Task Somebody_with_no_pin_is_told_so_and_never_locked()
    {
        // Nothing to guess: the sentinel is refused before the lockout counts, so ten tries at a
        // person with no PIN never lock them out of the PIN they are about to be given.
        var (sessions, hasher) = Build();

        for (var i = 0; i < 10; i++)
        {
            Assert.Equal(SignInOutcomes.NoPin, (await SignIn(sessions, "karim", "1234")).Outcome);
        }

        _credentials.Hashes["karim"] = "test$2468";
        Assert.Equal(SignInOutcomes.SignedIn, (await SignIn(sessions, "karim", "2468")).Outcome);
        Assert.Equal(1, hasher.Verified);
    }

    [Fact]
    public async Task A_terminal_the_store_does_not_have_opens_nothing()
    {
        var (sessions, hasher) = Build();

        var answer = await SignIn(sessions, "nabil", "4821", "no-such-till", terminalKnown: false);

        Assert.Equal(SignInOutcomes.UnknownTerminal, answer.Outcome);
        Assert.Null(answer.SessionToken);
        Assert.Equal(0, hasher.Verified);
    }

    // ------------------------------------------------------ one session per till

    [Fact]
    public async Task Signing_in_at_a_till_ends_whoever_was_signed_in_there()
    {
        var (sessions, _) = Build();
        var nabil = (await SignIn(sessions, "nabil", "4821")).SessionToken;

        var samia = (await SignIn(sessions, "samia", "1357")).SessionToken;

        Assert.Null(sessions.Resolve(nabil));
        Assert.Equal("samia", sessions.Resolve(samia)!.StaffId);
    }

    [Fact]
    public async Task One_person_may_be_signed_in_at_two_tills()
    {
        var (sessions, _) = Build();

        var one = (await SignIn(sessions, "nabil", "4821", "till-1")).SessionToken;
        var two = (await SignIn(sessions, "nabil", "4821", "till-2")).SessionToken;

        Assert.Equal("till-1", sessions.Resolve(one)!.TerminalId);
        Assert.Equal("till-2", sessions.Resolve(two)!.TerminalId);
    }

    [Fact]
    public async Task Signing_out_ends_the_session()
    {
        var (sessions, _) = Build();
        var token = (await SignIn(sessions, "nabil", "4821")).SessionToken;

        sessions.SignOut(token);

        Assert.Null(sessions.Resolve(token));
    }

    [Fact]
    public async Task Signing_out_an_old_token_leaves_the_tills_new_session_alone()
    {
        var (sessions, _) = Build();
        var old = (await SignIn(sessions, "nabil", "4821")).SessionToken;
        var current = (await SignIn(sessions, "samia", "1357")).SessionToken;

        sessions.SignOut(old);
        sessions.SignOut("made-up");
        sessions.SignOut(null);

        Assert.Equal("samia", sessions.Resolve(current)!.StaffId);
    }

    // ------------------------------------------------------------- authorising (B4, D-091)

    /// <summary>nabil is a cashier (rank 1), samia a manager (rank 2); anyone else has no rank.</summary>
    private static Task<long?> RankOf(string staffId) =>
        Task.FromResult<long?>(staffId switch { "nabil" => 1, "samia" => 2, _ => null });

    private async Task<(TillSessions Sessions, string Token)> CashierSignedIn()
    {
        var (sessions, _) = Build();
        var token = (await SignIn(sessions, "nabil", "4821")).SessionToken!;
        return (sessions, token);
    }

    private Task<AuthoriseAnswer> Authorise(TillSessions sessions, string? token, string? staff = null, string? pin = null) =>
        sessions.AuthoriseAsync(token, new AuthoriseRequest(Capabilities.ApplyDiscount, staff, pin), Capability.ApplyDiscount, _credentials, RankOf);

    [Fact]
    public async Task A_cashier_asking_alone_is_told_a_managers_pin_is_needed()
    {
        var (sessions, token) = await CashierSignedIn();

        Assert.Equal(AuthoriseOutcomes.PinRequired, (await Authorise(sessions, token)).Outcome);
    }

    [Fact]
    public async Task A_manager_signed_in_is_authorised_alone_and_is_the_one_recorded()
    {
        var (sessions, _) = Build();
        var token = (await SignIn(sessions, "samia", "1357")).SessionToken!;

        var answer = await Authorise(sessions, token);

        Assert.Equal(AuthoriseOutcomes.Authorised, answer.Outcome);
        Assert.Equal("samia", sessions.AuthorisedBy(token, answer.Authorisation, Capability.ApplyDiscount));
    }

    [Fact]
    public async Task A_managers_pin_at_the_cashiers_till_authorises_and_names_the_manager()
    {
        var (sessions, token) = await CashierSignedIn();

        var answer = await Authorise(sessions, token, "samia", "1357");

        Assert.Equal(AuthoriseOutcomes.Authorised, answer.Outcome);
        Assert.Equal("samia", sessions.AuthorisedBy(token, answer.Authorisation, Capability.ApplyDiscount));
    }

    [Fact]
    public async Task A_right_pin_of_someone_without_the_rank_is_not_allowed()
    {
        var (sessions, token) = await CashierSignedIn();

        // The cashier's own PIN: right, and still not a manager's.
        Assert.Equal(AuthoriseOutcomes.NotAllowed, (await Authorise(sessions, token, "nabil", "4821")).Outcome);
    }

    [Fact]
    public async Task A_wrong_manager_pin_counts_toward_the_same_lockout_as_signing_in()
    {
        var (sessions, token) = await CashierSignedIn();

        AuthoriseAnswer last = null!;
        for (var i = 0; i < SignInLockout.WrongBeforeLock; i++)
        {
            last = await Authorise(sessions, token, "samia", "0000");
        }

        Assert.Equal(AuthoriseOutcomes.Locked, last.Outcome);
        Assert.Equal(SignInOutcomes.Locked, (await SignIn(sessions, "samia", "1357", terminal: "till-2")).Outcome);
    }

    [Fact]
    public async Task Without_a_session_nothing_is_authorised()
    {
        var (sessions, _) = Build();

        Assert.Equal(AuthoriseOutcomes.NotSignedIn, (await Authorise(sessions, "no-such-token", "samia", "1357")).Outcome);
    }

    [Fact]
    public async Task An_authorisation_is_worth_nothing_at_another_till_or_after_its_session_ends()
    {
        var (sessions, token) = await CashierSignedIn();
        var other = (await SignIn(sessions, "samia", "1357", terminal: "till-2")).SessionToken!;
        var given = (await Authorise(sessions, token, "samia", "1357")).Authorisation;

        Assert.Null(sessions.AuthorisedBy(other, given, Capability.ApplyDiscount));
        Assert.Null(sessions.AuthorisedBy(token, given, Capability.VoidTransaction));

        sessions.SignOut(token);
        Assert.Null(sessions.AuthorisedBy(token, given, Capability.ApplyDiscount));
    }

    [Fact]
    public async Task An_approval_stays_with_the_till_when_the_cashier_changes()
    {
        // D-105: the ticket on hold with a manager's discount is paid by whoever takes the till next.
        // Tied to the session, the approval died at the change and the ticket could be neither paid
        // nor cancelled (block B review).
        var (sessions, token) = await CashierSignedIn();
        var given = (await Authorise(sessions, token, "samia", "1357")).Authorisation;

        sessions.SignOut(token);
        var next = (await SignIn(sessions, "samia", "1357")).SessionToken!;

        Assert.Equal("samia", sessions.AuthorisedBy(next, given, Capability.ApplyDiscount));

        // Still the till's alone, and for what it was given.
        var elsewhere = (await SignIn(sessions, "nabil", "4821", terminal: "till-2")).SessionToken!;
        Assert.Null(sessions.AuthorisedBy(elsewhere, given, Capability.ApplyDiscount));
        Assert.Null(sessions.AuthorisedBy(next, given, Capability.OverridePrice));
    }

    [Fact]
    public async Task An_approval_stands_for_the_stores_day_it_was_given_on()
    {
        // As a cashier's view of past tickets does (D-089): the day, not a count of hours. Given at
        // 08:00, it stands at 23:59:59 and is gone at midnight.
        var (sessions, token) = await CashierSignedIn();
        var given = (await Authorise(sessions, token, "samia", "1357")).Authorisation;

        _clock.Now = new DateTimeOffset(2026, 9, 24, 23, 59, 59, TimeSpan.Zero);
        Assert.Equal("samia", sessions.AuthorisedBy(token, given, Capability.ApplyDiscount));

        _clock.Now = new DateTimeOffset(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);
        Assert.Null(sessions.AuthorisedBy(token, given, Capability.ApplyDiscount));
    }

    [Fact]
    public async Task The_day_an_approval_stands_for_is_the_stores_own_not_utcs()
    {
        // A shop an hour ahead of UTC: 23:30 on its clock is 22:30 UTC. Forty minutes later it is
        // 00:10 there, another day, though UTC is still on the same one.
        var (sessions, token) = await CashierSignedIn();
        sessions.Zone = TimeZoneInfo.CreateCustomTimeZone("store", TimeSpan.FromHours(1), "store", "store");
        _clock.Now = new DateTimeOffset(2026, 9, 24, 22, 30, 0, TimeSpan.Zero);
        var given = (await Authorise(sessions, token, "samia", "1357")).Authorisation;
        Assert.Equal("samia", sessions.AuthorisedBy(token, given, Capability.ApplyDiscount));

        _clock.Now = new DateTimeOffset(2026, 9, 24, 23, 10, 0, TimeSpan.Zero);

        Assert.Null(sessions.AuthorisedBy(token, given, Capability.ApplyDiscount));
    }
}
