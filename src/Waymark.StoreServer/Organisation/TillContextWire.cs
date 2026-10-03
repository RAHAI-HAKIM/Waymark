using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;

namespace Waymark.StoreServer.Organisation;

/// <summary>The till's description as the till reads it (session A4). A pure mapping.</summary>
public static class TillContextWire
{
    /// <param name="settings">The tenant's (D-096): whether the till shows a customer key, and whether the tab may be a part.</param>
    public static TillContext ToWire(TillDescription? description, TenantSettings? settings = null) => description is null
        ? new TillContext(TillContextOutcome.UnknownTerminal, null, null, null, null, null, null)
        : new TillContext(
            TillContextOutcome.Found,
            description.StoreName,
            description.TerminalName,
            description.Currency,
            description.Staff?.StaffName,
            description.Staff?.RoleLabelFr,
            description.Staff?.RoleLabelAr,
            description.RoundingPolicy == Domain.Values.Rounding.HalfEven ? RoundingPolicies.HalfEven : RoundingPolicies.HalfUp,
            settings?.CustomerModule ?? false,
            settings?.TabAsPart ?? true);
}
