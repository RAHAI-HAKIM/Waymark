using System.Text.Json;
using Waymark.Application.Commands;
using Waymark.Application.Sync;
using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Ledgers;
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
/// <param name="Tenders">The card, BaridiMob and tab parts (B6, B7), in order; null or empty when the whole ticket is cash.</param>
/// <param name="CustomerId">The customer the sale is recorded against (B7, D-096); needed for a tab part.</param>
/// <param name="TabOverrideBy">
/// The person of <c>ManageCredit</c> who let the tab part past the limit, resolved by the host from the
/// till's authorisation; null when none was cited.
/// </param>
/// <param name="Removed">Lines struck on the ticket before it was paid (B8, D-097): recorded, never charged.</param>
public sealed record CompleteSale(
    string TerminalId, string StaffId, IReadOnlyList<SaleLineRequest> Lines, GivenDiscount? TicketDiscount = null,
    IReadOnlyList<GivenTender>? Tenders = null, string? CustomerId = null, string? TabOverrideBy = null,
    IReadOnlyList<RemovedLine>? Removed = null)
    : ICommand<CompletedSale>;

/// <summary>A line struck before the ticket was paid or cancelled (B8, D-097): what it was, and when.</summary>
/// <param name="WeightThousandths">For a typed weight, as on <see cref="SaleLineRequest"/>.</param>
public sealed record RemovedLine(string Barcode, int Count, long? WeightThousandths, DateTimeOffset RemovedAt);

/// <summary>
/// A ticket cancelled at the till (B8, D-097): recorded as a <c>voided</c> transaction, priced as its
/// sale would have been, with no batch, no stock moved, no payment, no invoice number and nothing sent.
/// </summary>
/// <param name="ReasonCode">An active <c>void</c> reason.</param>
/// <param name="SellerMayVoid">The seller's rank reaches <c>VoidTransaction</c>, as the host asked <c>StaffPermissions</c>.</param>
/// <param name="PaymentOpenedAt">When the payment panel was opened on the ticket; null when it never was.</param>
/// <param name="AuthorisedBy">The person of <c>VoidTransaction</c> whose PIN the till cited, resolved by the host; null when none.</param>
public sealed record VoidTicket(
    string TerminalId, string StaffId, IReadOnlyList<SaleLineRequest> Lines, GivenDiscount? TicketDiscount,
    string ReasonCode, bool SellerMayVoid, DateTimeOffset? PaymentOpenedAt, string? AuthorisedBy, IReadOnlyList<RemovedLine>? Removed = null)
    : ICommand<VoidedTicket>;

/// <summary>A cancelled ticket as recorded: its row and what it came to.</summary>
public sealed record VoidedTicket(string TransactionId, Money Total);

/// <summary>A part paid by card, BaridiMob or on the customer's tab, as the till says it was given (B6, B7).</summary>
/// <param name="Amount">Minor units of the sale's currency.</param>
/// <param name="Reference">As typed; read again here, and a card number is refused (D-095).</param>
public sealed record GivenTender(PaymentMethod Method, long Amount, string? Reference);

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
/// <param name="Note">What the cashier wrote, for a reason that asks for a note (F-28); null otherwise.</param>
public sealed record GivenDiscount(DiscountForm Form, long Value, string ReasonCode, string AuthorisedBy, string? Note = null);

/// <summary>A unit price typed at the counter in place of the price in force (B5, D-092).</summary>
/// <param name="NewPrice">The new unit price, in minor units of the sale's currency.</param>
/// <param name="ReasonCode">An active <c>price_override</c> reason (D-079).</param>
/// <param name="AuthorisedBy">The person of rank 3 who allowed it, resolved by the host from the authorisation.</param>
public sealed record GivenOverride(long NewPrice, string ReasonCode, string AuthorisedBy);

/// <summary>A scanned code and how many units of it.</summary>
/// <param name="WeightThousandths">
/// A weight typed at the till for a product sold by weight, in thousandths of its unit (B3);
/// null for a count, and for a scale label, which carries its own weight or price.
/// </param>
/// <param name="Discount">A discount given on this line at the counter (B4), or null.</param>
/// <param name="Override">A unit price typed in place of the price in force (B5), or null.</param>
public sealed record SaleLineRequest(
    string Barcode, int Count, long? WeightThousandths = null, GivenDiscount? Discount = null, GivenOverride? Override = null);

