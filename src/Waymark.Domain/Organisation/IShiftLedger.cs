namespace Waymark.Domain.Organisation;

/// <summary>
/// The clock (B10, D-102): who is on duty, as <c>shifts</c> rows. Implemented in Persistence; scoped to
/// the current store by the global filter, so a person clocked in at another store is not on duty here.
/// </summary>
public interface IShiftLedger
{
    /// <summary>The person's open shift at this store, the latest if there were ever two; null when they are not clocked in.</summary>
    Task<Shift?> OpenShiftAsync(string staffId, CancellationToken cancellationToken = default);

    /// <summary>Stages the shift closed at <paramref name="at"/>, written when the executor commits (D-050).</summary>
    Task StageCloseAsync(string shiftId, DateTimeOffset at, CancellationToken cancellationToken = default);
}
