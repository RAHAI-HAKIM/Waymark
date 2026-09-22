using System.Runtime.Versioning;
using System.Text.Json;
using Waymark.Domain.Reference;
using Waymark.StoreServer.Reference;
using DomainAppliesTo = Waymark.Domain.Enums.ReasonCodeAppliesTo;
using WireAppliesTo = Waymark.Contracts.Reference.ReasonCodeAppliesTo;

namespace Waymark.Integration.Tests;

/// <summary>
/// The reason list as a client reads it (session A3). A pure mapping, so none of this starts
/// a server.
/// </summary>
[SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
public sealed class ReasonCodeWireTests
{
    private static ReasonCodeChoice Choice(string code, bool note = false, bool manager = false) =>
        new(code, $"ar {code}", $"fr {code}", note, manager);

    [Fact]
    public void A_list_carries_the_kind_and_its_reasons_in_order()
    {
        var wire = ReasonCodeWire.ToWire(
            DomainAppliesTo.Discount,
            [Choice("DISC-FIDELITE"), Choice("DISC-GESTE")]);

        Assert.Equal(WireAppliesTo.Discount, wire.AppliesTo);
        Assert.Equal(["DISC-FIDELITE", "DISC-GESTE"], wire.ReasonCodes.Select(option => option.Code));
    }

    [Fact]
    public void The_mapping_does_not_reorder()
    {
        // The reader decided the order (display_order, then code). A second sort here would
        // be a second opinion about it, and the two would drift.
        var wire = ReasonCodeWire.ToWire(
            DomainAppliesTo.Void, [Choice("ZEBRA"), Choice("ALPHA")]);

        Assert.Equal(["ZEBRA", "ALPHA"], wire.ReasonCodes.Select(option => option.Code));
    }

    [Fact]
    public void Every_field_crosses()
    {
        var wire = ReasonCodeWire.ToWire(
            DomainAppliesTo.PriceOverride, [Choice("OVR", note: true, manager: true)]);

        var option = Assert.Single(wire.ReasonCodes);
        Assert.Equal("OVR", option.Code);
        Assert.Equal("ar OVR", option.LabelAr);
        Assert.Equal("fr OVR", option.LabelFr);
        Assert.True(option.RequiresNote);
        Assert.True(option.RequiresManager);
    }

    [Fact]
    public void An_empty_list_is_an_empty_list_and_not_a_null()
    {
        var wire = ReasonCodeWire.ToWire(DomainAppliesTo.WriteOff, []);

        Assert.Empty(wire.ReasonCodes);
        Assert.Equal(WireAppliesTo.WriteOff, wire.AppliesTo);
    }

    public static TheoryData<DomainAppliesTo, string> Kinds() => new()
    {
        { DomainAppliesTo.Discount, WireAppliesTo.Discount },
        { DomainAppliesTo.PriceOverride, WireAppliesTo.PriceOverride },
        { DomainAppliesTo.Adjustment, WireAppliesTo.Adjustment },
        { DomainAppliesTo.Void, WireAppliesTo.Void },
        { DomainAppliesTo.Return, WireAppliesTo.Return },
        { DomainAppliesTo.NoSale, WireAppliesTo.NoSale },
        { DomainAppliesTo.CashMovement, WireAppliesTo.CashMovement },
        { DomainAppliesTo.WriteOff, WireAppliesTo.WriteOff },
    };

    [Theory]
    [MemberData(nameof(Kinds))]
    public void Each_kind_has_a_name_and_reads_back_as_itself(DomainAppliesTo kind, string name)
    {
        Assert.Equal(name, ReasonCodeWire.Name(kind));
        Assert.Equal(kind, ReasonCodeWire.Parse(name));
    }

    [Fact]
    public void Every_kind_the_schema_allows_has_a_name()
    {
        // A kind added to the enum without a wire name would throw at the first client that
        // asked for it.
        Assert.Equal(Enum.GetValues<DomainAppliesTo>().Length, Kinds().Count);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("  ")]
    [InlineData("Discount")]
    [InlineData("discounts")]
    [InlineData("price-override")]
    [InlineData("nonsense")]
    public void A_kind_nobody_knows_does_not_parse(string? asked)
    {
        // Null, not a guess and not an empty list. "No reasons configured" and "no such kind"
        // are different answers, and a typo that produced the first would look like a shop
        // that never set anything up. Case matters: the wire names are the CHECK's values.
        Assert.Null(ReasonCodeWire.Parse(asked));
    }

    [Fact]
    public void The_json_uses_the_contracts_names()
    {
        var json = JsonSerializer.Serialize(ReasonCodeWire.ToWire(
            DomainAppliesTo.CashMovement, [Choice("CAISSE-APPORT", note: true)]));

        Assert.Contains("\"applies_to\":\"cash_movement\"", json, StringComparison.Ordinal);
        Assert.Contains("\"code\":\"CAISSE-APPORT\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label_ar\":\"ar CAISSE-APPORT\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label_fr\":\"fr CAISSE-APPORT\"", json, StringComparison.Ordinal);
        Assert.Contains("\"requires_note\":true", json, StringComparison.Ordinal);
        Assert.Contains("\"requires_manager\":false", json, StringComparison.Ordinal);
    }
}
