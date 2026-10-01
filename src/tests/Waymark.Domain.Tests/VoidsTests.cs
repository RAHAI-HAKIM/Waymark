using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Session B8, ✍ Hakim: <see cref="Voids"/>, a ticket cancelled at the till (D-097). The silent
/// failures: a cashier cancelling a ticket the customer has paid, with nobody asked and nothing
/// flagged; a manager asked for a PIN on every cancel, so the PIN gets shared; a threshold off by one,
/// so the shop's own rule never fires.
/// </summary>
public sealed class VoidsTests
{
    private static Money Dzd(long centimes) => Money.FromMinorUnits(centimes, Currency.Dzd);

    private static CancelledTicket Ticket(long centimes, bool paymentOpened = false) => new(Dzd(centimes), paymentOpened);

    // ------------------------------------------------------------------ NeedsAuthorisation

    [Theory]
    [InlineData(false, true, true)]   // a cashier, after Encaisser: the theft moment
    [InlineData(false, false, false)] // a cashier, before: a reason is enough
    [InlineData(true, true, false)]   // a manager or the owner: never asked
    [InlineData(true, false, false)]
    public void A_pin_is_asked_only_of_a_cashier_after_encaisser(bool sellerMayVoid, bool paymentOpened, bool needed)
    {
        Assert.Equal(needed, Voids.NeedsAuthorisation(sellerMayVoid, paymentOpened));
    }

    // ------------------------------------------------------------------ Flags

    [Fact]
    public void A_cancel_after_encaisser_is_always_flagged_whatever_the_thresholds()
    {
        Assert.Equal([VoidAlert.AfterEncaisser], Voids.Flags(Ticket(100, paymentOpened: true), 1, null, null));
    }

    [Fact]
    public void With_no_threshold_and_no_payment_started_nothing_is_flagged()
    {
        Assert.Empty(Voids.Flags(Ticket(10_000_000), 50, null, null));
    }

    [Theory]
    [InlineData(3, false)] // the third of three: not more than three
    [InlineData(4, true)]  // the fourth: flagged
    [InlineData(9, true)]
    public void The_count_flags_the_cancels_past_it_this_one_included(int cancelsThisSession, bool flagged)
    {
        var flags = Voids.Flags(Ticket(100), cancelsThisSession, 3, null);

        Assert.Equal(flagged, flags.Contains(VoidAlert.CountPerShift));
    }

    [Theory]
    [InlineData(500_000, false)] // exactly the value
    [InlineData(500_001, true)]  // a centime above
    [InlineData(10_000, false)]
    public void The_value_flags_a_ticket_worth_more_than_it(long centimes, bool flagged)
    {
        Assert.Equal(flagged, Voids.Flags(Ticket(centimes), 1, null, Dzd(500_000)).Contains(VoidAlert.Value));
    }

    [Fact]
    public void Every_flag_that_applies_is_given_in_its_order()
    {
        var flags = Voids.Flags(Ticket(900_000, paymentOpened: true), 6, 5, Dzd(500_000));

        Assert.Equal([VoidAlert.AfterEncaisser, VoidAlert.CountPerShift, VoidAlert.Value], flags);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void A_count_that_leaves_out_the_cancel_being_judged_throws(int cancelsThisSession)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => Voids.Flags(Ticket(100), cancelsThisSession, 3, null));
    }

    [Fact]
    public void A_value_in_another_currency_than_the_threshold_throws()
    {
        Assert.Throws<InvalidOperationException>(() =>
            Voids.Flags(new CancelledTicket(Money.FromMinorUnits(100, Currency.Eur), false), 1, null, Dzd(500_000)));
    }
}
