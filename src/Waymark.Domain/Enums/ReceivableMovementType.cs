namespace Waymark.Domain.Enums;

/// <summary>
/// Stored as TEXT with a CHECK constraint: 'charge', 'payment', 'adjustment', 'write_off'.
/// </summary>
public enum ReceivableMovementType
{
    /// <summary>Stored as <c>charge</c>. Mirrors an <c>on_account</c> payment row, sign included.</summary>
    Charge,

    /// <summary>Stored as <c>payment</c>. A repayment; always negative.</summary>
    Payment,

    /// <summary>Stored as <c>adjustment</c>. Either sign, with a reason.</summary>
    Adjustment,

    /// <summary>Stored as <c>write_off</c>. Debt the store gives up on; negative, with a reason.</summary>
    WriteOff
}
