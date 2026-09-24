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
            new TillContext(TillContextOutcome.Found, "El Bahdja", "Caisse 1", "DZD", "Nabil B.", "Caissier", "أمين الصندوق"),
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
}
