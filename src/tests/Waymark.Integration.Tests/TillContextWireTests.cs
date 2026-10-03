using System.Runtime.Versioning;
using System.Text.Json;
using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;
using Waymark.StoreServer.Organisation;

namespace Waymark.Integration.Tests;

/// <summary>The till's description on the wire (session A4). A pure mapping.</summary>
[SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
public sealed class TillContextWireTests
{
    [Fact]
    public void A_found_till_carries_everything_the_top_bar_shows()
    {
        var wire = TillContextWire.ToWire(new TillDescription(
            "El Bahdja", "Caisse 1", "DZD", new StaffDescription("Nabil B.", "Caissier", "أمين الصندوق")));

        Assert.Equal(
            new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق", RoundingPolicies.HalfUp),
            wire);
    }

    [Fact]
    public void Nobody_at_the_till_leaves_the_staff_fields_empty()
    {
        var wire = TillContextWire.ToWire(new TillDescription("El Bahdja", "Caisse 1", "DZD", null));

        Assert.Equal(TillContextOutcome.Found, wire.Outcome);
        Assert.Null(wire.StaffName);
        Assert.Null(wire.RoleLabelFr);
    }

    [Fact]
    public void An_unknown_terminal_says_so_and_names_nothing()
    {
        Assert.Equal(
            new TillContext(TillContextOutcome.UnknownTerminal, null, null, null, null, null, null),
            TillContextWire.ToWire(null));
    }

    [Fact]
    public void The_json_uses_the_contracts_names()
    {
        var json = JsonSerializer.Serialize(TillContextWire.ToWire(
            new TillDescription("El Bahdja", "Caisse 1", "DZD", new StaffDescription("Nabil B.", "Caissier", "x"))));

        foreach (var name in new[] { "outcome", "store_name", "terminal_name", "currency", "staff_name", "role_label_fr", "role_label_ar" })
        {
            Assert.Contains($"\"{name}\":", json, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_tenants_customer_switches_reach_the_till_and_a_tenant_never_configured_keeps_none()
    {
        // B7 (D-096): the till shows a customer key only with the module on, and the tab whole when it may not be a part.
        var description = new TillDescription("El Bahdja", "Caisse 1", "DZD", null);
        var on = TillContextWire.ToWire(description, new TenantSettings(true, null, null, false));
        var never = TillContextWire.ToWire(description, TenantSettings.Defaults);

        Assert.Equal((true, false), (on.CustomerModule, on.TabAsPart));
        Assert.Equal((false, true), (never.CustomerModule, never.TabAsPart));
    }
}
