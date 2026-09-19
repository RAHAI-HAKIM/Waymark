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
public sealed class StoreServerClient(HttpClient http) : IProductSource
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
