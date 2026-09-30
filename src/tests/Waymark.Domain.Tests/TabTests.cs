using Waymark.Domain.Customers;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B7, ✍ Hakim: <see cref="Tab"/>, le carnet (D-055, D-096). The silent failures: a balance
/// kept somewhere and wrong; a repayment that pays the newest charge, so an old debt never looks old;
/// a charge one centime past the limit let through; a tab with no limit charged anyway; an overdue
/// rule a day off; a repayment larger than the debt, which turns the tab into a safe.
/// </summary>
public sealed class TabTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 1, 10, 0, 0, TimeSpan.Zero);

    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static TabMovement On(int daysAgo, long centimes) => new(Now.AddDays(-daysAgo), Dzd(centimes));

    private static TabAge Owing(long centimes, int? daysAgo = 10) =>
        new(Dzd(centimes), daysAgo is { } days ? Now.AddDays(-days) : null);

    // ------------------------------------------------------------------ Age

    [Fact]
    public void A_tab_with_no_movement_owes_nothing()
    {
        Assert.Equal(new TabAge(Dzd(0), null), Tab.Age(Currency.Dzd, []));
    }

    [Fact]
    public void The_balance_is_every_movement_added_up()
    {
        var age = Tab.Age(Currency.Dzd, [On(30, 100_000), On(20, -40_000), On(10, 25_000)]);

        Assert.Equal(Dzd(85_000), age.Balance);
    }

    [Fact]
    public void A_repayment_pays_the_oldest_charge_first()
    {
        // 1 000 thirty days ago, 250 ten days ago; 1 000 repaid: the 250 is what is left, and it is ten days old.
        var age = Tab.Age(Currency.Dzd, [On(30, 100_000), On(10, 25_000), On(5, -100_000)]);

        Assert.Equal(new TabAge(Dzd(25_000), Now.AddDays(-10)), age);
    }

    [Fact]
    public void A_part_repayment_leaves_the_oldest_charge_still_the_oldest()
    {
        var age = Tab.Age(Currency.Dzd, [On(30, 100_000), On(10, 25_000), On(5, -60_000)]);

        Assert.Equal(new TabAge(Dzd(65_000), Now.AddDays(-30)), age);
    }

    [Fact]
    public void Movements_are_read_in_time_order_whatever_order_they_come_in()
    {
        var age = Tab.Age(Currency.Dzd, [On(5, -100_000), On(10, 25_000), On(30, 100_000)]);

        Assert.Equal(new TabAge(Dzd(25_000), Now.AddDays(-10)), age);
    }

    [Fact]
    public void A_tab_paid_off_owes_nothing_and_has_nothing_unpaid()
    {
        Assert.Equal(new TabAge(Dzd(0), null), Tab.Age(Currency.Dzd, [On(30, 100_000), On(5, -100_000)]));
    }

    [Fact]
    public void Paying_more_than_was_owed_leaves_a_credit_that_pays_the_next_charge()
    {
        // 1 000 owed, 1 200 paid; then 150 charged: the credit of 200 pays it, and 50 is still in hand.
        var age = Tab.Age(Currency.Dzd, [On(30, 100_000), On(20, -120_000), On(10, 15_000)]);

        Assert.Equal(new TabAge(Dzd(-5_000), null), age);
    }

    [Fact]
    public void A_positive_adjustment_is_owed_from_its_own_date()
    {
        // An adjustment is a movement like any other: positive is owed.
        var age = Tab.Age(Currency.Dzd, [On(40, 10_000), On(20, -10_000), On(15, 3_000)]);

        Assert.Equal(new TabAge(Dzd(3_000), Now.AddDays(-15)), age);
    }

    // ------------------------------------------------------------------ Check

    [Fact]
    public void A_charge_within_the_limit_is_accepted_and_says_what_is_left()
    {
        var check = Tab.Check(Owing(60_000), Dzd(100_000), false, Dzd(30_000), Now, null);

        Assert.Equal(new TabCheck(TabVerdict.Accepted, Dzd(40_000)), check);
        Assert.True(check.MayCharge);
    }

    [Fact]
    public void Reaching_the_limit_exactly_is_fine_and_a_centime_past_it_is_not()
    {
        Assert.Equal(TabVerdict.Accepted, Tab.Check(Owing(60_000), Dzd(100_000), false, Dzd(40_000), Now, null).Verdict);

        var past = Tab.Check(Owing(60_000), Dzd(100_000), false, Dzd(40_001), Now, null);
        Assert.Equal(TabVerdict.AboveLimit, past.Verdict);
        Assert.True(past.MayOverride);
    }

    [Fact]
    public void No_limit_is_no_tab_whatever_is_owed()
    {
        var check = Tab.Check(Owing(0, null), null, false, Dzd(100), Now, null);

        Assert.Equal(new TabCheck(TabVerdict.NoTab, null), check);
        Assert.False(check.MayOverride);
    }

    [Fact]
    public void A_limit_of_zero_takes_no_charge()
    {
        Assert.Equal(TabVerdict.AboveLimit, Tab.Check(Owing(0, null), Dzd(0), false, Dzd(100), Now, null).Verdict);
    }

    [Fact]
    public void A_frozen_tab_takes_no_charge_even_within_its_limit()
    {
        var check = Tab.Check(Owing(0, null), Dzd(100_000), true, Dzd(100), Now, null);

        Assert.Equal(new TabCheck(TabVerdict.Frozen, Dzd(100_000)), check);
        Assert.False(check.MayOverride);
    }

    [Fact]
    public void An_unpaid_charge_older_than_the_overdue_days_stops_new_charges()
    {
        Assert.Equal(TabVerdict.Overdue, Tab.Check(Owing(10_000, daysAgo: 31), Dzd(100_000), false, Dzd(100), Now, 30).Verdict);
    }

    [Fact]
    public void Exactly_the_overdue_days_is_not_yet_overdue_and_a_second_more_is()
    {
        var exactly = new TabAge(Dzd(10_000), Now.AddDays(-30));
        var justPast = new TabAge(Dzd(10_000), Now.AddDays(-30).AddSeconds(-1));

        Assert.Equal(TabVerdict.Accepted, Tab.Check(exactly, Dzd(100_000), false, Dzd(100), Now, 30).Verdict);
        Assert.Equal(TabVerdict.Overdue, Tab.Check(justPast, Dzd(100_000), false, Dzd(100), Now, 30).Verdict);
    }

    [Fact]
    public void With_the_overdue_rule_off_an_old_debt_does_not_stop_a_charge()
    {
        Assert.Equal(TabVerdict.Accepted, Tab.Check(Owing(10_000, daysAgo: 400), Dzd(100_000), false, Dzd(100), Now, null).Verdict);
    }

    [Fact]
    public void The_refusals_come_in_their_order()
    {
        // Frozen, overdue and past the limit all at once: frozen is what the cashier is told.
        var all = new TabAge(Dzd(90_000), Now.AddDays(-60));
        Assert.Equal(TabVerdict.Frozen, Tab.Check(all, Dzd(100_000), true, Dzd(50_000), Now, 30).Verdict);

        // Overdue and past the limit: overdue, which no override lets through.
        Assert.Equal(TabVerdict.Overdue, Tab.Check(all, Dzd(100_000), false, Dzd(50_000), Now, 30).Verdict);
    }

    [Fact]
    public void Available_is_negative_on_a_tab_already_past_its_limit()
    {
        Assert.Equal(Dzd(-20_000), Tab.Check(Owing(120_000), Dzd(100_000), false, Dzd(100), Now, null).Available);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-500)]
    public void A_charge_of_zero_or_less_throws(long centimes)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tab.Check(Owing(0, null), Dzd(100_000), false, Dzd(centimes), Now, null));
    }

    // ------------------------------------------------------------------ MayLimit

    [Theory]
    [InlineData(100_000L, null, LimitVerdict.Accepted)]
    [InlineData(0L, null, LimitVerdict.Accepted)]
    [InlineData(-1L, null, LimitVerdict.Negative)]
    [InlineData(500_000L, 500_000L, LimitVerdict.Accepted)]  // the ceiling itself
    [InlineData(500_001L, 500_000L, LimitVerdict.AboveCeiling)]
    public void A_limit_is_at_least_zero_and_at_most_the_ceiling(long limit, long? ceiling, LimitVerdict verdict)
    {
        Assert.Equal(verdict, Tab.MayLimit(Dzd(limit), ceiling is { } c ? Dzd(c) : null));
    }

    [Fact]
    public void Closing_the_tab_is_always_accepted()
    {
        Assert.Equal(LimitVerdict.Accepted, Tab.MayLimit(null, Dzd(0)));
    }

    // ------------------------------------------------------------------ MayRepay

    [Theory]
    [InlineData(50_000, 50_000, RepaymentVerdict.Accepted)] // the whole balance
    [InlineData(50_000, 10_000, RepaymentVerdict.Accepted)]
    [InlineData(50_000, 50_001, RepaymentVerdict.AboveBalance)]
    [InlineData(50_000, 0, RepaymentVerdict.NotAboveZero)]
    [InlineData(50_000, -100, RepaymentVerdict.NotAboveZero)]
    [InlineData(0, 100, RepaymentVerdict.AboveBalance)]     // nothing owed, nothing to repay
    public void A_repayment_is_above_zero_and_never_more_than_is_owed(long balance, long amount, RepaymentVerdict verdict)
    {
        Assert.Equal(verdict, Tab.MayRepay(Dzd(balance), Dzd(amount)));
    }

    [Fact]
    public void Figures_in_two_currencies_throw()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Tab.Check(Owing(0, null), Dzd(100_000), false, Money.FromMinorUnits(100, Currency.Eur), Now, null));
    }
}
