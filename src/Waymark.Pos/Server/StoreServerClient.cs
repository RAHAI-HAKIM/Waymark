using System.Net.Http.Json;
using System.Text.Json;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Contracts.Reference;

namespace Waymark.Pos.Server;

/// <summary>
/// The till's only way to StoreServer: HTTP, even when both run on one machine
/// (CLAUDE.md §2.2). One code path, not two.
///
/// <para>
/// <b>Two kinds of answer, never confused.</b> Anything StoreServer says about a
/// product, including "no such barcode" and "not sellable", is
/// <see cref="LookupAnswer.Answered"/> and goes to the cashier as it is. Anything
/// that stops it saying something (refused connection, timeout, an error status,
/// a reply this till cannot read) is <see cref="LookupAnswer.ServerUnavailable"/>.
/// There is no Level-2 cache in the skeleton, so that is shown plainly rather
/// than guessed around.
/// </para>
/// </summary>
public sealed class StoreServerClient(HttpClient http) : IProductSource, IStoreSales, ITillServer
{
    /// <summary>StoreServer's development address (its launch profile).</summary>
    public static readonly Uri DefaultAddress = new("http://localhost:5290/");

    /// <summary>
    /// How long a scan may wait for the server. The server is on the same machine
    /// or the same LAN; three seconds is already an outage to a cashier.
    /// </summary>
    public static readonly TimeSpan DefaultTimeout = TimeSpan.FromSeconds(3);

    /// <summary>An <see cref="HttpClient"/> for StoreServer at <paramref name="address"/>.</summary>
    public static HttpClient CreateHttp(Uri address, TimeSpan? timeout = null) => new()
    {
        BaseAddress = address,
        Timeout = timeout ?? DefaultTimeout,
    };

