using System.Globalization;
using static Waymark.Generator.Tests.Sql;

namespace Waymark.Generator.Tests;

/// <summary>
/// The tab (le carnet, F-16): charges derived from on-account payments, refunds that go back on
/// the tab, repayments that reach the drawer, and write-offs of the change. The mini fixture runs
/// it hot, with low limits, so sixty days hold every case.
/// </summary>
public sealed class ReceivablesTests(MiniSalesRun mini, GrocerySalesRun grocery) : IClassFixture<MiniSalesRun>, IClassFixture<GrocerySalesRun>
{
    private GeneratedStoreFixture Store(string name) => name == "mini" ? mini : grocery;

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Every_on_account_payment_row_is_charged_once_at_its_own_amount(string store)
    {
        using var db = Store(store).Open();

        var onAccount = Scalar(db, "SELECT count(*) FROM transaction_payments WHERE payment_method = 'on_account'");
        Assert.True(onAccount > 0);
        Assert.Equal(onAccount, Scalar(db, "SELECT count(*) FROM receivable_movements WHERE movement_type = 'charge'"));

        // The charge is the payment row: same amount and sign, same customer, same moment. So the
        // ledger is derived from the transactions rather than kept beside them.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM receivable_movements m
            LEFT JOIN transaction_payments p ON p.payment_id = m.payment_id
            LEFT JOIN transactions t ON t.transaction_id = p.transaction_id
            WHERE m.movement_type = 'charge'
              AND (p.payment_method IS NOT 'on_account' OR p.amount <> m.amount OR t.customer_id IS NOT m.customer_id
                   OR t.occurred_at <> m.occurred_at OR t.status = 'voided' OR m.staff_id IS NOT t.staff_id)
            """));
        Assert.Equal(
            Scalar(db, "SELECT sum(amount) FROM transaction_payments WHERE payment_method = 'on_account'"),
            Scalar(db, "SELECT sum(amount) FROM receivable_movements WHERE movement_type = 'charge'"));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void A_tab_is_only_given_within_a_credit_limit_and_is_never_negative(string store)
    {
        using var db = Store(store).Open();

        // Limits are round numbers inside the configured range, and not every customer has one.
        var (min, max, step) = store == "mini" ? (2000L, 6000L, 500L) : (3000L, 15000L, 500L);
        Assert.Equal(0, Scalar(db, $"""
            SELECT count(*) FROM customers
            WHERE credit_limit IS NOT NULL
              AND (credit_limit < {min * 100} OR credit_limit > {max * 100} OR credit_limit % {step * 100} <> 0)
            """));
        Assert.True(Scalar(db, "SELECT count(*) FROM customers WHERE credit_limit IS NULL") > 0);
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM receivable_movements m JOIN customers c USING (customer_id) WHERE c.credit_limit IS NULL
            """));

        var limits = Rows(db, "SELECT customer_id, credit_limit FROM customers WHERE credit_limit IS NOT NULL")
            .ToDictionary(row => (string)row[0], row => Long(row[1]), StringComparer.Ordinal);
        var balances = new Dictionary<string, long>(StringComparer.Ordinal);
        var highest = new Dictionary<string, long>(StringComparer.Ordinal);

        foreach (var row in Rows(db, "SELECT customer_id, amount FROM receivable_movements ORDER BY customer_id, occurred_at, movement_id"))
        {
            var customer = (string)row[0];
            var balance = balances.GetValueOrDefault(customer) + Long(row[1]);
            Assert.True(balance >= 0, $"Customer {customer}'s tab went negative: the store owed them {-balance}.");
            Assert.True(balance <= limits[customer], $"Customer {customer}'s tab reached {balance}, beyond its limit of {limits[customer]}.");
            balances[customer] = balance;
            highest[customer] = Math.Max(highest.GetValueOrDefault(customer), balance);
        }

        if (store == "mini")
        {
            // Low limits: at least one tab runs up to within one basket of its limit, so the
            // limit is what stopped it, not the length of the run.
            Assert.Contains(highest, entry => entry.Value * 10 >= limits[entry.Key] * 9);
        }
    }

    [Fact]
    public void A_refund_of_an_on_account_sale_goes_back_on_the_tab_not_out_of_the_drawer()
    {
        using var db = mini.Open();

        Assert.True(Scalar(db, "SELECT count(*) FROM returns WHERE refund_method = 'on_account'") > 0, "No on-account sale was refunded to the tab.");

        // Its only payment row is on account, negative, mirrored by a negative charge; no cash
        // moves and no tender is rounded.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM returns r
            JOIN transaction_items ri ON ri.transaction_item_id = r.refund_transaction_item_id
            JOIN transactions rt ON rt.transaction_id = ri.transaction_id
            WHERE r.refund_method = 'on_account'
              AND ((SELECT count(*) FROM transaction_payments p WHERE p.transaction_id = rt.transaction_id) <> 1
                   OR NOT EXISTS (SELECT 1 FROM transaction_payments p JOIN receivable_movements m ON m.payment_id = p.payment_id
                                  WHERE p.transaction_id = rt.transaction_id AND p.payment_method = 'on_account'
                                    AND p.amount = -r.refund_amount AND m.movement_type = 'charge' AND m.amount = -r.refund_amount
                                    AND m.customer_id = rt.customer_id)
                   OR EXISTS (SELECT 1 FROM rounding_variance v WHERE v.reference_id = rt.transaction_id))
            """));

        // The original was paid on account first; and no sale paid that way is ever refunded in cash or to a card.
        Assert.Equal(0, Scalar(db, """
            SELECT count(*) FROM returns r
            JOIN transaction_items oi ON oi.transaction_item_id = r.transaction_item_id
            JOIN transaction_payments first ON first.transaction_id = oi.transaction_id AND first.sequence = 1
            WHERE (r.refund_method = 'on_account') AND first.payment_method <> 'on_account'
               OR (r.refund_method IN ('cash', 'card') AND first.payment_method = 'on_account')
            """));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void A_repayment_is_a_paid_in_on_an_open_drawer_in_whole_cash_steps(string store)
    {
        using var db = Store(store).Open();

        var repayments = Scalar(db, "SELECT count(*) FROM receivable_movements WHERE movement_type = 'payment'");
        Assert.True(repayments > 0);
        var code = store == "mini" ? "REGLEMENT" : "CAISSE-REGLEMENT";

        Assert.Equal(0, Scalar(db, $"""
            SELECT count(*) FROM receivable_movements m
            LEFT JOIN cash_movements c ON c.movement_id = m.cash_movement_id
            LEFT JOIN cash_sessions s ON s.session_id = c.session_id
            WHERE m.movement_type = 'payment'
              AND (c.movement_type IS NOT 'paid_in' OR c.amount <> -m.amount OR c.amount % 500 <> 0
                   OR c.reason_code <> '{code}' OR c.occurred_at <> m.occurred_at OR c.staff_id <> m.staff_id
                   OR c.occurred_at < s.opened_at OR c.occurred_at > s.closed_at)
            """));

        // Every paid-in under the repayment reason is a repayment: no cash arrives off the ledger.
        Assert.Equal(repayments, Scalar(db, $"SELECT count(*) FROM cash_movements WHERE reason_code = '{code}'"));
    }

    [Theory]
    [InlineData("mini")]
    [InlineData("grocery")]
    public void Settling_a_whole_tab_writes_off_only_the_change_under_one_cash_step(string store)
    {
        using var db = Store(store).Open();

        var writeOffs = Rows(db, """
            SELECT m.customer_id, m.occurred_at, m.amount, r.applies_to FROM receivable_movements m
            JOIN reason_codes r USING (reason_code) WHERE m.movement_type = 'write_off'
            """);
        Assert.NotEmpty(writeOffs);

        foreach (var row in writeOffs)
        {
            Assert.Equal("write_off", row[3]);
            Assert.InRange(Long(row[2]), -499, -1);

            // Nothing is owed once the change is let go.
            Assert.Equal(0, Scalar(db, $"""
                SELECT sum(amount) FROM receivable_movements
                WHERE customer_id = '{row[0]}' AND (occurred_at < '{row[1]}' OR (occurred_at = '{row[1]}' AND movement_type <> 'charge'))
                """));
        }

        Assert.Equal(0, Scalar(db, "SELECT count(*) FROM receivable_movements WHERE movement_type = 'adjustment'"));
    }

    [Fact]
    public void Tabs_are_settled_mostly_in_the_payday_spike()
    {
        using var db = grocery.Open();

        // Spike: the last two and first five days of the month (calendar.payday), about a quarter of the days.
        var days = Column(db, "SELECT date(occurred_at) FROM receivable_movements WHERE movement_type = 'payment'")
            .Select(text => DateOnly.ParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture))
            .ToList();
        var spike = days.Count(day => day.Day <= 5 || day.Day > DateTime.DaysInMonth(day.Year, day.Month) - 2);

        Assert.True(days.Count >= 10, $"Only {days.Count} repayments; too few to judge.");
        Assert.True(spike * 10 >= days.Count * 6, $"{spike} of {days.Count} repayments fell in the payday spike; most should.");
    }

    [Fact]
    public void Some_customers_never_settle_while_others_do()
    {
        using var db = mini.Open();

        var tabs = Rows(db, """
            SELECT customer_id, sum(movement_type = 'charge' AND amount > 0), sum(movement_type = 'payment'), sum(amount)
            FROM receivable_movements GROUP BY customer_id
            """);

        // Sixty days hold two payday spikes, when a settler repays with a high daily chance; a
        // tab charged repeatedly and never paid across both is one that will not be.
        Assert.Contains(tabs, row => Long(row[1]) >= 3 && Long(row[2]) == 0 && Long(row[3]) > 0);
        Assert.Contains(tabs, row => Long(row[2]) > 0);
    }
}
