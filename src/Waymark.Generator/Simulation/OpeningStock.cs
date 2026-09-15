using Waymark.Domain.Enums;
using Waymark.Domain.Ids;
using Waymark.Domain.Inventory;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Configuration;
using Waymark.Generator.Randomness;
using Waymark.Generator.Writing;

namespace Waymark.Generator.Simulation;

/// <summary>
/// The shelf on the morning of the first trading day, as day-0 receipts.
///
/// <para>
/// One batch per product (<c>batches.product_id</c>), with an item and a <c>receipt</c>
/// movement per variant. The quantity is base demand times a drawn number of days of cover,
/// rounded up to whole cartons — how a shopkeeper stocks a shelf — so a slow mover can start
/// with far more than it will sell before it expires. That spoilage is real, and later steps
/// are meant to see it.
/// </para>
/// <para>
/// The batch arrived before the history started, so it carries only part of its shelf life,
/// drawn per batch, and cover is capped at the days it has left: nobody stocks three weeks of
/// a ten-day lben. Rounding up to a whole carton can still overshoot, which is the point.
/// Non-perishables have no expiry.
/// </para>
/// </summary>
internal static class OpeningStock
{
    /// <summary>The note on every opening receipt, so they are distinguishable from supplier deliveries.</summary>
    public const string Note = "Stock d'ouverture (synthétique)";

    /// <summary>The supplier document reference on opening batches.</summary>
    public const string DocumentReference = "OUVERTURE";

    private const int ReceiptMinute = 6 * 60;

    public static StoreState Write(
        StoreDatabase database,
        GeneratedStore store,
        OpeningStockSettings settings,
        RunWindow window,
        CalendarModel calendar,
        SimulatedClock clock,
        IIdGenerator ids,
        RandomSource random)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentNullException.ThrowIfNull(ids);
        ArgumentNullException.ThrowIfNull(random);

        var cover = random.Stream("opening_cover");
        var shelfLife = random.Stream("opening_shelf_life");
        var state = new StoreState(store.Variants.Count);
        var context = database.Context;
        var day = window.FirstDay;
        var at = calendar.ToUtc(day, ReceiptMinute);
        clock.AdvanceTo(at);

        var products = store.Variants
            .GroupBy(variant => variant.ProductId, StringComparer.Ordinal)
            .Select((variants, index) => (Index: index, Variants: variants.ToList()));

        foreach (var (productIndex, variants) in products)
        {
            var perishable = variants.Select(v => v.Catalogue.ShelfLifeDays).Where(days => days is not null).Min();
            int? remainingDays = perishable is { } days
                ? Math.Max(1, (int)Math.Floor(days * Distributions.Uniform(
                    shelfLife.Uniform(productIndex), settings.RemainingShelfLife.Value.Min, settings.RemainingShelfLife.Value.Max)))
                : null;
            DateOnly? expires = remainingDays is { } remaining ? day.AddDays(remaining) : null;

            var batchId = ids.NewId();
            context.Batches.Add(new Batch
            {
                BatchId = batchId,
                ProductId = variants[0].ProductId,
                StoreId = store.StoreId,
                SupplierId = variants[0].SupplierId,
                SupplierDocumentRef = DocumentReference,
                ReceivedDate = day,
                ExpirationDate = expires,
                ReceivedBy = store.Manager.StaffId,
                CreatedAt = at,
            });

            foreach (var variant in variants)
            {
                var pack = variant.Catalogue.UnitsPerPurchaseUnit;
                var coverDays = Math.Min(
                    Distributions.Uniform(cover.Uniform(variant.Index), settings.CoverDays.Value.Min, settings.CoverDays.Value.Max),
                    remainingDays ?? double.MaxValue);
                var cartons = Math.Max(1, (long)Math.Ceiling(variant.Catalogue.BaseDailyRate * coverDays / pack));
                var received = variant.SellingUnit.Whole(checked(cartons * pack));
                var change = QuantityDelta.Increase(received);
                var cost = variant.PriceOn(day).Purchase;

                context.BatchItems.Add(new BatchItem
                {
                    BatchId = batchId,
                    VariantId = variant.VariantId,
                    QuantityReceived = received.Thousandths,
                    UnitCode = received.Unit!,
                    UnitCost = cost,
                    Currency = store.Currency.Code,
                    CreatedAt = at,
                });

                context.StockMovements.Add(new StockMovement
                {
                    MovementId = ids.NewId(),
                    StoreId = store.StoreId,
                    VariantId = variant.VariantId,
                    BatchId = batchId,
                    MovementDate = day,
                    MovementType = StockMovementType.Receipt,
                    QuantityChanged = change.Thousandths,
                    UnitCode = change.Unit!,
                    UnitCost = cost,
                    ReferenceType = "manual",
                    ReferenceId = batchId,
                    StaffId = store.Manager.StaffId,
                    Note = Note,
                    CreatedAt = at,
                });

                state.Receive(variant.Index, new StockLot(batchId, day, expires, cost, Quantity.Zero(received.Unit!) + change));
            }
        }

        database.Save();
        return state;
    }
}
