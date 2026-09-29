using Waymark.Domain;
using Waymark.Domain.Catalogue;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// Sales read back as the till showed them (session B1, D-088; ✍ Hakim's <see cref="PastTickets"/>).
/// The silent failures: another store's ticket opened by its id (DPIA R9), a line split across two
/// batches shown as two lines, a sale at midnight in two days' lists or in none, a total recomputed
/// instead of read, and an unfinished sale shown as if it were one.
/// </summary>
public sealed class PastTicketsTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>The day under test, in UTC: 25/09/2026 from midnight to midnight.</summary>
    private static readonly DateTimeOffset Day = new(2026, 9, 25, 0, 0, 0, TimeSpan.Zero);

    private static Money Dzd(long minorUnits) => Money.FromMinorUnits(minorUnits, Currency.Dzd);

    /// <summary>Two stores; this one with two tills, a cashier, milk and bread. Unique ids per test.</summary>
    private sealed class Shop
    {
        private readonly MigratedDatabaseFixture _database;
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];
        private int _sequence;

        public Shop(MigratedDatabaseFixture database)
        {
            _database = database;
            StoreId = $"store-{_suffix}";
            OtherStoreId = $"other-{_suffix}";
            TillOne = $"till1-{_suffix}";
            TillTwo = $"till2-{_suffix}";
            OtherTill = $"till-other-{_suffix}";
            StaffId = $"staff-{_suffix}";
            OtherStaffId = $"staff-other-{_suffix}";
            Unit = $"pc-{_suffix}";
            Milk = $"v-milk-{_suffix}";
            Bread = $"v-bread-{_suffix}";

            using var context = database.NewContext();
            if (!context.Roles.Any(role => role.RoleCode == "cashier"))
            {
                context.Roles.Add(new Domain.Engine.Role { RoleCode = "cashier", Rank = 1, LabelAr = "أمين الصندوق", LabelFr = "caissier", CreatedAt = Moment });
            }

            foreach (var store in new[] { StoreId, OtherStoreId })
            {
                context.Stores.Add(new Store
                {
                    StoreId = store, StoreCode = store[..Math.Min(store.Length, 12)], StoreName = store, StoreType = "grocery",
                    RoundingPolicy = Rounding.HalfUp, CreatedAt = Moment, UpdatedAt = Moment,
                });
            }

            context.Terminals.Add(new Terminal { TerminalId = TillOne, StoreId = StoreId, TerminalName = "Caisse 1", CreatedAt = Moment, UpdatedAt = Moment });
            context.Terminals.Add(new Terminal { TerminalId = TillTwo, StoreId = StoreId, TerminalName = "Caisse 2", CreatedAt = Moment, UpdatedAt = Moment });
            context.Terminals.Add(new Terminal { TerminalId = OtherTill, StoreId = OtherStoreId, TerminalName = "Caisse", CreatedAt = Moment, UpdatedAt = Moment });
            context.Staff.Add(NewStaff(StaffId, StoreId, "Nabil B."));
            context.Staff.Add(NewStaff(OtherStaffId, OtherStoreId, "Ailleurs"));
            context.UnitsOfMeasure.Add(new UnitOfMeasure { UnitCode = Unit, NameAr = "قطعة", NameFr = "pièce", Dimension = Dimension.Count, CreatedAt = Moment });
            // A voided sale needs its reason (ck_transactions_status_2).
            context.ReasonCodes.Add(new ReasonCode
            {
                ReasonCodeValue = VoidReason, AppliesTo = ReasonCodeAppliesTo.Void, LabelFr = "erreur", LabelAr = "خطأ", CreatedAt = Moment,
            });
            context.SaveChanges();

            AddProduct(context, Milk, "Lait UHT Candia", "Brique 1L");
            AddProduct(context, Bread, "Pain", "Baguette");
            context.SaveChanges();
        }

        public string StoreId { get; }
        public string OtherStoreId { get; }
        public string TillOne { get; }
        public string TillTwo { get; }
        public string OtherTill { get; }
        public string StaffId { get; }
        public string OtherStaffId { get; }
        public string Unit { get; }
        public string Milk { get; }
        public string Bread { get; }

        private string VoidReason => $"VOID-{_suffix}";

        public PastTickets Reader()
        {
            var context = _database.NewContext(storeId: StoreId);
            return new PastTickets(context);
        }

        /// <summary>
        /// A sale's rows as <c>CompleteSale</c> writes them: the transaction, one item per
        /// (variant, price, units) given, in that order, and the payments.
        /// </summary>
        public string Sale(
            DateTimeOffset at,
            (string VariantId, long Price, long Units)[] items,
            string? invoice = null,
            string? till = null,
            TransactionStatus status = TransactionStatus.Completed,
            bool otherStore = false,
            long? total = null,
            (PaymentMethod Method, long Amount)[]? payments = null)
        {
            var id = $"t-{_suffix}-{++_sequence:D3}";
            using var context = _database.NewContext();
            var sum = items.Sum(item => item.Price * item.Units);
            context.Transactions.Add(new Transaction
            {
                TransactionId = id,
                StoreId = otherStore ? OtherStoreId : StoreId,
                TerminalId = otherStore ? OtherTill : till ?? TillOne,
                StaffId = otherStore ? OtherStaffId : StaffId,
                // A completed sale always has its number (ck_transactions_status_3).
                InvoiceNumber = invoice ?? $"S-{id}",
                OccurredAt = at,
                RoundingPolicy = Rounding.HalfUp,
                Subtotal = Dzd(sum),
                TaxTotal = Dzd(sum / 10),
                TotalAmount = Dzd(total ?? sum),
                Status = status,
                VoidedAt = status == TransactionStatus.Voided ? at : null,
                VoidReasonCode = status == TransactionStatus.Voided ? VoidReason : null,
                CreatedAt = at,
                UpdatedAt = at,
            });

            var n = 0;
            foreach (var (variant, price, units) in items)
            {
                n++;
                context.TransactionItems.Add(new TransactionItem
                {
                    TransactionItemId = $"{id}-i{n:D2}",
                    TransactionId = id,
                    VariantId = variant,
                    Quantity = units * Quantity.Scale,
                    UnitCode = Unit,
                    SellPrice = Dzd(price),
                    TaxAmount = Dzd(price * units / 10),
                    LineTotal = Dzd(price * units),
                    QuantitySource = Waymark.Domain.Enums.QuantitySource.Count,
                    CreatedAt = at.AddMilliseconds(n),
                });
            }

            var sequence = 0;
            foreach (var (method, amount) in payments ?? [(PaymentMethod.Cash, total ?? sum)])
            {
                sequence++;
                context.TransactionPayments.Add(new TransactionPayment
                {
                    PaymentId = $"{id}-p{sequence}",
                    TransactionId = id,
                    Sequence = sequence,
                    PaymentMethod = method,
                    Amount = Dzd(amount),
                    CreatedAt = at,
                });
            }

            context.SaveChanges();
            return id;
        }

        private static Staff NewStaff(string id, string store, string name) => new()
        {
            StaffId = id, StoreId = store, StaffName = name, Role = "cashier", PinHash = "-",
            JoinDate = new DateOnly(2026, 1, 1), CreatedAt = Moment, UpdatedAt = Moment,
        };

        private void AddProduct(Persistence.WaymarkDbContext context, string variantId, string product, string variant)
        {
            context.Products.Add(new Product { ProductId = "p-" + variantId, ProductName = product, CreatedAt = Moment, UpdatedAt = Moment });
            context.Variants.Add(new Variant
            {
                VariantId = variantId, ProductId = "p-" + variantId, VariantName = variant, SellingUnitCode = Unit,
                CreatedAt = Moment, UpdatedAt = Moment,
            });
        }
    }

    // ================================================================ the list

    [Fact]
    public async Task A_tills_sales_of_the_day_are_listed_newest_first()
    {
        var shop = new Shop(database);
        var morning = shop.Sale(Day.AddHours(8), [(shop.Milk, 14_300, 1)]);
        var noon = shop.Sale(Day.AddHours(12), [(shop.Bread, 2_000, 2)]);

        var listed = await shop.Reader().ListAsync(shop.TillOne, Day, Day.AddDays(1));

        Assert.Equal([noon, morning], listed.Select(ticket => ticket.TransactionId));
        Assert.Equal((Day.AddHours(12), Dzd(4_000), shop.TillOne), (listed[0].OccurredAt, listed[0].Total, listed[0].TerminalId));
    }

    [Fact]
    public async Task One_till_or_every_till()
    {
        var shop = new Shop(database);
        var one = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)], till: shop.TillOne);
        var two = shop.Sale(Day.AddHours(10), [(shop.Milk, 14_300, 1)], till: shop.TillTwo);

        Assert.Equal([one], (await shop.Reader().ListAsync(shop.TillOne, Day, Day.AddDays(1))).Select(ticket => ticket.TransactionId));
        Assert.Equal([two, one], (await shop.Reader().ListAsync(null, Day, Day.AddDays(1))).Select(ticket => ticket.TransactionId));
    }

    [Fact]
    public async Task The_window_takes_its_start_and_leaves_its_end()
    {
        // A sale at exactly midnight belongs to the day it opens, never to the one it closes.
        var shop = new Shop(database);
        var atStart = shop.Sale(Day, [(shop.Milk, 14_300, 1)]);
        var atEnd = shop.Sale(Day.AddDays(1), [(shop.Milk, 14_300, 1)]);
        var before = shop.Sale(Day.AddSeconds(-1), [(shop.Milk, 14_300, 1)]);

        var listed = (await shop.Reader().ListAsync(shop.TillOne, Day, Day.AddDays(1))).Select(ticket => ticket.TransactionId);

        Assert.Equal([atStart], listed);
        Assert.Contains(atEnd, (await shop.Reader().ListAsync(shop.TillOne, Day.AddDays(1), Day.AddDays(2))).Select(ticket => ticket.TransactionId));
        Assert.DoesNotContain(before, listed);
    }

    [Fact]
    public async Task The_window_is_compared_as_instants_whatever_offset_it_is_given_in()
    {
        // The same day, asked for in Algiers' offset: 25/09 00:00+01:00 is 24/09 23:00 UTC.
        var shop = new Shop(database);
        var lateUtc = shop.Sale(new DateTimeOffset(2026, 9, 24, 23, 30, 0, TimeSpan.Zero), [(shop.Milk, 14_300, 1)]);
        var algiers = TimeSpan.FromHours(1);

        var listed = await shop.Reader().ListAsync(
            shop.TillOne, new DateTimeOffset(2026, 9, 25, 0, 0, 0, algiers), new DateTimeOffset(2026, 9, 26, 0, 0, 0, algiers));

        Assert.Equal([lateUtc], listed.Select(ticket => ticket.TransactionId));
    }

    [Theory]
    [InlineData(TransactionStatus.Open, false)]
    [InlineData(TransactionStatus.Parked, false)]
    [InlineData(TransactionStatus.Completed, true)]
    [InlineData(TransactionStatus.Voided, true)]
    [InlineData(TransactionStatus.Refunded, true)]
    [InlineData(TransactionStatus.PartiallyRefunded, true)]
    public async Task Only_a_finished_sale_is_a_ticket(TransactionStatus status, bool isTicket)
    {
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)], invoice: $"S-{Guid.NewGuid():N}"[..20], status: status);

        var listed = await shop.Reader().ListAsync(shop.TillOne, Day, Day.AddDays(1));
        var found = await shop.Reader().FindAsync(id);

        Assert.Equal(isTicket, listed.Any(ticket => ticket.TransactionId == id));
        Assert.Equal(isTicket, found is not null);
        Assert.Equal(isTicket ? status : null, found?.Status);
    }

    [Fact]
    public async Task Another_stores_sales_are_never_listed()
    {
        var shop = new Shop(database);
        shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)], otherStore: true);

        Assert.Empty(await shop.Reader().ListAsync(null, Day, Day.AddDays(1)));
        Assert.Empty(await shop.Reader().ListAsync(shop.OtherTill, Day, Day.AddDays(1)));
    }

    [Fact]
    public async Task The_line_count_is_the_lines_the_ticket_showed()
    {
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 2), (shop.Milk, 14_300, 1), (shop.Bread, 2_000, 1)]);

        var listed = Assert.Single(await shop.Reader().ListAsync(shop.TillOne, Day, Day.AddDays(1)));

        Assert.Equal((id, 2), (listed.TransactionId, listed.LineCount));
    }

    // ================================================================ one ticket

    [Fact]
    public async Task A_ticket_is_found_by_its_id_or_by_its_invoice_number()
    {
        var shop = new Shop(database);
        var invoice = $"S-2026-{Guid.NewGuid():N}"[..20];
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)], invoice: invoice);

        Assert.Equal(id, (await shop.Reader().FindAsync(id))?.TransactionId);
        Assert.Equal(id, (await shop.Reader().FindAsync(invoice))?.TransactionId);
        Assert.Null(await shop.Reader().FindAsync("no-such-ticket"));
    }

    [Fact]
    public async Task Another_stores_ticket_is_not_found_even_by_its_own_id()
    {
        // DPIA R9: the id is enough to find a row; the store filter is what refuses it.
        var shop = new Shop(database);
        var invoice = $"S-2026-{Guid.NewGuid():N}"[..20];
        var theirs = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)], invoice: invoice, otherStore: true);

        Assert.Null(await shop.Reader().FindAsync(theirs));
        Assert.Null(await shop.Reader().FindAsync(invoice));
    }

    [Fact]
    public async Task A_line_split_across_batches_is_one_line_again()
    {
        // D-070 writes a row per batch: two rows of milk at one price are the one line the cashier
        // scanned three times.
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 2), (shop.Bread, 2_000, 1), (shop.Milk, 14_300, 1)]);

        var ticket = await shop.Reader().FindAsync(id);

        Assert.Equal(2, ticket!.Lines.Count);
        var milk = ticket.Lines[0];
        Assert.Equal((shop.Milk, 3L), (milk.VariantId, milk.Quantity.Thousandths / Quantity.Scale));
        Assert.Equal((Dzd(14_300), Dzd(42_900), Dzd(4_290)), (milk.UnitPrice, milk.LineTotal, milk.TaxAmount));
        Assert.Equal(shop.Bread, ticket.Lines[1].VariantId);
    }

    [Fact]
    public async Task One_product_at_two_prices_is_two_lines()
    {
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1), (shop.Milk, 12_000, 1)]);

        var ticket = await shop.Reader().FindAsync(id);

        Assert.Equal([Dzd(14_300), Dzd(12_000)], ticket!.Lines.Select(line => line.UnitPrice));
    }

    [Fact]
    public async Task Lines_keep_the_order_the_sale_wrote_them()
    {
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Bread, 2_000, 1), (shop.Milk, 14_300, 1), (shop.Bread, 2_000, 1)]);

        var ticket = await shop.Reader().FindAsync(id);

        Assert.Equal([shop.Bread, shop.Milk], ticket!.Lines.Select(line => line.VariantId));
        Assert.Equal(2L, ticket.Lines[0].Quantity.Thousandths / Quantity.Scale);
    }

    [Fact]
    public async Task The_figures_are_the_rows_never_summed_again()
    {
        // A receipt reads its own row (D-053). A total that differs from its lines (a rounding, a
        // discount B4 adds) must come back as recorded.
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)], total: 14_000);

        var ticket = await shop.Reader().FindAsync(id);

        Assert.Equal((Dzd(14_300), Dzd(1_430), Dzd(14_000)), (ticket!.Subtotal, ticket.TaxTotal, ticket.Total));
    }

    [Fact]
    public async Task Names_are_the_catalogues_and_the_seller_is_named()
    {
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)]);

        var ticket = await shop.Reader().FindAsync(id);

        Assert.Equal(("Lait UHT Candia", "Brique 1L"), (ticket!.Lines[0].ProductName, ticket.Lines[0].VariantName));
        Assert.Equal((shop.StaffId, "Nabil B."), (ticket.StaffId, ticket.StaffName));
        Assert.Equal((Day.AddHours(9), shop.TillOne), (ticket.OccurredAt, ticket.TerminalId));
    }

    [Fact]
    public async Task Payments_come_back_in_the_order_they_were_taken()
    {
        var shop = new Shop(database);
        var id = shop.Sale(Day.AddHours(9), [(shop.Milk, 14_300, 1)], payments: [(PaymentMethod.Card, 10_000), (PaymentMethod.Cash, 4_300)]);

        var ticket = await shop.Reader().FindAsync(id);

        Assert.Equal([new PastPayment(PaymentMethod.Card, Dzd(10_000)), new PastPayment(PaymentMethod.Cash, Dzd(4_300))], ticket!.Payments);
    }
}
