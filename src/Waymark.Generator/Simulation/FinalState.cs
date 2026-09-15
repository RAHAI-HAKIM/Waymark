using Microsoft.EntityFrameworkCore;
using Waymark.Generator.Calendar;
using Waymark.Generator.Writing;

namespace Waymark.Generator.Simulation;

/// <summary>
/// The rows that describe a state rather than an event, written once the run is over:
/// <list type="bullet">
/// <item><c>inventories</c>: one row per variant per batch still known to the store, including
/// batches drawn down to zero, so a batch's history never loses its level.</item>
/// <item><c>batches.status</c>: <c>written_off</c> for a batch emptied by an expiry write-off,
/// <c>depleted</c> for any other batch with nothing left.</item>
/// <item><c>customers.credit</c> and <c>last_order_date</c>: caches of the ledger and the sales,
/// as the schema describes them ("ledger is truth").</item>
/// </list>
/// </summary>
internal static class FinalState
{
    public static void Write(StoreDatabase database, GeneratedStore store, StoreState state, SimulatedClock clock, SaleWriter? sales)
    {
        ArgumentNullException.ThrowIfNull(database);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(state);
        ArgumentNullException.ThrowIfNull(clock);

        var at = clock.GetUtcNow();
        foreach (var (variantIndex, lot) in state.AllLots())
        {
            database.Context.Inventories.Add(new Domain.Inventory.Inventory
            {
                StoreId = store.StoreId,
                VariantId = store.Variants[variantIndex].VariantId,
                BatchId = lot.BatchId,
                Quantity = lot.OnHand.Thousandths,
                UpdatedAt = at,
            });
        }

        database.Save();

        database.Context.Database.ExecuteSqlRaw("""
            UPDATE batches SET status = CASE
                WHEN EXISTS (SELECT 1 FROM stock_movements m WHERE m.batch_id = batches.batch_id AND m.movement_type = 'expiry')
                THEN 'written_off' ELSE 'depleted' END
            WHERE NOT EXISTS (SELECT 1 FROM inventories i WHERE i.batch_id = batches.batch_id AND i.quantity <> 0)
            """);

        if (sales is null)
        {
            return;
        }

        foreach (var customer in store.Customers)
        {
            var credit = sales.CreditBalances.GetValueOrDefault(customer.CustomerId);
            var lastOrder = sales.LastOrderDates.TryGetValue(customer.CustomerId, out var date) ? date : (DateOnly?)null;
            if (credit == default && lastOrder is null)
            {
                continue;
            }

            database.Context.Customers
                .Where(c => c.CustomerId == customer.CustomerId)
                .ExecuteUpdate(setters => setters
                    .SetProperty(c => c.Credit, credit == default ? Domain.Values.Money.Zero(store.Currency) : credit)
                    .SetProperty(c => c.LastOrderDate, lastOrder)
                    .SetProperty(c => c.UpdatedAt, at));
        }
    }
}
