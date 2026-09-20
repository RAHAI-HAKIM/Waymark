using Waymark.Domain.Work;

namespace Waymark.Persistence;

/// <summary>
/// <see cref="IUnitOfWork"/> over <see cref="WaymarkDbContext"/>.
///
/// <para>
/// <b>No explicit transaction, and that is not an omission.</b> EF Core's
/// <c>SaveChanges</c> already wraps everything staged in one transaction and
/// rolls it all back on failure, which is exactly what §3.6 asks for. Opening
/// one by hand would add a second nested scope and, on SQLite, a second place
/// for a busy-timeout to surface.
/// </para>
/// <para>
/// Migrations are the exception that proves it: <c>Migrate()</c> manages its own
/// transaction and throws inside an ambient one (§3.7), so nothing here may wrap
/// it.
/// </para>
/// </summary>
public sealed class WaymarkUnitOfWork(WaymarkDbContext context) : IUnitOfWork, IStaging
{
    /// <summary>Tracks the row as added; <see cref="CommitAsync"/> writes it, <see cref="Discard"/> drops it.</summary>
    public void Add<TRow>(TRow row)
        where TRow : class => context.Add(row);

    public Task<int> CommitAsync(CancellationToken cancellationToken = default) =>
        context.SaveChangesAsync(cancellationToken);

    /// <summary>
    /// Detaches every tracked entity. A failed <c>SaveChanges</c> rolls the
    /// transaction back but leaves the entities tracked, so without this the
    /// next commit on the same context would write them.
    /// </summary>
    public void Discard() => context.ChangeTracker.Clear();
}
