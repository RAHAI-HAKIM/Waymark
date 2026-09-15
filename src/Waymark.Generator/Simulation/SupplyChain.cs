using Waymark.Domain.Enums;
using Waymark.Domain.Inventory;
using Waymark.Domain.Purchasing;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Randomness;

namespace Waymark.Generator.Simulation;

/// <summary>An order placed with a supplier and not yet delivered.</summary>
internal sealed record PendingOrder(
    int Number,
    string OrderId,
    GeneratedSupplier Supplier,
    DateOnly OrderDate,
    DateTimeOffset OrderedAt,
    string CreatedBy,
    DateOnly ArrivalDate,
    IReadOnlyList<OrderLine> Lines);

/// <summary>Whole cartons of a variant at the carton price agreed on the order date.</summary>
internal sealed record OrderLine(GeneratedVariant Variant, int Cartons, Money CartonCost);

/// <summary>
/// Restocking (W10 S6, D-046 §20–23): supplier visits, orders, late and short deliveries,
/// receipts into batches, and expired stock written off.
///
/// <para>
/// A supplier's representative visits on that supplier's delivery days and the shopkeeper
/// orders then, by <see cref="NaiveShopkeeperPolicy"/>. The delivery comes after the stated
/// lead time plus a drawn delay (<c>lead(supplier, order)</c>), on the first open day from
/// then, in the morning. Each line arrives in full with the fill rate, or short; the shortfall
/// is never delivered later. A delivery becomes one batch per product, as the schema has one
/// expiry per batch.
/// </para>
/// <para>
/// A purchase order changes state, so it is written once final (D-054): when it is received,
/// or at the end of the run if it is still on its way. Its id is minted when it was placed.
/// </para>
/// </summary>
internal sealed class SupplyChain
{
    private readonly SimulationContext _context;
    private readonly NaiveShopkeeperPolicy _policy;
    private readonly List<PendingOrder> _pending = [];
    private readonly long[] _onOrderUnits;
    private readonly IReadOnlyList<GeneratedVariant>[] _bySupplier;
    private readonly RandomStream _lead;
    private readonly RandomStream _fill;
    private readonly RandomStream _shelfLife;
    private readonly RandomStream _deliveryTime;
    private int _orders;

    public SupplyChain(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _policy = new NaiveShopkeeperPolicy(context.Config.Supply);
        _onOrderUnits = new long[context.Store.Variants.Count];
        _bySupplier = [.. context.Store.Suppliers.Select(supplier =>
            (IReadOnlyList<GeneratedVariant>)[.. context.Store.Variants.Where(v => v.SupplierId == supplier.SupplierId)])];
        _lead = context.Random.Stream("lead");
        _fill = context.Random.Stream("fill");
        _shelfLife = context.Random.Stream("delivered_shelf_life");
        _deliveryTime = context.Random.Stream("delivery_time");
    }

    /// <summary>Units of a variant ordered and not yet delivered.</summary>
    public long OnOrderUnits(int variantIndex) => _onOrderUnits[variantIndex];

    /// <summary>The suppliers whose representative visits today, and when: on their delivery days, mid-morning.</summary>
    public IEnumerable<(int Second, GeneratedSupplier Supplier)> Visits(DayContext day, IReadOnlyList<OpeningInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(intervals);

        var first = intervals[0];
        var second = (first.StartMinute * 60) + Math.Min(_context.Config.Supply.VisitMinutesAfterOpening.Value * 60, (first.EndMinute - first.StartMinute) * 30);
        return _context.Store.Suppliers
            .Where(supplier => supplier.DeliveryDays.Contains(day.Date.DayOfWeek))
            .Select(supplier => (second, supplier));
    }

    /// <summary>
    /// Orders due today or earlier, and when each arrives. A delivery due on a closed day comes
    /// on the next open one, since there is nobody to receive it.
    /// </summary>
    public IReadOnlyList<(int Second, PendingOrder Order)> Deliveries(DayContext day, IReadOnlyList<OpeningInterval> intervals)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(intervals);

