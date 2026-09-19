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

    private static LookupAnswer.Answered Found(string code, string variantId, string price = "120.50", string stock = "10") =>
        new LookupAnswer.Answered(new ProductLookup(
            ProductLookupOutcome.Found,
            code,
            new ProductForSale(variantId, "p-" + variantId, "Lait", "1L", "pc", 0, 1_900, price, "DZD", stock),
            Reason: null));

    private static LookupAnswer.Answered Unknown(string code) =>
        new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.UnknownBarcode, code, null, null));

    private static LookupAnswer.Answered Refused(string code, string reason) =>
        new LookupAnswer.Answered(new ProductLookup(ProductLookupOutcome.NotSellable, code, null, reason));

    private static (TillSession Session, ScriptedSource Source) Build()
    {
        var source = new ScriptedSource();
        return (new TillSession(source), source);
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
    public async Task A_refused_code_says_why_in_words()
    {
        var (session, source) = Build();
        source.Answer("222", Refused("222", NotSellableReason.NoCurrentPrice));

        await session.SubmitAsync("222");

        Assert.Empty(session.Cart.Lines);
        Assert.Equal(TillNoticeKind.NotSellable, session.Notice!.Kind);
        Assert.Contains("No price", session.Notice.Detail, StringComparison.Ordinal);
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
        var session = new TillSession(source);

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
        session.Remove("milk");

        Assert.Equal(2, changes);
    }

    [Fact]
    public async Task Every_refusal_reason_has_words_for_the_cashier()
    {
        // A reason added to the contract without words would show its raw code.
        var reasons = typeof(NotSellableReason).GetFields()
            .Select(field => (string)field.GetValue(null)!)
            .ToList();

        foreach (var reason in reasons)
        {
            var (session, source) = Build();
            source.Answer("x", Refused("x", reason));
            await session.SubmitAsync("x");

            // The fallback for a reason with no words is "Not sellable (<code>)".
            Assert.DoesNotContain("Not sellable (", session.Notice!.Detail, StringComparison.Ordinal);
        }
    }
}
