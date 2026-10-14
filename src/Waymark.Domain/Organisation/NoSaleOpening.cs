namespace Waymark.Domain.Organisation;

/// <summary>
/// Maps to <c>no_sale_openings</c>, append-only (D-123): the drawer opened with no sale, no refund
/// and no cash movement behind it. The shrinkage-audit point of <c>System_Architecture</c> §2: every
/// one has a person, a reason and, where the person may not open it alone, whose PIN allowed it.
/// </summary>
public sealed class NoSaleOpening : IStoreScoped
{
    /// <summary>Primary key (<c>no_sale_id</c>).</summary>
    public required string NoSaleId { get; init; }

    public required string StoreId { get; init; }

    /// <summary>The cash session it happened in: a drawer is not opened with the session shut (D-111).</summary>
    public required string SessionId { get; init; }

    public required string TerminalId { get; init; }

    /// <summary>Who opened it: the session's person.</summary>
    public required string StaffId { get; init; }

    /// <summary>Whose PIN allowed it, when who opened may not alone; null when they may.</summary>
    public string? AuthorisedBy { get; init; }

    /// <summary>An active <c>no_sale</c> reason.</summary>
    public required string ReasonCode { get; init; }

    public string? Note { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
