using Microsoft.EntityFrameworkCore;
using Waymark.Application.Sales;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Persistence.Sales;
using Waymark.StoreServer.Sales;
using RefusalCodes = Waymark.Contracts.Pos.RefusalCodes;

namespace Waymark.Integration.Tests;

/// <summary>
/// D2 (D-123, D-126): the drawer opened with nothing sold, against a real database. The silent
/// failures: a drawer opened with no row behind it; a cashier opening it with nobody's PIN; a no-sale
/// counted nowhere, or its threshold changed later rewriting old Zs; the names of who opened it sent to
/// someone who is not the owner.
/// </summary>
public sealed partial class CompleteSaleTests
{
    private const string Change = "tiroir-monnaie";

    private const string OtherOpening = "tiroir-autre";

    /// <summary>The shop's no-sale reasons: one plain, one that asks for a note.</summary>
    private void NoSaleReasons()
    {
        using var context = database.NewContext();
        foreach (var (code, note) in new[] { (Change, false), (OtherOpening, true) })
        {
            if (!context.ReasonCodes.Any(reason => reason.ReasonCodeValue == code))
            {
                context.ReasonCodes.Add(new ReasonCode
                {
                    ReasonCodeValue = code, AppliesTo = ReasonCodeAppliesTo.NoSale, LabelAr = code, LabelFr = $"Libellé {code}",
                    RequiresNote = note, RequiresManager = true, CreatedAt = Now,
                });
            }
        }

        context.SaveChanges();
    }

    private Task<RecordedNoSale> OpenWithNoSale(Shop shop, string reason = Change, string? note = null, bool sellerMay = false, string? authorisedBy = "staff-manager", string? by = null) =>
        Run(shop, (context, work) => new RecordNoSaleHandler(new SalesLedger(context), work, new Waymark.Persistence.Reference.ReasonCodes(context), new FixedClock()),
            new RecordNoSale(shop.TerminalId, by ?? shop.StaffId, reason, note, sellerMay, sellerMay ? null : authorisedBy));

    [Fact]
    public async Task A_no_sale_is_one_row_on_the_open_session_naming_whose_pin_allowed_it()
    {
        NoSaleReasons();
        var shop = new Shop(database);

        var opened = await OpenWithNoSale(shop, authorisedBy: shop.StaffId);
        await OpenWithNoSale(shop, sellerMay: true);

        using var read = Read(shop);
        var rows = await read.NoSaleOpenings.OrderBy(row => row.OccurredAt).ThenBy(row => row.NoSaleId).ToListAsync();
        Assert.Equal(2, rows.Count);
        var allowed = rows.Single(row => row.NoSaleId == opened.NoSaleId);
        Assert.Equal(($"session-{shop.StoreId[6..]}", shop.TerminalId, shop.StaffId, shop.StaffId, Change, Now),
            (allowed.SessionId, allowed.TerminalId, allowed.StaffId, allowed.AuthorisedBy, allowed.ReasonCode, allowed.OccurredAt));
        Assert.Null(rows.Single(row => row.NoSaleId != opened.NoSaleId).AuthorisedBy); // who may alone is nobody's approval
    }

    [Fact]
    public async Task A_no_sale_is_refused_and_writes_nothing_without_a_pin_a_reason_its_note_or_an_open_drawer()
    {
        NoSaleReasons();
        CashReasons();
        var shop = new Shop(database);

        async Task<string?> Refused(Func<Task> attempt) => (await Assert.ThrowsAsync<CashMovementRefusedException>(attempt)).Code;

        Assert.Equal(RefusalCodes.NoSaleNeedsManager, await Refused(() => OpenWithNoSale(shop, authorisedBy: null)));
        Assert.Equal(RefusalCodes.ReasonUnknown, await Refused(() => OpenWithNoSale(shop, reason: "caisse-apport", sellerMay: true)));
        Assert.Equal(RefusalCodes.NoteMissing, await Refused(() => OpenWithNoSale(shop, reason: OtherOpening, note: "  ", sellerMay: true)));
        Assert.Equal(RefusalCodes.NoteTooLong, await Refused(() => OpenWithNoSale(shop, reason: OtherOpening, note: new string('x', SaleLimits.MaxNoteLength + 1), sellerMay: true)));

        var shut = new Shop(database, drawerOpen: false);
        Assert.Equal(RefusalCodes.NoOpenSession, await Refused(() => OpenWithNoSale(shut, sellerMay: true)));

        using var read = database.NewContext();
        Assert.False(await read.NoSaleOpenings.AnyAsync(row => row.StoreId == shop.StoreId || row.StoreId == shut.StoreId));
    }

