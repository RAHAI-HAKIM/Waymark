using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.Engine;
using Waymark.Application.IdGenerator;
using Waymark.Domain;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Engine;
using Waymark.Persistence.Privacy;

namespace Waymark.Integration.Tests;

/// <summary>
/// The recommendation board and its decisions (hop 7, D-074). The risky rule: <b>the decision
/// row and the card's move to "decided" are written together, and only somebody senior enough
/// can cause either</b>. Half of that pair is the silent kind of wrong — a decision whose card
/// stayed pending is answered twice, and a card marked decided with no decision row is a
/// suggestion that vanished with nobody's name on it.
/// </summary>
public sealed class RecommendationBoardTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateTimeOffset Now = new(2026, 9, 20, 8, 0, 0, TimeSpan.Zero);

    private sealed class FixedClock : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => Now;
    }

    /// <summary>A store with a cashier, a manager, and one card on the board.</summary>
    private sealed class Shop
    {
        private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

        private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

        public Shop(MigratedDatabaseFixture database)
        {
            StoreId = $"store-{_suffix}";
            CashierId = $"cashier-{_suffix}";
            ManagerId = $"manager-{_suffix}";

            using var context = database.NewContext();

            if (!context.Roles.Any())
            {
                context.Roles.Add(new Role { RoleCode = "cashier", Rank = 1, LabelAr = "أمين الصندوق", LabelFr = "Caissier", CreatedAt = Moment });
                context.Roles.Add(new Role { RoleCode = "manager", Rank = 2, LabelAr = "المسؤول", LabelFr = "Responsable", CreatedAt = Moment });
            }

            context.Stores.Add(new Store
            {
                StoreId = StoreId,
                StoreCode = $"S{_suffix[..6]}",
                StoreName = StoreId,
                StoreType = "grocery",
                RoundingPolicy = Rounding.HalfUp,
                CreatedAt = Moment,
                UpdatedAt = Moment,
            });
            context.Staff.Add(Person(CashierId, "Caissier", "cashier"));
            context.Staff.Add(Person(ManagerId, "Responsable", "manager"));
            context.SaveChanges();

            Staff Person(string id, string name, string role) => new()
            {
                StaffId = id,
                StoreId = StoreId,
                StaffName = name,
                Role = role,
                PinHash = "-",
                JoinDate = new DateOnly(2026, 1, 1),
                CreatedAt = Moment,
                UpdatedAt = Moment,
            };
        }

        public string StoreId { get; }

        public string CashierId { get; }

        public string ManagerId { get; }

        /// <summary>Suspends a staff member, as a shop does when somebody stops working there.</summary>
        public void Suspend(MigratedDatabaseFixture database, string staffId)
        {
            using var context = database.NewContext(storeId: StoreId);
            var person = context.Staff.Single(member => member.StaffId == staffId);
            context.Entry(person).Property(member => member.Status).CurrentValue = StaffStatus.Suspended;
            context.SaveChanges();
        }

        /// <summary>A near-expiry card for a manager, with one option, as the evaluator writes them.</summary>
        public (string CardId, string OptionId) Card(
            MigratedDatabaseFixture database,
            string role = "manager",
            RecommendationStatus status = RecommendationStatus.Pending)
        {
            var cardId = $"card-{Guid.NewGuid():N}";
            var optionId = $"option-{Guid.NewGuid():N}";
            using var context = database.NewContext(storeId: StoreId);
            context.Recommendations.Add(new Recommendation
            {
                RecommendationId = cardId,
                StoreId = StoreId,
                Department = Department.Inventory,
                RecommendationType = NearExpiry.RecommendationType,
                Urgency = Urgency.Warning,
                ActionType = ActionType.Binary,
                SubjectType = RecommendationSubjectType.Batch,
                SubjectId = $"batch-{_suffix}",
                Headline = "Lait UHT Candia : péremption dans 3 jours, 12 en stock",
                BecauseJson = """{"key":"near_expiry","params":{},"factors":[{"label_key":"days_to_expiry","value":"3","unit":"days","direction":"supports"}]}""",
                ComputedAt = Moment,
                ParameterVersion = 1,
                Source = "store_rule:near_expiry:v1",
                MinimumRequiredRole = role,
                Status = status,
                IssuedAt = Moment,
            });
            context.RecommendationOptions.Add(new RecommendationOption
            {
                OptionId = optionId,
                RecommendationId = cardId,
                Label = "Appliquer une démarque",
                DisplayOrder = 1,
                PayloadJson = """{"command":"apply_markdown","arguments":{"batch_id":"b"}}""",
            });
            context.SaveChanges();
            return (cardId, optionId);
        }
    }

    private async Task<RecordedDecision> Decide(
        Shop shop, string staffId, string cardId, Decision decision = Decision.Accept, string? optionId = null)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var clock = new FixedClock();
        var ids = new UlidGenerator();
        var unitOfWork = new WaymarkUnitOfWork(context);
        var executor = new CommandExecutor(
            unitOfWork, ids, new ProcessingLogWriter(context, ids, new FixedCurrentStore(shop.StoreId), clock));
        var handler = new DecideRecommendationHandler(new RecommendationBoard(context), unitOfWork, clock);

        return await executor.ExecuteAsync(
            handler, new DecideRecommendation(cardId, staffId, decision, optionId));
    }

    private async Task<Board> BoardFor(Shop shop, string staffId)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        return await new PendingCards(new RecommendationBoard(context)).ForAsync(staffId);
    }

    private WaymarkDbContext Read(Shop shop) => database.NewContext(storeId: shop.StoreId);

    // ------------------------------------------------------- the board

    [Fact]
    public async Task A_manager_sees_the_card_with_its_reasoning_and_its_option()
    {
        var shop = new Shop(database);
        var (cardId, optionId) = shop.Card(database);

        var board = await BoardFor(shop, shop.ManagerId);

        Assert.Equal("manager", board.Staff?.RoleCode);
        Assert.Equal(0, board.Withheld);
        var card = Assert.Single(board.Cards);
        Assert.Equal(cardId, card.Card.RecommendationId);
        Assert.Equal(optionId, Assert.Single(card.Options).OptionId);
    }

    [Fact]
    public async Task A_cashier_is_told_how_many_cards_are_above_their_rank_rather_than_shown_an_empty_shop()
    {
        // An empty board and a quiet shop look identical, and only one of them is true.
        var shop = new Shop(database);
        shop.Card(database);

        var board = await BoardFor(shop, shop.CashierId);

        Assert.Equal("cashier", board.Staff?.RoleCode);
        Assert.Empty(board.Cards);
        Assert.Equal(1, board.Withheld);
    }

    [Fact]
    public async Task Somebody_the_store_does_not_employ_gets_no_board_at_all()
    {
        var shop = new Shop(database);
        shop.Card(database);

        var board = await BoardFor(shop, "nobody");

        Assert.Null(board.Staff);
        Assert.Empty(board.Cards);
        Assert.Equal(0, board.Withheld);
    }

    [Fact]
    public async Task One_stores_manager_never_sees_another_stores_card()
    {
        // Cross-tenant leakage is DPIA risk R9. The rank is right; the shop is not.
        var mine = new Shop(database);
        var theirs = new Shop(database);
        theirs.Card(database);

        var board = await BoardFor(mine, mine.ManagerId);

        Assert.Empty(board.Cards);
        Assert.Equal(0, board.Withheld);
    }

    [Fact]
    public async Task Another_stores_manager_is_not_this_stores_manager()
    {
        // A real staff member, a real manager, the right rank — and none of this shop's
        // business. Without the store filter on staff, their id would open this board.
        var mine = new Shop(database);
        var theirs = new Shop(database);
        mine.Card(database);

        var board = await BoardFor(mine, theirs.ManagerId);

        Assert.Null(board.Staff);
        Assert.Empty(board.Cards);
    }

    [Fact]
    public async Task Somebody_who_no_longer_works_here_gets_no_board()
    {
        // The row stays — the shop's history needs it — but a suspended staff member is not
        // somebody the store is currently letting decide anything.
        var shop = new Shop(database);
        shop.Card(database);
        shop.Suspend(database, shop.ManagerId);

        var board = await BoardFor(shop, shop.ManagerId);

        Assert.Null(board.Staff);
    }

    // ------------------------------------------------------- deciding

    [Fact]
    public async Task Accepting_writes_the_decision_and_closes_the_card_together()
    {
        var shop = new Shop(database);
        var (cardId, optionId) = shop.Card(database);

        var recorded = await Decide(shop, shop.ManagerId, cardId, Decision.Accept, optionId);

        using var read = Read(shop);
        var decision = await read.RecommendationDecisions.SingleAsync(row => row.RecommendationId == cardId);
        Assert.Equal(recorded.DecisionId, decision.DecisionId);
        Assert.Equal(Decision.Accept, decision.Decision);
        Assert.Equal(optionId, decision.ChosenOptionId);
        Assert.Equal(shop.ManagerId, decision.DecidedBy);
        Assert.Equal(Origin.Store, decision.Origin);
        Assert.Equal(Now, decision.DecidedAt);

        // The card is closed, in the same transaction that recorded the answer.
        var card = await read.Recommendations.SingleAsync(row => row.RecommendationId == cardId);
        Assert.Equal(RecommendationStatus.Decided, card.Status);

        // Accepting records the answer and stops there (D-069): nothing was applied, and
        // nothing was produced by it.
        Assert.Null(decision.AppliedAt);
        Assert.Null(decision.ResultingEntityId);
        Assert.Empty(await read.StockMovements.ToListAsync());
        Assert.Empty(await read.Prices.ToListAsync());
    }

    [Fact]
    public async Task A_cashier_is_refused_and_the_card_is_left_exactly_as_it_was()
    {
        var shop = new Shop(database);
        var (cardId, optionId) = shop.Card(database);

        var refusal = await Assert.ThrowsAsync<DecisionRefusedException>(
            () => Decide(shop, shop.CashierId, cardId, Decision.Accept, optionId));

        Assert.Contains("manager", refusal.Message, StringComparison.Ordinal);

        using var read = Read(shop);
        Assert.Empty(await read.RecommendationDecisions.ToListAsync());
        var card = await read.Recommendations.SingleAsync(row => row.RecommendationId == cardId);
        Assert.Equal(RecommendationStatus.Pending, card.Status);
    }

    [Fact]
    public async Task Somebody_the_store_does_not_employ_decides_nothing()
    {
        var shop = new Shop(database);
        var (cardId, optionId) = shop.Card(database);

        await Assert.ThrowsAsync<DecisionRefusedException>(
            () => Decide(shop, "nobody", cardId, Decision.Accept, optionId));

        using var read = Read(shop);
        Assert.Empty(await read.RecommendationDecisions.ToListAsync());
    }

    [Fact]
    public async Task A_card_in_another_store_is_not_found()
    {
        var mine = new Shop(database);
        var theirs = new Shop(database);
        var (cardId, optionId) = theirs.Card(database);

        var refusal = await Assert.ThrowsAsync<DecisionRefusedException>(
            () => Decide(mine, mine.ManagerId, cardId, Decision.Accept, optionId));

        Assert.Contains("no card", refusal.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_card_that_has_already_been_answered_is_not_answered_again()
    {
        // recommendation_decisions is append-only, so a second row would make "what was
        // decided about this" a question with two answers and no way to tell which stood.
        var shop = new Shop(database);
        var (cardId, optionId) = shop.Card(database, status: RecommendationStatus.Decided);

        var refusal = await Assert.ThrowsAsync<DecisionRefusedException>(
            () => Decide(shop, shop.ManagerId, cardId, Decision.Accept, optionId));

        Assert.Contains("decided", refusal.Message, StringComparison.Ordinal);
        using var read = Read(shop);
        Assert.Empty(await read.RecommendationDecisions.ToListAsync());
    }

    [Fact]
    public async Task An_option_belonging_to_another_card_is_refused()
    {
        // The foreign key would accept it: both are real options. Only this check knows that
        // the decision would then be recorded about the wrong thing.
        var shop = new Shop(database);
        var (cardId, _) = shop.Card(database);
        var (_, elsewhere) = shop.Card(database);

        await Assert.ThrowsAsync<DecisionRefusedException>(
            () => Decide(shop, shop.ManagerId, cardId, Decision.Accept, elsewhere));

        using var read = Read(shop);
        Assert.Empty(await read.RecommendationDecisions.ToListAsync());
    }

    [Fact]
    public async Task Adjust_and_snooze_are_refused_rather_than_written_without_what_they_need()
    {
        // Both carry a CHECK the slice has nothing to satisfy: an adjusted payload, and a date.
        // Refused on the way in, before anyone is looked up, because it is a question about the
        // request and not about the person.
        var shop = new Shop(database);
        var (cardId, optionId) = shop.Card(database);

        foreach (var decision in new[] { Decision.Adjust, Decision.Snooze })
        {
            var refusal = await Assert.ThrowsAsync<DecisionRefusedException>(
                () => Decide(shop, shop.ManagerId, cardId, decision, optionId));
            Assert.Contains("Phase 0.5", refusal.Message, StringComparison.Ordinal);
        }

        using var read = Read(shop);
        Assert.Empty(await read.RecommendationDecisions.ToListAsync());
    }
}
