using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// Paying at the till (hop 2, D-070). The silent failures: a cart cleared by a sale that was
/// never written (the goods leave unpaid), or kept after one that was (the cashier sells it
/// twice), and a request that carries the till's prices instead of its codes.
/// </summary>
public sealed class TillPaymentTests
{
    private sealed class Products : IProductSource
    {
        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            Task.FromResult<LookupAnswer>(new LookupAnswer.Answered(new ProductLookup(
                ProductLookupOutcome.Found,
                barcode,
                new ProductForSale("v-" + barcode, "p-" + barcode, "Lait", "1L", "pc", 0, 900,
                    TvaRateSource.FromCategory, "143.00", "DZD", false, "10"),
                Reason: null)));
    }

    private sealed class Sales(SaleAnswer answer) : IStoreSales
    {
        public List<SaleRequest> Sent { get; } = [];

        public List<string> Tokens { get; } = [];

        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            Tokens.Add(sessionToken);
            return Task.FromResult(answer);
        }
    }

    private static readonly SignedInStaff Cashier = new("staff-1", "token-1");

    private static readonly SaleOutcome Done =
        new(SaleOutcomes.Completed, "t1", "S-2026-000001", "286.00", "23.61", "285.00", "DZD", null);

    private static async Task<(TillSession Session, Sales Sales)> CartOf(SaleAnswer answer, TillIdentity? till = null, params string[] codes)
    {
        var sales = new Sales(answer);
        var session = new TillSession(new Products(), sales, till ?? new TillIdentity("till-1"));
        session.SignIn(Cashier);
        foreach (var code in codes)
        {
            await session.SubmitAsync(code);
        }

        return (session, sales);
    }

    [Fact]
    public async Task The_request_carries_codes_and_counts_never_prices()
    {
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done), null, "111", "111", "222");

        await session.PayAsync();

        var request = Assert.Single(sales.Sent);
        Assert.Equal("till-1", request.TerminalId);
        Assert.Equal([new SaleRequestLine("111", 2), new SaleRequestLine("222", 1)], request.Lines);
    }

    // ------------------------------------------------ who is selling (A5, D-083)

    [Fact]
    public void A_sale_names_nobody_the_session_token_does()
    {
        // The request has no staff field at all: the server takes the seller from the session.
        Assert.DoesNotContain(typeof(SaleRequest).GetProperties(), property => property.Name.Contains("Staff", StringComparison.Ordinal));
    }

    [Fact]
    public async Task The_signed_in_persons_token_goes_with_the_sale()
    {
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done), null, "111");

        await session.PayAsync();

        Assert.Equal(["token-1"], sales.Tokens);
    }

    [Fact]
    public async Task Nobody_signed_in_sends_nothing_and_keeps_the_ticket()
    {
        var sales = new Sales(new SaleAnswer.Completed(Done));
        var session = new TillSession(new Products(), sales, new TillIdentity("till-1"));
        await session.SubmitAsync("111");

        await session.PayAsync();

        Assert.Empty(sales.Sent);
        Assert.Equal(TillNoticeKind.NotSignedIn, session.Notice!.Kind);
        Assert.Single(session.Cart.ActiveLines);
    }

    [Fact]
    public async Task A_session_the_server_no_longer_holds_signs_the_till_out_and_keeps_the_ticket()
    {
        // The server restarted: nothing was written. The till asks for a PIN, and the ticket is
        // there for whoever signs in, rather than lost with the session.
        var (session, _) = await CartOf(new SaleAnswer.NotSignedIn(), null, "111", "222");

        await session.PayAsync();

        Assert.Null(session.SignedIn);
        Assert.Equal(TillNoticeKind.NotSignedIn, session.Notice!.Kind);
        Assert.Equal(2, session.Cart.ActiveLines.Count);
        Assert.Null(session.Paid);
        Assert.True(session.Server.IsReachable);
    }

    [Fact]
    public async Task Changing_cashier_mid_ticket_puts_the_ticket_on_hold_for_the_next_person()
    {
        // D-087: a ticket belongs to the till. The next person finds it as a tab and takes it up;
        // it is never sold under their name without somebody touching it.
        var (session, _) = await CartOf(new SaleAnswer.Completed(Done), null, "111");

        Assert.Equal("token-1", session.SignOut());

        Assert.Null(session.SignedIn);
        Assert.Empty(session.Cart.Lines);
        var held = Assert.Single(session.Parked);
        Assert.Equal(("staff-1", 1), (held.ByStaffId, held.Cart.ActiveLines.Count));
    }

    [Fact]
    public async Task Changing_cashier_after_a_sale_hands_back_the_token_and_clears_the_till()
    {
        var (session, _) = await CartOf(new SaleAnswer.Completed(Done), null, "111");
        await session.PayAsync();

        Assert.Equal("token-1", session.SignOut());

        Assert.Null(session.SignedIn);
        Assert.Null(session.Paid);
        Assert.Empty(session.Cart.Lines);
    }

    [Fact]
    public async Task A_ticket_whose_every_line_was_removed_does_not_block_the_change()
    {
        var (session, _) = await CartOf(new SaleAnswer.Completed(Done), null, "111");
        session.Remove(session.Cart.LineOf("v-111"));

        Assert.Equal("token-1", session.SignOut());
        Assert.Empty(session.Cart.Lines);
    }

    // ------------------------------------------------ a line taken out (G1)

    [Fact]
    public async Task A_removed_line_is_never_sent()
    {
        // The struck line stays on screen and must never be charged. Sent, it would put back
        // what the cashier took out: the total on the receipt would be right for a sale that
        // was not the one on the screen.
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done), null, "111", "222");
        session.Remove(session.Cart.LineOf("v-111"));

        await session.PayAsync();

        Assert.Equal([new SaleRequestLine("222", 1)], Assert.Single(sales.Sent).Lines);
    }

    [Fact]
    public async Task A_cart_whose_every_line_was_removed_sends_nothing()
    {
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done), null, "111");
        session.Remove(session.Cart.LineOf("v-111"));

        await session.PayAsync();

        Assert.Empty(sales.Sent);
    }

    // ------------------------------------------------- the paid ticket (G1)

    [Fact]
    public async Task A_paid_ticket_keeps_its_lines_for_the_screen_until_a_new_sale()
    {
        // "Monnaie à rendre": the ticket just paid stays on screen, struck lines and all, while
        // the cashier gives change. The cart itself is empty, ready for the next customer.
        var (session, _) = await CartOf(new SaleAnswer.Completed(Done), null, "111", "222");
        session.Remove(session.Cart.LineOf("v-222"));

        await session.PayAsync();

        Assert.Empty(session.Cart.Lines);
        var paid = Assert.IsType<PaidTicket>(session.Paid);
        Assert.Equal(Done, paid.Outcome);
        Assert.Equal(["v-111", "v-222"], paid.Lines.Select(line => line.VariantId));
        Assert.True(paid.Lines[1].IsRemoved);

        session.StartNewSale();

        Assert.Null(session.Paid);
        Assert.Equal(Done, session.LastSale);
    }

    [Fact]
    public async Task The_next_scan_closes_the_paid_ticket()
    {
        var (session, _) = await CartOf(new SaleAnswer.Completed(Done), null, "111");
        await session.PayAsync();

        await session.SubmitAsync("222");

        Assert.Null(session.Paid);
    }

    [Fact]
    public async Task A_refused_sale_leaves_no_paid_ticket()
    {
        var (session, _) = await CartOf(new SaleAnswer.Refused("no"), null, "111");

        await session.PayAsync();

        Assert.Null(session.Paid);
    }

    [Fact]
    public async Task A_completed_sale_empties_the_cart_and_says_what_to_collect()
    {
        var (session, _) = await CartOf(new SaleAnswer.Completed(Done), null, "111");

        await session.PayAsync();

        Assert.Empty(session.Cart.Lines);
        Assert.Equal(Done, session.LastSale);
        Assert.Null(session.Notice);
    }

    [Fact]
    public async Task A_refused_sale_keeps_the_cart_and_says_why()
    {
        var (session, _) = await CartOf(new SaleAnswer.Refused("111: not sellable (NoCurrentPrice)."), null, "111");

        await session.PayAsync();

        Assert.Single(session.Cart.Lines);
        Assert.Equal(TillNoticeKind.SaleRefused, session.Notice!.Kind);
        Assert.Contains("not sellable", session.Notice.Detail, StringComparison.Ordinal);
        Assert.Null(session.LastSale);
    }

    [Fact]
    public async Task No_answer_keeps_the_cart_and_warns_the_sale_may_have_been_recorded()
    {
        // The server may have committed it and the answer been lost. Clearing the cart would
        // be right half the time; keeping it with a warning is right every time.
        var (session, _) = await CartOf(new SaleAnswer.Unknown("StoreServer did not answer within 3 s."), null, "111");

        await session.PayAsync();

        Assert.Single(session.Cart.Lines);
        Assert.Contains("may have been recorded", session.Unconfirmed!.Why, StringComparison.Ordinal);
    }

    // ------------------------------------------ an unconfirmed sale is not sent twice (D-085)

    [Fact]
    public async Task While_a_sale_is_unconfirmed_paying_again_sends_nothing()
    {
        // O-27: Encaisser pressed again would send a sale the server may already have recorded.
        var (session, sales) = await CartOf(new SaleAnswer.Unknown("timeout"), null, "111");
        await session.PayAsync();

        await session.PayAsync();

        Assert.Single(sales.Sent);
        Assert.NotNull(session.Unconfirmed);
    }

    [Fact]
    public async Task A_scan_or_a_dismissal_does_not_clear_an_unconfirmed_sale()
    {
        // The notice slot is replaced by every scan and cleared by Échap. Neither is the cashier
        // saying they checked, so neither may re-open Encaisser.
        var (session, sales) = await CartOf(new SaleAnswer.Unknown("timeout"), null, "111");
        await session.PayAsync();

        await session.SubmitAsync("222");
        session.Dismiss();
        await session.PayAsync();

        Assert.NotNull(session.Unconfirmed);
        Assert.Single(sales.Sent);
    }

    [Fact]
    public async Task Once_the_cashier_has_checked_the_ticket_can_be_paid_again()
    {
        var (session, sales) = await CartOf(new SaleAnswer.Unknown("timeout"), null, "111");
        await session.PayAsync();

        session.AcknowledgeUnconfirmed();
        await session.PayAsync();

        Assert.Equal(2, sales.Sent.Count);
        Assert.Single(session.Cart.ActiveLines);
    }

    [Fact]
    public async Task Changing_cashier_is_refused_while_a_sale_is_unconfirmed()
    {
        // The unconfirmed ticket is still on the till; the next person must not inherit it silently.
        var (session, _) = await CartOf(new SaleAnswer.Unknown("timeout"), null, "111");
        await session.PayAsync();

        Assert.Null(session.SignOut());
    }

    [Fact]
    public async Task An_empty_cart_sends_nothing()
    {
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done));

        await session.PayAsync();

        Assert.Empty(sales.Sent);
    }

    [Fact]
    public async Task A_till_without_a_terminal_sends_nothing()
    {
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done), new TillIdentity(null), "111");

        await session.PayAsync();

        Assert.Empty(sales.Sent);
        Assert.Equal(TillNoticeKind.SaleRefused, session.Notice!.Kind);
        Assert.Single(session.Cart.Lines);
    }

    [Fact]
    public async Task Paying_waits_for_a_code_still_being_looked_up()
    {
        // Scan, then press Pay at once: the scan's line must be in the sale.
        var slow = new SlowProducts();
        var sales = new Sales(new SaleAnswer.Completed(Done));
        var session = new TillSession(slow, sales, new TillIdentity("till-1"));
        session.SignIn(Cashier);

        var scan = session.SubmitAsync("111");
        var pay = session.PayAsync();
        slow.Release();
        await Task.WhenAll(scan, pay);

        Assert.Equal([new SaleRequestLine("111", 1)], Assert.Single(sales.Sent).Lines);
    }

    private sealed class SlowProducts : IProductSource
    {
        private readonly TaskCompletionSource _gate = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public void Release() => _gate.SetResult();

        public async Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default)
        {
            await _gate.Task;
            return await new Products().LookupAsync(barcode, cancellationToken);
        }
    }

    [Fact]
    public async Task The_next_scan_starts_a_new_cart_and_hides_the_last_sale()
    {
        var (session, _) = await CartOf(new SaleAnswer.Completed(Done), null, "111");
        await session.PayAsync();

        await session.SubmitAsync("222");

        Assert.Null(session.LastSale);
        Assert.Single(session.Cart.Lines);
    }
}
