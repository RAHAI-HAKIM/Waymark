namespace Waymark.Domain.Organisation;

/// <summary>
/// Who may sign in at a till, and the stored PIN hash to check them against (session A5, D-083).
///
/// <para>
/// <b>The same people everywhere</b>: active staff of this store whose role is an active row —
/// the people <see cref="ITillDirectory"/> names and the recommendation board gives a rank to.
/// Anyone else cannot sign in, however right their PIN, rather than signing in with no rank.
/// Store scoping is the global filter (CLAUDE.md §3.3).
/// </para>
/// </summary>
public interface IStaffCredentials
{
    /// <summary>Everybody who may open a till, by name. No hash leaves this call; only whether one is set.</summary>
    Task<IReadOnlyList<SignInCandidate>> CandidatesAsync(CancellationToken cancellationToken = default);

    /// <summary>The stored hash to verify a PIN against; null when this is nobody who may sign in.</summary>
    Task<string?> PinHashAsync(string staffId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages a new <c>pin_hash</c> for an active staff member, written at commit (D-050).
    /// </summary>
    /// <returns>False, staging nothing, when this store has no such active staff member.</returns>
    Task<bool> StagePinHashAsync(string staffId, string pinHash, DateTimeOffset at, CancellationToken cancellationToken = default);
}

/// <summary>One person the sign-in screen lists.</summary>
/// <param name="HasPin">Whether their stored hash could ever verify (<see cref="StaffPin.IsUsable"/>).</param>
public sealed record SignInCandidate(string StaffId, string StaffName, string RoleLabelFr, string RoleLabelAr, bool HasPin);
