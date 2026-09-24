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

        if (string.IsNullOrWhiteSpace(request.StaffId)
            || await credentials.PinHashAsync(request.StaffId, cancellationToken) is not { } storedHash)
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
            var lockout = LockoutOf(request.StaffId);

            if (lockout.IsLocked(now))
            {
                return Answer(SignInOutcomes.Locked, lockedUntil: lockout.LockedUntil);
            }

            if (!hasher.Verify(request.Pin ?? string.Empty, storedHash))
            {
                var after = lockout.AfterWrong(now);
                SetLockout(request.StaffId, after);

                return after.IsLocked(now)
                    ? Answer(SignInOutcomes.Locked, lockedUntil: after.LockedUntil)
                    : Answer(SignInOutcomes.WrongPin, attemptsLeft: SignInLockout.WrongBeforeLock - after.WrongInARow);
            }

            SetLockout(request.StaffId, lockout.AfterRight());
            return Answer(SignInOutcomes.SignedIn, token: Open(request.StaffId, request.TerminalId, now));
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

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
