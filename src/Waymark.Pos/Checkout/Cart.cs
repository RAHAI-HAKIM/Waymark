using System.Globalization;
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
    /// <summary>
    /// The most units one line may hold (B2): a count typed with one digit too many is refused, not
    /// sold. B3's weighed lines are counted in their unit and have their own rule.
    /// </summary>
    public const int MaxCount = 9_999;

    private readonly List<CartLine> _lines = [];
    private int _lastLineId;

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
    /// One more unit of the scanned product. A second scan of the same variant adds to its latest
    /// line when that line <see cref="TakesAnotherScan">takes another scan</see>, and takes the
    /// fresher price and stock level from this answer; otherwise it starts a new line (D-087).
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

        // The latest line of this product, and only if it takes another scan. A removed line never
        // does: scanning the product again starts a new line rather than bringing the struck one
        // back, so the ticket still shows what was taken out.
        var index = _lines.FindLastIndex(line => line.VariantId == product.VariantId);
        if (index >= 0 && !TakesAnotherScan(_lines[index]))
        {
            index = -1;
        }

        var line = index < 0
            ? new CartLine(
                NextLineId(), product.VariantId, barcode, product.ProductName, product.VariantName,
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
    /// Whether a second scan of the same product adds to this line rather than starting a new one
    /// (D-087): only a plain line does. A line taken out is finished with. <b>B3, B4 and B5 add to
    /// this rule</b>: a weighed line, a line with a discount and a line whose price was overridden
    /// are not plain, and a scan merged into one would take its weight, discount or price without
    /// anybody deciding it.
    /// </summary>
    public static bool TakesAnotherScan(CartLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return !line.IsRemoved;
    }

    /// <summary>
    /// Takes a line out of the sale, and leaves it on the ticket struck through with the time
    /// (G1). False if the sale holds no such line still in it. The reason and the log entry
    /// arrive with B8's dialog; until then the struck line is the trace.
    /// </summary>
    public bool Remove(string lineId, DateTimeOffset at)
    {
        var index = ActiveIndex(lineId);
        if (index < 0)
        {
            return false;
        }

        _lines[index] = _lines[index] with { RemovedAt = at };
        if (LastAdded?.LineId == lineId)
        {
            LastAdded = null;
        }

        return true;
    }

    /// <summary>
    /// Sets how many units a line holds (the − / + under a selected line, B2). At least one: taking
    /// the last unit out is "Retirer la ligne", which leaves the line struck on the ticket, never a
    /// line that quietly vanished at zero.
    /// </summary>
    /// <returns>False, changing nothing, for a line not in the sale or a count outside 1 to <see cref="MaxCount"/>.</returns>
    public bool SetCount(string lineId, int count)
    {
        var index = ActiveIndex(lineId);
        if (index < 0 || count is < 1 or > MaxCount)
        {
            return false;
        }

        _lines[index] = _lines[index] with { Count = count };
        return true;
    }

    private int ActiveIndex(string lineId) => _lines.FindIndex(line => line.LineId == lineId && !line.IsRemoved);

    /// <summary>
    /// This cart's next line id. Local to the cart and never stored: the server reads codes and
    /// counts (D-070), so a line id only has to tell two lines of one ticket apart.
    /// </summary>
    private string NextLineId() => (++_lastLineId).ToString(CultureInfo.InvariantCulture);

    /// <summary>Empties the cart once its sale is completed.</summary>
    public void Clear()
    {
        _lines.Clear();
        LastAdded = null;
    }
}

/// <summary>A line of the ticket: one product, and as many units as were scanned into it.</summary>
/// <param name="LineId">
/// The line itself (D-087). Two lines can hold the same product — one struck and one not, and from
/// B3 a weighed or discounted line beside a plain one — so a touch, a removal or a count names the
/// line, never the product.
/// </param>
/// <param name="Barcode">The code scanned for it: what the sale sends, never the price (D-070).</param>
/// <param name="RemovedAt">When the line was taken out of the sale, or null while it is in it.</param>
/// <param name="IsPromotionalPrice">
/// The unit price came from a promotional row rather than a retail one (D-076). Shown to the
/// cashier because customers ask; it changes no arithmetic, because a promotional price is
/// the price and not a discount taken off one.
/// </param>
public sealed record CartLine(
    string LineId,
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
