using System.Globalization;
using Waymark.Application.Commands;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Domain.Work;

namespace Waymark.Application.Sales;

/// <summary>A cash sale of the scanned lines, at this terminal, by this staff member (hop 2, D-070).</summary>
public sealed record CompleteSale(string TerminalId, string StaffId, IReadOnlyList<SaleLineRequest> Lines)
    : ICommand<CompletedSale>;

/// <summary>A scanned code and how many units of it.</summary>
public sealed record SaleLineRequest(string Barcode, int Count);

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
/// <item>a cash session for the terminal, when it has none open.</item>
/// </list>
/// <para>
/// <b>Every line is priced again here</b>, through the same lookup and rules as the scan
/// (D-066). The till's cart is a preview (D-068); a price that changed, or a product archived,
/// since the scan is caught at payment rather than sold at the old figure.
/// </para>
/// </summary>
public sealed class CompleteSaleHandler(
    IProductLookup products,
    ISalesLedger ledger,
    IStaging staging,
    IStoreCalendar calendar,
    TimeProvider clock) : ICommandHandler<CompleteSale, CompletedSale>
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

        var priced = new List<(ProductForSale Product, int Count)>();
        foreach (var line in command.Lines)
        {
            priced.Add(await Price(line, cancellationToken));
        }

        var currency = priced[0].Product.PriceTtc.Currency;
        var zero = Money.Zero(currency);
        var sessionId = await CashSession(command, store, now, context, cancellationToken);
        var transactionId = context.NewId();
        var (net, tax, total) = (zero, zero, zero);

        foreach (var (product, count) in priced)
        {
            var sold = product.Unit.Whole(count);
            var batches = await ledger.BatchesAsync(product.VariantId, product.Unit.Code, cancellationToken);
            if (batches.Count == 0)
            {
                throw new SaleRefusedException($"{product.ProductName} has never been received: there is no batch to sell it from.");
            }

            foreach (var (batch, taken) in BatchAllocation.Take(batches, sold, today))
            {
                var amounts = SaleArithmetic.Line(product.PriceTtc, taken, zero, product.TvaRate, policy);
                (net, tax, total) = (net + amounts.Split.Net, tax + amounts.Split.Tax, total + amounts.LineTotal);

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
                    DiscountAmount = zero,
                    TaxAmount = amounts.Split.Tax,
                    LineTotal = amounts.LineTotal,
                    CreatedAt = now,
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

                ledger.AdjustLevel(product.VariantId, batch.BatchId, QuantityDelta.Decrease(taken), now);
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
            DiscountTotal = zero,
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

        return new CompletedSale(transactionId, invoice, total, tax, cash);
    }

    /// <summary>The line priced by the scan's own rules, or the reason it cannot be sold.</summary>
    private async Task<(ProductForSale, int)> Price(SaleLineRequest line, CancellationToken cancellationToken) =>
        await products.FindForSaleAsync(line.Barcode, cancellationToken) switch
        {
            ProductLookupResult.Found found => (found.Product, line.Count),
            ProductLookupResult.NotSellable refused => throw new SaleRefusedException($"{line.Barcode}: not sellable ({refused.Reason})."),
            _ => throw new SaleRefusedException($"{line.Barcode}: no product carries this code."),
        };

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
