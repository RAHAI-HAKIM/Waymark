using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.Sales;
using Waymark.Domain.Enums;
using Waymark.StoreServer.Sales;
using RefusalCodes = Waymark.Contracts.Pos.RefusalCodes;

namespace Waymark.Integration.Tests;

/// <summary>
/// The block D review (16/10), against a real database. The silent failures: the manager who just
/// let a close through asked again for its Z, or somebody whose rank reads no report let in by that
/// PIN; a refund window counted in UTC days, turning away an evening sale a day early; a no-sale
/// reason the shop switched off still accepted.
/// </summary>
public sealed partial class CompleteSaleTests
{
    [Theory]
    [InlineData(2L, null, true)]   // a manager, the ladder's rank for reports
    [InlineData(1L, null, false)]  // a cashier's PIN closes nothing here anyway, and reads nothing
    [InlineData(2L, 3L, false)]    // the shop raised reports to the owner: the closer's PIN does not reach them
    [InlineData(3L, 3L, true)]
    [InlineData(null, null, false)]
    [SupportedOSPlatform("windows")] // StoreServer is Windows-only (D-017), and so is its code.
    public void The_pin_that_let_a_close_through_reads_its_z_only_if_its_giver_reads_reports(long? rank, long? xReportMinRank, bool reads) =>
        Assert.Equal(reads, ShiftReportEndpoints.ClosersRead(rank, xReportMinRank));

    [Fact]
    public async Task A_refund_window_counts_the_stores_days_so_an_evening_sale_is_not_a_day_older()
    {
        ReturnReasons();
        var shop = new Shop(database);
        shop.Receive(database, shop.Milk, 10, daysAgo: 30);

        // 23:30 UTC on the 11th is 00:30 on the 12th in the store: the 7th day before the 19th, not the 8th.
        var evening = await SellAt(shop, new DateTimeOffset(2026, 9, 11, 23, 30, 0, TimeSpan.Zero), shop.StaffId, new SaleLineRequest(shop.Milk.Barcode, 1));
        await Window(shop, 7);
        try
        {
            Assert.Equal(Dzd(14_300), (await RefundPastWindow(shop, evening.TransactionId, mayPass: false)).Total);
        }
        finally
        {
            await Window(shop, null);
        }
    }

    [Fact]
    public async Task A_no_sale_reason_the_shop_switched_off_is_refused()
    {
        NoSaleReasons();
        var shop = new Shop(database);
        await using (var context = database.NewContext())
        {
            if (!await context.ReasonCodes.AnyAsync(reason => reason.ReasonCodeValue == "tiroir-ancien"))
            {
                context.ReasonCodes.Add(new Waymark.Domain.Reference.ReasonCode
                {
                    ReasonCodeValue = "tiroir-ancien", AppliesTo = ReasonCodeAppliesTo.NoSale, LabelAr = "-", LabelFr = "Ancien motif", IsActive = false, CreatedAt = Now,
                });
                await context.SaveChangesAsync();
            }
        }

        var refused = await Assert.ThrowsAsync<CashMovementRefusedException>(() => OpenWithNoSale(shop, reason: "tiroir-ancien", sellerMay: true));
        Assert.Equal(RefusalCodes.ReasonUnknown, refused.Code);
    }
}