/// <summary>What the sale came to.</summary>
/// <param name="TransactionId">The sale's row.</param>
/// <param name="InvoiceNumber">Gapless per store per calendar year.</param>
/// <param name="Total">The TTC total: the sum of the line totals.</param>
/// <param name="TaxTotal">The TVA in it.</param>
/// <param name="Cash">What the customer hands over (rounded to the cash step) and the difference.</param>
/// <param name="Payments">The <c>transaction_payments</c> rows, in order: the parts, then the exact cash rest (B6).</param>
public sealed record CompletedSale(
    string TransactionId, string InvoiceNumber, Money Total, Money TaxTotal, CashTender Cash, IReadOnlyList<TenderPart> Payments);

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
/// <item>a <c>transaction_payments</c> row per card or BaridiMob part, then one cash row for the
/// exact rest (<see cref="Tender"/>, B6), and the difference the cash step makes to that rest in
/// <c>rounding_variance</c>, never in the drawer's variance (D-034);</item>
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
    IReasonCodes reasons,
    Waymark.Application.Customers.TabCharges tabs) : ICommandHandler<CompleteSale, CompletedSale>
{
    public async Task<CompletedSale> HandleAsync(
        CompleteSale command, CommandContext context, CancellationToken cancellationToken = default) =>
        await RecordAsync(command, null, context, cancellationToken);

    /// <param name="voiding">The cancel, when the ticket is recorded as cancelled rather than sold (B8).</param>
    internal async Task<CompletedSale> RecordAsync(
        CompleteSale command, VoidTicket? voiding, CommandContext context, CancellationToken cancellationToken)
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
        var sessionId = await CashSessions.OpenAsync(ledger, staging, context, store, command.TerminalId, command.StaffId, now, cancellationToken);
        var transactionId = context.NewId();
        var (net, tax, total, discounted) = (zero, zero, zero, zero);
        var sold = new List<SoldLine>();
        var tier2Lines = new List<Tier2Line>();

        // Pass 1: what each line takes from which batch, and each row's gross before any discount.
        var plans = new List<LinePlan>();
        for (var index = 0; index < priced.Count; index++)
        {
            var (product, count, weighed) = priced[index];

            // A price overridden at the counter (B5, D-092): the band is checked again here, and the
            // line is sold at the new price, the price in force kept beside it on the row.
            var listPrice = (Money?)null;
            if (command.Lines[index].Override is { } given)
            {
                if (weighed is not null)
                {
                    throw new SaleRefusedException($"{product.ProductName}: a weighed line's price is not overridden at the counter.");
                }

                var newPrice = Money.FromMinorUnits(given.NewPrice, currency);
                var check = PriceOverride.Check(product.PriceTtc, newPrice, null);
                if (!check.MayCharge)
                {
                    throw new SaleRefusedException($"{product.ProductName}: the price cannot be {newPrice} ({check.Verdict}; at most {check.Ceiling}).");
                }

                listPrice = product.PriceTtc;
                product = product with { PriceTtc = newPrice };
            }

            // A weighed line takes its weight from the lookup (typed, or read from a label), a
            // counted one its count (D-090).
            var wanted = weighed?.Quantity ?? product.Unit.Whole(count);
            var source = weighed?.Source ?? QuantitySource.Count;

            // A cancelled ticket takes nothing from the shelf (B8): one row per line, with no batch.
            IReadOnlyList<(BatchLevel? Batch, Quantity Taken)> takes;
            if (voiding is not null)
            {
                takes = [(null, wanted)];
            }
            else
            {
                var batches = await ledger.BatchesAsync(product.VariantId, product.Unit.Code, cancellationToken);
                if (batches.Count == 0)
                {
                    throw new SaleRefusedException($"{product.ProductName} has never been received: there is no batch to sell it from.");
                }

                takes = [.. BatchAllocation.Take(batches, wanted, today).Select(take => ((BatchLevel?)take.Batch, take.Taken))];
            }

            // A price label's price is exact, so it is split over the batches with Allocate and
            // sums back to the label (O-26). Every other line is priced per batch, as D-070 does:
            // each row is then its own quantity × price, and recomputes from itself.
            var shares = source == QuantitySource.LabelPrice
                ? WeighedLine.SplitDeclared(weighed!.Amounts.LineTotal, [.. takes.Select(take => take.Taken)])
                : null;

            var rowGross = shares
                ?? [.. takes.Select(take => SaleArithmetic.Line(product.PriceTtc, take.Taken, zero, product.TvaRate, policy).Gross)];
            plans.Add(new LinePlan(product, source, takes, rowGross, command.Lines[index].Discount, listPrice, command.Lines[index].Override));

            // Lowered now, as each line is taken, so a second line of the same product reads the
            // levels the first left.
            foreach (var (batch, taken) in takes)
            {
                if (batch is not null)
                {
                    ledger.AdjustLevel(product.VariantId, batch.BatchId, QuantityDelta.Decrease(taken), now);
                }
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
            var (product, source, takes, rowGross, given, listPrice, overridden) = plans[index];
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
                    BatchId = batch?.BatchId,
                    Quantity = taken.Thousandths,
                    UnitCode = product.Unit.Code,
                    SellPrice = product.PriceTtc,
                    UnitCostAtSale = batch?.UnitCost,
                    DiscountAmount = part,
                    DiscountReasonCode = part.IsZero ? null : why?.ReasonCode,
                    AuthorisedBy = part.IsZero ? null : why?.AuthorisedBy,
                    DiscountNote = part.IsZero ? null : why?.Note,
                    ListPrice = listPrice,
                    OverrideReasonCode = overridden?.ReasonCode,
                    OverrideAuthorisedBy = overridden?.AuthorisedBy,
                    TaxAmount = amounts.Split.Tax,
                    LineTotal = amounts.LineTotal,
                    CreatedAt = now,
                    QuantitySource = source,
                });

                // Stock moves only for a sale: a cancelled ticket's row has no batch.
                if (batch is null)
                {
                    continue;
                }

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

        // Lines struck before the ticket was paid or cancelled (B8, D-097): each a row priced at the
        // price in force, with who struck it and when, no batch, and outside every total.
        foreach (var removed in command.Removed ?? [])
        {
            var (product, count, weighed) = await Price(new SaleLineRequest(removed.Barcode, removed.Count, removed.WeightThousandths), cancellationToken);
            var quantity = weighed?.Quantity ?? product.Unit.Whole(count);
            var amounts = weighed?.Amounts ?? SaleArithmetic.Line(product.PriceTtc, quantity, zero, product.TvaRate, policy);
            staging.Add(new TransactionItem
            {
                TransactionItemId = context.NewId(),
                TransactionId = transactionId,
                VariantId = product.VariantId,
                Quantity = quantity.Thousandths,
                UnitCode = product.Unit.Code,
                SellPrice = product.PriceTtc,
                DiscountAmount = zero,
                TaxAmount = amounts.Split.Tax,
                LineTotal = amounts.LineTotal,
                CreatedAt = now,
                QuantitySource = weighed?.Source ?? QuantitySource.Count,
                RemovedAt = removed.RemovedAt,
                RemovedBy = command.StaffId,
            });
        }

        if (voiding is not null)
        {
            staging.Add(new Transaction
            {
                TransactionId = transactionId,
                StoreId = store.StoreId,
                TerminalId = command.TerminalId,
                CashSessionId = sessionId,
                StaffId = command.StaffId,
                OccurredAt = now,
                RoundingPolicy = policy,
                Subtotal = net,
                DiscountTotal = discounted,
                DiscountReasonCode = command.TicketDiscount?.ReasonCode,
                DiscountAuthorisedBy = command.TicketDiscount?.AuthorisedBy,
                DiscountNote = command.TicketDiscount?.Note,
                TaxTotal = tax,
                TotalAmount = total,
                Currency = currency.Code,
                Status = TransactionStatus.Voided,
                VoidedAt = now,
                VoidedBy = command.StaffId,
                VoidReasonCode = voiding.ReasonCode,
                PaymentOpenedAt = voiding.PaymentOpenedAt,
                VoidAuthorisedBy = voiding.AuthorisedBy,
                CreatedAt = now,
                UpdatedAt = now,
            });

            // No payment, no number, no basket, nothing for tier 2: nothing was sold.
            return new CompletedSale(transactionId, string.Empty, total, tax, zero.ToCashTender(), []);
        }

        // Settled, and the tab asked, before a number is taken, so a refusal spends none (B6, B7).
        var settings = await tabs.SettingsAsync(cancellationToken);
        var settlement = Settle(total, command.Tenders, settings.TabAsPart);
        var tabPart = settlement.Payments.FirstOrDefault(payment => payment.Method == PaymentMethod.OnAccount)?.Amount;
        var tabSale = await tabs.PrepareAsync(settings, command.CustomerId, tabPart, command.TabOverrideBy, now, cancellationToken);
        var creditPart = settlement.Payments.FirstOrDefault(payment => payment.Method == PaymentMethod.StoreCredit)?.Amount;
        var creditSpend = await tabs.PrepareCreditAsync(settings, command.CustomerId, creditPart, now, cancellationToken);
        var invoice = await InvoiceNumbers.NextAsync(ledger, store, today, cancellationToken);
        staging.Add(new Transaction
        {
            TransactionId = transactionId,
            StoreId = store.StoreId,
            TerminalId = command.TerminalId,
            CashSessionId = sessionId,
            StaffId = command.StaffId,
            CustomerId = tabSale.CustomerId,
            InvoiceNumber = invoice,
            OccurredAt = now,
            RoundingPolicy = policy,
            Subtotal = net,
            DiscountTotal = discounted,
            DiscountReasonCode = command.TicketDiscount?.ReasonCode,
            DiscountAuthorisedBy = command.TicketDiscount?.AuthorisedBy,
            DiscountNote = command.TicketDiscount?.Note,
            TaxTotal = tax,
            TotalAmount = total,
            Currency = currency.Code,
            Status = TransactionStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var cash = settlement.Cash;
        var sequence = 0;
        foreach (var payment in settlement.Payments)
        {
            var row = new TransactionPayment
            {
                PaymentId = context.NewId(),
                TransactionId = transactionId,
                Sequence = ++sequence,
                PaymentMethod = payment.Method,
                Amount = payment.Amount,
                Currency = currency.Code,
                Reference = payment.Reference,
                CreatedAt = now,
            };
            staging.Add(row);

            // The tab part (B7, D-055): its charge, the same amount, pointing at its payment row;
            // both go in with the sale or neither does.
            if (payment.Method == PaymentMethod.OnAccount)
            {
                staging.Add(new ReceivableMovement
                {
                    MovementId = context.NewId(),
                    StoreId = store.StoreId,
                    CustomerId = tabSale.CustomerId!,
                    MovementType = ReceivableMovementType.Charge,
                    Amount = payment.Amount,
                    OccurredAt = now,
                    PaymentId = row.PaymentId,
                    StaffId = command.StaffId,
                    OverrideAuthorisedBy = tabSale.OverrideAuthorisedBy,
                });
            }

            // The store credit part (B9b, D-101): what has expired written off first, then the spend,
            // each with the balance after it, and customers.credit kept equal; all with the sale or none.
            if (payment.Method == PaymentMethod.StoreCredit && creditSpend is { } spend)
            {
                var balance = spend.Balance;
                if (spend.Expired.IsPositive)
                {
                    balance -= spend.Expired;
                    staging.Add(new CreditMovement
                    {
                        MovementId = context.NewId(),
                        CustomerId = spend.CustomerId,
                        MovementType = CreditMovementType.Expire,
                        Amount = -spend.Expired,
                        BalanceAfter = balance,
                        StaffId = command.StaffId,
                        TerminalId = command.TerminalId,
                        OccurredAt = now,
                    });
                }

                balance -= payment.Amount;
                staging.Add(new CreditMovement
                {
                    MovementId = context.NewId(),
                    CustomerId = spend.CustomerId,
                    MovementType = CreditMovementType.Redeem,
                    Amount = -payment.Amount,
                    BalanceAfter = balance,
                    TransactionId = transactionId,
                    StaffId = command.StaffId,
                    TerminalId = command.TerminalId,
                    OccurredAt = now,
                });
                await tabs.KeepCreditAsync(spend.CustomerId, balance, now, cancellationToken);
            }
        }

        if (tabSale.CustomerId is { } customerId)
        {
            tabs.Log(context, customerId, command.StaffId, command.TerminalId, tabPart is not null || creditSpend is not null);
        }

        // The tender rounds, never the invoice (D-034): the cash row is the exact rest, and what
        // the 5 DZD step adds or takes off it is recorded here.
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

        return new CompletedSale(transactionId, invoice, total, tax, cash, settlement.Payments);
    }

    /// <summary>
    /// How the ticket is paid (B6, D-095), on the server's own total: the till's figure is a preview.
    /// With no part the ticket is all cash, the first of <see cref="Tender"/>'s rules, and the rule is
    /// not asked: a sale paid in cash never depends on the parts' rules. A reference that reads as a
    /// card number refuses the sale, so one is never written.
    /// </summary>
    private static Settlement Settle(Money total, IReadOnlyList<GivenTender>? tenders, bool tabMayBePart)
    {
        if (tenders is not { Count: > 0 })
        {
            var all = total.ToCashTender();
            return new Settlement(TenderVerdict.Settled, total.IsZero ? [] : [new TenderPart(PaymentMethod.Cash, total)], all);
        }

        var parts = new List<TenderPart>(tenders.Count);
        foreach (var tender in tenders)
        {
            if (PaymentReference.Read(tender.Reference, out var reference) != ReferenceVerdict.Accepted)
            {
                throw new SaleRefusedException("A payment reference was refused: it reads as a card number, or no terminal prints it. Nothing was kept.");
            }

            parts.Add(new TenderPart(tender.Method, Money.FromMinorUnits(tender.Amount, total.Currency), reference));
        }

        var settled = Tender.Settle(total, parts, tabMayBePart);
        return settled.Verdict switch
        {
            TenderVerdict.Settled => settled,
            TenderVerdict.AboveTotal => throw new SaleRefusedException(
                $"The parts come to more than the ticket's {total}: a card gives no change."),
            TenderVerdict.NotAboveZero => throw new SaleRefusedException("A part of zero or less."),
            TenderVerdict.TabTwice => throw new SaleRefusedException("A ticket goes on the tab once."),
            TenderVerdict.TabNotWhole => throw new SaleRefusedException("This shop puts a ticket on the tab whole, or not at all."),
            TenderVerdict.CreditTwice => throw new SaleRefusedException("A ticket spends store credit once."),
            _ => throw new SaleRefusedException("Only a card, BaridiMob, the tab or store credit can be a part; cash is what is left."),
        };
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
        IReadOnlyList<(BatchLevel? Batch, Quantity Taken)> Takes,
        IReadOnlyList<Money> RowGross,
        GivenDiscount? Given,
        Money? ListPrice,
        GivenOverride? Override);

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
        if (given.Count > 0)
        {
            var accepted = (await reasons.ForAsync(ReasonCodeAppliesTo.Discount, cancellationToken)).ToDictionary(reason => reason.Code, StringComparer.Ordinal);
            if (given.FirstOrDefault(discount => !accepted.ContainsKey(discount.ReasonCode) || string.IsNullOrWhiteSpace(discount.AuthorisedBy)) is { } wrong)
            {
                throw new SaleRefusedException($"'{wrong.ReasonCode}' is not a discount reason this shop accepts, or nobody allowed it.");
            }

            // A reason that asks for a note is not recorded without one (F-28, D-092).
            if (given.FirstOrDefault(discount => accepted[discount.ReasonCode].RequiresNote && string.IsNullOrWhiteSpace(discount.Note)) is { } bare)
            {
                throw new SaleRefusedException($"'{bare.ReasonCode}' asks for a note, and none was written.");
            }
        }

        var overrides = command.Lines.Select(line => line.Override).OfType<GivenOverride>().ToList();
        if (overrides.Count > 0)
        {
            var accepted = (await reasons.ForAsync(ReasonCodeAppliesTo.PriceOverride, cancellationToken)).Select(reason => reason.Code).ToHashSet(StringComparer.Ordinal);
            if (overrides.FirstOrDefault(given => !accepted.Contains(given.ReasonCode) || string.IsNullOrWhiteSpace(given.AuthorisedBy)) is { } wrong)
            {
                throw new SaleRefusedException($"'{wrong.ReasonCode}' is not a price override reason this shop accepts, or nobody allowed it.");
            }
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
}

/// <summary>
/// A ticket cancelled (B8, D-097). A cashier's cancel after the payment panel was opened needs a
/// manager's authorisation (<see cref="Voids.NeedsAuthorisation"/>); every cancel needs a reason. Then
/// it is written as the sale would have been, voided, by <see cref="CompleteSaleHandler"/>: one pricing,
/// never two. Refused, nothing is written.
/// </summary>
public sealed class VoidTicketHandler(CompleteSaleHandler sales, IReasonCodes reasons) : ICommandHandler<VoidTicket, VoidedTicket>
{
    public async Task<VoidedTicket> HandleAsync(VoidTicket command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (Voids.NeedsAuthorisation(command.SellerMayVoid, command.PaymentOpenedAt is not null) && string.IsNullOrWhiteSpace(command.AuthorisedBy))
        {
            throw new SaleRefusedException("This ticket was being paid: a manager authorises its cancellation.");
        }

        if ((await reasons.ForAsync(ReasonCodeAppliesTo.Void, cancellationToken)).All(reason => reason.Code != command.ReasonCode))
        {
            throw new SaleRefusedException($"'{command.ReasonCode}' is not a reason this shop gives for cancelling a ticket.");
        }

        var written = await sales.RecordAsync(
            new CompleteSale(command.TerminalId, command.StaffId, command.Lines, command.TicketDiscount, Removed: command.Removed),
            command, context, cancellationToken);
        return new VoidedTicket(written.TransactionId, written.Total);
    }
}
