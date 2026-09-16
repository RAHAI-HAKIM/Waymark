
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;

namespace Waymark.Domain.Ledgers;

public class ReceivableMovement
{
    /// <summary>Primary key (<c>movement_id</c>).</summary>
    public required string MovementId {get; init;}
    public required string StoreId {get; init;}
    public required string CustomerId {get; init;}
    public required ReceivableMovementType MovementType {get; init;}
    public required Money Amount { get; init; }
    public required DateTimeOffset OccurredAt { get; init; }
    public string? PaymentId {get; init;}
    public string? ReasonCode { get; init; }
    public string? StaffId {get; init;}

}
