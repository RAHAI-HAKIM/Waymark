using System.Globalization;
using System.Text.Json;
using Waymark.Application.Commands;
using Waymark.Application.Sync;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Statistics;
using Waymark.Domain.Sync;
using Waymark.Domain.Values;
using Waymark.Domain.Work;

namespace Waymark.Application.Sales;

/// <summary>A cash sale of the scanned lines, at this terminal, by this staff member (hop 2, D-070).</summary>
/// <param name="TicketDiscount">A discount given on the whole ticket at the counter (B4, D-091), or null.</param>
public sealed record CompleteSale(
    string TerminalId, string StaffId, IReadOnlyList<SaleLineRequest> Lines, GivenDiscount? TicketDiscount = null)
    : ICommand<CompletedSale>;

/// <summary>Whether a discount is a percent or an amount of money.</summary>
public enum DiscountForm
{
    /// <summary><see cref="GivenDiscount.Value"/> is basis points: 1000 is 10 %.</summary>
    Percent,

    /// <summary><see cref="GivenDiscount.Value"/> is minor units of the sale's currency: 5000 is 50,00.</summary>
    Amount,
}

/// <summary>A discount given at the counter (B4, D-091): what, why, and who allowed it.</summary>
/// <param name="ReasonCode">An active <c>discount</c> reason (D-079); anything else is refused.</param>
/// <param name="AuthorisedBy">
/// The person of rank 2 who allowed it: the seller, or the manager whose PIN the till asked for. The
/// host resolves it from the session and the authorisation; the till never names anyone (D-083).
/// </param>
public sealed record GivenDiscount(DiscountForm Form, long Value, string ReasonCode, string AuthorisedBy);

/// <summary>A scanned code and how many units of it.</summary>
/// <param name="WeightThousandths">
/// A weight typed at the till for a product sold by weight, in thousandths of its unit (B3);
/// null for a count, and for a scale label, which carries its own weight or price.
/// </param>
/// <param name="Discount">A discount given on this line at the counter (B4), or null.</param>
public sealed record SaleLineRequest(string Barcode, int Count, long? WeightThousandths = null, GivenDiscount? Discount = null);

/// <summary>What the sale came to.</summary>
/// <param name="TransactionId">The sale's row.</param>
/// <param name="InvoiceNumber">Gapless per store per calendar year.</param>
/// <param name="Total">The TTC total: the sum of the line totals.</param>
/// <param name="TaxTotal">The TVA in it.</param>
/// <param name="Cash">What the customer hands over (rounded to the cash step) and the difference.</param>
public sealed record CompletedSale(string TransactionId, string InvoiceNumber, Money Total, Money TaxTotal, CashTender Cash);

/// <summary>The sale cannot be completed, for a reason the cashier is told. Nothing is written.</summary>
public sealed class SaleRefusedException(string reason) : Exception(reason);

