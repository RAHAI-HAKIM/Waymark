namespace Waymark.Domain.Enums;

/// <summary>
/// Which way a <c>cash_movement</c> reason moves money (B10, D-102): stored as TEXT, <c>in</c> or <c>out</c>,
/// with a CHECK; null on a cash reason means either way, and on every other kind of reason it is null.
/// </summary>
public enum CashDirection
{
    /// <summary>Stored as <c>in</c>: money into the drawer, a paid-in (and a tab repaid, D-055).</summary>
    In,

    /// <summary>Stored as <c>out</c>: money out of the drawer, a paid-out.</summary>
    Out,
}
