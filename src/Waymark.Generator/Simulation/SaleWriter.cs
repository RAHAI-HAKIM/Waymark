using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Randomness;

namespace Waymark.Generator.Simulation;

/// <summary>A sold line that will be brought back, and everything its refund needs to know.</summary>
internal sealed record ScheduledReturn(
    DateOnly Due,
    int DayNumber,
    int Basket,
    int Item,
    string OriginalTransactionId,
    string OriginalItemId,
    ServedLine Line,
    Money SellPrice,
    Money Discount,
    string? DiscountReason,
    Money LineTotal,
    int Units,
    GeneratedCustomer? Customer,
    PaymentMethod FirstMethod);

/// <summary>
/// Writes what happens at the till (W10 S5, S7): completed sales, voided mis-scans and refunds,
/// with their items, stock movements, payments, tender rounding and store-credit ledger.
///
/// <para>
/// <b>Discounts</b> are taken off a line's TTC before TVA is extracted (D-033) and always carry
/// a reason, and a manager when the reason requires one. <b>Payments</b>: a customer holding
/// store credit may spend it first, a customer with a tab may put the basket on account (more
/// often before payday, and only within the credit limit), and the tender pays the rest.
/// <b>A void</b> is a basket rung wrongly — one unit too many on a line — cancelled with a
/// reason and rung again at once; it moves no stock and no money. <b>A return</b> is decided
/// when the line is sold, for a later day: a refund
/// transaction linked by <c>original_transaction_id</c>, a <c>returns</c> row against the
/// original line naming the refund line, a <c>return_in</c> movement if it goes back on the
/// shelf, and store credit issued if refunded that way. A sale paid on account is refunded to the
/// tab while the tab still covers it (F-16). The original is marked refunded or partially
/// refunded.
/// </para>
/// </summary>
internal sealed class SaleWriter
{
    private readonly SimulationContext _context;
    private readonly Dictionary<int, int> _invoiceSequence = [];
    private readonly Dictionary<string, Money> _credit = new(StringComparer.Ordinal);
    private readonly Dictionary<string, DateOnly> _lastOrder = new(StringComparer.Ordinal);
    private readonly List<ScheduledReturn> _returns = [];
    private readonly Dictionary<string, Dictionary<string, (int Sold, int Returned)>> _returnable = new(StringComparer.Ordinal);
    private readonly RandomStream _discount;
    private readonly RandomStream _void;
    private readonly RandomStream _return;
    private readonly RandomStream _paymentMix;

    private readonly Outbox _outbox;
    private readonly Receivables _receivables;

    public SaleWriter(SimulationContext context, Outbox outbox, Receivables receivables)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(outbox);
        ArgumentNullException.ThrowIfNull(receivables);

