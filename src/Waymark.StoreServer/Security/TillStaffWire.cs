using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;

namespace Waymark.StoreServer.Security;

/// <summary>
/// What a till may ask an approval for, and who may give it. Pure, so it is tested without a
/// server. The rank is never compared here (§3.10): <see cref="StaffPermissions"/> is asked.
/// </summary>
public static class TillStaffWire
{
    /// <summary>A capability as the wire names it; null for one the server does not know.</summary>
    public static Capability? Capability(string? capability) => capability switch
    {
        Capabilities.ApplyDiscount => Domain.Organisation.Capability.ApplyDiscount,
        Capabilities.OverridePrice => Domain.Organisation.Capability.OverridePrice,
        Capabilities.CreateCustomer => Domain.Organisation.Capability.CreateCustomer,
        Capabilities.ManageCredit => Domain.Organisation.Capability.ManageCredit,
        Capabilities.VoidTransaction => Domain.Organisation.Capability.VoidTransaction,
        Capabilities.Refund => Domain.Organisation.Capability.Refund,
        Capabilities.PaidOut => Domain.Organisation.Capability.PaidOut,
        Capabilities.ViewOtherTickets => Domain.Organisation.Capability.ViewOtherTickets,
        Capabilities.CloseSession => Domain.Organisation.Capability.CloseSession,
        Capabilities.ReadXReport => Domain.Organisation.Capability.ReadXReport,
        _ => null,
    };

    /// <summary>
    /// What the shop set a capability to: a refund, a paid-out, closing the drawer and reading it have
    /// a tenant setting (B9, B10, C1; D-110); everything else is the ladder alone, null.
    /// </summary>
    public static long? RaisedTo(Capability capability, TenantSettings settings)
    {
        ArgumentNullException.ThrowIfNull(settings);
        return capability switch
        {
            Domain.Organisation.Capability.Refund => settings.RefundMinRank,
            Domain.Organisation.Capability.PaidOut => settings.PaidOutMinRank,
            Domain.Organisation.Capability.CloseSession => settings.CloseSessionMinRank,
            Domain.Organisation.Capability.ReadXReport => settings.XReportMinRank,
            _ => null,
        };
    }

    /// <summary>
    /// Whether a person of this rank could approve the capability with their PIN: what the manager
    /// step lists (block B review: it listed every cashier, whose PIN would then be refused).
    /// </summary>
    public static bool MayApprove(long? rank, Capability capability, TenantSettings settings) =>
        RaisedTo(capability, settings) is { } raisedTo
            ? StaffPermissions.May(rank, capability, raisedTo)
            : StaffPermissions.May(rank, capability);
}
