using Waymark.Domain.Organisation;

namespace Waymark.Domain.Tests;

/// <summary>
/// The no-sale flag (D-126): one person opening the drawer with nothing sold more often than the
/// threshold. Strictly more: at the threshold nothing is flagged, as for cancels (D-097).
/// </summary>
public sealed class ShiftReportNoSaleTests
{
    private static readonly DateTimeOffset Nine = new(2026, 10, 14, 9, 0, 0, TimeSpan.Zero);

    private static ReportNoSale By(string staff, int minutes) => new(staff, Nine.AddMinutes(minutes), "Monnaie", "صرف", null, null);

    [Fact]
    public void Past_the_threshold_one_flag_a_person_with_their_count_in_the_order_of_their_first_opening()
    {
        ReportNoSale[] openings = [By("yacine", 10), By("nabil", 5), By("yacine", 30), By("nabil", 40), By("samia", 50), By("nabil", 60)];

        var flags = ShiftReport.NoSaleFlagsOf(openings, threshold: 1);

        Assert.Equal(
            [new ShiftFlag(ShiftFlagKind.TooManyNoSales, "nabil", null, null, 3), new ShiftFlag(ShiftFlagKind.TooManyNoSales, "yacine", null, null, 2)],
            flags);
    }

    [Fact]
    public void At_the_threshold_or_with_none_nothing_is_flagged()
    {
        ReportNoSale[] openings = [By("nabil", 5), By("nabil", 40)];

        Assert.Empty(ShiftReport.NoSaleFlagsOf(openings, threshold: 2));
        Assert.Empty(ShiftReport.NoSaleFlagsOf(openings, threshold: null));
    }
}
