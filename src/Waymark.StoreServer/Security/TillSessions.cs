using System.Security.Cryptography;
using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;

namespace Waymark.StoreServer.Security;

/// <summary>A till somebody has signed in at: who, where, and since when.</summary>
public sealed record SignedInTill(string StaffId, string TerminalId, DateTimeOffset Since);

/// <summary>
/// Who is signed in at which till, and who is locked out (session A5, D-083). One per server, in
/// memory.
///
/// <para>
/// <b>What the memory costs, knowingly.</b> A restart ends every session (each till asks for a
/// PIN again, and keeps its ticket) and forgets every lockout. Nothing about a sign-in is written to
/// the store database; a table for it would be a schema decision, and this one was not taken.
/// </para>
/// <para>
/// <b>The rules</b>, each argued in <c>TillSessionsTests</c>:
/// </para>
/// <list type="bullet">
///   <item><description>A token is 32 random bytes from the operating system's generator, and it
///   is the only thing a sale needs to name its seller: the server believes the session, never
///   the till.</description></item>
///   <item><description>One session per till. Signing in at a till ends whoever was signed in
///   there; the same person may be signed in at two tills.</description></item>
///   <item><description>A locked person is not checked at all, right PIN or not, and the attempt
///   changes nothing (<see cref="SignInLockout"/>). A person with no PIN is refused before the
///   lockout counts anything: there is nothing to guess.</description></item>
///   <item><description><b>One sign-in at a time.</b> An Argon2 check takes a tenth of a second;
///   without the gate, ten guesses sent together would all be checked before the fifth locked
///   anything.</description></item>
/// </list>
/// </summary>
public sealed class TillSessions(IPinHasher hasher, TimeProvider clock) : IDisposable
{
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);
    private readonly Lock _gate = new();
    private readonly Dictionary<string, SignedInTill> _byToken = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _tokenByTerminal = new(StringComparer.Ordinal);
    private readonly Dictionary<string, SignInLockout> _lockouts = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Authorised> _authorisations = new(StringComparer.Ordinal);

    /// <summary>What an authorisation stands for: who gave it, for what, and in which till's session (B4).</summary>
    private sealed record Authorised(string StaffId, string SessionToken, Capability Capability);

    /// <summary>
    /// Checks a PIN and, when it is right, opens a session at the till.
    /// </summary>
    /// <param name="terminalKnown">Whether this store has the terminal; asked by the caller, which has the database.</param>
    public async Task<SignInAnswer> SignInAsync(
        SignInRequest request, bool terminalKnown, IStaffCredentials credentials, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);

        if (!terminalKnown)
        {
            return Answer(SignInOutcomes.UnknownTerminal);
        }

        var check = await CheckPinAsync(request.StaffId, request.Pin, credentials, cancellationToken);
        return check.Outcome == SignInOutcomes.SignedIn
            ? Answer(SignInOutcomes.SignedIn, token: Open(request.StaffId, request.TerminalId, clock.GetUtcNow()))
            : check;
    }

    /// <summary>
    /// Whether the seller at this till may do <paramref name="capability"/>, or the manager whose
    /// PIN is typed may (session B4, D-091). The same PIN check and the same lockout as signing in:
    /// a manager's PIN is guessed no more easily at the counter than at the sign-in screen.
    /// </summary>
    /// <param name="rankOf">A person's <c>roles.rank</c>, null when they have none (D-037).</param>
    public async Task<AuthoriseAnswer> AuthoriseAsync(
        string? sessionToken,
        AuthoriseRequest request,
        Capability capability,
        IStaffCredentials credentials,
        Func<string, Task<long?>> rankOf,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(credentials);
        ArgumentNullException.ThrowIfNull(rankOf);

        if (Resolve(sessionToken) is not { } session)
        {
            return Refused(AuthoriseOutcomes.NotSignedIn);
        }

        // Asked without a PIN: the seller alone, or a PIN is needed. Never compared here (§3.10).
        if (string.IsNullOrWhiteSpace(request.StaffId))
        {
            return StaffPermissions.May(await rankOf(session.StaffId), capability)
                ? Grant(session.StaffId, sessionToken!, capability)
                : Refused(AuthoriseOutcomes.PinRequired);
        }

        var check = await CheckPinAsync(request.StaffId, request.Pin, credentials, cancellationToken);
        if (check.Outcome != SignInOutcomes.SignedIn)
        {
            return new AuthoriseAnswer(check.Outcome, null, null, check.LockedUntil, check.AttemptsLeft);
        }

        return StaffPermissions.May(await rankOf(request.StaffId), capability)
            ? Grant(request.StaffId, sessionToken!, capability)
            : Refused(AuthoriseOutcomes.NotAllowed);
    }

    /// <summary>
    /// Who gave <paramref name="authorisation"/>, when it was given for <paramref name="capability"/>
    /// in this session; null otherwise. An authorisation from another till, or from a session since
    /// ended, authorises nothing.
    /// </summary>
    public string? AuthorisedBy(string? sessionToken, string? authorisation, Capability capability)
    {
        if (string.IsNullOrEmpty(sessionToken) || string.IsNullOrEmpty(authorisation) || Resolve(sessionToken) is null)
        {
            return null;
        }

        lock (_gate)
        {
            return _authorisations.TryGetValue(authorisation, out var given)
                && given.Capability == capability
                && string.Equals(given.SessionToken, sessionToken, StringComparison.Ordinal)
                    ? given.StaffId
                    : null;
        }
    }

    /// <summary>
    /// The PIN check sign-in and authorisation share: the person, a usable PIN, the lockout asked
    /// first, then the hash. <see cref="SignInOutcomes.SignedIn"/> means the PIN was right.
    /// </summary>
    private async Task<SignInAnswer> CheckPinAsync(string? staffId, string? pin, IStaffCredentials credentials, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(staffId)
            || await credentials.PinHashAsync(staffId, cancellationToken) is not { } storedHash)
        {
            return Answer(SignInOutcomes.UnknownStaff);
        }

        if (!StaffPin.IsUsable(storedHash))
        {
            return Answer(SignInOutcomes.NoPin);
        }

        await _oneAtATime.WaitAsync(cancellationToken);
        try
        {
            var now = clock.GetUtcNow();
            var lockout = LockoutOf(staffId);

            if (lockout.IsLocked(now))
            {
                return Answer(SignInOutcomes.Locked, lockedUntil: lockout.LockedUntil);
            }

            if (!hasher.Verify(pin ?? string.Empty, storedHash))
            {
                var after = lockout.AfterWrong(now);
                SetLockout(staffId, after);

                return after.IsLocked(now)
                    ? Answer(SignInOutcomes.Locked, lockedUntil: after.LockedUntil)
                    : Answer(SignInOutcomes.WrongPin, attemptsLeft: SignInLockout.WrongBeforeLock - after.WrongInARow);
            }

            SetLockout(staffId, lockout.AfterRight());
            return Answer(SignInOutcomes.SignedIn);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    private AuthoriseAnswer Grant(string staffId, string sessionToken, Capability capability)
    {
        var id = Convert.ToBase64String(RandomNumberGenerator.GetBytes(16)).TrimEnd('=').Replace('+', '-').Replace('/', '_');
        lock (_gate)
        {
            _authorisations[id] = new Authorised(staffId, sessionToken, capability);
        }

        return new AuthoriseAnswer(AuthoriseOutcomes.Authorised, id, null, null, null);
    }

    private static AuthoriseAnswer Refused(string outcome) => new(outcome, null, null, null, null);

    /// <summary>The session a token opened, or null when this server holds no such session.</summary>
    public SignedInTill? Resolve(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return null;
        }

        lock (_gate)
        {
            return _byToken.GetValueOrDefault(token);
        }
    }

    /// <summary>Ends the session a token opened. A token the server does not hold ends nothing, quietly.</summary>
    public void SignOut(string? token)
    {
        if (string.IsNullOrEmpty(token))
        {
            return;
        }

        lock (_gate)
        {
            if (_byToken.Remove(token, out var session)
                && _tokenByTerminal.TryGetValue(session.TerminalId, out var current)
                && current == token)
            {
                _tokenByTerminal.Remove(session.TerminalId);
            }

            // What the session was allowed ends with it.
            foreach (var ended in _authorisations.Where(pair => pair.Value.SessionToken == token).Select(pair => pair.Key).ToList())
            {
                _authorisations.Remove(ended);
            }
        }
    }

    private string Open(string staffId, string terminalId, DateTimeOffset now)
    {
        var token = Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
            .TrimEnd('=').Replace('+', '-').Replace('/', '_');

        lock (_gate)
        {
            // One session per till: whoever was signed in here is signed out.
            if (_tokenByTerminal.Remove(terminalId, out var previous))
            {
                _byToken.Remove(previous);
            }

            _byToken[token] = new SignedInTill(staffId, terminalId, now);
            _tokenByTerminal[terminalId] = token;
        }

        return token;
    }

    private SignInLockout LockoutOf(string staffId)
    {
        lock (_gate)
        {
            return _lockouts.GetValueOrDefault(staffId, SignInLockout.Clear);
        }
    }

    private void SetLockout(string staffId, SignInLockout lockout)
    {
        lock (_gate)
        {
            _lockouts[staffId] = lockout;
        }
    }

    /// <summary>Releases the sign-in gate. The container calls it when the server stops.</summary>
    public void Dispose() => _oneAtATime.Dispose();

    private static SignInAnswer Answer(
        string outcome, string? token = null, DateTimeOffset? lockedUntil = null, int? attemptsLeft = null) =>
        new(outcome, token, lockedUntil, attemptsLeft);
}
