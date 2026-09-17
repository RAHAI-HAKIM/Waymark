using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// The untidiness of a real shop (W10 S7): discounts, voids, returns, store credit, on-account
/// sales, cash movements and stock counts, each held to the rule that makes it auditable.
/// The mini fixture runs them at high rates, so sixty days of a small shop have plenty.
/// </summary>
public sealed class MessTests(MiniSalesRun mini, GrocerySalesRun grocery) : IClassFixture<MiniSalesRun>, IClassFixture<GrocerySalesRun>
{
    private GeneratedStoreFixture Store(string name) => name == "mini" ? mini : grocery;

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_discount_has_a_discount_reason_and_a_manager_when_the_reason_needs_one(string store)
    {
        using var db = Store(store).Open();

        Assert.True(Scalar(db, "SELECT count(*) FROM transaction_items WHERE discount_amount > 0") > 0);
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transaction_items i
            LEFT JOIN reason_codes r ON r.reason_code = i.discount_reason_code
            WHERE i.discount_amount > 0
              AND (r.reason_code IS NULL OR r.applies_to <> 'discount'
                   OR (r.requires_manager = 1 AND i.authorised_by IS DISTINCT FROM (SELECT manager_id FROM stores)))
            """));
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM transaction_items WHERE discount_amount = 0 AND discount_reason_code IS NOT NULL"));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void A_void_is_a_wrong_ringing_with_a_reason_that_moves_nothing_and_is_rung_again_at_once(string store)
    {
        using var db = Store(store).Open();

        Assert.True(Scalar(db, "SELECT count(*) FROM transactions WHERE status = 'voided'") > 0);
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transactions t LEFT JOIN reason_codes r ON r.reason_code = t.void_reason_code
            WHERE t.status = 'voided'
              AND (r.applies_to IS NOT 'void' OR t.voided_at <> t.occurred_at OR t.voided_by IS NULL OR t.invoice_number IS NOT NULL
                   OR EXISTS (SELECT 1 FROM transaction_payments p WHERE p.transaction_id = t.transaction_id)
                   OR EXISTS (SELECT 1 FROM stock_movements m WHERE m.reference_id = t.transaction_id)
                   OR EXISTS (SELECT 1 FROM transaction_items i WHERE i.transaction_id = t.transaction_id AND i.batch_id IS NOT NULL))
            """));

        // The correct ringing follows in the same second, by the same cashier, one unit fewer.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transactions v
            WHERE v.status = 'voided' AND NOT EXISTS (
                SELECT 1 FROM transactions s
                WHERE s.status <> 'voided' AND s.original_transaction_id IS NULL
                  AND s.occurred_at = v.occurred_at AND s.staff_id = v.staff_id AND s.transaction_id > v.transaction_id
                  AND (SELECT sum(quantity) FROM transaction_items WHERE transaction_id = s.transaction_id)
                    = (SELECT sum(quantity) FROM transaction_items WHERE transaction_id = v.transaction_id) - 1000)
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_refund_links_its_original_sale_and_line_and_restocks_only_what_it_says(string store)
    {
        using var db = Store(store).Open();

        Assert.True(Scalar(db, "SELECT count(*) FROM returns") > 0);

        // Each return names its refund line (F-17), and each refund line belongs to one return.
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM returns WHERE refund_transaction_item_id IS NULL"));
        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM (SELECT 1 FROM returns GROUP BY refund_transaction_item_id HAVING count(*) > 1)"));

        // One refund transaction per return: a single negative line, same variant and batch as
        // the original line, refunding exactly its line total, paid out by the stated method, at
        // the moment and by the cashier the return records.
        Assert.Equal(Scalar(db, "SELECT count(*) FROM returns"), Scalar(db, "SELECT count(*) FROM transactions WHERE original_transaction_id IS NOT NULL"));
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transactions rt
            JOIN transaction_items ri ON ri.transaction_id = rt.transaction_id
            JOIN returns r ON r.refund_transaction_item_id = ri.transaction_item_id
            JOIN transaction_items oi ON oi.transaction_item_id = r.transaction_item_id
            JOIN transactions ot ON ot.transaction_id = oi.transaction_id
            JOIN transaction_payments p ON p.transaction_id = rt.transaction_id
            JOIN reason_codes rc ON rc.reason_code = r.reason_code
            WHERE rt.original_transaction_id IS NOT NULL
              AND (ot.transaction_id <> rt.original_transaction_id
                   OR r.created_at <> rt.occurred_at OR r.staff_id <> rt.staff_id OR r.batch_id IS NOT ri.batch_id
                   OR (SELECT count(*) FROM transaction_items x WHERE x.transaction_id = rt.transaction_id) <> 1
                   OR ri.variant_id <> oi.variant_id OR ri.quantity <> -r.quantity_returned OR ri.line_total <> -r.refund_amount
                   OR p.amount <> ri.line_total OR p.payment_method <> r.refund_method
                   OR ot.status NOT IN ('refunded', 'partially_refunded') OR rc.applies_to <> 'return'
                   OR r.quantity_returned > oi.quantity OR ot.occurred_at >= rt.occurred_at
                   OR (r.quantity_returned = oi.quantity AND ri.line_total <> -oi.line_total))
            """));

        // A whole line comes back at what was paid for it, discount and all; a discounted line only ever comes back whole.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM returns r JOIN transaction_items oi ON oi.transaction_item_id = r.transaction_item_id
            WHERE oi.discount_amount > 0 AND r.quantity_returned <> oi.quantity
            """));
        Assert.Equal(
            Scalar(db, "SELECT count(*) FROM returns"),
            Scalar(db, """
                SELECT count(*) FROM transactions rt JOIN transaction_items ri ON ri.transaction_id = rt.transaction_id
                JOIN returns r ON r.refund_transaction_item_id = ri.transaction_item_id
                JOIN transaction_items oi ON oi.transaction_item_id = r.transaction_item_id AND oi.transaction_id = rt.original_transaction_id
                WHERE rt.original_transaction_id IS NOT NULL
                """));

        // Restocked means a return_in movement of exactly the returned quantity, and nothing otherwise.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM returns r
            WHERE (r.restock_flag = 1) <> EXISTS (SELECT 1 FROM stock_movements m WHERE m.movement_type = 'return_in' AND m.reference_type = 'return'
                                                  AND m.reference_id = r.return_id AND m.quantity_changed = r.quantity_returned AND m.batch_id = r.batch_id)
            """));

        // An original is refunded when every unit of every line came back, partially refunded otherwise.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transactions t
            WHERE t.status IN ('refunded', 'partially_refunded')
              AND (t.status = 'refunded') <> NOT EXISTS (
                  SELECT 1 FROM transaction_items i WHERE i.transaction_id = t.transaction_id
                    AND i.quantity > coalesce((SELECT sum(quantity_returned) FROM returns r WHERE r.transaction_item_id = i.transaction_item_id), 0))
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Store_credit_is_a_ledger_whose_balance_is_never_negative_and_chains_exactly(string store)
    {
        using var db = Store(store).Open();

        var movements = Rows(db, "SELECT customer_id, movement_type, amount, balance_after, return_id, transaction_id FROM credit_movements ORDER BY customer_id, occurred_at, movement_id");
        Assert.NotEmpty(movements);

        var balances = new Dictionary<string, long>(StringComparer.Ordinal);
        foreach (var row in movements)
        {
            var customer = (string)row[0];
            var balance = balances.GetValueOrDefault(customer) + Long(row[2]);
            Assert.Equal(balance, Long(row[3]));
            Assert.True(balance >= 0, $"Customer {customer} held a negative store credit balance.");
            Assert.True((string)row[1] == "issue" ? Long(row[2]) > 0 && row[4] is string : Long(row[2]) < 0 && row[4] is DBNull);
            Assert.IsType<string>(row[5]);
            balances[customer] = balance;
        }

        // Each redemption is the store-credit payment of its sale; each issue, the refund of its return.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM credit_movements c
            WHERE (c.movement_type = 'redeem' AND NOT EXISTS (SELECT 1 FROM transaction_payments p WHERE p.transaction_id = c.transaction_id AND p.payment_method = 'store_credit' AND p.amount = -c.amount))
               OR (c.movement_type = 'issue' AND NOT EXISTS (SELECT 1 FROM returns r WHERE r.return_id = c.return_id AND r.refund_method = 'store_credit' AND r.refund_amount = c.amount))
            """));
        Assert.Equal(
            Scalar(db, "SELECT count(*) FROM transaction_payments WHERE payment_method = 'store_credit'"),
            Scalar(db, "SELECT count(*) FROM credit_movements"));

        // customers.credit is the ledger's cache. The mini store never spends credit, so some is
        // still held at the end (with every balance at zero, a cache never written would pass);
        // the grocery store spends it, so redemption is exercised.
        if (store == "mini")
        {
            Assert.Contains(balances.Values, balance => balance > 0);
        }
        else
        {
            Assert.Contains(movements, row => (string)row[1] == "redeem");
        }

        var cached = Rows(db, "SELECT customer_id, credit FROM customers").ToDictionary(row => (string)row[0], row => Long(row[1]), StringComparer.Ordinal);
        Assert.All(cached, entry => Assert.Equal(balances.GetValueOrDefault(entry.Key), entry.Value));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Only_an_enrolled_customer_buys_on_account_or_with_store_credit(string store)
    {
        using var db = Store(store).Open();

        Assert.True(Scalar(db, "SELECT count(*) FROM transaction_payments WHERE payment_method = 'on_account'") > 0);
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transaction_payments p JOIN transactions t USING (transaction_id)
            WHERE p.payment_method IN ('on_account', 'store_credit') AND t.customer_id IS NULL
            """));

        // On account needs a tab: a customer with no credit limit never buys that way (F-16).
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM transaction_payments p JOIN transactions t USING (transaction_id) JOIN customers c USING (customer_id)
            WHERE p.payment_method = 'on_account' AND c.credit_limit IS NULL
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_cash_movement_has_a_cash_movement_reason_and_a_note_when_the_reason_needs_one(string store)
    {
        using var db = Store(store).Open();

        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM cash_movements m JOIN reason_codes r USING (reason_code)
            WHERE r.applies_to <> 'cash_movement' OR (r.requires_note = 1 AND m.note IS NULL)
               OR (r.requires_manager = 1 AND m.authorised_by IS NULL) OR m.amount <= 0
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void A_count_posts_exactly_its_variances_as_movements(string store)
    {
        using var db = Store(store).Open();

        Assert.True(Scalar(db, "SELECT count(*) FROM stock_count_items WHERE variance_quantity <> 0") > 0, "No count ever found a variance.");
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM stock_count_items i
            WHERE i.variance_quantity <> i.counted_quantity - i.expected_quantity
               OR i.variance_value <> i.unit_cost * i.variance_quantity / 1000
               OR coalesce((SELECT sum(m.quantity_changed) FROM stock_movements m
                            WHERE m.movement_type = 'count' AND m.reference_type = 'stock_count' AND m.reference_id = i.count_id
                              AND m.variant_id = i.variant_id AND m.batch_id = i.batch_id), 0) <> i.variance_quantity
            """));
        Assert.Equal(
            Scalar(db, "SELECT count(*) FROM stock_count_items WHERE variance_quantity <> 0"),
            Scalar(db, "SELECT count(*) FROM stock_movements WHERE movement_type = 'count'"));
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM stock_counts c
            WHERE c.status <> 'posted' OR c.approved_by IS NULL OR c.scope_category_id IS NULL
               OR c.total_variance_value <> (SELECT sum(variance_value) FROM stock_count_items i WHERE i.count_id = c.count_id)
            """));
    }
}
