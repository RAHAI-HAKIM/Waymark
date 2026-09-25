using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// More than one ticket at the till (B2, D-087): on hold, cancelled into the drafts, taken back.
/// The silent failures: a ticket lost when another is resumed, a ticket on hold that pays with the
/// wrong lines, yesterday's draft still offered today, and a cancelled ticket that reached the
/// server.
/// </summary>
public sealed class TillHoldTests
{
    private static readonly DateTimeOffset T0 = new(2026, 9, 25, 13, 5, 0, TimeSpan.Zero);

    /// <summary>The till's zone for these tests: Algiers, UTC+1 all year.</summary>
    private static readonly TimeZoneInfo Algiers = TimeZoneInfo.CreateCustomTimeZone("Africa/Algiers", TimeSpan.FromHours(1), "Algiers", "Algiers");

    private sealed class HeldClock(DateTimeOffset now) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = now;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private sealed class Products : IProductSource
    {
        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(
                ProductLookupOutcome.Found,
                barcode,
                new ProductForSale("v-" + barcode, "p-" + barcode, "Lait", "1L", "pc", 0, 900,
                    TvaRateSource.FromCategory, "100.00", "DZD", false, "10"),
                Reason: null)));
    }

    private sealed class Sales : IStoreSales
    {
        public SaleAnswer Answer { get; set; } = new SaleAnswer.Completed(
            new SaleOutcome(SaleOutcomes.Completed, "t1", "S-2026-000001", "100.00", "8.26", "100.00", "DZD", null));

        public List<SaleRequest> Sent { get; } = [];

        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult(Answer);
        }
    }

    private readonly HeldClock _clock = new(T0);
    private readonly Sales _sales = new();

    private TillSession Till()
    {
        var session = new TillSession(new Products(), _sales, new TillIdentity("till-1"), _clock, Algiers);
        session.SignIn(new SignedInStaff("nabil", "token", "Nabil B."));
        return session;
    }

    private static async Task Scan(TillSession session, params string[] codes)
    {
        foreach (var code in codes)
        {
            await session.SubmitAsync(code);
        }
    }

    private static string[] Codes(Cart cart) => [.. cart.ActiveLines.Select(line => line.Barcode)];

    // ================================================================ on hold

    [Fact]
    public async Task Attente_puts_the_ticket_on_hold_and_leaves_an_empty_one()
    {
        var session = Till();
        await Scan(session, "111", "222");

        Assert.True(session.Park());

        Assert.Empty(session.Cart.Lines);
        var held = Assert.Single(session.Parked);
        Assert.Equal(["111", "222"], Codes(held.Cart));
        Assert.Equal((T0, "nabil"), (held.At, held.ByStaffId));
    }

    [Fact]
    public async Task There_is_nothing_to_put_on_hold_without_a_line_in_the_sale()
    {
        var session = Till();
        Assert.False(session.Park());

        await Scan(session, "111");
        session.Remove(session.Cart.LineOf("v-111"));
        Assert.False(session.Park());

        Assert.Empty(session.Parked);
    }

    [Fact]
    public async Task Resuming_a_ticket_puts_the_one_on_screen_on_hold_in_its_place()
    {
        // Nothing is lost by resuming: two tickets before, two tickets after.
        var session = Till();
        await Scan(session, "111");
        session.Park();
        await Scan(session, "222");

        Assert.True(session.ResumeParked(session.Parked[0].Id));

        Assert.Equal(["111"], Codes(session.Cart));
        Assert.Equal(["222"], Codes(Assert.Single(session.Parked).Cart));
    }

    [Fact]
    public async Task Resuming_onto_an_empty_ticket_parks_nothing()
    {
        var session = Till();
        await Scan(session, "111");
        session.Park();

        session.ResumeParked(session.Parked[0].Id);

        Assert.Empty(session.Parked);
    }

    [Fact]
    public async Task A_resumed_ticket_pays_with_its_own_lines_and_counts()
    {
        var session = Till();
        await Scan(session, "111", "111");
        session.Park();
        await Scan(session, "333");
        session.ResumeParked(session.Parked[0].Id);

        await session.PayAsync();

        Assert.Equal([new SaleRequestLine("111", 2)], Assert.Single(_sales.Sent).Lines);
        Assert.Equal(["333"], Codes(Assert.Single(session.Parked).Cart));
    }

    [Fact]
    public async Task Resuming_after_a_sale_closes_the_paid_ticket_and_parks_nothing()
    {
        var session = Till();
        await Scan(session, "111");
        session.Park();
        await Scan(session, "222");
        await session.PayAsync();

        session.ResumeParked(session.Parked[0].Id);

        Assert.Null(session.Paid);
        Assert.Equal(["111"], Codes(session.Cart));
        Assert.Empty(session.Parked);
    }

    [Fact]
    public async Task Nothing_is_put_aside_or_resumed_while_a_sale_is_unconfirmed()
    {
        // D-085: the cashier checks the unconfirmed ticket first. Parking it would hide a ticket
        // that may already be paid, and resuming another would put it on hold behind their back.
        var session = Till();
        await Scan(session, "111");
        session.Park();
        await Scan(session, "222");
        _sales.Answer = new SaleAnswer.Unknown("timeout");
        await session.PayAsync();

        Assert.False(session.Park());
        Assert.False(session.CancelTicket());
        Assert.False(session.ResumeParked(session.Parked[0].Id));
        Assert.Equal(["222"], Codes(session.Cart));
    }

    [Fact]
    public async Task An_unknown_ticket_resumes_nothing()
    {
        var session = Till();
        await Scan(session, "111");

        Assert.False(session.ResumeParked("H99"));
        Assert.False(session.ResumeDraft("H99"));
        Assert.Equal(["111"], Codes(session.Cart));
    }

    // ================================================================ drafts

    [Fact]
    public async Task Annuler_ticket_puts_it_in_the_drafts_with_who_and_when_and_sends_nothing()
    {
        var session = Till();
        await Scan(session, "111");

        Assert.True(session.CancelTicket());

        Assert.Empty(session.Cart.Lines);
        var draft = Assert.Single(session.Drafts);
        Assert.Equal((T0, "nabil", "Nabil B."), (draft.At, draft.ByStaffId, draft.ByName));
        Assert.Empty(_sales.Sent);
    }

    [Fact]
    public async Task The_newest_draft_comes_first()
    {
        var session = Till();
        await Scan(session, "111");
        session.CancelTicket();
        _clock.Now = T0.AddMinutes(10);
        await Scan(session, "222");
        session.CancelTicket();

        Assert.Equal(["222", "111"], session.Drafts.Select(draft => draft.Cart.Lines[0].Barcode));
    }

    [Fact]
    public async Task Reprendre_brings_a_draft_back_and_puts_the_ticket_on_screen_on_hold()
    {
        var session = Till();
        await Scan(session, "111");
        session.CancelTicket();
        await Scan(session, "222");

        Assert.True(session.ResumeDraft(session.Drafts[0].Id));

        Assert.Equal(["111"], Codes(session.Cart));
        Assert.Empty(session.Drafts);
        Assert.Equal(["222"], Codes(Assert.Single(session.Parked).Cart));
    }

    [Fact]
    public async Task A_draft_is_gone_when_the_tills_date_changes()
    {
        // Cancelled at 23:30 in Algiers (22:30 UTC). At 23:59 it is there; at 00:10 the next day,
        // in the till's zone, it is gone, although UTC is still on the same date.
        var session = Till();
        _clock.Now = new DateTimeOffset(2026, 9, 25, 22, 30, 0, TimeSpan.Zero);
        await Scan(session, "111");
        session.CancelTicket();

        _clock.Now = new DateTimeOffset(2026, 9, 25, 22, 59, 0, TimeSpan.Zero);
        Assert.Single(session.Drafts);

        _clock.Now = new DateTimeOffset(2026, 9, 25, 23, 10, 0, TimeSpan.Zero);
        Assert.Empty(session.Drafts);
        Assert.False(session.ResumeDraft("H1"));
    }

    [Fact]
    public async Task A_ticket_on_hold_lasts_past_midnight()
    {
        // Only drafts end with the day. A ticket on hold is a customer at the counter.
        var session = Till();
        await Scan(session, "111");
        session.Park();

        _clock.Now = T0.AddDays(1);

        Assert.Single(session.Parked);
    }

    // ================================================================ the stepper

    [Fact]
    public async Task The_stepper_sets_the_count_that_is_sent()
    {
        var session = Till();
        await Scan(session, "111");
        var line = session.Cart.LineOf("v-111");

        session.SetCount(line, 3);
        session.SetCount(line, 0);
        await session.PayAsync();

        Assert.Equal([new SaleRequestLine("111", 3)], Assert.Single(_sales.Sent).Lines);
    }

    // ================================================================ changing cashier

    [Fact]
    public async Task A_ticket_with_only_struck_lines_is_not_put_on_hold_at_a_change_of_cashier()
    {
        // An abandoned ticket, not a sale: the next person starts clean.
        var session = Till();
        await Scan(session, "111");
        session.Remove(session.Cart.LineOf("v-111"));

        Assert.NotNull(session.SignOut());

        Assert.Empty(session.Parked);
        Assert.Empty(session.Cart.Lines);
    }

    [Fact]
    public async Task Any_cashier_may_take_up_a_ticket_another_put_on_hold()
    {
        var session = Till();
        await Scan(session, "111");
        session.SignOut();
        session.SignIn(new SignedInStaff("samia", "token-2", "Samia K."));

        Assert.True(session.ResumeParked(session.Parked[0].Id));
        await session.PayAsync();

        Assert.Single(_sales.Sent);
    }
}