        var first = intervals[0];
        var window = Math.Min(_context.Config.Supply.DeliveryWindowMinutes.Value * 60, (first.EndMinute - first.StartMinute) * 60);
        return [.. _pending
            .Where(order => order.ArrivalDate <= day.Date)
            .Select(order => ((first.StartMinute * 60) + Distributions.UniformInt(_deliveryTime.Uniform(order.Number), 0, window - 1), order))];
    }

    /// <summary>The representative's visit: the shopkeeper looks along the supplier's shelf and orders.</summary>
    public void Visit(DayContext day, GeneratedSupplier supplier, GeneratedStaff staff)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(supplier);
        ArgumentNullException.ThrowIfNull(staff);

        var date = day.Date;
        var settings = _context.Config.Supply;
        var ramadanComing = RamadanComing(_context.Calendar, date, settings.RamadanLookaheadDays.Value);
        var lines = new List<OrderLine>();

        foreach (var variant in _bySupplier[supplier.Index])
        {
            var pack = variant.Catalogue.UnitsPerPurchaseUnit;
            var perceived = _policy.PerceivedRate(_context.State.RecentSales(variant.Index, settings.MemoryDays.Value), variant.Catalogue.BaseDailyRate);
            var position = (_context.State.Sellable(variant.Index, date) / Quantity.Scale) + _onOrderUnits[variant.Index];
            var cartons = _policy.CartonsToOrder(perceived, position, pack, ramadanComing);

            if (cartons > 0)
            {
                lines.Add(new OrderLine(variant, cartons, variant.PriceOn(date).Purchase * pack));
            }
        }

        if (lines.Count == 0)
        {
            return;
        }

        var late = settings.LateDeliveryMaxDays.Value;
        var delay = Math.Min(late, (int)Math.Floor(Distributions.Triangular(_lead.Uniform(supplier.Index, date.DayNumber), 0, 0, late + 1)));
        var arrival = date.AddDays(Math.Max(1, supplier.StatedLeadTimeDays + delay));

        _pending.Add(new PendingOrder(_orders++, _context.Ids.NewId(), supplier, date, _context.Clock.GetUtcNow(), staff.StaffId, arrival, lines));
        foreach (var line in lines)
        {
            _onOrderUnits[line.Variant.Index] += (long)line.Cartons * line.Variant.Catalogue.UnitsPerPurchaseUnit;
        }
    }

    /// <summary>A delivery: the order as received, a batch per product, receipt movements and new lots.</summary>
    public void Receive(PendingOrder order, DateOnly date, GeneratedStaff staff)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(staff);

        var settings = _context.Config.Supply;
        var db = _context.Database.Context;
        var store = _context.Store;
        var now = _context.Clock.GetUtcNow();
        _pending.Remove(order);

        var received = order.Lines.Select(line =>
        {
            if (Distributions.Bernoulli(_fill.Uniform(order.Number, line.Variant.Index, 0), settings.FillRate.Value))
            {
                return line.Cartons;
            }

            var fraction = Distributions.Uniform(_fill.Uniform(order.Number, line.Variant.Index, 1), settings.ShortDeliveryFraction.Value.Min, settings.ShortDeliveryFraction.Value.Max);
            return Math.Min(line.Cartons - 1, (int)Math.Floor(line.Cartons * fraction));
        }).ToList();

        var status = received.Zip(order.Lines).All(pair => pair.First == pair.Second.Cartons) ? PurchaseOrderStatus.Received
            : received.Any(cartons => cartons > 0) ? PurchaseOrderStatus.PartiallyReceived
            : PurchaseOrderStatus.Cancelled;

        WriteOrder(order, status, received, status == PurchaseOrderStatus.Cancelled ? null : now, now);

        var products = order.Lines
            .Select((line, index) => (Line: line, Cartons: received[index]))
            .Where(pair => pair.Cartons > 0)
            .GroupBy(pair => pair.Line.Variant.ProductId, StringComparer.Ordinal)
            .Select((group, index) => (Index: index, Lines: group.ToList()));

        foreach (var (productIndex, lines) in products)
        {
            var shelf = lines.Select(pair => pair.Line.Variant.Catalogue.ShelfLifeDays).Where(days => days is not null).Min();
            DateOnly? expires = shelf is { } days
                ? date.AddDays(Math.Max(1, (int)Math.Floor(days * Distributions.Uniform(
                    _shelfLife.Uniform(order.Number, productIndex), settings.DeliveredShelfLife.Value.Min, settings.DeliveredShelfLife.Value.Max))))
                : null;

            var batchId = _context.Ids.NewId();
            db.Batches.Add(new Batch
            {
                BatchId = batchId,
                ProductId = lines[0].Line.Variant.ProductId,
                StoreId = store.StoreId,
                SupplierId = order.Supplier.SupplierId,
                OrderId = order.OrderId,
                ReceivedDate = date,
                ExpirationDate = expires,
                ReceivedBy = staff.StaffId,
                CreatedAt = now,
            });

            foreach (var (line, cartons) in lines)
            {
                var variant = line.Variant;
                var quantity = variant.SellingUnit.Whole((long)cartons * variant.Catalogue.UnitsPerPurchaseUnit);
                var change = QuantityDelta.Increase(quantity);
                var unitCost = variant.PriceOn(order.OrderDate).Purchase;

                db.BatchItems.Add(new BatchItem
                {
                    BatchId = batchId,
                    VariantId = variant.VariantId,
                    QuantityReceived = quantity.Thousandths,
                    UnitCode = quantity.Unit!,
                    UnitCost = unitCost,
                    Currency = store.Currency.Code,
                    CreatedAt = now,
                });

                db.StockMovements.Add(new StockMovement
                {
                    MovementId = _context.Ids.NewId(),
                    StoreId = store.StoreId,
                    VariantId = variant.VariantId,
                    BatchId = batchId,
                    MovementDate = date,
                    MovementType = StockMovementType.Receipt,
                    QuantityChanged = change.Thousandths,
                    UnitCode = change.Unit!,
                    UnitCost = unitCost,
                    ReferenceType = "purchase_order",
                    ReferenceId = order.OrderId,
                    StaffId = staff.StaffId,
                    CreatedAt = now,
                });

                _context.State.Receive(variant.Index, new StockLot(batchId, date, expires, unitCost, Quantity.Zero(quantity.Unit!) + change));
            }
        }

        foreach (var line in order.Lines)
        {
            _onOrderUnits[line.Variant.Index] -= (long)line.Cartons * line.Variant.Catalogue.UnitsPerPurchaseUnit;
        }
    }

    /// <summary>
    /// Before opening, everything past its expiration date comes off the shelf as an
    /// <c>expiry</c> movement. The date is the last day of sale, so a lot goes the morning after.
    /// </summary>
    public void WriteOffExpired(DateOnly date, GeneratedStaff staff)
    {
        ArgumentNullException.ThrowIfNull(staff);

        var now = _context.Clock.GetUtcNow();
        foreach (var (variantIndex, lot) in _context.State.AllLots())
        {
            if (lot.Expires is not { } expires || expires >= date || !lot.OnHand.IsPositive)
            {
                continue;
            }

            var change = QuantityDelta.Decrease(lot.OnHand);
            lot.Apply(change);

            _context.Database.Context.StockMovements.Add(new StockMovement
            {
                MovementId = _context.Ids.NewId(),
                StoreId = _context.Store.StoreId,
                VariantId = _context.Store.Variants[variantIndex].VariantId,
                BatchId = lot.BatchId,
                MovementDate = date,
                MovementType = StockMovementType.Expiry,
                QuantityChanged = change.Thousandths,
                UnitCode = change.Unit!,
                UnitCost = lot.UnitCost,
                StaffId = staff.StaffId,
                CreatedAt = now,
            });
        }
    }

    /// <summary>At the end of the run: orders still on their way, as sent.</summary>
    public void WritePending()
    {
        foreach (var order in _pending)
        {
            WriteOrder(order, PurchaseOrderStatus.Sent, [.. order.Lines.Select(_ => 0)], null, order.OrderedAt);
        }

        _pending.Clear();
    }

    private void WriteOrder(PendingOrder order, PurchaseOrderStatus status, List<int> receivedCartons, DateTimeOffset? receivedAt, DateTimeOffset updatedAt)
    {
        var db = _context.Database.Context;
        var currency = _context.Store.Currency;
        var total = Money.Zero(currency);

        for (var i = 0; i < order.Lines.Count; i++)
        {
            var line = order.Lines[i];
            var lineTotal = line.CartonCost * line.Cartons;
            total += lineTotal;

            db.PurchaseOrderItems.Add(new PurchaseOrderItem
            {
                OrderId = order.OrderId,
                VariantId = line.Variant.VariantId,
                QuantityOrdered = (long)line.Cartons * Quantity.Scale,
                QuantityReceived = (long)receivedCartons[i] * Quantity.Scale,
                PurchaseUnitCode = line.Variant.Catalogue.PurchaseUnit,
                UnitsPerPurchaseUnit = (long)line.Variant.Catalogue.UnitsPerPurchaseUnit * Quantity.Scale,
                UnitCost = line.CartonCost,
                Discount = Money.Zero(currency),
                LineTotal = lineTotal,
            });
        }

        db.PurchaseOrders.Add(new PurchaseOrder
        {
            OrderId = order.OrderId,
            StoreId = _context.Store.StoreId,
            SupplierId = order.Supplier.SupplierId,
            OrderDate = order.OrderDate,
            ExpectedArrivalDate = order.OrderDate.AddDays(Math.Max(1, order.Supplier.StatedLeadTimeDays)),
            ReceivedAt = receivedAt,
            TotalAmount = total,
            Currency = currency.Code,
            Source = PurchaseOrderSource.Manual,
            CreatedBy = order.CreatedBy,
            Status = status,
            CreatedAt = order.OrderedAt,
            UpdatedAt = updatedAt,
        });
    }

    /// <summary>
    /// Whether the shopkeeper is stocking up for Ramadan: during the stock-up week, or when it
    /// begins within the look-ahead. Not during Ramadan or Aïd, when he orders by what he sees.
    /// </summary>
    internal static bool RamadanComing(CalendarModel calendar, DateOnly date, int lookaheadDays)
    {
        ArgumentNullException.ThrowIfNull(calendar);

        var today = calendar.Day(date).RamadanPhase;
        if (today == RamadanPhase.StockUp)
        {
            return true;
        }

        if (today is RamadanPhase.Ramadan or RamadanPhase.Aid)
        {
            return false;
        }

        return Enumerable.Range(1, lookaheadDays)
            .Any(ahead => calendar.Day(date.AddDays(ahead)).RamadanPhase is RamadanPhase.StockUp or RamadanPhase.Ramadan);
    }
}
