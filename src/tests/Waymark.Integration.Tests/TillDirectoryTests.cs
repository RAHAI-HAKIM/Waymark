using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.Persistence.Organisation;

namespace Waymark.Integration.Tests;

/// <summary>
/// Who and where the till is, for its top bar (session A4). The silent failures: another store's
/// name at the top of this store's till, which the global filter must make impossible (DPIA R9);
/// and a suspended cashier's name still shown as the person selling.
/// </summary>
public sealed class TillDirectoryTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);

    /// <summary>Two stores, each with a till and a cashier; roles with unique codes so tests share nothing.</summary>
    private sealed class Stores
    {
        // roles.rank is unique across the tenant, and the fixture's database is shared by the
        // class, so every instance takes a rank nobody else has.
        private static int _nextRank = 1_000;

        private readonly MigratedDatabaseFixture _database;
        private readonly string _suffix = Guid.NewGuid().ToString("N")[..10];

        public Stores(MigratedDatabaseFixture database, bool roleActive = true, StaffStatus staffStatus = StaffStatus.Active)
        {
            _database = database;
            StoreId = $"store-{_suffix}";
            OtherStoreId = $"other-{_suffix}";
            TerminalId = $"till-{_suffix}";
            OtherTerminalId = $"other-till-{_suffix}";
            StaffId = $"staff-{_suffix}";
            OtherStaffId = $"other-staff-{_suffix}";
            RoleCode = $"cashier-{_suffix}";

            using var context = database.NewContext();
            context.Roles.Add(new Role
            {
                RoleCode = RoleCode, Rank = Interlocked.Increment(ref _nextRank), LabelFr = "Caissier", LabelAr = "أمين الصندوق", IsActive = roleActive, CreatedAt = Moment,
            });
            context.Stores.Add(NewStore(StoreId, "El Bahdja"));
            context.Stores.Add(NewStore(OtherStoreId, "Autre magasin"));
            context.Terminals.Add(NewTerminal(TerminalId, StoreId, "Caisse 1"));
            context.Terminals.Add(NewTerminal(OtherTerminalId, OtherStoreId, "Caisse de l'autre"));
            context.Staff.Add(NewStaff(StaffId, StoreId, "Nabil B.", staffStatus));
            context.Staff.Add(NewStaff(OtherStaffId, OtherStoreId, "Quelqu'un d'ailleurs", StaffStatus.Active));
            context.SaveChanges();
        }

        public string StoreId { get; }
        public string OtherStoreId { get; }
        public string TerminalId { get; }
        public string OtherTerminalId { get; }
        public string StaffId { get; }
        public string OtherStaffId { get; }
        public string RoleCode { get; }

        public async Task<TillDescription?> Describe(string terminalId, string? staffId)
        {
            await using var context = _database.NewContext(storeId: StoreId);
            return await new TillDirectory(context).DescribeAsync(terminalId, staffId);
        }

        private static Store NewStore(string id, string name) => new()
        {
            StoreId = id,
            StoreCode = id[..Math.Min(id.Length, 12)],
            StoreName = name,
            StoreType = "grocery",
            Currency = "DZD",
            RoundingPolicy = Rounding.HalfUp,
            CreatedAt = Moment,
            UpdatedAt = Moment,
        };

        private static Terminal NewTerminal(string id, string storeId, string name) => new()
        {
            TerminalId = id, StoreId = storeId, TerminalName = name, CreatedAt = Moment, UpdatedAt = Moment,
        };

        private Staff NewStaff(string id, string storeId, string name, StaffStatus status) => new()
        {
            StaffId = id,
            StoreId = storeId,
            StaffName = name,
            Role = RoleCode,
            PinHash = "-",
            JoinDate = new DateOnly(2026, 1, 1),
            Status = status,
            CreatedAt = Moment,
            UpdatedAt = Moment,
        };
    }

    [Fact]
    public async Task A_till_is_named_with_its_store_its_currency_and_the_person_at_it()
    {
        var stores = new Stores(database);

        var till = await stores.Describe(stores.TerminalId, stores.StaffId);

        Assert.Equal(
            new TillDescription("El Bahdja", "Caisse 1", "DZD", new StaffDescription("Nabil B.", "Caissier", "أمين الصندوق")),
            till);
    }

    [Fact]
    public async Task Another_stores_till_is_not_found_rather_than_named()
    {
        // The global filter: this store's context cannot see the other store's terminal, so the
        // top bar can never show another shop's name (CLAUDE.md §3.3, DPIA R9).
        var stores = new Stores(database);

        Assert.Null(await stores.Describe(stores.OtherTerminalId, stores.StaffId));
    }

    [Fact]
    public async Task An_unknown_terminal_is_not_found()
    {
        var stores = new Stores(database);

        Assert.Null(await stores.Describe("no-such-till", stores.StaffId));
    }

    [Fact]
    public async Task Another_stores_staff_member_is_nobody_here()
    {
        var stores = new Stores(database);

        var till = await stores.Describe(stores.TerminalId, stores.OtherStaffId);

        Assert.NotNull(till);
        Assert.Null(till.Staff);
    }

    [Fact]
    public async Task A_suspended_staff_member_is_nobody_rather_than_a_stale_name()
    {
        // The same people RecommendationBoard.StaffAsync gives a rank to, and no one else.
        var stores = new Stores(database, staffStatus: StaffStatus.Suspended);

        Assert.Null((await stores.Describe(stores.TerminalId, stores.StaffId))!.Staff);
    }

    [Fact]
    public async Task A_person_whose_role_is_retired_is_nobody()
    {
        var stores = new Stores(database, roleActive: false);

        Assert.Null((await stores.Describe(stores.TerminalId, stores.StaffId))!.Staff);
    }

    [Fact]
    public async Task No_staff_named_is_a_till_with_nobody_at_it()
    {
        var stores = new Stores(database);

        var till = await stores.Describe(stores.TerminalId, null);

        Assert.Equal("Caisse 1", till!.TerminalName);
        Assert.Null(till.Staff);
    }
}
