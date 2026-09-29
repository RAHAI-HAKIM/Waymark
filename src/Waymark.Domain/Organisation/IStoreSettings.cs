namespace Waymark.Domain.Organisation;

/// <summary>
/// The current store's own settings, written by a command (session B3, D-090). Store scoping is the
/// global filter (CLAUDE.md §3.3): the store changed is the one the context is filtered to.
/// </summary>
public interface IStoreSettings
{
    /// <summary>
    /// Stages the store's scale-label format, a preset's name or a custom mask, null for the default;
    /// written when the executor commits (D-050). False when the store has no row.
    /// </summary>
    Task<bool> StageScaleLabelFormatAsync(string? format, DateTimeOffset at, CancellationToken cancellationToken = default);
}
