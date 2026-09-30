using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B6, ✍ Hakim: <see cref="Tender"/>, a ticket paid by card, BaridiMob and cash (D-095). The
/// silent failures: a card part rounded to the cash step, so the terminal and the till disagree by a
/// few dinars; the rounding applied to the whole total instead of the cash left; a rest that depends
/// on the order the parts were typed; a card for more than the ticket, which would need cash back.
/// </summary>
public sealed class TenderTests
{
    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static TenderPart Card(long centimes, string? reference = null) => new(PaymentMethod.Card, Dzd(centimes), reference);

    private static TenderPart Wallet(long centimes, string? reference = null) => new(PaymentMethod.MobileWallet, Dzd(centimes), reference);

    private static TenderPart Cash(long centimes) => new(PaymentMethod.Cash, Dzd(centimes));

    private static TenderPart OnTab(long centimes) => new(PaymentMethod.OnAccount, Dzd(centimes));

    // ------------------------------------------------------------------ settled

    [Fact]
    public void With_no_part_the_whole_ticket_is_cash_and_only_the_tender_rounds()
    {
        var settled = Tender.Settle(Dzd(332_080), []);

        Assert.Equal(TenderVerdict.Settled, settled.Verdict);
        Assert.Equal([Cash(332_080)], settled.Payments);
        Assert.Equal(new CashTender(Dzd(332_000), Dzd(-80)), settled.Cash);
    }

    [Fact]
    public void A_card_and_a_wallet_come_first_and_the_cash_row_is_the_exact_rest()
    {
        // The board's ticket: 3 320,80, 2 000,00 by card and 500,00 by BaridiMob; 820,80 is left, 820,00 collected.
        var settled = Tender.Settle(Dzd(332_080), [Card(200_000, "4417"), Wallet(50_000, "88213")]);

        Assert.Equal([Card(200_000, "4417"), Wallet(50_000, "88213"), Cash(82_080)], settled.Payments);
        Assert.Equal(new CashTender(Dzd(82_000), Dzd(-80)), settled.Cash);
    }

    [Fact]
    public void A_card_part_is_never_rounded()
    {
        var settled = Tender.Settle(Dzd(332_080), [Card(332_080)]);

        Assert.Equal([Card(332_080)], settled.Payments);
    }

    [Fact]
    public void Parts_that_pay_everything_leave_no_cash_row_and_no_variance()
    {
        var settled = Tender.Settle(Dzd(332_080), [Card(300_000), Wallet(32_080)]);

        Assert.Equal(TenderVerdict.Settled, settled.Verdict);
        Assert.DoesNotContain(settled.Payments, payment => payment.Method == PaymentMethod.Cash);
        Assert.Equal(new CashTender(Dzd(0), Dzd(0)), settled.Cash);
    }

    [Fact]
    public void The_rest_rounds_whatever_order_the_parts_came_in_and_the_rows_keep_that_order()
    {
        var one = Tender.Settle(Dzd(332_080), [Card(200_000), Wallet(50_000)]);
        var other = Tender.Settle(Dzd(332_080), [Wallet(50_000), Card(200_000)]);

        Assert.Equal(one.Cash, other.Cash);
        Assert.Equal([Wallet(50_000), Card(200_000), Cash(82_080)], other.Payments);
    }

    [Theory]
    [InlineData(200, 0, -200)]  // 2,00 left: nearer nothing than 5,00; nothing is collected
    [InlineData(250, 500, 250)] // 2,50 left: a tie, away from zero
    [InlineData(740, 500, -240)]
    public void A_rest_below_the_cash_step_still_has_its_cash_row_and_rounds(long rest, long collected, long variance)
    {
        var settled = Tender.Settle(Dzd(100_000 + rest), [Card(100_000)]);

        Assert.Equal([Card(100_000), Cash(rest)], settled.Payments);
        Assert.Equal(new CashTender(Dzd(collected), Dzd(variance)), settled.Cash);
    }

    [Fact]
    public void A_ticket_of_nothing_is_settled_with_no_payment()
    {
        var settled = Tender.Settle(Dzd(0), []);

        Assert.Equal(TenderVerdict.Settled, settled.Verdict);
        Assert.Empty(settled.Payments);
        Assert.Equal(new CashTender(Dzd(0), Dzd(0)), settled.Cash);
    }

    // ------------------------------------------------------------------ refused

    [Theory]
    [InlineData(332_081)]
    [InlineData(500_000)]
    public void A_card_for_more_than_the_ticket_is_refused(long card)
    {
        var refused = Tender.Settle(Dzd(332_080), [Card(card)]);

        Assert.Equal(TenderVerdict.AboveTotal, refused.Verdict);
        Assert.Empty(refused.Payments);
        Assert.Equal(new CashTender(Dzd(0), Dzd(0)), refused.Cash);
    }

