namespace Waymark.Domain.Work;

/// <summary>
/// Where a handler puts the rows it creates (D-050, CLAUDE.md §2.3). Adding here writes
/// nothing: the row waits in the unit of work, and <see cref="IUnitOfWork.CommitAsync"/>
/// writes it with everything else staged by the same command, or <see cref="IUnitOfWork.Discard"/>
/// drops it. A handler never saves.
/// </summary>
public interface IStaging
{
    /// <summary>Stages a new row, to be written at commit.</summary>
    void Add<TRow>(TRow row)
        where TRow : class;
}
