using System.Globalization;
using Waymark.Contracts;
using Waymark.Contracts.Pos;
using Waymark.Domain.Sales;
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
/// <see cref="Money"/> does exactly. <b>A weighed line's total is the server's</b>
/// (B3, D-090): the till never rounds a weight times a price itself.
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
    /// The store's rounding policy (D-053), which a discount's preview is worked out with (B4): the
    /// same <see cref="Discounts"/> and the same policy as the sale, so the till shows what it will
    /// charge. Set from the till's context.
    /// </summary>
    public Rounding Policy { get; set; } = Rounding.HalfUp;

    /// <summary>A discount given on the whole ticket (B4, D-091), or null. It follows the ticket: worked out on its lines as they are.</summary>
    public CounterDiscount? TicketDiscount { get; private set; }

    /// <summary>
    /// The sum of the lines still in the sale before any discount, or null for a cart that has never
    /// had a line: with no line there is no currency yet, and a zero without one is not a total.
    /// </summary>
    public Money? Subtotal => _lines.Count == 0
        ? null
        : ActiveLines.Aggregate(Money.Zero(_lines[0].UnitPrice.Currency), (sum, line) => sum + line.LineTotal);

    /// <summary>
    /// The total to pay: the lines less every discount, or null for a cart that has never had a
    /// line. A cart whose every line was taken out totals zero, in the currency its lines were priced in.
    /// </summary>
    public Money? Total => Subtotal is { } subtotal && DiscountTotal is { } off ? subtotal - off : null;

    /// <summary>Everything taken off, the lines' own and the ticket's; null for a cart that has never had a line.</summary>
    public Money? DiscountTotal => Subtotal is { } subtotal
        ? ActiveLines.Select(LineDiscountOf).Concat(TicketShares()).Aggregate(subtotal - subtotal, (sum, off) => sum + off)
        : null;

    /// <summary>What a line's own discount takes off it (B4): zero with none, and for a line taken out.</summary>
    public Money LineDiscountOf(CartLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return line.Discount is { } discount && !line.IsRemoved
            ? Discounts.OnLine(line.LineTotal, discount.AsDomain(line.LineTotal.Currency), Policy)
            : Money.Zero(line.LineTotal.Currency);
    }

    /// <summary>
    /// Each line still in the sale's share of the ticket's discount, in <see cref="ActiveLines"/>'
    /// order: worked out on what they come to after their own, as the sale will (B4).
    /// </summary>
    public IReadOnlyList<Money> TicketShares()
    {
        var active = ActiveLines;
        if (TicketDiscount is not { } ticket || active.Count == 0)
        {
            return [.. active.Select(line => Money.Zero(line.LineTotal.Currency))];
        }

        return Discounts.OnTicket(
            [.. active.Select(line => line.LineTotal - LineDiscountOf(line))], ticket.AsDomain(active[0].LineTotal.Currency), Policy);
    }

    /// <summary>
    /// A discount given on one line, or taken off it with null (B4). False, changing nothing, for a
    /// line not in the sale. The line is then not plain: a repeat scan starts a new line (D-087).
    /// </summary>
    public bool SetDiscount(string lineId, CounterDiscount? discount)
    {
        var index = ActiveIndex(lineId);
        if (index < 0)
        {
            return false;
        }

        _lines[index] = _lines[index] with { Discount = discount };
        return true;
    }

    /// <summary>
    /// A unit price typed in place of the price in force (B5, D-092), or taken off with null. False,
    /// changing nothing, for a line not in the sale or a weighed one: a weight's price is the server's.
    /// </summary>
    public bool SetOverride(string lineId, PriceOverride? priceOverride)
    {
        var index = ActiveIndex(lineId);
        if (index < 0 || _lines[index].IsWeighed)
        {
            return false;
        }

        _lines[index] = _lines[index] with { Override = priceOverride };
        return true;
    }

    /// <summary>A discount given on the whole ticket, or taken off it with null (B4). Refused on a ticket with no line in the sale.</summary>
    public bool SetTicketDiscount(CounterDiscount? discount)
    {
        if (discount is not null && ActiveLines.Count == 0)
        {
            return false;
        }

        TicketDiscount = discount;
        return true;
    }

    /// <summary>The line most recently scanned in, for the notice slot's "last article".</summary>
    public CartLine? LastAdded { get; private set; }

    /// <summary>
    /// One more unit of the scanned product. A second scan of the same variant adds to its latest
    /// line when that line <see cref="TakesAnotherScan">takes another scan</see>, and takes the
    /// fresher price and stock level from this answer; otherwise it starts a new line (D-087).
    /// </summary>
    /// <exception cref="FormatException">A figure on the wire cannot be read exactly.</exception>
    /// <exception cref="InvalidOperationException">The product is priced in another currency than the cart.</exception>
    /// <param name="units">How many units this scan or touch adds: 1, or the "QTÉ × n" typed before it (B1).</param>
    public CartLine Add(WireProduct product, string barcode, int units = 1)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(units, 1);
        ArgumentNullException.ThrowIfNull(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(barcode);
        if (product.IsWeighted)
        {
            // A weighed product is added with its weight (AddWeighed), never as units of nothing.
            throw new InvalidOperationException($"{product.ProductName} is sold by weight: it needs a weight, not a count.");
        }

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
                product.SellingUnitCode, price, units, stock, product.IsPromotionalPrice,
                UnitCost: product.UnitCost is { } cost ? WireFigures.Money(cost, product.Currency) : null)
            : _lines[index] with
            {
                UnitPrice = price,
                Count = _lines[index].Count + units,
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
    /// (D-087): only a plain line does. A line taken out is finished with, and so is a weighed one
    /// (B3): a second weighing is a second line, never a weight added to the first. <b>B4 and B5 add
    /// to this rule</b>: a line with a discount and a line whose price was overridden are not plain
    /// either, and a scan merged into one would take its discount or price without anybody deciding it.
    /// </summary>
    public static bool TakesAnotherScan(CartLine line)
    {
        ArgumentNullException.ThrowIfNull(line);
        return !line.IsRemoved && !line.IsWeighed && line.Discount is null && line.Override is null;
    }

    /// <summary>
    /// A weighed line (B3, D-090): the product, the code sent for it, and the weight and total the
    /// server answered. Always a line of its own, one weighing each.
    /// </summary>
    /// <exception cref="FormatException">A figure on the wire cannot be read exactly.</exception>
    /// <exception cref="InvalidOperationException">The product is priced in another currency than the cart.</exception>
    public CartLine AddWeighed(WireProduct product, string code, LineWeight weight)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        ArgumentNullException.ThrowIfNull(weight);

        var price = WireFigures.Money(product.PriceTtc, product.Currency);
        if (_lines.Count > 0 && _lines[0].UnitPrice.Currency.Code != price.Currency.Code)
        {
            throw new InvalidOperationException(
                $"{product.ProductName} is priced in {price.Currency.Code}, the cart in {_lines[0].UnitPrice.Currency.Code}.");
        }

        var line = new CartLine(
            NextLineId(), product.VariantId, code, product.ProductName, product.VariantName, product.SellingUnitCode, price, 1,
            WireFigures.Quantity(product.StockOnHand, product.SellingUnitCode), product.IsPromotionalPrice, Weight: weight);
        _lines.Add(line);
        LastAdded = line;
        return line;
    }

    /// <summary>
    /// A typed weight corrected ("Poids" under a selected line, B3). Only a typed weight: a label's
    /// weight or price is the label's, and a wrong label is a line removed, not a weight retyped.
    /// </summary>
    /// <returns>False, changing nothing, for a line not in the sale or not weighed by hand.</returns>
    public bool SetWeight(string lineId, LineWeight weight)
    {
        ArgumentNullException.ThrowIfNull(weight);
        var index = ActiveIndex(lineId);
        if (index < 0 || _lines[index].Weight is not { IsTyped: true })
        {
            return false;
        }

        _lines[index] = _lines[index] with { Weight = weight };
        return true;
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
        if (index < 0 || count is < 1 or > MaxCount || _lines[index].IsWeighed)
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

    /// <summary>
    /// When the payment panel was first opened on this ticket (B8, D-097); null while it never was. A
    /// cancel says so: a cashier's cancel after it needs a manager, and the owner sees it flagged.
    /// </summary>
    public DateTimeOffset? PaymentOpenedAt { get; private set; }

    /// <summary>The payment panel opened on this ticket: the first time is the one kept.</summary>
    public void MarkPaymentOpened(DateTimeOffset at) => PaymentOpenedAt ??= at;

    /// <summary>Empties the cart once its sale is completed.</summary>
    public void Clear()
    {
        _lines.Clear();
        LastAdded = null;
        TicketDiscount = null;
        PaymentOpenedAt = null;
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
/// <param name="Weight">For a weighed line (B3): its weight, where it came from and the server's total. Null for a count.</param>
/// <param name="Discount">A discount given on this line at the counter (B4), or null.</param>
/// <param name="Override">A unit price typed in place of the price in force (B5), or null.</param>
/// <param name="UnitCost">What a unit cost the shop, from the lookup; only for the below-cost warning (B5).</param>
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
    DateTimeOffset? RemovedAt = null,
    LineWeight? Weight = null,
    CounterDiscount? Discount = null,
    PriceOverride? Override = null,
    Money? UnitCost = null)
{
    /// <summary>What a unit is charged: the price typed at the counter when there is one (B5), else the price in force.</summary>
    public Money ChargedPrice => Override?.NewPrice ?? UnitPrice;

    /// <summary>Sold by weight (B3): one weighing, never merged, never stepped.</summary>
    public bool IsWeighed => Weight is not null;

    /// <summary>Taken out before payment: shown struck through, never charged, never sent.</summary>
    public bool IsRemoved => RemovedAt is not null;

    /// <summary>How much of the product the line holds, in its selling unit.</summary>
    public Quantity Quantity => Weight?.Quantity
        ?? Domain.Values.Quantity.FromThousandths(checked(Count * (long)Domain.Values.Quantity.Scale), UnitCode);

    /// <summary>
    /// The line before any discount: the unit price times a whole count, exact and with nothing to
    /// round; for a weighed line, the server's total, which the till never works out itself (D-090).
    /// A discount is shown under it (<see cref="Cart.LineDiscountOf"/>), never folded into it.
    /// </summary>
    public Money LineTotal => Weight?.LineTotal ?? ChargedPrice * Count;

    /// <summary>
    /// The cart holds more than the store records on hand (Hakim, 18/09). A
    /// notice for the cashier, never a refusal: the product is in the customer's
    /// hand, and a level going negative is not a bug (CLAUDE.md §3.8).
    /// </summary>
    public bool ExceedsStockOnHand => Quantity > StockOnHand;
}

/// <summary>A weighed line's weight, as the server answered it (B3, D-090).</summary>
/// <param name="Quantity">In the selling unit.</param>
/// <param name="Source">One of <c>QuantitySources</c>: typed, a weight label or a price label.</param>
/// <param name="LineTotal">The server's total. For a price label, the label's price.</param>
/// <param name="UnitDecimals">How finely the unit is sold, for the weight typed again.</param>
public sealed record LineWeight(Quantity Quantity, string Source, Money LineTotal, int UnitDecimals)
{
    /// <summary>Typed at the till: the only weight the cashier may type again, and the one the sale sends.</summary>
    public bool IsTyped => Source == Contracts.Pos.QuantitySources.TypedWeight;
}

/// <summary>A discount given at the counter as the till holds it (B4, D-091): what, why, and the authorisation that allows it.</summary>
/// <param name="Form">One of <see cref="DiscountForms"/>.</param>
/// <param name="Hundredths">A percent in basis points (1000 is 10 %), or an amount in centimes.</param>
/// <param name="Authorisation">What StoreServer answered; it names nobody on the wire.</param>
/// <param name="Note">What the cashier wrote, for a reason that asks for a note (F-28); null otherwise.</param>
public sealed record CounterDiscount(
    string Form, long Hundredths, string ReasonCode, string ReasonFr, string ReasonAr, string Authorisation, string? Note = null)
{
    public bool IsPercent => Form == DiscountForms.Percent;

    /// <summary>The same discount as the Domain works it out.</summary>
    public Discount AsDomain(Currency currency) => IsPercent
        ? new Discount.Percent(new BasisPoints(checked((int)Hundredths)))
        : new Discount.Amount(Money.FromMinorUnits(Hundredths, currency));

    /// <summary>As the sale sends it: the value as invariant text, never the money it comes to.</summary>
    public DiscountRequest ToWire() => new(Form, Figures.Amount(Hundredths), ReasonCode, Authorisation, Note);
}

/// <summary>A unit price typed at the counter in place of the price in force (B5, D-092), as the till holds it.</summary>
/// <param name="Authorisation">What StoreServer answered for <c>override_price</c>, rank 3.</param>
public sealed record PriceOverride(Money NewPrice, string ReasonCode, string ReasonFr, string ReasonAr, string Authorisation)
{
    /// <summary>As the sale sends it: the price as invariant text; the server checks the band again.</summary>
    public PriceOverrideRequest ToWire() => new(Figures.Amount(NewPrice.MinorUnits), ReasonCode, Authorisation);
}
