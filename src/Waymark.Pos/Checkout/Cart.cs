using Waymark.Domain.Values;
using WireProduct = Waymark.Contracts.Pos.ProductForSale;

namespace Waymark.Pos.Checkout;

/// <summary>
/// The lines of the sale being rung up (hop 1, D-068).
///
/// <para>
/// <b>The totals here are a preview.</b> The receipt's figures (the TVA split
/// and the cash rounding) are computed by StoreServer when the sale completes
/// (hop 2). So the cart does only arithmetic that cannot round: a scan adds one
/// whole unit, and a line total is the unit price times a whole count, which
/// <see cref="Money"/> does exactly. Fractional quantities arrive with weighing
/// in Phase 1, and with them a rounding policy.
/// </para>
/// </summary>
public sealed class Cart
{
    private readonly List<CartLine> _lines = [];

    /// <summary>
    /// Every line, in scan order, <b>including the ones taken out</b> (G1): a line removed before
    /// payment stays on the ticket. It is what the cashier
    /// and whoever reviews the drawer see; it is never charged and never sent.
    /// </summary>
    public IReadOnlyList<CartLine> Lines => _lines;

    /// <summary>The lines still in the sale: what is charged, and what <c>Pay</c> sends.</summary>
    public IReadOnlyList<CartLine> ActiveLines => [.. _lines.Where(line => !line.IsRemoved)];

    /// <summary>
    /// The sum of the lines still in the sale, or null for a cart that has never had one: with
    /// no line there is no currency yet, and a zero without one is not a total. A cart whose
    /// every line was taken out totals zero, in the currency its lines were priced in.
    /// </summary>
    public Money? Total => _lines.Count == 0
        ? null
        : ActiveLines.Aggregate(Money.Zero(_lines[0].UnitPrice.Currency), (sum, line) => sum + line.LineTotal);

    /// <summary>The line most recently scanned in, for the notice slot's "last article".</summary>
    public CartLine? LastAdded { get; private set; }

    /// <summary>
    /// One more unit of the scanned product. A second scan of the same variant
    /// adds to its line, and takes the fresher stock level from this answer.
    /// </summary>
    /// <exception cref="FormatException">A figure on the wire cannot be read exactly.</exception>
    /// <exception cref="InvalidOperationException">The product is priced in another currency than the cart.</exception>
    public CartLine Add(WireProduct product, string barcode)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);

        var price = WireFigures.Money(product.PriceTtc, product.Currency);
        var stock = WireFigures.Quantity(product.StockOnHand, product.SellingUnitCode);

        if (_lines.Count > 0 && _lines[0].UnitPrice.Currency.Code != price.Currency.Code)
        {
            throw new InvalidOperationException(
                $"{product.ProductName} is priced in {price.Currency.Code}, the cart in {_lines[0].UnitPrice.Currency.Code}.");
        }

        // A removed line is finished with: scanning the product again starts a new line rather
        // than bringing the struck one back, so the ticket still shows what was taken out.
        var index = _lines.FindIndex(line => line.VariantId == product.VariantId && !line.IsRemoved);
        var line = index < 0
            ? new CartLine(
                product.VariantId, barcode, product.ProductName, product.VariantName,
                product.SellingUnitCode, price, 1, stock, product.IsPromotionalPrice)
            : _lines[index] with
            {
                UnitPrice = price,
                Count = _lines[index].Count + 1,
                StockOnHand = stock,

                // Taken from this answer like the price and the level: a
                // promotion that started or ended between two scans of the same
                // product must not leave the line labelled by the first scan.
                IsPromotionalPrice = product.IsPromotionalPrice,
            };

        if (index < 0)
        {
            _lines.Add(line);
        }
        else
        {
            _lines[index] = line;
        }

        LastAdded = line;
        return line;
    }

    /// <summary>
    /// Takes a line out of the sale, and leaves it on the ticket struck through with the time
    /// (G1). False if the sale holds no such line. The reason and the log entry arrive with
    /// B8's dialog; until then the struck line is the trace.
    /// </summary>
    public bool Remove(string variantId, DateTimeOffset at)
    {
        var index = _lines.FindIndex(line => line.VariantId == variantId && !line.IsRemoved);
        if (index < 0)
        {
            return false;
        }

        _lines[index] = _lines[index] with { RemovedAt = at };
        if (LastAdded?.VariantId == variantId)
        {
            LastAdded = null;
        }

        return true;
    }

    /// <summary>Empties the cart once its sale is completed.</summary>
    public void Clear()
    {
        _lines.Clear();
        LastAdded = null;
    }
}

/// <summary>One product in the cart, however many times it was scanned.</summary>
/// <param name="Barcode">The code scanned for it: what the sale sends, never the price (D-070).</param>
/// <param name="RemovedAt">When the line was taken out of the sale, or null while it is in it.</param>
/// <param name="IsPromotionalPrice">
/// The unit price came from a promotional row rather than a retail one (D-076). Shown to the
/// cashier because customers ask; it changes no arithmetic, because a promotional price is
/// the price and not a discount taken off one.
/// </param>
public sealed record CartLine(
    string VariantId,
    string Barcode,
    string ProductName,
    string VariantName,
    string UnitCode,
    Money UnitPrice,
    int Count,
    Quantity StockOnHand,
    bool IsPromotionalPrice = false,
    DateTimeOffset? RemovedAt = null)
{
    /// <summary>Taken out before payment: shown struck through, never charged, never sent.</summary>
    public bool IsRemoved => RemovedAt is not null;

    /// <summary>How much of the product the line holds, in its selling unit.</summary>
    public Quantity Quantity => Domain.Values.Quantity.FromThousandths(checked(Count * (long)Domain.Values.Quantity.Scale), UnitCode);

    /// <summary>The unit price times a whole count: exact, nothing to round.</summary>
    public Money LineTotal => UnitPrice * Count;

    /// <summary>
    /// The cart holds more than the store records on hand (Hakim, 18/09). A
    /// notice for the cashier, never a refusal: the product is in the customer's
    /// hand, and a level going negative is not a bug (CLAUDE.md §3.8).
    /// </summary>
    public bool ExceedsStockOnHand => Quantity > StockOnHand;
}
