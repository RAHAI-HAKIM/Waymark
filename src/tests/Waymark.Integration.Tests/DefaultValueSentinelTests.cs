using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Integration.Tests;

/// <summary>
/// A value the caller set explicitly must reach the database, even when it
/// happens to be the CLR default for its type.
///
/// <para>
/// EF omits a property from the INSERT when it equals the *sentinel* — the
/// value EF reads as "not set" — and lets the database default apply instead.
/// The sentinel defaults to the CLR default, which is silently wrong wherever
/// the database default is something else. Setting <c>IsActive = false</c> on a
/// column declared <c>DEFAULT 1</c> would be dropped and the row would come
/// back active; <c>LegalBasis = Consent</c> would be stored as
/// <c>'contract'</c>. Fifteen properties were exposed to this.
/// </para>
/// <para>
/// The fix is uniform: every <c>HasDefaultValue(x)</c> is paired with
/// <c>HasSentinel(x)</c>, so the value EF omits and the value the database
/// writes are the same thing. These tests are what says it holds, and they read
/// the raw column rather than trusting the round trip — EF reads back whatever
/// it wrote, so a swallowed value looks correct through the model.
/// </para>
/// </summary>
public sealed class DefaultValueSentinelTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _database;

    public DefaultValueSentinelTests(MigratedDatabaseFixture database) => _database = database;

    private string? RawValue(string sql)
    {
        using var connection = _database.Connect(enforceForeignKeys: false);
        using var command = connection.CreateCommand();
        command.CommandText = sql;
        return command.ExecuteScalar()?.ToString();
    }

    [Fact]
    public void False_reaches_a_column_that_defaults_to_true()
    {
        // reason_codes.is_active is INTEGER NOT NULL DEFAULT 1. Deactivating a
        // reason code is an ordinary administrative act, and before the
        // sentinel was set it would have silently done nothing.
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = "sentinel-inactive",
                AppliesTo = ReasonCodeAppliesTo.Discount,
                LabelAr = "غير نشط",
                LabelFr = "inactif",
                IsActive = false,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
            context.SaveChanges();
        }

        Assert.Equal(
            "0",
            RawValue("SELECT is_active FROM reason_codes WHERE reason_code = 'sentinel-inactive'"));
    }

    [Fact]
    public void True_still_reaches_a_column_that_defaults_to_true()
    {
        // The other half: making the sentinel equal to the database default is
        // only safe because omitting the value writes that same default.
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = "sentinel-active",
                AppliesTo = ReasonCodeAppliesTo.Discount,
                LabelAr = "نشط",
                LabelFr = "actif",
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
            context.SaveChanges();
        }

        Assert.Equal(
            "1",
            RawValue("SELECT is_active FROM reason_codes WHERE reason_code = 'sentinel-active'"));
    }

    [Fact]
    public void The_first_enum_member_reaches_a_column_that_defaults_to_another()
    {
        // customers.legal_basis is TEXT NOT NULL DEFAULT 'contract', and
        // LegalBasis.Consent is the CLR default because it is declared first.
        // This is the DPIA case: the lawful basis recorded against a customer
        // is not a field that may quietly become something else.
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.Customers.Add(new Customer
            {
                CustomerId = "sentinel-consent",
                CustomerName = "Consent Customer",
                JoinDate = new DateOnly(2026, 1, 1),
                LegalBasis = LegalBasis.Consent,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero),
                UpdatedAt = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero)
            });
            context.SaveChanges();
        }

        Assert.Equal(
            "consent",
            RawValue("SELECT legal_basis FROM customers WHERE customer_id = 'sentinel-consent'"));
    }

    [Fact]
    public void Zero_reaches_a_column_that_defaults_to_a_hundred()
    {
        // promotion_product.priority is INTEGER NOT NULL DEFAULT 100. Priority
        // zero is a legitimate value and means something different from 100.
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.PromotionProduct.Add(new Waymark.Domain.Pricing.PromotionProduct
            {
                PromotionId = "sentinel-priority",
                ProductId = "product-1",
                ValidFrom = "2026-01-01",
                ValueType = PromotionProductValueType.Percent,
                PromotionValue = 1000,
                Priority = 0,
                Status = PromotionProductStatus.Active
            });
            context.SaveChanges();
        }

        Assert.Equal(
            "0",
            RawValue("SELECT priority FROM promotion_product WHERE promotion_id = 'sentinel-priority'"));
    }

    [Fact]
    public void HalfEven_reaches_both_rounding_policy_columns_that_default_to_half_up()
    {
        // stores.rounding_policy and transactions.rounding_policy default to
        // 'half_up' (D-053), while HalfEven is the CLR default of Rounding. Without
        // the sentinel a banker's-rounding store would be stored as half_up, and
        // every receipt it stamped would recompute with the wrong policy.
        var moment = new DateTimeOffset(2026, 9, 14, 9, 0, 0, TimeSpan.Zero);

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.Stores.Add(new Store
            {
                StoreId = "sentinel-store-even",
                StoreCode = "SENTINEL-EVEN",
                StoreName = "sentinel",
                StoreType = "grocery",
                RoundingPolicy = Rounding.HalfEven,
                CreatedAt = moment,
                UpdatedAt = moment
            });
            context.Transactions.Add(new Transaction
            {
                TransactionId = "sentinel-tx-even",
                StoreId = "sentinel-store-even",
                TerminalId = "terminal-1",
                StaffId = "staff-1",
                OccurredAt = moment,
                RoundingPolicy = Rounding.HalfEven,
                Subtotal = Money.Zero(Currency.Dzd),
                DiscountTotal = Money.Zero(Currency.Dzd),
                TaxTotal = Money.Zero(Currency.Dzd),
                TotalAmount = Money.Zero(Currency.Dzd),
                CreatedAt = moment,
                UpdatedAt = moment
            });
            context.SaveChanges();
        }

        Assert.Equal(
            "half_even",
            RawValue("SELECT rounding_policy FROM stores WHERE store_id = 'sentinel-store-even'"));
        Assert.Equal(
            "half_even",
            RawValue("SELECT rounding_policy FROM transactions WHERE transaction_id = 'sentinel-tx-even'"));
    }
}
