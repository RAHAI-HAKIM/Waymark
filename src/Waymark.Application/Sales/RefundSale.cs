using Waymark.Application.Commands;
using Waymark.Application.Customers;
using Waymark.Domain;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Organisation;
using Waymark.Domain.Pricing;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Domain.Work;
using RefusalCodes = Waymark.Contracts.Pos.RefusalCodes;

namespace Waymark.Application.Sales;

/// <summary>Where the part of a refund that does not go back on the tab is paid (D-098).</summary>
public enum RefundTo
{
    /// <summary>Out of the drawer, rounded once to the cash step (D-034).</summary>
    Cash,

    /// <summary>As store credit to a named customer, the customer module on.</summary>
    StoreCredit,
}

/// <summary>
/// A refund linked to its sale (B9, D-098): some of its lines brought back, written as a refund
/// transaction of its own, with its own number.
/// </summary>
/// <param name="Original">The sale refunded: its transaction id or its invoice number.</param>
/// <param name="ReasonCode">An active <c>return</c> reason (D-079).</param>
/// <param name="Note">What the cashier wrote, for a reason that asks for a note; null otherwise.</param>
/// <param name="CustomerId">
/// The customer the store credit goes to when the sale had none (attached by phone at the refund);
/// null otherwise. A sale that had a customer refunds to that customer only.
/// </param>
/// <param name="SellerMayRefund">The seller's rank reaches <c>Refund</c> as the tenant raised it, as the host asked <c>StaffPermissions</c>.</param>
/// <param name="AuthorisedBy">The person whose PIN the till cited, resolved by the host; null when none.</param>
/// <param name="Quote">True to work the refund out and write nothing: what the till shows before the cashier confirms.</param>
public sealed record RefundSale(
    string TerminalId,
    string StaffId,
    string Original,
    IReadOnlyList<ReturnedLine> Lines,
    string ReasonCode,
    string? Note,
    RefundTo To,
    string? CustomerId,
    bool SellerMayRefund,
    string? AuthorisedBy,
    bool Quote = false) : ICommand<RefundedSale>;

/// <summary>
/// A line of the sale brought back, as the till showed it: one line per product and price (D-088), so
/// it is named by its variant and its unit price, and may be several rows of the sale.
/// </summary>
/// <param name="UnitPrice">The price each unit sold at, in minor units.</param>
/// <param name="Quantity">In thousandths of the line's selling unit: 2 units is 2000.</param>
/// <param name="Restock">The cashier's "Remis en rayon"; an expired batch is never restocked.</param>
public sealed record ReturnedLine(string VariantId, long UnitPrice, long Quantity, bool Restock);

/// <summary>The refund as written.</summary>
/// <param name="Total">What it gives back, above zero or zero: the sum of its rows.</param>
/// <param name="ToTab">What went back on the tab (D-055); zero when none.</param>
/// <param name="Rest">What was paid as <see cref="RestTo"/>: <see cref="Total"/> less <see cref="ToTab"/>.</param>
/// <param name="Cash">For a cash rest, what comes out of the drawer, rounded once, and the difference; zero otherwise.</param>
/// <param name="CreditBalance">For store credit, the customer's balance after it; null otherwise.</param>
/// <param name="ToCredit">What the sale paid in store credit and came back as store credit first (B9b, D-101); zero when none.</param>
/// <param name="MayCredit">Store credit may be offered: the customer module is on.</param>
/// <param name="CustomerOnTicket">The ticket has its customer, or one was attached: store credit has somebody to go to.</param>
/// <param name="TransactionId">The refund's row; empty for a quote, which writes nothing.</param>
public sealed record RefundedSale(
    string TransactionId, string InvoiceNumber, string OriginalTransactionId, Money Total, Money ToTab, Money Rest, RefundTo RestTo,
    CashTender Cash, Money? CreditBalance, bool MayCredit, bool CustomerOnTicket = false, Money? ToCredit = null);

