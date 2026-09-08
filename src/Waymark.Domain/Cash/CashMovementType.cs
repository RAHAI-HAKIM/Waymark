namespace Waymark.Domain.Cash;

/// <summary>
/// Why cash entered or left the drawer outside a sale. Stored as TEXT with a
/// CHECK constraint.
/// </summary>
public enum CashMovementType
{
    PaidIn,
    PaidOut,
    Drop,
    FloatAdd,
    FloatRemove
}