        _context = context;
        _outbox = outbox;
        _receivables = receivables;
        _discount = context.Random.Stream("discount");
        _void = context.Random.Stream("void");
        _return = context.Random.Stream("return");
        _paymentMix = context.Random.Stream("payment_mix");
    }

    /// <summary>Store credit held per customer at the end of the run.</summary>
    public IReadOnlyDictionary<string, Money> CreditBalances => _credit;

    /// <summary>The last day each customer bought something.</summary>
    public IReadOnlyDictionary<string, DateOnly> LastOrderDates => _lastOrder;

    /// <summary>Returns due on or before <paramref name="date"/>, oldest first. A return due on a closed day comes on the next open one.</summary>
    public IReadOnlyList<ScheduledReturn> ReturnsDue(DateOnly date) =>
        [.. _returns.Where(r => r.Due <= date).OrderBy(r => r.Due).ThenBy(r => r.DayNumber).ThenBy(r => r.Basket).ThenBy(r => r.Item)];

    /// <summary>Whether this basket is first rung wrongly and voided.</summary>
    public bool IsMisrung(DateOnly date, PlannedBasket basket) =>
        Distributions.Bernoulli(_void.Uniform(date.DayNumber, basket.Number, 0), _context.Config.Mess.VoidShare.Value);

    /// <summary>
    /// The wrong ringing: one unit too many on the first line, voided with a reason at once.
    /// No batch, no movement, no payment: nothing left the shelf or the drawer.
    /// </summary>
    public void WriteVoid(DateOnly date, PlannedBasket basket, CashDrawer drawer, GeneratedStaff staff, IReadOnlyList<ServedLine> served)
    {
        var db = _context.Database.Context;
        var now = _context.Clock.GetUtcNow();
        var policy = _context.Store.RoundingPolicy;
        var zero = Money.Zero(_context.Store.Currency);
        var reason = _context.Reason(_context.Config.Mess.ReasonCodes.Void, _void.Uniform(date.DayNumber, basket.Number, 1));
        var transactionId = _context.Ids.NewId();
        var totals = (Net: zero, Tax: zero, Total: zero);

        var rung = served
            .GroupBy(line => line.Variant.Index)
            .Select((group, index) => (Variant: group.First().Variant, Units: group.Sum(line => line.Quantity.Thousandths / Quantity.Scale) + (index == 0 ? 1 : 0)));

        foreach (var (variant, units) in rung)
        {
            var price = variant.PriceOn(date).Retail;
            var quantity = variant.SellingUnit.Whole(units);
            var amounts = SaleArithmetic.Line(price, quantity, zero, variant.Vat, policy);
            totals = (totals.Net + amounts.Split.Net, totals.Tax + amounts.Split.Tax, totals.Total + amounts.LineTotal);

            db.TransactionItems.Add(new TransactionItem
            {
                TransactionItemId = _context.Ids.NewId(),
                TransactionId = transactionId,
                VariantId = variant.VariantId,
                Quantity = quantity.Thousandths,
                UnitCode = quantity.Unit!,
                SellPrice = price,
                DiscountAmount = zero,
                TaxAmount = amounts.Split.Tax,
                LineTotal = amounts.LineTotal,
                CreatedAt = now,
            });
        }

        db.Transactions.Add(new Transaction
        {
            TransactionId = transactionId,
            StoreId = _context.Store.StoreId,
            TerminalId = drawer.TerminalId,
            CashSessionId = drawer.SessionId,
            StaffId = staff.StaffId,
            CustomerId = basket.Customer?.CustomerId,
            OccurredAt = now,
            RoundingPolicy = policy,
            Subtotal = totals.Net,
            DiscountTotal = zero,
            TaxTotal = totals.Tax,
            TotalAmount = totals.Total,
            Currency = _context.Store.Currency.Code,
            Status = TransactionStatus.Voided,
            VoidedAt = now,
            VoidedBy = _context.Authoriser(reason) ?? staff.StaffId,
            VoidReasonCode = reason.Code,
            CreatedAt = now,
            UpdatedAt = now,
        });
    }

    /// <summary>A completed sale: items with any discounts, sale movements, payments, and returns scheduled for later.</summary>
    public void WriteSale(DayContext day, PlannedBasket basket, CashDrawer drawer, GeneratedStaff staff, IReadOnlyList<ServedLine> served)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(basket);
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(served);

        var db = _context.Database.Context;
        var mess = _context.Config.Mess;
        var store = _context.Store;
        var date = day.Date;
        var dayNumber = date.DayNumber;
        var now = _context.Clock.GetUtcNow();
        var policy = store.RoundingPolicy;
        var zero = Money.Zero(store.Currency);
        var transactionId = _context.Ids.NewId();
        var totals = (Net: zero, Tax: zero, Total: zero, Discount: zero);
        var items = new List<(string ItemId, ServedLine Line, Money Price, Money Discount, string? Reason, Money Total)>();

        for (var j = 0; j < served.Count; j++)
        {
            var line = served[j];
            var variant = line.Variant;
            var price = variant.PriceOn(date).Retail;
            var discount = zero;
            string? reasonCode = null;
            string? authorisedBy = null;

            if (Distributions.Bernoulli(_discount.Uniform(dayNumber, basket.Number, j, 0), mess.DiscountLineShare.Value))
            {
                var percent = Distributions.UniformInt(_discount.Uniform(dayNumber, basket.Number, j, 1), (int)mess.DiscountPercent.Value.Min, (int)mess.DiscountPercent.Value.Max);
                var gross = SaleArithmetic.Line(price, line.Quantity, zero, variant.Vat, policy).Gross;
                discount = gross.Percent(new BasisPoints(percent * 100), policy);

                if (discount.IsPositive)
                {
                    var reason = _context.Reason(mess.ReasonCodes.Discount, _discount.Uniform(dayNumber, basket.Number, j, 2));
                    reasonCode = reason.Code;
                    authorisedBy = _context.Authoriser(reason);
                }
            }

            var amounts = SaleArithmetic.Line(price, line.Quantity, discount, variant.Vat, policy);
            totals = (totals.Net + amounts.Split.Net, totals.Tax + amounts.Split.Tax, totals.Total + amounts.LineTotal, totals.Discount + discount);
            var itemId = _context.Ids.NewId();
            items.Add((itemId, line, price, discount, reasonCode, amounts.LineTotal));

            db.TransactionItems.Add(new TransactionItem
            {
                TransactionItemId = itemId,
                TransactionId = transactionId,
                VariantId = variant.VariantId,
                BatchId = line.Lot.BatchId,
                Quantity = line.Quantity.Thousandths,
                UnitCode = line.Quantity.Unit!,
                SellPrice = price,
                UnitCostAtSale = line.Lot.UnitCost,
                DiscountAmount = discount,
                DiscountReasonCode = reasonCode,
                AuthorisedBy = authorisedBy,
                TaxAmount = amounts.Split.Tax,
                LineTotal = amounts.LineTotal,
                CreatedAt = now,
            });

            Movement(StockMovementType.Sale, line, QuantityDelta.Decrease(line.Quantity), date, "transaction", transactionId, null, staff, now);
        }

        db.Transactions.Add(new Transaction
        {
            TransactionId = transactionId,
            StoreId = store.StoreId,
            TerminalId = drawer.TerminalId,
            CashSessionId = drawer.SessionId,
            StaffId = staff.StaffId,
            CustomerId = basket.Customer?.CustomerId,
            InvoiceNumber = NextInvoiceNumber(date),
            OccurredAt = now,
            RoundingPolicy = policy,
            Subtotal = totals.Net,
            DiscountTotal = totals.Discount,
            TaxTotal = totals.Tax,
            TotalAmount = totals.Total,
            Currency = store.Currency.Code,
            Status = TransactionStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var methods = Pay(day, basket, drawer, staff, transactionId, totals.Total);
        var firstMethod = methods[0];

        if (basket.Customer is { } customer)
        {
            _lastOrder[customer.CustomerId] = date;
        }

        // The anonymous basket goes to the outbox with the sale (CLAUDE.md §3.6).
        _outbox.EmitBasket(date, [.. items.Select(item => (item.Line.Variant, item.Line.Quantity, item.Total, item.Discount.IsPositive))], methods);

        ScheduleReturns(date, basket, transactionId, items, firstMethod);
    }

    /// <summary>
    /// A refund for a returned line, on the day it comes back. Returns false, writing nothing,
    /// when the refund is due in cash to an anonymous customer and the drawer cannot cover it:
    /// the customer comes back on the next open day.
    /// </summary>
    public bool WriteRefund(DateOnly date, ScheduledReturn returned, CashDrawer drawer, GeneratedStaff staff)
    {
        ArgumentNullException.ThrowIfNull(returned);
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(staff);

        var db = _context.Database.Context;
        var mess = _context.Config.Mess;
        var store = _context.Store;
        var now = _context.Clock.GetUtcNow();
        var policy = store.RoundingPolicy;
        var zero = Money.Zero(store.Currency);
        var line = returned.Line;
        var variant = line.Variant;
        var soldUnits = (int)(line.Quantity.Thousandths / Quantity.Scale);
        var wholeLine = returned.Units == soldUnits;
        var coordinates = (returned.DayNumber, returned.Basket, returned.Item);

        // The refund line is the sale line negated: its quantity is negative, and a discount on
        // a whole returned line comes back with it, so the refund equals what was paid.
        var discount = wholeLine ? returned.Discount : zero;
        var amounts = SaleArithmetic.Line(returned.SellPrice, variant.SellingUnit.Whole(-returned.Units), -discount, variant.Vat, policy);
        var refund = -amounts.LineTotal;

        var reason = _context.Reason(mess.ReasonCodes.Return, Draw(coordinates, 3));
        var customer = returned.Customer;

        // An on-account sale is taken off the tab, not paid out of the drawer. If the customer has
        // already paid the tab down below the refund, it becomes store credit instead.
        var method = returned.FirstMethod switch
        {
            PaymentMethod.Card => RefundMethod.Card,
            PaymentMethod.OnAccount when _receivables.Owed(customer!) >= refund => RefundMethod.OnAccount,
            PaymentMethod.StoreCredit or PaymentMethod.OnAccount => RefundMethod.StoreCredit,
            _ => RefundMethod.Cash,
        };

        if (customer is not null && method is RefundMethod.Cash or RefundMethod.Card
            && Distributions.Bernoulli(Draw(coordinates, 4), mess.StoreCreditRefundShare.Value))
        {
            method = RefundMethod.StoreCredit;
        }

        // A shopkeeper cannot pay out cash the drawer does not hold. A regular is refunded in
        // store credit instead; anyone else is asked to come back (Phase 0 final test: a mini
        // store closed its drawer below zero).
        if (method == RefundMethod.Cash && drawer.Expected < refund.ToCashTender().Tendered)
        {
            if (customer is null)
            {
                return false;
            }

            method = RefundMethod.StoreCredit;
        }

        _returns.Remove(returned);

        var restock = Distributions.Bernoulli(Draw(coordinates, 5), mess.RestockShare.Value)
            && (line.Lot.Expires is null || date <= line.Lot.Expires);

        var transactionId = _context.Ids.NewId();
        var refundItemId = _context.Ids.NewId();
        db.TransactionItems.Add(new TransactionItem
        {
            TransactionItemId = refundItemId,
            TransactionId = transactionId,
            VariantId = variant.VariantId,
            BatchId = line.Lot.BatchId,
            Quantity = -returned.Units * (long)Quantity.Scale,
            UnitCode = line.Quantity.Unit!,
            SellPrice = returned.SellPrice,
            UnitCostAtSale = line.Lot.UnitCost,
            DiscountAmount = discount,
            DiscountReasonCode = discount.IsPositive ? returned.DiscountReason : null,
            AuthorisedBy = discount.IsPositive ? _context.Authoriser(store.ReasonCodes[returned.DiscountReason!]) : null,
            TaxAmount = amounts.Split.Tax,
            LineTotal = amounts.LineTotal,
            CreatedAt = now,
        });

        db.Transactions.Add(new Transaction
        {
            TransactionId = transactionId,
            StoreId = store.StoreId,
            TerminalId = drawer.TerminalId,
            CashSessionId = drawer.SessionId,
            StaffId = staff.StaffId,
            CustomerId = customer?.CustomerId,
            InvoiceNumber = NextInvoiceNumber(date),
            OccurredAt = now,
            RoundingPolicy = policy,
            Subtotal = amounts.Split.Net,
            DiscountTotal = -discount,
            TaxTotal = amounts.Split.Tax,
            TotalAmount = amounts.LineTotal,
            Currency = store.Currency.Code,
            OriginalTransactionId = returned.OriginalTransactionId,
            Status = TransactionStatus.Completed,
            CreatedAt = now,
            UpdatedAt = now,
        });

        var returnId = _context.Ids.NewId();
        db.Returns.Add(new SalesReturn
        {
            ReturnId = returnId,
            TransactionItemId = returned.OriginalItemId,
            RefundTransactionItemId = refundItemId,
            StoreId = store.StoreId,
            TerminalId = drawer.TerminalId,
            BatchId = line.Lot.BatchId,
            StaffId = staff.StaffId,
            ApprovedBy = _context.Authoriser(reason),
            QuantityReturned = returned.Units * (long)Quantity.Scale,
            RefundAmount = refund,
            RefundMethod = method,
            RestockFlag = restock,
            ReasonCode = reason.Code,
            Note = reason.RequiresNote ? $"{reason.LabelFr} (synthétique)" : null,
            CreatedAt = now,
        });

        var paymentMethod = method switch
        {
            RefundMethod.Card => PaymentMethod.Card,
            RefundMethod.StoreCredit => PaymentMethod.StoreCredit,
            RefundMethod.OnAccount => PaymentMethod.OnAccount,
            _ => PaymentMethod.Cash,
        };

        var paymentId = AddPayment(transactionId, 1, paymentMethod, -refund, now);

        switch (method)
        {
            case RefundMethod.Cash:
                drawer.TakeCash(-refund, transactionId);
                break;
            case RefundMethod.StoreCredit:
                Credit(customer!, CreditMovementType.Issue, refund, transactionId, returnId, reason.Code, staff, drawer, now);
                break;
            case RefundMethod.OnAccount:
                _receivables.Charge(customer!, paymentId, -refund, staff);
                break;
        }

        if (restock)
        {
            var change = QuantityDelta.Increase(variant.SellingUnit.Whole(returned.Units));
            line.Lot.Apply(change);
            Movement(StockMovementType.ReturnIn, line, change, date, "return", returnId, reason.Code, staff, now);
        }

        var original = _returnable[returned.OriginalTransactionId];
        var (sold, already) = original[returned.OriginalItemId];
        original[returned.OriginalItemId] = (sold, already + returned.Units);
        var status = original.Values.All(item => item.Returned >= item.Sold) ? TransactionStatus.Refunded : TransactionStatus.PartiallyRefunded;

        db.Transactions
            .Where(t => t.TransactionId == returned.OriginalTransactionId)
            .ExecuteUpdate(setters => setters.SetProperty(t => t.Status, status).SetProperty(t => t.UpdatedAt, now));

        return true;
    }

    /// <summary>
    /// Pays a completed sale: store credit first if the customer holds some and uses it, then on
    /// account for an enrolled customer (more often before payday), then the basket's tender.
    /// Returns the methods used in order; the first is how a return of this sale is refunded.
    /// </summary>
    private List<PaymentMethod> Pay(DayContext day, PlannedBasket basket, CashDrawer drawer, GeneratedStaff staff, string transactionId, Money total)
    {
        var mess = _context.Config.Mess;
        var now = _context.Clock.GetUtcNow();
        var remaining = total;
        var sequence = 0;
        var used = new List<PaymentMethod>();

        string Paid(PaymentMethod method, Money amount)
        {
            var paymentId = AddPayment(transactionId, ++sequence, method, amount, now);
            used.Add(method);
            remaining -= amount;
            return paymentId;
        }

        if (basket.Customer is { } customer)
        {
            var balance = _credit.GetValueOrDefault(customer.CustomerId, Money.Zero(_context.Store.Currency));
            if (balance.IsPositive && remaining.IsPositive
                && Distributions.Bernoulli(_paymentMix.Uniform(day.Date.DayNumber, basket.Number, 0), mess.StoreCreditUseShare.Value))
            {
                var spent = balance < remaining ? balance : remaining;
                Credit(customer, CreditMovementType.Redeem, -spent, transactionId, null, null, staff, drawer, now);
                Paid(PaymentMethod.StoreCredit, spent);
            }

            if (remaining.IsPositive
                && Distributions.Bernoulli(_paymentMix.Uniform(day.Date.DayNumber, basket.Number, 1), Math.Min(1, mess.OnAccountShare.Value * day.Payday.OnAccount))
                && _receivables.CanCharge(customer, remaining))
            {
                var onAccount = remaining;
                _receivables.Charge(customer, Paid(PaymentMethod.OnAccount, onAccount), onAccount, staff);
            }
        }

        if (remaining.IsPositive)
        {
            var method = basket.Method;
            var amount = remaining;
            Paid(method, amount);
            if (method == PaymentMethod.Cash)
            {
                drawer.TakeCash(amount, transactionId);
            }
        }

        return used;
    }

    private void ScheduleReturns(DateOnly date, PlannedBasket basket, string transactionId, List<(string ItemId, ServedLine Line, Money Price, Money Discount, string? Reason, Money Total)> items, PaymentMethod firstMethod)
    {
        var mess = _context.Config.Mess;
        var dayNumber = date.DayNumber;
        var scheduled = false;

        for (var j = 0; j < items.Count; j++)
        {
            var coordinates = (dayNumber, basket.Number, j);
            if (!Distributions.Bernoulli(Draw(coordinates, 0), mess.ReturnLineShare.Value))
            {
                continue;
            }

            var (itemId, line, price, discount, reason, total) = items[j];
            var sold = (int)(line.Quantity.Thousandths / Quantity.Scale);
            var delay = Distributions.UniformInt(Draw(coordinates, 1), (int)mess.ReturnDelayDays.Value.Min, (int)mess.ReturnDelayDays.Value.Max);
            var units = discount.IsPositive ? sold : Distributions.UniformInt(Draw(coordinates, 2), 1, sold);

            _returns.Add(new ScheduledReturn(date.AddDays(delay), dayNumber, basket.Number, j, transactionId, itemId, line, price, discount, reason, total, units, basket.Customer, firstMethod));
            scheduled = true;
        }

        if (scheduled)
        {
            _returnable[transactionId] = items.ToDictionary(
                item => item.ItemId,
                item => ((int)(item.Line.Quantity.Thousandths / Quantity.Scale), 0),
                StringComparer.Ordinal);
        }
    }

    private void Credit(GeneratedCustomer customer, CreditMovementType type, Money amount, string transactionId, string? returnId, string? reasonCode, GeneratedStaff staff, CashDrawer drawer, DateTimeOffset now)
    {
        var balance = _credit.GetValueOrDefault(customer.CustomerId, Money.Zero(_context.Store.Currency)) + amount;
        if (balance.IsNegative)
        {
            throw new InvalidOperationException($"Customer {customer.Number} would hold {balance} of store credit; a redemption spent more than the balance.");
        }

        _credit[customer.CustomerId] = balance;
        _context.Database.Context.CreditMovements.Add(new CreditMovement
        {
            MovementId = _context.Ids.NewId(),
            CustomerId = customer.CustomerId,
            MovementType = type,
            Amount = amount,
            BalanceAfter = balance,
            TransactionId = transactionId,
            ReturnId = returnId,
            StaffId = staff.StaffId,
            TerminalId = drawer.TerminalId,
            ReasonCode = reasonCode,
            OccurredAt = now,
        });
    }

    private string AddPayment(string transactionId, int sequence, PaymentMethod method, Money amount, DateTimeOffset now)
    {
        var paymentId = _context.Ids.NewId();
        _context.Database.Context.TransactionPayments.Add(new TransactionPayment
        {
            PaymentId = paymentId,
            TransactionId = transactionId,
            Sequence = sequence,
            PaymentMethod = method,
            Amount = amount,
            Currency = _context.Store.Currency.Code,
            CreatedAt = now,
        });
        return paymentId;
    }

    private void Movement(StockMovementType type, ServedLine line, QuantityDelta change, DateOnly date, string referenceType, string referenceId, string? reasonCode, GeneratedStaff staff, DateTimeOffset now) =>
        _context.Database.Context.StockMovements.Add(new StockMovement
        {
            MovementId = _context.Ids.NewId(),
            StoreId = _context.Store.StoreId,
            VariantId = line.Variant.VariantId,
            BatchId = line.Lot.BatchId,
            MovementDate = date,
            MovementType = type,
            QuantityChanged = change.Thousandths,
            UnitCode = change.Unit!,
            UnitCost = line.Lot.UnitCost,
            ReferenceType = referenceType,
            ReferenceId = referenceId,
            ReasonCode = reasonCode,
            StaffId = staff.StaffId,
            CreatedAt = now,
        });

    /// <summary>
    /// Gapless per store per fiscal year (schema: <c>transactions.invoice_number</c>), refunds
    /// included. The generated store keeps the default fiscal year, January to December.
    /// </summary>
    private string NextInvoiceNumber(DateOnly date)
    {
        var sequence = _invoiceSequence.GetValueOrDefault(date.Year) + 1;
        _invoiceSequence[date.Year] = sequence;
        return string.Create(CultureInfo.InvariantCulture, $"{_context.Store.StoreCode}-{date.Year}-{sequence:000000}");
    }

    private double Draw((int DayNumber, int Basket, int Item) at, int k) => _return.Uniform(at.DayNumber, at.Basket, at.Item, k);
}
