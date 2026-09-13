using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;

namespace Waymark.Application.Commands;

/// <summary>
/// The two things a handler is allowed to reach for that outlast it: new ids,
/// and the processing log.
///
/// <para>
/// Both are here rather than injected into the handler because both are
/// scoped to one unit of work, and a constructor-injected dependency is scoped
/// to the handler. Handlers are cheap to register as singletons and somebody
/// eventually will; an <see cref="IIdGenerator"/> captured in a singleton field
/// is fine, but an <see cref="IProcessingLog"/> staging into whichever
/// transaction happens to be open is not.
/// </para>
/// <para>
/// <b>The context stops working when the handler returns.</b> The executor
/// seals it before committing, and a sealed context throws. A handler that
/// stores the context, starts background work and mints an id afterwards would
/// otherwise get one silently attached to a unit of work that has already
/// closed — the failure CLAUDE.md §3.2 is written against, and the kind that
/// shows up as a foreign key violation days later rather than as an exception
/// at the call site.
/// </para>
/// </summary>
public sealed class CommandContext
{
    private readonly IIdGenerator _ids;
    private readonly IProcessingLog _log;
    private bool _sealed;

    internal CommandContext(IIdGenerator ids, IProcessingLog log)
    {
        _ids = ids;
        _log = log;
    }

    /// <summary>
    /// How many ids this execution minted. Read by tests; a handler has no use
    /// for it.
    /// </summary>
    public int IdsMinted { get; private set; }

    /// <summary>
    /// How many processing-log entries this execution staged.
    /// </summary>
    public int EventsRecorded { get; private set; }

    /// <summary>
    /// A new ULID, for a row this unit of work is about to write.
    ///
    /// <para>
    /// The only id source a handler gets. CLAUDE.md §3.2 bans
    /// <c>Ulid.NewUlid()</c> in an entity constructor — a static call cannot be
    /// substituted, and W10's generator has to be deterministic from a seed
    /// (D-038). Going through the context rather than through an injected
    /// <see cref="IIdGenerator"/> adds the part the port cannot enforce on its
    /// own: the id belongs to <i>this</i> unit of work.
    /// </para>
    /// </summary>
    /// <exception cref="InvalidOperationException">The unit of work has closed.</exception>
    public string NewId()
    {
        ThrowIfSealed(nameof(NewId));
        IdsMinted++;
        return _ids.NewId();
    }

    /// <summary>
    /// Records a personal-data operation. It commits with the work that caused
    /// it, or not at all (D-045).
    /// </summary>
    /// <exception cref="InvalidOperationException">The unit of work has closed.</exception>
    public void Record(in ProcessingEvent processingEvent)
    {
        ThrowIfSealed(nameof(Record));
        EventsRecorded++;
        _log.Record(processingEvent);
    }

    /// <summary>
    /// Closes the context. Called by the executor once the handler has returned
    /// and before anything is committed.
    /// </summary>
    internal void Seal() => _sealed = true;

    private void ThrowIfSealed(string member)
    {
        if (_sealed)
        {
            throw new InvalidOperationException(
                $"{nameof(CommandContext)}.{member} was called after the command had finished. "
                + "A context belongs to one unit of work and stops working when that unit of "
                + "work closes; an id or a log entry produced now would attach to a transaction "
                + "that has already committed (CLAUDE.md §3.2, §3.6). Do the work inside the "
                + "handler, or make it a command of its own.");
        }
    }
}