/// <summary>
/// Completes a cash sale (D-070). It stages, for the executor to commit together or not at all
/// (D-050):
/// <list type="bullet">
/// <item>the <c>transactions</c> row, completed, with the store's rounding policy copied onto it
/// (D-053) and the next invoice number;</item>
/// <item>one <c>transaction_items</c> row per line and batch, the TVA extracted from the TTC total
/// (D-033) by <see cref="SaleArithmetic"/>, the same code the synthetic store uses;</item>
/// <item>one <c>stock_movements</c> row per item, and the batch's level lowered to match;</item>
/// <item>one cash <c>transaction_payments</c> row for the exact total, and the difference the cash
/// step makes in <c>rounding_variance</c>, never in the drawer's variance (D-034);</item>
/// <item>a cash session for the terminal, when it has none open;</item>
/// <item>the anonymous basket, as an <c>outbox</c> row on the statistics channel (D-043,
/// D-064, hop 3). <b>It goes in with the sale</b>: both rows or neither (CLAUDE.md §3.6).
/// Tier 2 is handed the same sale and keeps nothing in the skeleton (D-065).</item>
/// </list>
/// <para>
/// <b>Every line is priced again here</b>, through the same lookup and rules as the scan
/// (D-066). The till's cart is a preview (D-068); a price that changed, or a product archived,
/// since the scan is caught at payment rather than sold at the old figure.
/// </para>
/// <para>
/// <b>Discounts are worked out here too</b> (B4, D-091), in two passes: first what every line
/// takes from which batch and its gross; then each line's own discount on its gross, the ticket's
/// on the lines' totals, and each line's sum of the two spread over its batch rows
/// (<see cref="Discounts"/>). A row keeps the reason and the authoriser of its line's own discount,
/// else the ticket's.
/// </para>
/// </summary>
public sealed class CompleteSaleHandler(
    IProductLookup products,
    ISalesLedger ledger,
    IStaging staging,
    IOutboxSequence outbox,
    ITier2Writer tier2,
    IStoreCalendar calendar,
    TimeProvider clock,
    IReasonCodes reasons) : ICommandHandler<CompleteSale, CompletedSale>
{
    public async Task<CompletedSale> HandleAsync(
        CompleteSale command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TerminalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.StaffId);

        if (command.Lines.Count == 0 || command.Lines.Any(line => line.Count <= 0))
        {
            throw new SaleRefusedException("A sale needs at least one line, each of at least one unit.");
        }

        var store = await ledger.CurrentStoreAsync(cancellationToken)
            ?? throw new SaleRefusedException("This store has no row in stores; it is not commissioned.");
        var policy = store.RoundingPolicy;
        var now = clock.GetUtcNow();
        var today = calendar.Today;

        var priced = new List<(ProductForSale Product, int Count, WeighedQuantity? Weighed)>();
        foreach (var line in command.Lines)
        {
            priced.Add(await Price(line, cancellationToken));
        }

        var currency = priced[0].Product.PriceTtc.Currency;
        var zero = Money.Zero(currency);
        await CheckReasonsAsync(command, cancellationToken);
        var sessionId = await CashSession(command, store, now, context, cancellationToken);
        var transactionId = context.NewId();
        var (net, tax, total, discounted) = (zero, zero, zero, zero);
        var sold = new List<SoldLine>();
        var tier2Lines = new List<Tier2Line>();

        // Pass 1: what each line takes from which batch, and each row's gross before any discount.
        var plans = new List<LinePlan>();
        for (var index = 0; index < priced.Count; index++)
        {
            var (product, count, weighed) = priced[index];
            // A weighed line takes its weight from the lookup (typed, or read from a label), a
            // counted one its count (D-090).
            var wanted = weighed?.Quantity ?? product.Unit.Whole(count);
            var source = weighed?.Source ?? QuantitySource.Count;
            var batches = await ledger.BatchesAsync(product.VariantId, product.Unit.Code, cancellationToken);
            if (batches.Count == 0)
            {
                throw new SaleRefusedException($"{product.ProductName} has never been received: there is no batch to sell it from.");
            }

            var takes = BatchAllocation.Take(batches, wanted, today);

            // A price label's price is exact, so it is split over the batches with Allocate and
            // sums back to the label (O-26). Every other line is priced per batch, as D-070 does:
            // each row is then its own quantity × price, and recomputes from itself.
            var shares = source == QuantitySource.LabelPrice
                ? WeighedLine.SplitDeclared(weighed!.Amounts.LineTotal, [.. takes.Select(take => take.Taken)])
                : null;

            var rowGross = shares
                ?? [.. takes.Select(take => SaleArithmetic.Line(product.PriceTtc, take.Taken, zero, product.TvaRate, policy).Gross)];
            plans.Add(new LinePlan(product, source, takes, rowGross, command.Lines[index].Discount));

            // Lowered now, as each line is taken, so a second line of the same product reads the
            // levels the first left.
            foreach (var (batch, taken) in takes)
            {
                ledger.AdjustLevel(product.VariantId, batch.BatchId, QuantityDelta.Decrease(taken), now);
            }
        }

        // Pass 2: the discounts (D-091). A line's own on its gross; the ticket's on the lines'
        // totals after theirs, once; then each line's sum of both over its batch rows.
        var lineOff = plans.Select(plan => plan.Given is { } given
            ? Off(() => Discounts.OnLine(Sum(plan.RowGross, zero), AsDiscount(given, currency), policy))
            : zero).ToList();
        IReadOnlyList<Money> ticketOff = command.TicketDiscount is { } ticket
            ? Off(() => Discounts.OnTicket(
                [.. plans.Select((plan, i) => Sum(plan.RowGross, zero) - lineOff[i])], AsDiscount(ticket, currency), policy))
            : [.. plans.Select(_ => zero)];

        for (var index = 0; index < plans.Count; index++)
        {
            var (product, source, takes, rowGross, given) = plans[index];
            // A line with nothing off takes nothing from any row, and never asks the rules.
            var off = lineOff[index] + ticketOff[index];
            IReadOnlyList<Money> parts = off.IsZero ? [.. rowGross.Select(_ => zero)] : Discounts.Spread(off, rowGross);
            var why = !lineOff[index].IsZero ? given : command.TicketDiscount;

            for (var i = 0; i < takes.Count; i++)
            {
                var (batch, taken) = takes[i];
                var part = parts[i];
                var amounts = source == QuantitySource.LabelPrice
                    ? new LineAmounts(rowGross[i], rowGross[i] - part, (rowGross[i] - part).SplitTaxInclusive(product.TvaRate, policy))
                    : SaleArithmetic.Line(product.PriceTtc, taken, part, product.TvaRate, policy);
                (net, tax, total, discounted) =
                    (net + amounts.Split.Net, tax + amounts.Split.Tax, total + amounts.LineTotal, discounted + part);

                staging.Add(new TransactionItem
                {
                    TransactionItemId = context.NewId(),
                    TransactionId = transactionId,
                    VariantId = product.VariantId,
                    BatchId = batch.BatchId,
                    Quantity = taken.Thousandths,
                    UnitCode = product.Unit.Code,
                    SellPrice = product.PriceTtc,
                    UnitCostAtSale = batch.UnitCost,
                    DiscountAmount = part,
                    DiscountReasonCode = part.IsZero ? null : why?.ReasonCode,
                    AuthorisedBy = part.IsZero ? null : why?.AuthorisedBy,
                    TaxAmount = amounts.Split.Tax,
                    LineTotal = amounts.LineTotal,
                    CreatedAt = now,
                    QuantitySource = source,
                });

                staging.Add(new StockMovement
                {
                    MovementId = context.NewId(),
                    StoreId = store.StoreId,
                    VariantId = product.VariantId,
                    BatchId = batch.BatchId,
                    MovementDate = today,
                    MovementType = StockMovementType.Sale,
                    QuantityChanged = -taken.Thousandths,
                    UnitCode = product.Unit.Code,
                    UnitCost = batch.UnitCost,
                    ReferenceType = "transaction",
                    ReferenceId = transactionId,
                    StaffId = command.StaffId,
                    CreatedAt = now,
                });

                sold.Add(new SoldLine(product.ProductId, taken, amounts.LineTotal));
                tier2Lines.Add(new Tier2Line(product.VariantId, taken, amounts.LineTotal));
            }
        }

        var invoice = await NextInvoiceNumber(store, today, cancellationToken);
        staging.Add(new Transaction
        {
            TransactionId = transactionId,
            StoreId = store.StoreId,
            TerminalId = command.TerminalId,
            CashSessionId = sessionId,
            StaffId = command.StaffId,
            InvoiceNumber = invoice,
            OccurredAt = now,
            RoundingPolicy = policy,
            Subtotal = net,
            DiscountTotal = discounted,
            TaxTotal = tax,
            TotalAmount = total,
            Currency = currency.Code,
            Status = TransactionStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var cash = total.ToCashTender();
        if (!total.IsZero)
        {
            staging.Add(new TransactionPayment
            {
                PaymentId = context.NewId(),
                TransactionId = transactionId,
                Sequence = 1,
                PaymentMethod = PaymentMethod.Cash,
                Amount = total,
                Currency = currency.Code,
                CreatedAt = now,
            });
        }

        // The tender rounds, never the invoice (D-034): the payment is the exact total, and what
        // the 5 DZD step adds or takes off is recorded here.
        if (cash.HasVariance)
        {
            staging.Add(new RoundingVariance
            {
                VarianceId = context.NewId(),
                StoreId = store.StoreId,
                OccurredAt = now,
                ReferenceType = VarianceReferenceType.Transaction,
                ReferenceId = transactionId,
                Source = VarianceSource.CashTender,
                Amount = cash.Variance,
                Policy = policy,
                CreatedAt = now,
            });
        }

        await EmitBasket(sold, today, context, now, store.StoreId, discounted.IsPositive, cancellationToken);
        tier2.Record(new Tier2Sale(transactionId, today, calendar.HourOfDay, tier2Lines));

        return new CompletedSale(transactionId, invoice, total, tax, cash);
    }

    /// <summary>
    /// The anonymous basket, staged as an outbox row in the sale's own unit of work (D-043,
    /// CLAUDE.md §3.6). The row names no entity: <c>entity_type</c> and <c>entity_id</c> would
    /// be the transaction, and the point of the basket is that nothing joins it back to the
    /// till's row.
    /// </summary>
    private async Task EmitBasket(
        IReadOnlyList<SoldLine> sold,
        DateOnly today,
        CommandContext context,
        DateTimeOffset now,
        string storeId,
        bool hasDiscount,
        CancellationToken cancellationToken)
    {
        var basket = AnonymousBasket.From(
            context.NewId(),
            storeId,
            today,
            calendar.HourOfDay,
            sold,
            AnonymousBasket.Cash,
            hasDiscount);

        staging.Add(new OutboxMessage
        {
            OutboxId = context.NewId(),
            SequenceNumber = await outbox.LastAsync(cancellationToken) + 1,
            Channel = OutboxMessageChannel.AStatistics,
            MessageType = AnonymousBasket.MessageType,
            PayloadJson = JsonSerializer.Serialize(basket),
            CreatedAt = now,
        });
    }

    /// <summary>One line after pass 1: its product, where its quantity came from, its batches, each row's gross, its discount.</summary>
    private sealed record LinePlan(
        ProductForSale Product,
        QuantitySource Source,
        IReadOnlyList<(BatchLevel Batch, Quantity Taken)> Takes,
        IReadOnlyList<Money> RowGross,
        GivenDiscount? Given);

    private static Money Sum(IReadOnlyList<Money> amounts, Money zero) => amounts.Aggregate(zero, (sum, amount) => sum + amount);

    private static Discount AsDiscount(GivenDiscount given, Currency currency) => given.Form switch
    {
        DiscountForm.Percent when given.Value is > 0 and <= BasisPoints.Scale => new Discount.Percent(new BasisPoints((int)given.Value)),
        DiscountForm.Amount => new Discount.Amount(Money.FromMinorUnits(given.Value, currency)),
        _ => throw new SaleRefusedException("A discount is above 0 and at most 100 %."),
    };

    /// <summary>A discount the rules refuse (nothing off, over 100 %) is a refused sale, never a server error.</summary>
    private static T Off<T>(Func<T> work)
    {
        try
        {
            return work();
        }
        catch (ArgumentOutOfRangeException refused)
        {
            throw new SaleRefusedException($"A discount cannot be given: {refused.Message}");
        }
    }

    /// <summary>
    /// Every reason given is an active <c>discount</c> reason (D-079). The column is a foreign key,
    /// so anything else would fail at commit, mid-sale, as an error rather than an answer.
    /// </summary>
    private async Task CheckReasonsAsync(CompleteSale command, CancellationToken cancellationToken)
    {
        var given = command.Lines.Select(line => line.Discount).Append(command.TicketDiscount).OfType<GivenDiscount>().ToList();
        if (given.Count == 0)
        {
            return;
        }

        var accepted = (await reasons.ForAsync(ReasonCodeAppliesTo.Discount, cancellationToken)).Select(reason => reason.Code).ToHashSet(StringComparer.Ordinal);
        if (given.FirstOrDefault(discount => !accepted.Contains(discount.ReasonCode) || string.IsNullOrWhiteSpace(discount.AuthorisedBy)) is { } wrong)
        {
            throw new SaleRefusedException($"'{wrong.ReasonCode}' is not a discount reason this shop accepts, or nobody allowed it.");
        }
    }

    /// <summary>The line priced by the scan's own rules, or the reason it cannot be sold.</summary>
    private async Task<(ProductForSale, int, WeighedQuantity?)> Price(SaleLineRequest line, CancellationToken cancellationToken)
    {
        var found = await products.FindForSaleAsync(line.Barcode, line.WeightThousandths, cancellationToken) switch
        {
            ProductLookupResult.Found answer => answer,
            ProductLookupResult.NotSellable refused => throw new SaleRefusedException($"{line.Barcode}: not sellable ({refused.Reason})."),
            _ => throw new SaleRefusedException($"{line.Barcode}: no product carries this code."),
        };

        // A weighed product sells by its weight, once: never by a count, never without a weight
        // (D-090). The lookup already refused a weight typed for a product sold by count.
        if (found.Product.IsWeighted && found.Weighed is null)
        {
            throw new SaleRefusedException($"{line.Barcode}: {found.Product.ProductName} is sold by weight, and no weight was given.");
        }

        if (found.Weighed is not null && line.Count != 1)
        {
            throw new SaleRefusedException($"{line.Barcode}: a weighed line is one weight, not {line.Count}.");
        }

        return (found.Product, line.Count, found.Weighed);
    }

    /// <summary>
    /// The terminal's open session, or a new one opened by this sale's staff member with no float.
    /// Opening a session with a counted float is a cashier's action, and comes in Phase 1.
    /// </summary>
    private async Task<string> CashSession(
        CompleteSale command, Store store, DateTimeOffset now, CommandContext context, CancellationToken cancellationToken)
    {
        if (await ledger.OpenCashSessionAsync(command.TerminalId, cancellationToken) is { } open)
        {
            return open.SessionId;
        }

        var session = new CashSession
        {
            SessionId = context.NewId(),
            StoreId = store.StoreId,
            TerminalId = command.TerminalId,
            OpenedBy = command.StaffId,
            OpenedAt = now,
            OpeningFloat = Money.Zero(Currency.FromCode(store.Currency)),
            CreatedAt = now,
            UpdatedAt = now,
        };
        staging.Add(session);
        return session.SessionId;
    }

    /// <summary>
    /// <c>{store code}-{year}-{000001}</c>, gapless per store per calendar year, as the synthetic
    /// store numbers them. Read and staged in the sale's own transaction, so a sale that fails
    /// uses no number; StoreServer runs one sale at a time, and the unique index on
    /// (store, invoice number) refuses a duplicate if two ever raced.
    /// </summary>
    private async Task<string> NextInvoiceNumber(Store store, DateOnly today, CancellationToken cancellationToken)
    {
        var prefix = string.Create(CultureInfo.InvariantCulture, $"{store.StoreCode}-{today.Year}-");
        var last = await ledger.LastInvoiceNumberAsync(prefix, cancellationToken);
        var next = last is null ? 1 : int.Parse(last.AsSpan(prefix.Length), NumberStyles.None, CultureInfo.InvariantCulture) + 1;
        return string.Create(CultureInfo.InvariantCulture, $"{prefix}{next:D6}");
    }
}