/// <summary>
/// Refunds part or all of a sale (B9, D-098). It stages, for the executor to commit together or not
/// at all (D-050):
/// <list type="bullet">
/// <item>a refund transaction, completed, with the next invoice number of the shared sequence and
/// <c>original_transaction_id</c>; its rows the sold rows negated, on the same batches, each giving
/// back exactly its share of what was paid (<see cref="Refunds.Share"/>);</item>
/// <item>one <c>returns</c> row per sold row brought back, naming its refund row (F-17);</item>
/// <item>a <c>return_in</c> movement and the batch's level raised, for what goes back on the shelf
/// (<see cref="Refunds.Restocks"/>);</item>
/// <item>the payments: the tab's share first (<see cref="Refunds.ToTab"/>) as a negative
/// <c>on_account</c> row with its negative charge (D-055), then the rest in cash, rounded once to
/// the cash step with the difference in <c>rounding_variance</c>, or as store credit, issued on
/// <c>credit_movements</c> with <c>customers.credit</c> kept in step;</item>
/// <item>the sale's new status (<see cref="Refunds.StatusAfter"/>).</item>
/// </list>
/// <para>
/// Like a cancel, nothing is sent: no outbox row and nothing for tier 2 (D-043). Refused, nothing is
/// written and no number is spent.
/// </para>
/// </summary>
public sealed class RefundSaleHandler(
    IRefundLedger refunds,
    ISalesLedger ledger,
    ICustomerLedger customers,
    ITenantConfiguration configuration,
    IStaging staging,
    IStoreCalendar calendar,
    TimeProvider clock,
    IReasonCodes reasons,
    TabCharges tabs) : ICommandHandler<RefundSale, RefundedSale>
{
    public async Task<RefundedSale> HandleAsync(RefundSale command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.TerminalId);
        ArgumentException.ThrowIfNullOrWhiteSpace(command.StaffId);

        // A quote writes nothing, so it needs nobody's authorisation; the refund itself does.
        if (!command.Quote && !command.SellerMayRefund && string.IsNullOrWhiteSpace(command.AuthorisedBy))
        {
            throw new SaleRefusedException("This shop asks for a manager to authorise a refund.", RefusalCodes.RefundNeedsManager);
        }

        if (command.Lines.Count == 0)
        {
            throw new SaleRefusedException("A refund brings back at least one line.");
        }

        var found = await refunds.SaleAsync(command.Original, cancellationToken)
            ?? throw new SaleRefusedException("This store has no such ticket.");
        var sale = found.Sale;
        if (sale.OriginalTransactionId is not null)
        {
            throw new SaleRefusedException("A refund is not refunded: the sale it refunds is.", RefusalCodes.RefundOfRefund);
        }

        if (sale.Status is not (TransactionStatus.Completed or TransactionStatus.PartiallyRefunded))
        {
            throw sale.Status == TransactionStatus.Refunded
                ? new SaleRefusedException("Everything on this ticket has already been refunded.", RefusalCodes.RefundAlreadyWhole)
                : new SaleRefusedException("Only a paid ticket is refunded.");
        }

        await CheckReasonAsync(command, cancellationToken);

        var store = await ledger.CurrentStoreAsync(cancellationToken)
            ?? throw new SaleRefusedException("This store has no row in stores; it is not commissioned.");
        var currency = Currency.FromCode(sale.Currency);
        var zero = Money.Zero(currency);
        var now = clock.GetUtcNow();
        var today = calendar.Today;

        // The customer: the sale's, else the one attached at the refund for store credit. Store credit
        // asked for where there can be none is refused before anything is worked out.
        if (command.CustomerId is { } attached && sale.CustomerId is { } own && !string.Equals(attached, own, StringComparison.Ordinal))
        {
            throw new SaleRefusedException("This ticket was sold to another customer: the refund is theirs.", RefusalCodes.RefundOtherCustomer);
        }

        var customerId = sale.CustomerId ?? command.CustomerId;
        var module = (await configuration.CurrentAsync(cancellationToken)).CustomerModule;
        Customer? creditor = null;
        if (command.To == RefundTo.StoreCredit)
        {
            if (!module)
            {
                throw new SaleRefusedException("This shop keeps no customers: a refund is paid in cash.", RefusalCodes.ModuleOff);
            }

            creditor = (customerId is null ? null : await customers.FindAsync(customerId, cancellationToken))
                ?? throw new SaleRefusedException("Store credit is a named customer's: attach one first.", RefusalCodes.CreditNeedsCustomer);
        }

        // Each line asked for is a line of the ticket: its rows, the variant at that price (D-088).
        var returning = new Dictionary<SoldItem, (Quantity Portion, bool Restock)>();
        var asked = new HashSet<(string, long)>();
        foreach (var line in command.Lines)
        {
            if (!asked.Add((line.VariantId, line.UnitPrice)))
            {
                throw new SaleRefusedException("A line is brought back once in a refund.");
            }

            var rows = found.Items.Where(item => item.Item.VariantId == line.VariantId && item.Item.SellPrice.MinorUnits == line.UnitPrice).ToList();
            if (rows.Count == 0)
            {
                throw new SaleRefusedException("That line is not on this ticket.");
            }

            var unit = rows[0].Item.UnitCode;
            var take = Refunds.Take([.. rows.Select(Row)], Quantity.FromThousandths(line.Quantity, unit));
            switch (take.Verdict)
            {
                case ReturnVerdict.NotAboveZero:
                    throw new SaleRefusedException("A line brought back is at least one unit.");
                case ReturnVerdict.AboveReturnable:
                    throw new SaleRefusedException(
                        "More than is left of that line: some of it was already refunded.", RefusalCodes.RefundMoreThanLeft);
                case ReturnVerdict.NotWhole:
                    throw new SaleRefusedException("A weighed line comes back whole, and a counted one in whole units.");
            }

            for (var i = 0; i < rows.Count; i++)
            {
                if (take.PerRow[i].IsPositive)
                {
                    returning[rows[i]] = (take.PerRow[i], line.Restock);
                }
            }
        }

        // What each row gives back, from what it was paid (the risky rule, Refunds.Share).
        var shares = returning.ToDictionary(pair => pair.Key, pair => Refunds.Share(Row(pair.Key), pair.Value.Portion));
        var total = shares.Values.Aggregate(zero, (sum, share) => sum + share.Total);
        var tax = shares.Values.Aggregate(zero, (sum, share) => sum + share.Tax);

        // The tab first (D-055): what the sale put on it, never more than is still owed.
        var toTab = zero;
        if (total.IsPositive && found.TabPaid.IsPositive && sale.CustomerId is { } debtor)
        {
            var movements = await customers.MovementsAsync(debtor, cancellationToken);
            var owed = Tab.Age(currency, [.. movements.Select(movement => new TabMovement(movement.OccurredAt, movement.Amount))]).Balance;
            toTab = Refunds.ToTab(total, found.TabPaid, found.TabRefunded, owed);
        }

        var rest = total - toTab;

        // Then store credit back as store credit (B9b, D-101): a refund never turns it into cash. The
        // sale that spent credit had its customer, and the credit goes back to them.
        var toCredit = rest.IsPositive && found.CreditPaid.IsPositive && sale.CustomerId is not null
            ? Refunds.ToCredit(rest, found.CreditPaid, found.CreditRefunded)
            : zero;
        rest -= toCredit;
        var creditTo = toCredit.IsPositive ? sale.CustomerId : creditor?.CustomerId;
        var issued = toCredit + (creditor is not null && rest.IsPositive ? rest : zero);

        var mayCredit = module;
        var customerOnTicket = customerId is not null;
        if (command.Quote)
        {
            var quoted = issued.IsPositive && creditTo is not null
                ? await customers.CreditBalanceAsync(creditTo, currency, cancellationToken) + issued
                : (Money?)null;
            return new RefundedSale(
                string.Empty, string.Empty, sale.TransactionId, total, toTab, rest, creditor is not null && rest.IsPositive ? RefundTo.StoreCredit : RefundTo.Cash,
                creditor is null && rest.IsPositive ? (-rest).ToCashTender() : zero.ToCashTender(), quoted, mayCredit, customerOnTicket, toCredit);
        }

        // Everything asked and answered: now the number, then the rows.
        var sessionId = await CashSessions.OpenIdAsync(ledger, command.TerminalId, cancellationToken)
            ?? throw new SaleRefusedException(CashSessions.NoneOpen, RefusalCodes.NoOpenSession);
        var invoice = await InvoiceNumbers.NextAsync(ledger, store, today, cancellationToken);
        var refundId = context.NewId();
        var method = rest.IsPositive ? (command.To == RefundTo.Cash ? RefundMethod.Cash : RefundMethod.StoreCredit)
            : toCredit.IsPositive ? RefundMethod.StoreCredit
            : toTab.IsPositive ? RefundMethod.OnAccount
            : command.To == RefundTo.Cash ? RefundMethod.Cash : RefundMethod.StoreCredit;
        var (net, discounted) = (zero, zero);

        // A refund's line is the ticket's: the variant at that price (D-088). Its rows share a place
        // on the refund ticket, in the order the lines were asked (D-104).
        var places = new Dictionary<(string Variant, long Price), int>();
        foreach (var (sold, (portion, restockAsked)) in returning)
        {
            var item = sold.Item;
            var share = shares[sold];
            if (!places.TryGetValue((item.VariantId, item.SellPrice.MinorUnits), out var place))
            {
                places[(item.VariantId, item.SellPrice.MinorUnits)] = place = places.Count + 1;
            }

            // The refund row recomputes from itself: price × quantity − discount = line total. A whole
            // row gives its own discount back; a part, what its units' share leaves of their gross.
            var whole = sold.Returned.IsZero && portion.Thousandths == item.Quantity;
            var off = whole
                ? item.DiscountAmount
                : item.SellPrice.Times(portion.Thousandths, Quantity.Scale, sale.RoundingPolicy) - share.Total;
            if (off.IsNegative)
            {
                throw new InvalidOperationException($"A refund row would give back more than its units' gross: {share.Total} for {portion}.");
            }

            (net, discounted) = (net + (share.Total - share.Tax), discounted + off);
            var refundItemId = context.NewId();
            staging.Add(new TransactionItem
            {
                TransactionItemId = refundItemId,
                TransactionId = refundId,
                VariantId = item.VariantId,
                BatchId = item.BatchId,
                PromotionId = item.PromotionId,
                Quantity = -portion.Thousandths,
                UnitCode = item.UnitCode,
                SellPrice = item.SellPrice,
                UnitCostAtSale = item.UnitCostAtSale,
                DiscountAmount = off,
                DiscountReasonCode = off.IsZero ? null : item.DiscountReasonCode,
                AuthorisedBy = off.IsZero ? null : item.AuthorisedBy,
                DiscountNote = off.IsZero ? null : item.DiscountNote,
                ListPrice = item.ListPrice,
                OverrideReasonCode = item.OverrideReasonCode,
                OverrideAuthorisedBy = item.OverrideAuthorisedBy,
                TaxAmount = -share.Tax,
                LineTotal = -share.Total,
                CreatedAt = now,
                QuantitySource = item.QuantitySource,
                LineNumber = place,
            });

            var restock = item.BatchId is not null && Refunds.Restocks(restockAsked, sold.BatchExpires, today);
            var returnId = context.NewId();
            staging.Add(new SalesReturn
            {
                ReturnId = returnId,
                TransactionItemId = item.TransactionItemId,
                RefundTransactionItemId = refundItemId,
                StoreId = store.StoreId,
                TerminalId = command.TerminalId,
                BatchId = item.BatchId,
                StaffId = command.StaffId,
                ApprovedBy = command.AuthorisedBy,
                QuantityReturned = portion.Thousandths,
                RefundAmount = share.Total,
                RefundMethod = method,
                RestockFlag = restock,
                ReasonCode = command.ReasonCode,
                Note = string.IsNullOrWhiteSpace(command.Note) ? null : command.Note.Trim(),
                CreatedAt = now,
            });

            if (restock)
            {
                // The level is changed through the rows the ledger loaded and tracks, as a sale's is.
                await ledger.BatchesAsync(item.VariantId, item.UnitCode, cancellationToken);
                ledger.AdjustLevel(item.VariantId, item.BatchId!, QuantityDelta.Increase(portion), now);
                staging.Add(new StockMovement
                {
                    MovementId = context.NewId(),
                    StoreId = store.StoreId,
                    VariantId = item.VariantId,
                    BatchId = item.BatchId!,
                    MovementDate = today,
                    MovementType = StockMovementType.ReturnIn,
                    QuantityChanged = portion.Thousandths,
                    UnitCode = item.UnitCode,
                    UnitCost = item.UnitCostAtSale,
                    ReferenceType = "return",
                    ReferenceId = returnId,
                    ReasonCode = command.ReasonCode,
                    StaffId = command.StaffId,
                    CreatedAt = now,
                });
            }
        }

        staging.Add(new Transaction
        {
            TransactionId = refundId,
            StoreId = store.StoreId,
            TerminalId = command.TerminalId,
            CashSessionId = sessionId,
            StaffId = command.StaffId,
            CustomerId = customerId,
            InvoiceNumber = invoice,
            OccurredAt = now,
            RoundingPolicy = store.RoundingPolicy,
            Subtotal = -net,
            DiscountTotal = -discounted,
            TaxTotal = -tax,
            TotalAmount = -total,
            Currency = currency.Code,
            OriginalTransactionId = sale.TransactionId,
            Status = TransactionStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var sequence = 0;
        if (toTab.IsPositive)
        {
            var paymentId = Pay(refundId, ++sequence, PaymentMethod.OnAccount, -toTab, currency, context, now);
            staging.Add(new ReceivableMovement
            {
                MovementId = context.NewId(),
                StoreId = store.StoreId,
                CustomerId = sale.CustomerId!,
                MovementType = ReceivableMovementType.Charge,
                Amount = -toTab,
                OccurredAt = now,
                PaymentId = paymentId,
                StaffId = command.StaffId,
            });
        }

        var cash = zero.ToCashTender();
        Money? balance = null;
        if (issued.IsPositive && creditTo is not null)
        {
            // The credit share and any rest given as credit: one row, one issue, the balance in step.
            Pay(refundId, ++sequence, PaymentMethod.StoreCredit, -issued, currency, context, now);
            balance = await customers.CreditBalanceAsync(creditTo, currency, cancellationToken) + issued;
            staging.Add(new CreditMovement
            {
                MovementId = context.NewId(),
                CustomerId = creditTo,
                MovementType = CreditMovementType.Issue,
                Amount = issued,
                BalanceAfter = balance.Value,
                TransactionId = refundId,
                ReturnId = null,
                StaffId = command.StaffId,
                TerminalId = command.TerminalId,
                ReasonCode = command.ReasonCode,
                OccurredAt = now,
            });
            await customers.StageCreditAsync(creditTo, balance.Value, now, cancellationToken);
        }

        if (rest.IsPositive && creditor is null)
        {
            // Out of the drawer: the exact rest on the row, the cash step's difference beside it (D-034).
            Pay(refundId, ++sequence, PaymentMethod.Cash, -rest, currency, context, now);
            cash = (-rest).ToCashTender();
            if (cash.HasVariance)
            {
                staging.Add(new RoundingVariance
                {
                    VarianceId = context.NewId(),
                    StoreId = store.StoreId,
                    OccurredAt = now,
                    ReferenceType = VarianceReferenceType.Transaction,
                    ReferenceId = refundId,
                    Source = VarianceSource.CashTender,
                    Amount = cash.Variance,
                    Policy = store.RoundingPolicy,
                    CreatedAt = now,
                });
            }
        }

        // The sale's new status, from every one of its rows with this refund counted.
        var after = found.Items.Select(sold => Row(sold) with
        {
            Returned = returning.TryGetValue(sold, out var back) ? sold.Returned + QuantityDelta.Increase(back.Portion) : sold.Returned,
        }).ToList();
        await refunds.StageStatusAsync(sale.TransactionId, Refunds.StatusAfter(after), now, cancellationToken);

        if (customerId is not null)
        {
            tabs.Log(context, customerId, command.StaffId, command.TerminalId, toTab.IsPositive || balance is not null);
        }

        return new RefundedSale(
            refundId, invoice, sale.TransactionId, total, toTab, rest, creditor is not null && rest.IsPositive ? RefundTo.StoreCredit : RefundTo.Cash,
            cash, balance, mayCredit, customerOnTicket, toCredit);
    }

    private static SoldRow Row(SoldItem sold) => new(
        Quantity.FromThousandths(sold.Item.Quantity, sold.Item.UnitCode),
        sold.Returned,
        sold.Item.LineTotal,
        sold.Item.TaxAmount,
        sold.Item.QuantitySource != QuantitySource.Count);

    private string Pay(string transactionId, int sequence, PaymentMethod method, Money amount, Currency currency, CommandContext context, DateTimeOffset now)
    {
        var payment = new TransactionPayment
        {
            PaymentId = context.NewId(),
            TransactionId = transactionId,
            Sequence = sequence,
            PaymentMethod = method,
            Amount = amount,
            Currency = currency.Code,
            CreatedAt = now,
        };
        staging.Add(payment);
        return payment.PaymentId;
    }

    /// <summary>An active <c>return</c> reason (D-079), with its note when it asks for one (F-28).</summary>
    private async Task CheckReasonAsync(RefundSale command, CancellationToken cancellationToken)
    {
        var reason = (await reasons.ForAsync(ReasonCodeAppliesTo.Return, cancellationToken)).FirstOrDefault(r => r.Code == command.ReasonCode)
            ?? throw new SaleRefusedException(
                $"'{command.ReasonCode}' is not a reason this shop gives for a refund.", RefusalCodes.ReasonUnknown, command.ReasonCode);
        if (reason.RequiresNote && string.IsNullOrWhiteSpace(command.Note))
        {
            throw new SaleRefusedException($"'{command.ReasonCode}' asks for a note, and none was written.", RefusalCodes.NoteMissing);
        }
    }
}