    [Fact]
    public void Two_parts_each_below_the_ticket_but_above_it_together_are_refused()
    {
        Assert.Equal(TenderVerdict.AboveTotal, Tender.Settle(Dzd(332_080), [Card(200_000), Wallet(200_000)]).Verdict);
    }

    [Fact]
    public void A_part_on_a_ticket_of_nothing_is_refused()
    {
        Assert.Equal(TenderVerdict.AboveTotal, Tender.Settle(Dzd(0), [Card(100)]).Verdict);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5_000)]
    public void A_part_of_zero_or_less_is_refused(long centimes)
    {
        var refused = Tender.Settle(Dzd(332_080), [Card(100_000), Wallet(centimes)]);

        Assert.Equal(TenderVerdict.NotAboveZero, refused.Verdict);
        Assert.Empty(refused.Payments);
    }

    [Theory]
    [InlineData(PaymentMethod.Cash)]
    [InlineData(PaymentMethod.StoreCredit)]
    public void Only_a_card_a_wallet_or_the_tab_is_a_part(PaymentMethod method)
    {
        var refused = Tender.Settle(Dzd(332_080), [new TenderPart(method, Dzd(10_000))]);

        Assert.Equal(TenderVerdict.NotAPart, refused.Verdict);
        Assert.Empty(refused.Payments);
    }

    [Fact]
    public void Each_part_is_judged_before_the_sum()
    {
        // Above the total together, and one of them is not a part: that is what the cashier is told.
        Assert.Equal(TenderVerdict.NotAPart, Tender.Settle(Dzd(100_000), [Card(90_000), Cash(90_000)]).Verdict);
        Assert.Equal(TenderVerdict.NotAboveZero, Tender.Settle(Dzd(100_000), [Card(200_000), Card(0)]).Verdict);
    }

    [Fact]
    public void A_total_below_zero_throws()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Tender.Settle(Dzd(-100), []));
    }

    [Fact]
    public void A_part_in_another_currency_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Tender.Settle(Dzd(100_000), [new TenderPart(PaymentMethod.Card, Money.FromMinorUnits(1_000, Currency.Eur))]));
    }

    // ------------------------------------------------------------------ B7: the tab as a part

    [Fact]
    public void The_tab_is_a_part_exact_like_a_card_and_the_rest_is_cash()
    {
        var settled = Tender.Settle(Dzd(332_080), [OnTab(200_000)]);

        Assert.Equal(TenderVerdict.Settled, settled.Verdict);
        Assert.Equal([OnTab(200_000), Cash(132_080)], settled.Payments);
        Assert.Equal(new CashTender(Dzd(132_000), Dzd(-80)), settled.Cash);
    }

    [Fact]
    public void The_tab_may_take_the_whole_ticket_never_rounded()
    {
        var settled = Tender.Settle(Dzd(332_080), [OnTab(332_080)]);

        Assert.Equal([OnTab(332_080)], settled.Payments);
        Assert.Equal(new CashTender(Dzd(0), Dzd(0)), settled.Cash);
    }

    [Fact]
    public void A_ticket_goes_on_the_tab_once()
    {
        var refused = Tender.Settle(Dzd(332_080), [OnTab(100_000), Card(50_000), OnTab(100_000)]);

        Assert.Equal(TenderVerdict.TabTwice, refused.Verdict);
        Assert.Empty(refused.Payments);
    }

    [Fact]
    public void With_the_tab_not_a_part_it_takes_the_whole_ticket_alone()
    {
        Assert.Equal(TenderVerdict.Settled, Tender.Settle(Dzd(332_080), [OnTab(332_080)], tabMayBePart: false).Verdict);
        Assert.Equal(TenderVerdict.TabNotWhole, Tender.Settle(Dzd(332_080), [OnTab(200_000)], tabMayBePart: false).Verdict);
        Assert.Equal(TenderVerdict.TabNotWhole, Tender.Settle(Dzd(332_080), [Card(32_080), OnTab(300_000)], tabMayBePart: false).Verdict);
    }

    [Fact]
    public void With_the_tab_not_a_part_cards_and_wallets_still_are()
    {
        Assert.Equal(TenderVerdict.Settled, Tender.Settle(Dzd(332_080), [Card(200_000), Wallet(50_000)], tabMayBePart: false).Verdict);
    }

    [Fact]
    public void The_tab_refusals_come_in_their_order()
    {
        // Two tab parts that also pass the total: two tab parts is what the cashier is told.
        Assert.Equal(TenderVerdict.TabTwice, Tender.Settle(Dzd(100_000), [OnTab(90_000), OnTab(90_000)]).Verdict);

        // Past the total and not whole: past the total.
        Assert.Equal(TenderVerdict.AboveTotal, Tender.Settle(Dzd(100_000), [OnTab(150_000)], tabMayBePart: false).Verdict);

        // A tab part of zero: each part is judged first.
        Assert.Equal(TenderVerdict.NotAboveZero, Tender.Settle(Dzd(100_000), [OnTab(0), OnTab(0)]).Verdict);
    }
}
