using Waymark.Contracts.Reference;
using Waymark.Domain.Enums;

namespace Waymark.StoreServer.Sales;

/// <summary>A cash movement's request as the handler reads it (B10, D-102). Pure, so it is tested without a server.</summary>
public static class CashWire
{
    /// <summary>
    /// Which way the cash goes: <c>in</c> or <c>out</c>, as the wire spells them. Anything else is
    /// null, and nothing is recorded: a word nobody knows is not cash in (D-079's rule for a kind
    /// nobody knows). It was read as cash in, whatever it said (block B review).
    /// </summary>
    public static CashDirection? Direction(string? direction) => direction switch
    {
        CashDirections.In => CashDirection.In,
        CashDirections.Out => CashDirection.Out,
        _ => null,
    };
}
