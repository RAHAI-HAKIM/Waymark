using System.Net.Http.Json;
using System.Text.Json;
using Waymark.Contracts.Pos;

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
public sealed class StoreServerClient(HttpClient http) : IProductSource, IStoreSales
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
        var path = new Uri($"api/products/lookup?barcode={Uri.EscapeDataString(barcode)}", UriKind.Relative);

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
    /// </summary>
    public async Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);

        try
        {
            using var response = await http.PostAsJsonAsync(new Uri("api/sales", UriKind.Relative), request, cancellationToken);
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
public interface IProductSource
{
    Task<LookupAnswer> LookupAsync(string barcode, CancellationToken cancellationToken = default);
}

/// <summary>Where the till sends a sale: <see cref="StoreServerClient"/>, or a script in tests.</summary>
public interface IStoreSales
{
    Task<SaleAnswer> CompleteSaleAsync(SaleRequest request, CancellationToken cancellationToken = default);
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