    /// <summary>What StoreServer says the till may sell for <paramref name="barcode"/> (D-066).</summary>
    public async Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        // A query parameter, escaped: a code typed by hand can hold '/', '?' or
        // '&', and the server decodes a query string exactly once (D-066).
        return await AskLookupAsync(
            new Uri($"api/products/lookup?barcode={Uri.EscapeDataString(barcode)}", UriKind.Relative), cancellationToken);
    }

    /// <summary>
    /// What StoreServer says of a product sold by weight, weighed at <paramref name="weight"/> (B3,
    /// D-090): the weight in its selling unit as invariant text, "0.556". The server prices it.
    /// </summary>
    public async Task<LookupAnswer> WeighAsync(string code, string weight, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentException.ThrowIfNullOrWhiteSpace(weight);

        return await AskLookupAsync(
            new Uri($"api/products/lookup?barcode={Uri.EscapeDataString(code)}&weight={Uri.EscapeDataString(weight)}", UriKind.Relative),
            cancellationToken);
    }

    private async Task<LookupAnswer> AskLookupAsync(Uri path, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await http.GetAsync(path, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new LookupAnswer.ServerUnavailable(
                    $"StoreServer answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var lookup = await response.Content.ReadFromJsonAsync<ProductLookup>(cancellationToken);
            return lookup is not null && IsWellFormed(lookup)
                ? new LookupAnswer.Answered(lookup)
                : new LookupAnswer.ServerUnavailable("StoreServer's answer could not be read.");
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            // HttpClient reports its own timeout as a cancellation. The caller's
            // cancellation is not caught: it propagates, because nobody is
            // waiting for the answer any more.
            return new LookupAnswer.ServerUnavailable($"StoreServer did not answer within {http.Timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException exception)
        {
            return new LookupAnswer.ServerUnavailable($"StoreServer could not be reached: {exception.Message}");
        }
        catch (JsonException)
        {
            return new LookupAnswer.ServerUnavailable("StoreServer's answer could not be read.");
        }
    }

    /// <summary>
    /// Asks StoreServer to complete a cash sale (D-070). The request carries codes and counts;
    /// the server prices them.
    ///
    /// <para>
    /// <b>No answer is not a failed sale.</b> The server may have committed it and the answer
    /// been lost. So an outage here says the outcome is unknown, and the till keeps the cart
    /// for the cashier to check rather than selling it again blindly. A key that makes a
    /// repeated request harmless is Phase 1.
    /// </para>
    /// <para>
    /// <b>Who is selling is the session's</b> (A5, D-083): the token goes in
    /// <see cref="TillSessionHeader.Name"/>, and the request names nobody.
    /// </para>
    /// </summary>
    public async Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("api/sales", UriKind.Relative))
            {
                Content = JsonContent.Create(request),
            };
            message.Headers.Add(TillSessionHeader.Name, sessionToken);
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return new SaleAnswer.Unknown($"StoreServer answered {(int)response.StatusCode} {response.ReasonPhrase}.");
            }

            var outcome = await response.Content.ReadFromJsonAsync<SaleOutcome>(cancellationToken);
            return outcome switch
            {
                { Outcome: SaleOutcomes.Completed, InvoiceNumber: not null, TotalTtc: not null, CashToCollect: not null, Currency: not null } =>
                    new SaleAnswer.Completed(outcome),
                { Outcome: SaleOutcomes.Refused, Reason: { } reason } => new SaleAnswer.Refused(reason),
                { Outcome: SaleOutcomes.NotSignedIn } => new SaleAnswer.NotSignedIn(),
                _ => new SaleAnswer.Unknown("StoreServer's answer could not be read."),
            };
        }
        catch (TaskCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            return new SaleAnswer.Unknown($"StoreServer did not answer within {http.Timeout.TotalSeconds:0} s.");
        }
        catch (HttpRequestException exception)
        {
            return new SaleAnswer.Unknown($"StoreServer could not be reached: {exception.Message}");
        }
        catch (JsonException)
        {
            return new SaleAnswer.Unknown("StoreServer's answer could not be read.");
        }
    }

    /// <summary>Products by name (B1, D-088). Null when the server could not say, never an empty list for an outage.</summary>
    public async Task<ProductSearchAnswer?> SearchAsync(string query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var answer = await GetAsync<ProductSearchAnswer>($"api/products/search?q={Uri.EscapeDataString(query)}", cancellationToken);
        return answer?.Results is null ? null : answer;
    }

    /// <summary>The finished sales of one store day (B1). Null when the server could not say.</summary>
    /// <param name="allTills">Every till of the store rather than this one: rank 2 (D-088).</param>
    public async Task<TicketList?> TicketsAsync(DateOnly day, bool allTills, string sessionToken, CancellationToken cancellationToken = default)
    {
        var path = $"api/tickets?day={day:yyyy-MM-dd}" + (allTills ? "&all=true" : string.Empty);
        var list = await GetWithSessionAsync<TicketList>(path, sessionToken, cancellationToken);
        return list?.Tickets is null ? null : list;
    }

    /// <summary>One past ticket by its id or number (B1). Null when the server could not say.</summary>
    public async Task<TicketAnswer?> TicketAsync(string idOrNumber, string sessionToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(idOrNumber);
        var answer = await GetWithSessionAsync<TicketAnswer>($"api/tickets/one?ticket={Uri.EscapeDataString(idOrNumber)}", sessionToken, cancellationToken);
        return answer?.Outcome is null || (answer.Outcome == TicketOutcomes.Found && answer.Ticket is null) ? null : answer;
    }

    /// <summary>The shop's active discount reasons (B4). Null when the server could not say.</summary>
    public async Task<ReasonCodeList?> DiscountReasonsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetAsync<ReasonCodeList>("api/reason-codes?applies_to=discount", cancellationToken);
        return list?.ReasonCodes is null ? null : list;
    }

    /// <summary>The shop's active reasons for cancelling a ticket (B8). Null when the server could not say.</summary>
    public async Task<ReasonCodeList?> VoidReasonsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetAsync<ReasonCodeList>("api/reason-codes?applies_to=void", cancellationToken);
        return list?.ReasonCodes is null ? null : list;
    }

    /// <summary>
    /// A cancel, recorded before the till lets the ticket go (B8, D-097). Null when the server could
    /// not say: the ticket stays, since nothing says the cancel was recorded.
    /// </summary>
    public Task<VoidAnswer?> VoidAsync(VoidRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<VoidAnswer>(HttpMethod.Post, "api/sales/void", request, sessionToken, cancellationToken);

    /// <summary>The shop's reasons for money into the drawer (B7, a tab repaid). Null when the server could not say.</summary>
    public async Task<ReasonCodeList?> CashReasonsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetAsync<ReasonCodeList>("api/reason-codes?applies_to=cash_movement", cancellationToken);
        return list?.ReasonCodes is null ? null : list;
    }

    /// <summary>The shop's active return reasons (B9). Null when the server could not say.</summary>
    public async Task<ReasonCodeList?> ReturnReasonsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetAsync<ReasonCodeList>("api/reason-codes?applies_to=return", cancellationToken);
        return list?.ReasonCodes is null ? null : list;
    }

    /// <summary>
    /// A refund, quoted or written (B9, D-098). Null when the server could not say: nothing is taken to
    /// be refunded, and the cashier asks again rather than paying out what nothing recorded.
    /// </summary>
    public Task<RefundAnswer?> RefundAsync(RefundRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<RefundAnswer>(HttpMethod.Post, "api/sales/refund", request, sessionToken, cancellationToken);

    /// <summary>A paid-in or a paid-out (B10, D-102). Null when the server could not say: nothing is taken as recorded.</summary>
    public Task<CashMovementAnswer?> CashMovementAsync(CashMovementRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<CashMovementAnswer>(HttpMethod.Post, "api/cash/movements", request, sessionToken, cancellationToken);

    /// <summary>A person clocking in or out (B10, D-102). Null when the server could not say.</summary>
    public Task<ClockAnswer?> ClockAsync(ClockRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<ClockAnswer>(HttpMethod.Post, "api/till/clock", request, sessionToken, cancellationToken);

    /// <summary>The shop's active price override reasons (B5). Null when the server could not say.</summary>
    public async Task<ReasonCodeList?> OverrideReasonsAsync(CancellationToken cancellationToken = default)
    {
        var list = await GetAsync<ReasonCodeList>("api/reason-codes?applies_to=price_override", cancellationToken);
        return list?.ReasonCodes is null ? null : list;
    }

    /// <summary>
    /// Whether a discount may be given (B4, D-091): the seller alone, or a manager's PIN. Null when
    /// the server could not say: the till then says it is offline, never that the PIN was wrong.
    /// </summary>
    public async Task<AuthoriseAnswer?> AuthoriseAsync(AuthoriseRequest request, string sessionToken, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("api/till/authorise", UriKind.Relative))
            {
                Content = JsonContent.Create(request),
            };
            message.Headers.Add(TillSessionHeader.Name, sessionToken);
            using var response = await http.SendAsync(message, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var answer = await response.Content.ReadFromJsonAsync<AuthoriseAnswer>(cancellationToken);
            return answer switch
            {
                { Outcome: AuthoriseOutcomes.Authorised, Authorisation: { Length: > 0 } } => answer,
                { Outcome: AuthoriseOutcomes.Locked, LockedUntil: not null } => answer,
                { Outcome: AuthoriseOutcomes.PinRequired or AuthoriseOutcomes.NotAllowed or AuthoriseOutcomes.WrongPin
                    or AuthoriseOutcomes.NoPin or AuthoriseOutcomes.UnknownStaff or AuthoriseOutcomes.NotSignedIn } => answer,
                _ => null,
            };
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return null;
        }
    }

    // ---------------------------------------------------------- B7: customers and their tabs (D-096)

    /// <summary>The customers with a full name (D-100). Null when the server could not say.</summary>
    public Task<CustomerSearchAnswer?> FindCustomersByNameAsync(string name, string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<CustomerSearchAnswer>(
            HttpMethod.Get, $"api/customers?name={Uri.EscapeDataString(name)}&terminal={Uri.EscapeDataString(terminalId)}", null, sessionToken, cancellationToken);

    /// <summary>The customers with this number, as typed: the server puts it in its one form. Null when the server could not say.</summary>
    public Task<CustomerSearchAnswer?> FindCustomersAsync(string phone, string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<CustomerSearchAnswer>(
            HttpMethod.Get, $"api/customers?phone={Uri.EscapeDataString(phone)}&terminal={Uri.EscapeDataString(terminalId)}", null, sessionToken, cancellationToken);

    /// <summary>A customer created at the till. Null when the server could not say: the till then does not know whether one was made.</summary>
    public Task<CustomerAnswer?> CreateCustomerAsync(CreateCustomerRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<CustomerAnswer>(HttpMethod.Post, "api/customers", request, sessionToken, cancellationToken);

    /// <summary>A customer's tab: the server's figures. Null when the server could not say.</summary>
    public Task<TabAnswer?> TabAsync(string customerId, string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<TabAnswer>(
            HttpMethod.Get, $"api/customers/{Uri.EscapeDataString(customerId)}/tab?terminal={Uri.EscapeDataString(terminalId)}", null, sessionToken, cancellationToken);

    /// <summary>A repayment in cash. Null when the server could not say: the till then does not know whether it was recorded.</summary>
    public Task<TabAnswer?> RepayAsync(string customerId, RepaymentRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<TabAnswer>(HttpMethod.Post, $"api/customers/{Uri.EscapeDataString(customerId)}/repayments", request, sessionToken, cancellationToken);

    /// <summary>A change to a tab's limit, or a freeze. Null when the server could not say.</summary>
    public Task<TabAnswer?> ChangeLimitAsync(string customerId, LimitRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        SendAsync<TabAnswer>(HttpMethod.Post, $"api/customers/{Uri.EscapeDataString(customerId)}/limit", request, sessionToken, cancellationToken);

    /// <summary>A request carrying the session's token, answered in JSON; null for an outage or an answer that is not a 200.</summary>
    private async Task<T?> SendAsync<T>(HttpMethod method, string path, object? body, string sessionToken, CancellationToken cancellationToken)
        where T : class
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        try
        {
            using var message = new HttpRequestMessage(method, new Uri(path, UriKind.Relative));
            if (body is not null)
            {
                message.Content = JsonContent.Create(body, body.GetType());
            }

            message.Headers.Add(TillSessionHeader.Name, sessionToken);
            using var response = await http.SendAsync(message, cancellationToken);
            return response.IsSuccessStatusCode ? await response.Content.ReadFromJsonAsync<T>(cancellationToken) : null;
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>Who may open this till (A5). Null when the server could not say.</summary>
    public async Task<TillStaff?> StaffAsync(CancellationToken cancellationToken = default)
    {
        var staff = await GetAsync<TillStaff>("api/till/staff", cancellationToken);
        return staff?.Staff is null ? null : staff;
    }

    /// <summary>
    /// A PIN typed at this till (A5, D-083). Null when the server could not say: the till then
    /// says it is offline, never that the PIN was wrong.
    /// </summary>
    public async Task<SignInAnswer?> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("api/till/sign-in", UriKind.Relative), request, cancellationToken);
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var answer = await response.Content.ReadFromJsonAsync<SignInAnswer>(cancellationToken);
            return answer switch
            {
                { Outcome: SignInOutcomes.SignedIn, SessionToken: { Length: > 0 } } => answer,
                { Outcome: SignInOutcomes.Locked, LockedUntil: not null } => answer,
                { Outcome: SignInOutcomes.WrongPin or SignInOutcomes.NoPin or SignInOutcomes.UnknownStaff or SignInOutcomes.UnknownTerminal } => answer,
                _ => null,
            };
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>Ends this till's session. Best effort: a server that cannot be reached has no session to end after a restart.</summary>
    public async Task SignOutAsync(string sessionToken, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sessionToken);

        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Post, new Uri("api/till/sign-out", UriKind.Relative));
            message.Headers.Add(TillSessionHeader.Name, sessionToken);
            using var response = await http.SendAsync(message, cancellationToken);
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
        }
    }

    /// <summary>The store, the till and the person selling (A4). Null when the server could not say.</summary>
    public async Task<TillContext?> ContextAsync(string terminalId, string? staffId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(terminalId);
        var query = $"api/till/context?terminal={Uri.EscapeDataString(terminalId)}"
            + (string.IsNullOrWhiteSpace(staffId) ? string.Empty : $"&staff={Uri.EscapeDataString(staffId)}");

        var context = await GetAsync<TillContext>(query, cancellationToken);
        return context?.Outcome is TillContextOutcome.Found or TillContextOutcome.UnknownTerminal ? context : null;
    }

    /// <summary>The Almanac board for this person (D-074). Null when the server could not say.</summary>
    public async Task<BoardAnswer?> BoardAsync(string staffId, CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(staffId);
        var board = await GetAsync<BoardAnswer>($"api/recommendations?staff={Uri.EscapeDataString(staffId)}", cancellationToken);
        return board?.Outcome is BoardOutcome.Answered or BoardOutcome.UnknownStaff && board.Cards is not null ? board : null;
    }

    /// <summary>
    /// Accepts or dismisses a card (D-074). Null when the server could not say; the board is
    /// fetched again either way, so a decision that did land shows as gone.
    /// </summary>
    public async Task<DecisionAnswer?> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("api/recommendations/decide", UriKind.Relative), request, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<DecisionAnswer>(cancellationToken)
                : null;
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>Whether StoreServer answers at all. An idle till asks this so it learns of an outage, and of the end of one.</summary>
    public async Task<bool> HealthAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            using var response = await http.GetAsync(new Uri("health", UriKind.Relative), cancellationToken);
            return response.IsSuccessStatusCode;
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return false;
        }
    }

    private async Task<T?> GetWithSessionAsync<T>(string path, string sessionToken, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var message = new HttpRequestMessage(HttpMethod.Get, new Uri(path, UriKind.Relative));
            message.Headers.Add(TillSessionHeader.Name, sessionToken);
            using var response = await http.SendAsync(message, cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                : null;
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return null;
        }
    }

    private async Task<T?> GetAsync<T>(string path, CancellationToken cancellationToken)
        where T : class
    {
        try
        {
            using var response = await http.GetAsync(new Uri(path, UriKind.Relative), cancellationToken);
            return response.IsSuccessStatusCode
                ? await response.Content.ReadFromJsonAsync<T>(cancellationToken)
                : null;
        }
        catch (Exception exception) when (IsOutage(exception, cancellationToken))
        {
            return null;
        }
    }

    /// <summary>
    /// A timeout, a refused connection, or an answer this till cannot read. The caller's own
    /// cancellation is not one: it propagates, because nobody is waiting for the answer any more.
    /// </summary>
    private static bool IsOutage(Exception exception, CancellationToken cancellationToken) => exception switch
    {
        TaskCanceledException => !cancellationToken.IsCancellationRequested,
        HttpRequestException or JsonException or NotSupportedException => true,
        _ => false,
    };

    /// <summary>
    /// Whether the answer is one of the three the contract defines, with what
    /// that outcome carries. A till showing half an answer (found, with no
    /// product) would show a blank line with a price of nothing.
    /// </summary>
    private static bool IsWellFormed(ProductLookup lookup) => lookup.Outcome switch
    {
        ProductLookupOutcome.Found => lookup.Product is not null,
        ProductLookupOutcome.UnknownBarcode => true,
        ProductLookupOutcome.NotSellable => lookup.Reason is not null,
        _ => false,
    };
}

/// <summary>
/// Where the till asks about a barcode: <see cref="StoreServerClient"/> in the
/// till, a scripted source in the tests of what the till does with the answer.
/// </summary>
/// <summary>
/// What the till's shell asks StoreServer besides products and sales (A4): who and where it is,
/// the Almanac board, a decision, and whether the server is there at all.
/// </summary>
public interface ITillServer
{
    Task<ProductSearchAnswer?> SearchAsync(string query, CancellationToken cancellationToken = default);

    Task<TicketList?> TicketsAsync(DateOnly day, bool allTills, string sessionToken, CancellationToken cancellationToken = default);

    Task<TicketAnswer?> TicketAsync(string idOrNumber, string sessionToken, CancellationToken cancellationToken = default);

    Task<TillStaff?> StaffAsync(CancellationToken cancellationToken = default);

    Task<SignInAnswer?> SignInAsync(SignInRequest request, CancellationToken cancellationToken = default);

    Task SignOutAsync(string sessionToken, CancellationToken cancellationToken = default);

    Task<TillContext?> ContextAsync(string terminalId, string? staffId, CancellationToken cancellationToken = default);

    Task<BoardAnswer?> BoardAsync(string staffId, CancellationToken cancellationToken = default);

    Task<DecisionAnswer?> DecideAsync(DecisionRequest request, CancellationToken cancellationToken = default);

    Task<bool> HealthAsync(CancellationToken cancellationToken = default);

    /// <summary>The shop's active reasons for a discount (B4, D-079). Null when the server could not say.</summary>
    Task<ReasonCodeList?> DiscountReasonsAsync(CancellationToken cancellationToken = default) => Task.FromResult<ReasonCodeList?>(null);

    /// <summary>The shop's active reasons for a price override (B5, D-079). Null when the server could not say.</summary>
    Task<ReasonCodeList?> OverrideReasonsAsync(CancellationToken cancellationToken = default) => Task.FromResult<ReasonCodeList?>(null);

    /// <summary>May the seller give a discount, or the manager whose PIN is typed (B4)? Null when the server could not say.</summary>
    Task<AuthoriseAnswer?> AuthoriseAsync(AuthoriseRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<AuthoriseAnswer?>(null);

    /// <summary>The shop's active reasons for cancelling a ticket (B8). Null when the server could not say.</summary>
    Task<ReasonCodeList?> VoidReasonsAsync(CancellationToken cancellationToken = default) => Task.FromResult<ReasonCodeList?>(null);

    /// <summary>A cancel recorded (B8). Null when the server could not say.</summary>
    Task<VoidAnswer?> VoidAsync(VoidRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<VoidAnswer?>(null);

    /// <summary>The shop's active return reasons (B9). Null when the server could not say.</summary>
    Task<ReasonCodeList?> ReturnReasonsAsync(CancellationToken cancellationToken = default) => Task.FromResult<ReasonCodeList?>(null);

    /// <summary>The shop's reasons for money into the drawer (B7). Null when the server could not say.</summary>
    Task<ReasonCodeList?> CashReasonsAsync(CancellationToken cancellationToken = default) => Task.FromResult<ReasonCodeList?>(null);

    /// <summary>A refund quoted or written (B9). Null when the server could not say.</summary>
    Task<RefundAnswer?> RefundAsync(RefundRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<RefundAnswer?>(null);

    /// <summary>A paid-in or a paid-out (B10). Null when the server could not say.</summary>
    Task<CashMovementAnswer?> CashMovementAsync(CashMovementRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<CashMovementAnswer?>(null);

    /// <summary>A person clocking in or out (B10). Null when the server could not say.</summary>
    Task<ClockAnswer?> ClockAsync(ClockRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<ClockAnswer?>(null);

    /// <summary>The customers with a number (B7). Null when the server could not say.</summary>
    Task<CustomerSearchAnswer?> FindCustomersAsync(string phone, string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<CustomerSearchAnswer?>(null);

    /// <summary>The customers with a full name (D-100). Null when the server could not say.</summary>
    Task<CustomerSearchAnswer?> FindCustomersByNameAsync(string name, string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<CustomerSearchAnswer?>(null);

    /// <summary>A customer created at the till (B7). Null when the server could not say.</summary>
    Task<CustomerAnswer?> CreateCustomerAsync(CreateCustomerRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<CustomerAnswer?>(null);

    /// <summary>A customer's tab (B7). Null when the server could not say.</summary>
    Task<TabAnswer?> TabAsync(string customerId, string terminalId, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<TabAnswer?>(null);

    /// <summary>A repayment in cash (B7). Null when the server could not say.</summary>
    Task<TabAnswer?> RepayAsync(string customerId, RepaymentRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<TabAnswer?>(null);

    /// <summary>A change to a tab's limit (B7). Null when the server could not say.</summary>
    Task<TabAnswer?> ChangeLimitAsync(string customerId, LimitRequest request, string sessionToken, CancellationToken cancellationToken = default) =>
        Task.FromResult<TabAnswer?>(null);
}

public interface IProductSource
{
    Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default);

    /// <summary>
    /// A product sold by weight, weighed (B3, D-090). A source that cannot weigh says the server is
    /// out of reach, which is what the cashier would be told: the scripted sources of tests before
    /// B3 need not know about weights.
    /// </summary>
    Task<LookupAnswer> WeighAsync(string code, string weight, CancellationToken cancellationToken = default) =>
        Task.FromResult<LookupAnswer>(new LookupAnswer.ServerUnavailable("This source cannot weigh."));
}

/// <summary>Where the till sends a sale: <see cref="StoreServerClient"/>, or a script in tests.</summary>
public interface IStoreSales
{
    Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, string sessionToken, CancellationToken cancellationToken = default);
}

/// <summary>What became of a sale the till sent.</summary>
public abstract record SaleAnswer
{
    private SaleAnswer()
    {
    }

    /// <summary>Written. The figures are the server's.</summary>
    public sealed record Completed(SaleOutcome Outcome) : SaleAnswer;

    /// <summary>Not written, for this reason.</summary>
    public sealed record Refused(string Reason) : SaleAnswer;

    /// <summary>Not written: the server holds no session for this till. The till asks for a PIN again.</summary>
    public sealed record NotSignedIn : SaleAnswer;

    /// <summary>No usable answer: the sale may or may not have been written.</summary>
    public sealed record Unknown(string Why) : SaleAnswer;
}

/// <summary>What the till got when it asked about a barcode.</summary>
public abstract record LookupAnswer
{
    private LookupAnswer()
    {
    }

    /// <summary>StoreServer answered. Its outcome, whatever it is, goes to the cashier.</summary>
    public sealed record Answered(ProductLookup Lookup) : LookupAnswer;

    /// <summary>StoreServer could not answer; <paramref name="Why"/> says what happened.</summary>
    public sealed record ServerUnavailable(string Why) : LookupAnswer;
}
