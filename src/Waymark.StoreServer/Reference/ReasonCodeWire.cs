using System.Diagnostics;
using Waymark.Contracts.Reference;
using Waymark.Domain.Reference;
using DomainAppliesTo = Waymark.Domain.Enums.ReasonCodeAppliesTo;
using WireAppliesTo = Waymark.Contracts.Reference.ReasonCodeAppliesTo;

namespace Waymark.StoreServer.Reference;

/// <summary>
/// The reason list as a client reads it (session A3). A pure function, so the whole mapping
/// is tested without starting a server.
/// </summary>
public static class ReasonCodeWire
{
    /// <summary>
    /// The kind a client asked for, or null if it is not one this shop knows. Null is a bad
    /// request, not an empty list: "no reasons for adjustments" and "there is no such thing
    /// as an adjustment" are different answers, and collapsing them would let a typo look
    /// like a shop that never configured anything.
    /// </summary>
    public static DomainAppliesTo? Parse(string? appliesTo) => appliesTo switch
    {
        WireAppliesTo.Discount => DomainAppliesTo.Discount,
        WireAppliesTo.PriceOverride => DomainAppliesTo.PriceOverride,
        WireAppliesTo.Adjustment => DomainAppliesTo.Adjustment,
        WireAppliesTo.Void => DomainAppliesTo.Void,
        WireAppliesTo.Return => DomainAppliesTo.Return,
        WireAppliesTo.NoSale => DomainAppliesTo.NoSale,
        WireAppliesTo.CashMovement => DomainAppliesTo.CashMovement,
        WireAppliesTo.WriteOff => DomainAppliesTo.WriteOff,
        _ => null,
    };

    /// <summary>The name a client uses for this kind.</summary>
    public static string Name(DomainAppliesTo appliesTo) => appliesTo switch
    {
        DomainAppliesTo.Discount => WireAppliesTo.Discount,
        DomainAppliesTo.PriceOverride => WireAppliesTo.PriceOverride,
        DomainAppliesTo.Adjustment => WireAppliesTo.Adjustment,
        DomainAppliesTo.Void => WireAppliesTo.Void,
        DomainAppliesTo.Return => WireAppliesTo.Return,
        DomainAppliesTo.NoSale => WireAppliesTo.NoSale,
        DomainAppliesTo.CashMovement => WireAppliesTo.CashMovement,
        DomainAppliesTo.WriteOff => WireAppliesTo.WriteOff,
        _ => throw new UnreachableException($"A reason kind this mapping does not know: {appliesTo}."),
    };

    public static ReasonCodeList ToWire(DomainAppliesTo appliesTo, IReadOnlyList<ReasonCodeChoice> choices)
    {
        ArgumentNullException.ThrowIfNull(choices);

        // The order the reader returned is the order offered, so this maps and does not sort.
        return new ReasonCodeList(
            Name(appliesTo),
            [.. choices.Select(choice => new ReasonCodeOption(
                choice.Code,
                choice.LabelAr,
                choice.LabelFr,
                choice.RequiresNote,
                choice.RequiresManager))]);
    }
}
