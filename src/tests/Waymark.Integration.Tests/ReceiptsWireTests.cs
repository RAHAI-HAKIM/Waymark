using System.Runtime.Versioning;
using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.StoreServer.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// A receipt on the wire (D1, D-125). The silent failures: an evening sale in Algiers printed with
/// the next day's date because the hour was UTC; a cashier's full name on a paper that leaves the
/// shop; a refund printed as a sale.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class ReceiptsWireTests
{
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    private static ReceiptFacts Sale(ReceiptOriginal? original = null) => new(
        new ReceiptStore("EL AMEL", "Cité 300 Logements\n31000 Es Senia, Oran\n", " 041 00 00 00 ", "", null, "Africa/Algiers"),
        "t-1", "S-2026-000142", new DateTimeOffset(2026, 10, 12, 23, 30, 0, TimeSpan.Zero), "till-3", "Caisse 03", "Karim Benali",
        [new ReceiptItem("Tomates", "", Quantity.FromThousandths(742, "kg"), Dzd(18_000), Dzd(0), Dzd(13_356), true)],
        Dzd(13_356), Dzd(0), Dzd(13_356), Dzd(0), [new PastPayment(PaymentMethod.MobileWallet, Dzd(13_356))], null, 2, original);

    [Fact]
    public void The_day_and_hour_are_the_stores_not_utcs()
    {
        // 23:30 UTC on the 12th is 00:30 on the 13th in Algiers: the paper says the 13th.
        var receipt = ReceiptsWire.Found(Sale(), Algiers, null, Language.Fr).Receipt!;

        Assert.Equal(("13/10/2026", "00:30"), (receipt.Day, receipt.Time));
    }

    [Theory]
    [InlineData("Karim Benali", "Karim")]
    [InlineData("  Nadia  ", "Nadia")]
    [InlineData("Yacine", "Yacine")]
    [InlineData("   ", null)]
    [InlineData(null, null)]
    public void The_cashier_is_a_first_name(string? name, string? printed) =>
        Assert.Equal(printed, ReceiptsWire.FirstName(name));

    [Fact]
    public void A_sale_is_a_sale_a_refund_names_its_original_in_the_stores_day_and_the_store_prints_what_it_filled()
    {
        var sale = ReceiptsWire.Found(Sale(), Algiers, 7, Language.Ar).Receipt!;
        var refund = ReceiptsWire.Found(Sale(new ReceiptOriginal("S-2026-000100", new DateTimeOffset(2026, 10, 11, 17, 42, 0, TimeSpan.Zero), "Caisse 02")), Algiers, null, Language.Fr).Receipt!;

        Assert.Equal((ReceiptKinds.Sale, 7, "ar", 2L), (sale.Kind, sale.RefundWindowDays, sale.Language, sale.ReprintCount));
        Assert.Equal(new ReceiptOriginalWire("S-2026-000100", "11/10/2026", "18:42", "Caisse 02"), refund.Original);
        Assert.Equal(ReceiptKinds.Refund, refund.Kind);

        // Address lines as written, a blank one dropped; a blank NIF prints nothing; the phone trimmed.
        Assert.Equal(["Cité 300 Logements", "31000 Es Senia, Oran"], sale.Store.AddressLines);
        Assert.Equal(("041 00 00 00", (string?)null, (string?)null), (sale.Store.Phone, sale.Store.TaxNumber, sale.Store.Footer));

        // A variant with no name prints the product alone; figures in the wire's invariant text.
        var line = Assert.Single(sale.Lines);
        Assert.Equal(new ReceiptLineWire("Tomates", "0.742", "kg", "180.00", "0.00", "133.56", true), line);
        Assert.Equal(new PastPaymentWire("mobile_wallet", "133.56"), Assert.Single(sale.Payments));
    }
}
