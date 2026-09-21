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
                new ProductForSale("v-" + barcode, "p-" + barcode, "Lait", "1L", "pc", 0, 900, "143.00", "DZD", "10"),
                Reason: null)));
    }

    private sealed class Sales(SaleAnswer answer) : IStoreSales
    {
        public List<SaleRequest> Sent { get; } = [];

        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, CancellationToken cancellationToken = default)
        {
            Sent.Add(request);
            return Task.FromResult(answer);
        }
    }

    private static readonly SaleOutcome Done =
        new(SaleOutcomes.Completed, "t1", "S-2026-000001", "286.00", "23.61", "285.00", "DZD", null);

    private static async Task<(TillSession Session, Sales Sales)> CartOf(SaleAnswer answer, TillIdentity? till = null, params string[] codes)
    {
        var sales = new Sales(answer);
        var session = new TillSession(new Products(), sales, till ?? new TillIdentity("till-1", "staff-1"));
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
        Assert.Equal("staff-1", request.StaffId);
        Assert.Equal([new SaleRequestLine("111", 2), new SaleRequestLine("222", 1)], request.Lines);
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
        Assert.Equal(TillNoticeKind.SaleOutcomeUnknown, session.Notice!.Kind);
        Assert.Contains("may have been recorded", session.Notice.Detail, StringComparison.Ordinal);
    }

    [Fact]
    public async Task An_empty_cart_sends_nothing()
    {
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done));

        await session.PayAsync();

        Assert.Empty(sales.Sent);
    }

    [Theory]
    [InlineData(null, "staff-1")]
    [InlineData("till-1", null)]
    public async Task A_till_without_a_terminal_or_staff_sends_nothing(string? terminal, string? staff)
    {
        var (session, sales) = await CartOf(new SaleAnswer.Completed(Done), new TillIdentity(terminal, staff), "111");

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
        var session = new TillSession(slow, sales, new TillIdentity("till-1", "staff-1"));

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
