using System.Net;
using System.Text;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// The shell's own calls to StoreServer (A4): who and where the till is, the Almanac board, a
/// decision, and whether the server is there. The line that matters is the client's usual one:
/// the server said something, or it could not say anything — and the second never reads as an
/// empty answer. An outage shown as "no cards for you" would tell a cashier the engine found
/// nothing.
/// </summary>
public sealed class TillServerClientTests
{
    private sealed class FakeServer(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond) : HttpMessageHandler
    {
        public List<Uri> Asked { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Asked.Add(request.RequestUri!);
            return respond(request);
        }
    }

    private static (StoreServerClient Client, FakeServer Server) Build(Func<HttpRequestMessage, Task<HttpResponseMessage>> respond)
    {
        var server = new FakeServer(respond);
        return (new StoreServerClient(new HttpClient(server) { BaseAddress = StoreServerClient.DefaultAddress }), server);
    }

    private static Task<HttpResponseMessage> Json(string body, HttpStatusCode status = HttpStatusCode.OK) =>
        Task.FromResult(new HttpResponseMessage(status) { Content = new StringContent(body, Encoding.UTF8, "application/json") });

    private static Task<HttpResponseMessage> Refused(HttpRequestMessage _) =>
        throw new HttpRequestException("connection refused");

    // ================================================================= context

    [Fact]
    public async Task The_context_is_asked_for_with_the_terminal_and_the_staff_escaped()
    {
        var (client, server) = Build(_ => Json("""
            {"outcome":"found","store_name":"El Bahdja","terminal_name":"Caisse 1","currency":"DZD",
             "staff_name":"Nabil B.","role_label_fr":"Caissier","role_label_ar":"أمين صندوق"}
            """));

        var context = await client.ContextAsync("till 1", "staff/1");

        Assert.Equal("El Bahdja", context!.StoreName);
        Assert.Equal("أمين صندوق", context.RoleLabelAr);
        Assert.Equal("/api/till/context?terminal=till%201&staff=staff%2F1", Assert.Single(server.Asked).PathAndQuery);
    }

    [Fact]
    public async Task An_unknown_terminal_is_an_answer()
    {
        var (client, _) = Build(_ => Json("""{"outcome":"unknown_terminal"}"""));

        Assert.Equal(TillContextOutcome.UnknownTerminal, (await client.ContextAsync("x", null))!.Outcome);
    }

    [Fact]
    public async Task No_context_when_the_server_cannot_say()
    {
        var (refused, _) = Build(Refused);
        var (garbled, _) = Build(_ => Json("""{"outcome":"something else"}"""));
        var (failing, _) = Build(_ => Json("{}", HttpStatusCode.InternalServerError));

        Assert.Null(await refused.ContextAsync("x", null));
        Assert.Null(await garbled.ContextAsync("x", null));
        Assert.Null(await failing.ContextAsync("x", null));
    }

    // =================================================================== board

    [Fact]
    public async Task A_board_is_read_with_its_withheld_count()
    {
        var (client, server) = Build(_ => Json("""{"outcome":"answered","staff_name":"Nabil B.","role_code":"cashier","withheld":2,"cards":[]}"""));

        var board = await client.BoardAsync("staff-1");

        Assert.Equal(2, board!.Withheld);
        Assert.Empty(board.Cards);
        Assert.Equal("/api/recommendations?staff=staff-1", Assert.Single(server.Asked).PathAndQuery);
    }

    [Fact]
    public async Task No_board_when_the_server_cannot_say_rather_than_an_empty_one()
    {
        // An empty board is a claim: "nothing for you". An outage is not evidence of that.
        var (refused, _) = Build(Refused);
        var (garbled, _) = Build(_ => Json("not json"));

        Assert.Null(await refused.BoardAsync("staff-1"));
        Assert.Null(await garbled.BoardAsync("staff-1"));
    }

    // ================================================================ decision

    [Fact]
    public async Task A_decision_posts_what_was_chosen_and_reads_the_answer()
    {
        string? posted = null;
        var (client, _) = Build(async request =>
        {
            posted = await request.Content!.ReadAsStringAsync();
            return await Json("""{"outcome":"recorded","decision_id":"d1"}""");
        });

        var answer = await client.DecideAsync(new DecisionRequest("r1", "staff-1", "accept", "o1"));

        Assert.Equal(DecisionOutcome.Recorded, answer!.Outcome);
        Assert.Contains("\"decision\":\"accept\"", posted, StringComparison.Ordinal);
        Assert.Contains("\"option_id\":\"o1\"", posted, StringComparison.Ordinal);
    }

    [Fact]
    public async Task No_decision_answer_when_the_server_cannot_say()
    {
        var (client, _) = Build(Refused);

        Assert.Null(await client.DecideAsync(new DecisionRequest("r1", "staff-1", "dismiss", null)));
    }

    // ================================================================== health

    [Fact]
    public async Task Health_is_whether_the_server_answers_at_all()
    {
        var (up, server) = Build(_ => Json("""{"status":"up"}"""));
        var (down, _) = Build(Refused);
        var (failing, _) = Build(_ => Json("{}", HttpStatusCode.ServiceUnavailable));

        Assert.True(await up.HealthAsync());
        Assert.Equal("/health", Assert.Single(server.Asked).PathAndQuery);
        Assert.False(await down.HealthAsync());
        Assert.False(await failing.HealthAsync());
    }

    [Fact]
    public async Task The_callers_own_cancellation_is_not_an_outage()
    {
        using var cancelled = new CancellationTokenSource();
        await cancelled.CancelAsync();
        var (client, _) = Build(_ => throw new TaskCanceledException());

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => client.HealthAsync(cancelled.Token));
    }
}
