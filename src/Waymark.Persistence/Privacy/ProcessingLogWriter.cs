using Waymark.Domain;
using Waymark.Domain.Customers;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;

namespace Waymark.Persistence.Privacy;

/// <summary>
/// Writes the logbook Loi 25-11 article 41 bis 3 requires (decisions.md D-045).
///
/// <para>
/// It stages rather than saves. The entry joins whatever unit of work is open
/// and commits with it, so an operation that rolled back leaves no evidence
/// claiming it happened, and one that committed cannot have failed to log.
/// </para>
/// <para>
/// <b>Register it scoped, never as a singleton.</b> It holds a
/// <see cref="WaymarkDbContext"/>, and a singleton would stage entries into
/// whichever transaction happened to be open.
/// </para>
/// </summary>
/// <param name="context">The store database.</param>
/// <param name="ids">Where <c>log_id</c> comes from — never a database sequence (§3.2).</param>
/// <param name="currentStore">
/// Which store is acting. The caller does not get to say: see
/// <see cref="ProcessingEvent"/>.
/// </param>
/// <param name="clock">
/// Where <c>occurred_at</c> comes from. Injected rather than
/// <c>DateTimeOffset.UtcNow</c> so W10's generator can write a year of history
/// with the timestamps it simulated, and so a test can assert on a known
/// instant.
/// </param>
public sealed class ProcessingLogWriter(
    WaymarkDbContext context,
    IIdGenerator ids,
    ICurrentStore currentStore,
    TimeProvider clock) : IProcessingLog
{
    public void Record(in ProcessingEvent processingEvent)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(
            processingEvent.SourceModule, nameof(processingEvent));

        context.ProcessingLog.Add(new ProcessingLogEntry
        {
            LogId = ids.NewId(),
            OccurredAt = clock.GetUtcNow(),
            Operation = processingEvent.Operation,
            SubjectType = processingEvent.SubjectType,

            // The only place a subject_id is produced. An undefined Pseudonym
            // is an operation with no subject — a retention sweep, a system
            // task — and writes NULL. It is not a fallback for a pseudonym that
            // could not be computed: computing one cannot fail quietly.
            SubjectId = processingEvent.Subject.IsDefined
                ? processingEvent.Subject.Value
                : null,

            ActorType = processingEvent.ActorType,
            ActorId = processingEvent.ActorId,
            Purpose = processingEvent.Purpose,
            LegalBasis = processingEvent.LegalBasis,
            Recipient = processingEvent.Recipient,
            SourceModule = processingEvent.SourceModule,
            StoreId = currentStore.StoreId,
            TerminalId = processingEvent.TerminalId,
        });
    }
}
