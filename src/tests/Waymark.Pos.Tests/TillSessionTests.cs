using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// What the till does with a code (D-068): a line, or a notice that says why not,
/// and never one mistaken for the other. Each code is answered by a script, and
/// the tests decide when each answer arrives.
/// </summary>
public sealed class TillSessionTests
{
    /// <summary>Answers each code when the test releases it, in whatever order the test chooses.</summary>
    private sealed class ScriptedSource : IProductSource
    {
        private readonly Dictionary<string, TaskCompletionSource<LookupAnswer>> _pending = [];

        public List<string> Asked { get; } = [];

        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default)
        {
            Asked.Add(barcode);
            return Pending(barcode).Task;
        }

        public void Answer(string barcode, LookupAnswer answer) => Pending(barcode).SetResult(answer);

        private TaskCompletionSource<LookupAnswer> Pending(string barcode)
        {
            if (!_pending.TryGetValue(barcode, out var pending))
            {
                pending = new TaskCompletionSource<LookupAnswer>(TaskCreationOptions.RunContinuationsAsynchronously);
                _pending[barcode] = pending;
            }

            return pending;
        }
    }

    private static LookupAnswer.Answered Found(
        string code, string variantId, string price = "120.50", string stock = "10", bool promotional = false) =>
        new LookupAnswer.Answered(new ProductLookup(
            ProductLookupOutcome.Found,
            code,
            new ProductForSale(variantId, "p-" + variantId, "Lait", "1L", "pc", 0, 1_900,
                TvaRateSource.FromCategory, price, "DZD", promotional, stock),
            Reason: null));

    private static LookupAnswer.Answered Unknown(string code) =>
        new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.UnknownBarcode, code, null, null));

    private static LookupAnswer.Answered Refused(string code, string reason) =>
        new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.NotSellable, code, null, reason));

    private static readonly TillIdentity Till = new("till-1");

    /// <summary>For the tests about scanning, where nothing is paid.</summary>
    private sealed class NoSales : IStoreSales
    {
        public Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("These tests never pay.");
    }

    private static (TillSession Session, ScriptedSource Source) Build()
    {
        var source = new ScriptedSource();
        return (new TillSession(source, new NoSales(), Till), source);
    }

    [Fact]
    public async Task A_found_code_becomes_a_line_and_no_notice()
    {
        var (session, source) = Build();
        source.Answer("111", Found("111", "milk"));

        await session.SubmitAsync("111");

        Assert.Equal("milk", Assert.Single(session.Cart.Lines).VariantId);
        Assert.Null(session.Notice);
    }

    [Fact]
    public async Task An_unknown_code_is_a_notice_naming_the_code_and_the_cart_is_untouched()
    {
        var (session, source) = Build();
        source.Answer("999", Unknown("999"));

        await session.SubmitAsync("999");

        Assert.Empty(session.Cart.Lines);
        Assert.Equal(new TillNotice(TillNoticeKind.UnknownCode, "999", "No product carries this code."), session.Notice);
    }

    [Fact]
    public async Task A_refused_code_carries_its_reason_for_the_screen_to_say()
    {
        // The session keeps the reason's code; TillText says it in the till's language
        // (TillTextTests.Every_refusal_reason_has_words_in_both_languages).
        var (session, source) = Build();
        source.Answer("222", Refused("222", NotSellableReason.NoCurrentPrice));

        await session.SubmitAsync("222");

        Assert.Empty(session.Cart.Lines);
        Assert.Equal(TillNoticeKind.NotSellable, session.Notice!.Kind);
        Assert.Equal(NotSellableReason.NoCurrentPrice, session.Notice.Detail);
    }

    [Fact]
    public async Task An_outage_is_its_own_notice_never_an_unknown_code()
    {
        // During an outage every code would otherwise read as "no such
        // product", and the cashier would start typing codes that are fine.
        var (session, source) = Build();
        source.Answer("111", new LookupAnswer.ServerUnavailable("StoreServer could not be reached."));

        await session.SubmitAsync("111");

        Assert.Equal(TillNoticeKind.ServerUnavailable, session.Notice!.Kind);
        Assert.Empty(session.Cart.Lines);
    }

    [Fact]
    public async Task An_answer_whose_figures_cannot_be_read_exactly_is_a_notice_not_a_line()
    {
        var (session, source) = Build();
        source.Answer("111", Found("111", "milk", price: "120.505"));

        await session.SubmitAsync("111");

        Assert.Empty(session.Cart.Lines);
        Assert.Equal(TillNoticeKind.ServerUnavailable, session.Notice!.Kind);
    }

    [Fact]
    public async Task The_next_good_code_clears_the_notice()
    {
        var (session, source) = Build();
        source.Answer("999", Unknown("999"));
        source.Answer("111", Found("111", "milk"));

        await session.SubmitAsync("999");
        await session.SubmitAsync("111");

        Assert.Null(session.Notice);
        Assert.Single(session.Cart.Lines);
    }

    [Fact]
    public async Task Codes_are_handled_in_the_order_they_were_scanned_whatever_order_answers_arrive_in()
    {
        var (session, source) = Build();

        var first = session.SubmitAsync("111");
        var second = session.SubmitAsync("222");

        // The server answers the second scan first.
        source.Answer("222", Found("222", "bread"));
        source.Answer("111", Found("111", "milk"));
        await Task.WhenAll(first, second);

        Assert.Equal(["milk", "bread"], session.Cart.Lines.Select(line => line.VariantId));
    }

    [Fact]
    public async Task A_notice_for_an_earlier_scan_does_not_hide_a_later_one()
    {
        var (session, source) = Build();

        var first = session.SubmitAsync("111");
        var second = session.SubmitAsync("999");
        source.Answer("999", Unknown("999"));
        source.Answer("111", Found("111", "milk"));
        await Task.WhenAll(first, second);

        // The last code handled was 999, so its notice is what stays on screen.
        Assert.Equal("999", session.Notice!.Code);
        Assert.Single(session.Cart.Lines);
    }

    [Fact]
    public async Task A_failed_lookup_does_not_stop_the_codes_behind_it()
    {
        var source = new ThrowingOnce();
        var session = new TillSession(source, new NoSales(), Till);

        var first = session.SubmitAsync("boom");
        var second = session.SubmitAsync("111");

        await Assert.ThrowsAsync<InvalidOperationException>(() => first);
        await second;
        Assert.Single(session.Cart.Lines);
    }

    private sealed class ThrowingOnce : IProductSource
    {
        public Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default) =>
            barcode == "boom"
                ? Task.FromException<LookupAnswer>(new InvalidOperationException("boom"))
                : Task.FromResult<LookupAnswer>(Found(barcode, "milk"));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task A_blank_code_asks_nothing(string code)
    {
        var (session, source) = Build();

        // Answered in advance, so a session that wrongly asks fails the
        // assertion below instead of waiting forever.
        source.Answer(code.Trim(), Unknown(code));
        await session.SubmitAsync(code);

        Assert.Empty(source.Asked);
    }

    [Fact]
    public async Task A_typed_code_is_trimmed_before_it_is_asked()
    {
        var (session, source) = Build();
        source.Answer("111", Found("111", "milk"));

        await session.SubmitAsync("  111 ");

        Assert.Equal(["111"], source.Asked);
    }

    [Fact]
    public async Task Every_change_is_announced()
    {
        var (session, source) = Build();
        var changes = 0;
        session.Changed += (_, _) => changes++;
        source.Answer("111", Found("111", "milk"));

        await session.SubmitAsync("111");
        session.Remove(session.Cart.LineOf("milk"));

        Assert.Equal(2, changes);
    }

    // --------------------------------------------- the server's reachability

    /// <summary>A clock the test moves by hand.</summary>
    private sealed class Clock(DateTimeOffset start) : TimeProvider
    {
        public DateTimeOffset Now { get; set; } = start;

        public override DateTimeOffset GetUtcNow() => Now;
    }

    private static readonly DateTimeOffset Morning = new(2026, 9, 23, 13, 31, 0, TimeSpan.Zero);

    [Fact]
    public async Task An_unanswered_scan_marks_the_server_unreachable_from_that_moment()
    {
        var source = new ScriptedSource();
        var clock = new Clock(Morning);
        var session = new TillSession(source, new NoSales(), Till, clock);
        source.Answer("111", new LookupAnswer.ServerUnavailable("timeout"));

        await session.SubmitAsync("111");

        Assert.False(session.Server.IsReachable);
        Assert.Equal(Morning, session.Server.UnreachableSince);
    }

    [Fact]
    public async Task Unreachable_since_is_the_first_failure_not_the_latest()
    {
        // "Injoignable depuis 14:31" must keep saying 14:31 while scans keep failing; moving it
        // to each new failure would make a long outage look like it had just begun.
        var source = new ScriptedSource();
        var clock = new Clock(Morning);
        var session = new TillSession(source, new NoSales(), Till, clock);
        source.Answer("111", new LookupAnswer.ServerUnavailable("timeout"));
        source.Answer("222", new LookupAnswer.ServerUnavailable("timeout"));

        await session.SubmitAsync("111");
        clock.Now = Morning.AddMinutes(5);
        await session.SubmitAsync("222");

        Assert.Equal(Morning, session.Server.UnreachableSince);
    }

    [Fact]
    public async Task Any_answer_means_the_server_is_back()
    {
        // An unknown barcode is still an answer: the server is up, the code is wrong.
        var source = new ScriptedSource();
        var session = new TillSession(source, new NoSales(), Till, new Clock(Morning));
        source.Answer("111", new LookupAnswer.ServerUnavailable("timeout"));
        source.Answer("222", Unknown("222"));

        await session.SubmitAsync("111");
        await session.SubmitAsync("222");

        Assert.True(session.Server.IsReachable);
    }

    [Fact]
    public void A_health_check_changes_the_state_and_says_so_only_when_it_changes()
    {
        var session = new TillSession(new ScriptedSource(), new NoSales(), Till, new Clock(Morning));
        var changes = 0;
        session.Changed += (_, _) => changes++;

        session.ReportHealth(reachable: true);
        session.ReportHealth(reachable: false);
        session.ReportHealth(reachable: false);
        session.ReportHealth(reachable: true);

        Assert.Equal(2, changes);
        Assert.True(session.Server.IsReachable);
    }

    // ------------------------------------------------------ a line taken out

    [Fact]
    public async Task Removing_a_line_stamps_it_with_the_tills_clock()
    {
        var source = new ScriptedSource();
        var clock = new Clock(Morning);
        var session = new TillSession(source, new NoSales(), Till, clock);
        source.Answer("111", Found("111", "milk"));
        await session.SubmitAsync("111");
        clock.Now = Morning.AddMinutes(3);

        session.Remove(session.Cart.LineOf("milk"));

        Assert.Equal(Morning.AddMinutes(3), Assert.Single(session.Cart.Lines).RemovedAt);
    }
}
