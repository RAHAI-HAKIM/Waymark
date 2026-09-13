namespace Waymark.Domain.Privacy;

/// <summary>
/// Where a personal-data operation is recorded. The signature is the privacy
/// promise (decisions.md D-045).
///
/// <para>
/// Declared in Domain and implemented in <c>Waymark.Persistence</c>, like every
/// other port here. It is named by <c>Waymark.Application</c>, which is where
/// §4 puts the write — "at every access site", and in one place rather than
/// scattered through handlers.
/// </para>
/// <para>
/// <b>There is exactly one method and it takes a
/// <see cref="ProcessingEvent"/>.</b> No overload taking a customer id, no
/// overload taking a string subject, no convenience method that fills in a
/// purpose. Each of those would be a route to a log row that names a person
/// directly, and the column is <c>TEXT</c> either way — so the mistake would
/// cost nothing at write time and everything at audit time.
/// <c>ProcessingLogContractTests</c> asserts the shape rather than trusting this
/// paragraph.
/// </para>
/// </summary>
public interface IProcessingLog
{
    /// <summary>
    /// Stages one entry. It is written when the surrounding unit of work
    /// commits, and not before.
    ///
    /// <para>
    /// <b>Staged rather than written immediately, and that is the whole
    /// point.</b> An operation that rolled back but logged claims processing
    /// that never happened; one that committed but failed to log is processing
    /// with no trace. Both are wrong in the direction a regulator asks about, so
    /// the log row commits with the operation it records or not at all — the
    /// same rule §3.6 applies to the outbox, for the same reason.
    /// </para>
    /// <para>
    /// A consultation that reads and writes nothing else still commits: the
    /// unit of work has one row in it, and that row is the evidence.
    /// </para>
    /// </summary>
    void Record(in ProcessingEvent processingEvent);
}
