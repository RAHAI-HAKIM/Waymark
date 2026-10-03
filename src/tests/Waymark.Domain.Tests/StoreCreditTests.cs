using Waymark.Domain.Customers;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B9b, ✍ Hakim: <see cref="StoreCredit"/> (D-101). The silent failures: credit spent twice
/// because the balance was a stored figure; last year's credit spent this year after the shop said it
/// expires; new credit counted as expired because spending took from the newest first.
/// </summary>
public sealed class StoreCreditTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 2, 12, 0, 0, TimeSpan.Zero);

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static CreditLine At(int daysAgo, long centimes) => new(Now.AddDays(-daysAgo), Dzd(centimes));

    // ------------------------------------------------------------------ Age

    [Fact]
    public void No_movement_is_zero_everywhere()
    {
        Assert.Equal(new CreditAge(Dzd(0), Dzd(0), Dzd(0)), StoreCredit.Age(Currency.Dzd, [], Now, 30));
    }

    [Fact]
    public void With_no_expiry_the_balance_is_all_available()
    {
        var age = StoreCredit.Age(Currency.Dzd, [At(400, 124_000), At(10, -24_000)], Now, null);

        Assert.Equal(new CreditAge(Dzd(100_000), Dzd(100_000), Dzd(0)), age);
    }

    [Fact]
    public void Credit_issued_more_than_the_days_ago_and_unspent_has_expired()
    {
        // 1 000,00 issued 40 days ago, 500,00 issued 5 days ago; 30 days: the first has expired.
        var age = StoreCredit.Age(Currency.Dzd, [At(40, 100_000), At(5, 50_000)], Now, 30);

        Assert.Equal(new CreditAge(Dzd(150_000), Dzd(50_000), Dzd(100_000)), age);
    }

    [Fact]
    public void Exactly_the_days_is_not_expired_a_second_more_is()
    {
        Assert.Equal(Dzd(0), StoreCredit.Age(Currency.Dzd, [At(30, 100_000)], Now, 30).Expired);
        Assert.Equal(Dzd(100_000), StoreCredit.Age(Currency.Dzd, [new CreditLine(Now.AddDays(-30).AddSeconds(-1), Dzd(100_000))], Now, 30).Expired);
    }

    [Fact]
    public void Spending_takes_the_oldest_credit_first()
    {
        // 1 000,00 issued 40 days ago, 500,00 five days ago; 800,00 spent ten days ago came out of the old one:
        // 200,00 of it is left and expired, the new 500,00 is all available.
        var age = StoreCredit.Age(Currency.Dzd, [At(40, 100_000), At(10, -80_000), At(5, 50_000)], Now, 30);

        Assert.Equal(new CreditAge(Dzd(70_000), Dzd(50_000), Dzd(20_000)), age);
    }

    [Fact]
    public void Movements_are_read_in_time_order_whatever_order_they_come_in()
    {
        var age = StoreCredit.Age(Currency.Dzd, [At(5, 50_000), At(10, -80_000), At(40, 100_000)], Now, 30);

        Assert.Equal(new CreditAge(Dzd(70_000), Dzd(50_000), Dzd(20_000)), age);
    }

    [Fact]
    public void An_expiry_written_off_is_taken_from_the_oldest_and_leaves_nothing_expired()
    {
        // The 200,00 left of the old credit written off by an expire movement: nothing expired any more.
        var age = StoreCredit.Age(Currency.Dzd, [At(40, 100_000), At(10, -80_000), At(5, 50_000), At(1, -20_000)], Now, 30);

        Assert.Equal(new CreditAge(Dzd(50_000), Dzd(50_000), Dzd(0)), age);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void An_expiry_of_no_days_throws(int days)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => StoreCredit.Age(Currency.Dzd, [At(1, 100)], Now, days));
    }

    // ------------------------------------------------------------------ MayRedeem

    [Theory]
    [InlineData(50_000, 50_000, RedeemVerdict.Accepted)]  // all of it
    [InlineData(50_000, 10_000, RedeemVerdict.Accepted)]
    [InlineData(50_000, 50_001, RedeemVerdict.AboveAvailable)]
    [InlineData(50_000, 0, RedeemVerdict.NotAboveZero)]
    [InlineData(50_000, -100, RedeemVerdict.NotAboveZero)]
    [InlineData(0, 100, RedeemVerdict.AboveAvailable)]
    public void Never_more_than_is_available_and_never_nothing(long available, long amount, RedeemVerdict verdict)
    {
        Assert.Equal(verdict, StoreCredit.MayRedeem(Dzd(available), Dzd(amount)));
    }

    [Fact]
    public void Another_currency_throws()
    {
        Assert.ThrowsAny<InvalidOperationException>(() => StoreCredit.MayRedeem(Dzd(100), Money.FromMinorUnits(50, Currency.FromCode("EUR"))));
    }
}