    [Fact]
    public async Task No_sales_are_counted_on_the_x_and_the_z_flagged_past_the_threshold_frozen_at_the_close()
    {
        NoSaleReasons();
        var shop = new Shop(database);
        await OpenWithNoSale(shop, sellerMay: true);
        await OpenWithNoSale(shop, reason: OtherOpening, note: "Billet tombé", sellerMay: true);

        // The X reads today's threshold: one a person, and two are past it.
        var x = (await ReadX(shop, new ReportSettings(noSaleCount: 1)))!;
        Assert.Equal(2, x.NoSales!.Count);
        var flag = Assert.Single(x.Flags, f => f.Kind == ShiftFlagKind.TooManyNoSales);
        Assert.Equal((shop.StaffId, 2), (flag.StaffId, flag.Count));
        Assert.Equal(2, Assert.Single(x.Cashiers).NoSales);
        Assert.True(x.CashiersReconcile);

        // Closed under a threshold of one, read under none: the Z keeps the one it closed under (D-112).
        var closed = await CloseWith(shop, 0, new ReportSettings(noSaleCount: 1));
        var z = (await ReadZ(shop, closed.ZReportNumber, new ReportSettings()))!;
        Assert.Equal(1, z.Thresholds.NoSaleCount);
        Assert.Contains(z.Flags, f => f.Kind == ShiftFlagKind.TooManyNoSales);

        // And none, nothing flagged.
        Assert.DoesNotContain((await ReadX(new Shop(database), new ReportSettings()))?.Flags ?? [], f => f.Kind == ShiftFlagKind.TooManyNoSales);
    }

    [Fact]
    [System.Runtime.Versioning.SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
    public async Task The_count_goes_to_every_reader_and_each_opening_only_to_who_reads_the_flags()
    {
        NoSaleReasons();
        var shop = new Shop(database);
        await OpenWithNoSale(shop, reason: OtherOpening, note: "Billet tombé", authorisedBy: shop.StaffId);
        var x = (await ReadX(shop, new ReportSettings()))!;

        var manager = ShiftReportEndpoints.ToWire(x, id => id is null ? null : "Caissier", readsFlags: false, Now);
        Assert.Equal((1, (IReadOnlyList<Waymark.Contracts.Pos.NoSaleRowWire>?)null), (manager.NoSales!.Count, manager.NoSales.Openings));

        var owner = ShiftReportEndpoints.ToWire(x, id => id is null ? null : "Caissier", readsFlags: true, Now);
        var row = Assert.Single(owner.NoSales!.Openings!);
        Assert.Equal(("Caissier", $"Libellé {OtherOpening}", "Billet tombé", "Caissier"), (row.StaffName, row.ReasonFr, row.Note, row.AuthorisedBy));
        Assert.Equal(1, Assert.Single(owner.Cashiers!.Columns).NoSales);
    }

    [Fact]
    public async Task A_no_sale_threshold_is_one_or_more_kept_until_switched_off()
    {
        var shop = new Shop(database);
        Task<TenantSettings> Set(Waymark.Application.Organisation.SetTenantSettings command) =>
            Run(shop, (context, _) => new Waymark.Application.Organisation.SetTenantSettingsHandler(
                new Waymark.Persistence.Organisation.TenantConfigurationStore(context, TabChargesFor.Dzd), TabChargesFor.Dzd, new FixedClock()), command);
        try
        {
            Assert.Null(TenantSettings.Defaults.NoSaleAlertCount);
            Assert.Equal(2, (await Set(new(NoSaleAlertCount: 2))).NoSaleAlertCount);
            await Assert.ThrowsAsync<Waymark.Application.Organisation.TenantSettingRefusedException>(() => Set(new(NoSaleAlertCount: 0)));
            Assert.Null((await Set(new(NoSaleAlertOff: true))).NoSaleAlertCount);
        }
        finally
        {
            await Set(new(NoSaleAlertOff: true));
        }
    }
}
