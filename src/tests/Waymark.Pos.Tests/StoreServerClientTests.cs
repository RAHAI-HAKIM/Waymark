using System.Net;
using System.Text;
using Waymark.Contracts.Pos;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// The till asking StoreServer about a barcode. The line that matters is between
/// "the server answered" and "the server could not answer": an unknown barcode is
/// the first, a timeout is the second, and a till that confused them would show
/// "no such product" during an outage, or "server down" for a typo.
/// </summary>
public sealed class StoreServerClientTests
{
    /// <summary>Answers every request with whatever the test decides, and remembers what was asked.</summary>
    private sealed class FakeServer(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond)
        : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            return respond(request, cancellationToken);
        }
    }

    private static (StoreServerClient Client, FakeServer Server) Build(
        Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> respond, TimeSpan? timeout = null)
    {
        var server = new FakeServer(respond);
        var http = new HttpClient(server)
        {
            BaseAddress = StoreServerClient.DefaultAddress,
            Timeout = timeout ?? StoreServerClient.DefaultTimeout,
        };
        return (new StoreServerClient(http), server);
    }

    private static (StoreServerClient Client, FakeServer Server) Replying(string json, HttpStatusCode status = HttpStatusCode.OK) =>
        Build((_, _) => Task.FromResult(new HttpResponseMessage(status)
        {
            Content = new StringContent(json, Encoding.UTF8, "application/json"),
        }));

    private const string Found = """
        {"outcome":"found","barcode":"6130000000017","reason":null,"product":{
          "variant_id":"v1","product_id":"p1","product_name":"Lait UHT Candia","variant_name":"Brique 1L",
          "selling_unit_code":"pc","selling_unit_decimal_places":0,"tva_rate_basis_points":1900,
          "price_ttc":"120.50","currency":"DZD","stock_on_hand":"-2"}}
        """;

    // ------------------------------------------------------ the server answered

    [Fact]
    public async Task A_found_product_arrives_with_its_figures_as_sent()
    {
        var (client, _) = Replying(Found);

        var answer = await client.LookupAsync("6130000000017");

        var lookup = Assert.IsType<LookupAnswer.Answered>(answer).Lookup;
        Assert.Equal(ProductLookupOutcome.Found, lookup.Outcome);
        Assert.Equal("120.50", lookup.Product!.PriceTtc);
        Assert.Equal("-2", lookup.Product.StockOnHand);
        Assert.Equal(1_900, lookup.Product.TvaRateBasisPoints);
    }

    [Fact]
    public async Task An_unknown_barcode_is_an_answer_not_an_outage()
    {
        var (client, _) = Replying("""{"outcome":"unknown_barcode","barcode":"000","product":null,"reason":null}""");

        var lookup = Assert.IsType<LookupAnswer.Answered>(await client.LookupAsync("000")).Lookup;

        Assert.Equal(ProductLookupOutcome.UnknownBarcode, lookup.Outcome);
    }

    [Fact]
    public async Task A_refusal_is_an_answer_with_its_reason()
    {
        var (client, _) = Replying("""{"outcome":"not_sellable","barcode":"1","product":null,"reason":"no_current_price"}""");

        var lookup = Assert.IsType<LookupAnswer.Answered>(await client.LookupAsync("1")).Lookup;

        Assert.Equal(NotSellableReason.NoCurrentPrice, lookup.Reason);
    }

    [Fact]
    public async Task The_barcode_is_escaped_into_the_route()
    {
        // Typed by hand, a code can hold characters that would otherwise change
        // the request: "12/34 ?5&x" must ask about exactly that code.
        var (client, server) = Replying("""{"outcome":"unknown_barcode","barcode":"x","product":null,"reason":null}""");

        await client.LookupAsync("12/34 ?5&x");

        var asked = server.Asked.Single();
        Assert.Equal("/api/products/lookup", asked.AbsolutePath);
        Assert.Equal("?barcode=12%2F34%20%3F5%26x", asked.Query);
    }

    // ------------------------------------------------- the server did not answer

    [Fact]
    public async Task An_error_status_is_an_outage_even_with_a_body()
    {
        var (client, _) = Replying("""{"outcome":"unknown_barcode","barcode":"1","product":null,"reason":null}""", HttpStatusCode.InternalServerError);

        var answer = await client.LookupAsync("1");

        Assert.Contains("500", Assert.IsType<LookupAnswer.ServerUnavailable>(answer).Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_refused_connection_is_an_outage()
    {
        var (client, _) = Build((_, _) => throw new HttpRequestException("No connection could be made"));

        var answer = await client.LookupAsync("1");

        Assert.IsType<LookupAnswer.ServerUnavailable>(answer);
    }

    [Fact]
    public async Task A_server_that_does_not_answer_in_time_is_an_outage()
    {
        var (client, _) = Build(
            async (_, token) =>
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, token);
                return new HttpResponseMessage(HttpStatusCode.OK);
            },
            timeout: TimeSpan.FromMilliseconds(50));

        var answer = await client.LookupAsync("1");

        Assert.Contains("did not answer", Assert.IsType<LookupAnswer.ServerUnavailable>(answer).Why, StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_callers_cancellation_is_not_reported_as_an_outage()
    {
        // Nobody is waiting for this answer any more (the cashier scanned again,
        // or closed the till). Reporting "server down" would be a lie.
        using var cancel = new CancellationTokenSource();
        var (client, _) = Build(async (_, token) =>
        {
            await cancel.CancelAsync();
            await Task.Delay(Timeout.InfiniteTimeSpan, token);
            return new HttpResponseMessage(HttpStatusCode.OK);
        });

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.LookupAsync("1", cancel.Token));
    }

    [Theory]
    [InlineData("not json at all")]
    [InlineData("""{"outcome":"sold_out","barcode":"1","product":null,"reason":null}""")]
    [InlineData("""{"outcome":"found","barcode":"1","product":null,"reason":null}""")]
    [InlineData("""{"outcome":"not_sellable","barcode":"1","product":null,"reason":null}""")]
    [InlineData("null")]
    public async Task An_answer_the_till_cannot_read_is_an_outage_not_a_guess(string body)
    {
        var (client, _) = Replying(body);

        var answer = await client.LookupAsync("1");

        Assert.IsType<LookupAnswer.ServerUnavailable>(answer);
    }
}
