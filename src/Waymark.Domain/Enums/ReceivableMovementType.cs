

namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'charge', 'payment', 'adjustment', 'write_off'.
/// </summary>
public enum ReceivableMovementType
{
    /// <summary>Stored as <c>charge</c>.</summary>
    Charge,

    /// <summary>Stored as <c>payment</c>.</summary>
    Payment,

    /// <summary>Stored as <c>adjustment</c>.</summary>
    Adjustment,

    /// <summary>Stored as <c>write_off</c>.</summary>
    WriteOff
}
