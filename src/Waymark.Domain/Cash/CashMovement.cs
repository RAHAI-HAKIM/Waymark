namespace Waymark.Domain.Cash;

/// <summary>
/// Money into or out of the drawer that is not a sale: petty cash, a safe drop,
/// the opening float.
///
/// <para>
/// Append-only in the database — <c>trg_cash_movements</c>' siblings on
/// <c>stock_movements</c> and <c>consent_events</c> follow the same pattern, and
/// the tests assert the behaviour rather than the trigger's presence.
/// </para>
/// </summary>
public sealed class CashMovement
{
    /// <summary>ULID, generated in application code. Never a database identity.</summary>
    public required string MovementId { get; init; }

    public required string SessionId { get; init; }

    public required CashMovementType MovementType { get; init; }

    /// <summary>
    /// Centimes, always positive; the direction is carried by
    /// <see cref="MovementType"/>, not by the sign.
    ///
    /// <para>
    /// <b>Provisional.</b> The schema's own conventions say money is "mapped to
    /// decimal in C# through the Money value object", and CLAUDE.md §3.1 agrees.
    /// <c>Money</c> does not exist yet — its rounding, currency and negative
    /// rules are O-4, and not mine to guess. When it lands this property becomes
    /// <c>Money Amount</c> and the configuration gains one
    /// <c>HasConversion</c> line. The column does not change.
    /// </para>
    /// <para>
    /// <c>long</c> until then, never <c>int</c>. The scaffolder's default of
    /// <c>int</c> overflows at 21,474,836.47 DZD.
    /// </para>
    /// </summary>
    public required long AmountCentimes { get; init; }

    public required string ReasonCode { get; init; }

    public string? Note { get; init; }

    public required string StaffId { get; init; }

    /// <summary>Set when a movement needed a manager's PIN.</summary>
    public string? AuthorisedBy { get; init; }

    public required DateTimeOffset OccurredAt { get; init; }
}
