using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// The products whose name answers what the cashier typed (session B1, D-088), best first. Each is
/// answered as a scan of it would be, so the list is also the stock lookup.
/// </summary>
public sealed record ProductSearchAnswer(
    [property: JsonPropertyName("query")] string Query,
    [property: JsonPropertyName("results")] IReadOnlyList<ProductSearchResult> Results);

/// <summary>One product found.</summary>
/// <param name="Code">What the till sends to sell it (barcode, else PLU); null when it has neither.</param>
/// <param name="Outcome"><see cref="ProductLookupOutcome.Found"/> or <see cref="ProductLookupOutcome.NotSellable"/>.</param>
/// <param name="Product">When found: the product as a scan would add it.</param>
/// <param name="Reason">When not sellable: one of <see cref="NotSellableReason"/>.</param>
public sealed record ProductSearchResult(
    [property: JsonPropertyName("variant_id")] string VariantId,
    [property: JsonPropertyName("product_name")] string ProductName,
    [property: JsonPropertyName("variant_name")] string VariantName,
    [property: JsonPropertyName("code")] string? Code,
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("product")] ProductForSale? Product,
    [property: JsonPropertyName("reason")] string? Reason);
