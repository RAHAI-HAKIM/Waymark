using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;

namespace Waymark.StoreServer.Organisation;

/// <summary>The till's description as the till reads it (session A4). A pure mapping.</summary>
public static class TillContextWire
{
    public static TillContext ToWire(TillDescription? description) => description is null
        ? new TillContext(TillContextOutcome.UnknownTerminal, null, null, null, null, null, null)
        : new TillContext(
            TillContextOutcome.Found,
            description.StoreName,
            description.TerminalName,
            description.Currency,
            description.Staff?.StaffName,
            description.Staff?.RoleLabelFr,
            description.Staff?.RoleLabelAr);
}
