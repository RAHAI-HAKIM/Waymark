using System.Runtime.Versioning;
using Microsoft.EntityFrameworkCore;
using Waymark.Application.Commands;
using Waymark.Application.IdGenerator;
using Waymark.Application.Organisation;
using Waymark.Domain;
using Waymark.Domain.Engine;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.Persistence;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Privacy;
using Waymark.StoreServer.Security;

namespace Waymark.Integration.Tests;

/// <summary>
/// Who may sign in, and setting their PIN (session A5, D-083). The silent failures: another
/// store's cashier offered on this till's list (DPIA R9), a suspended person still able to sign in
/// with an old PIN, and a PIN written by the maintenance switch that nothing ever committed — or
/// that it wrote in the clear.
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class StaffCredentialsTests(MigratedDatabaseFixture database) : IClassFixture<MigratedDatabaseFixture>
{
    private static readonly DateTimeOffset Moment = new(2026, 1, 1, 8, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset Later = new(2026, 9, 24, 8, 0, 0, TimeSpan.Zero);

    /// <summary>"test$" and the PIN. Argon2 is <c>Argon2PinHasherTests</c>'.</summary>
    private sealed class TestHasher : IPinHasher
    {
        public string Hash(string pin) => "test$" + pin;

        public bool Verify(string pin, string? storedHash) => storedHash == "test$" + pin;
    }

    private sealed class HeldClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    /// <summary>A store with four people and a neighbour with one; each instance uses its own ids.</summary>
    private sealed class Shop
    {
        private static int _nextRank = 1_000;

        public Shop(MigratedDatabaseFixture database)
        {
            var suffix = Guid.NewGuid().ToString("N")[..10];
            StoreId = $"store-{suffix}";
            var other = $"other-{suffix}";
            var role = $"cashier-{suffix}";
            var retired = $"retired-{suffix}";

            using var context = database.NewContext();
            context.Roles.Add(NewRole(role, true));
            context.Roles.Add(NewRole(retired, false));
            context.Stores.Add(NewStore(StoreId));
            context.Stores.Add(NewStore(other));

            Samia = Add(context, $"samia-{suffix}", StoreId, "Samia K.", role, StaffStatus.Active, "test$1357");
            Nabil = Add(context, $"nabil-{suffix}", StoreId, "Nabil B.", role, StaffStatus.Active, StaffPin.NeverUsable);
            Suspended = Add(context, $"suspended-{suffix}", StoreId, "Yacine R.", role, StaffStatus.Suspended, "test$2468");
            RoleRetired = Add(context, $"old-role-{suffix}", StoreId, "Karim M.", retired, StaffStatus.Active, "test$9753");
            Elsewhere = Add(context, $"elsewhere-{suffix}", other, "Ailleurs", role, StaffStatus.Active, "test$1111");
            context.SaveChanges();
        }

        public string StoreId { get; }
        public string Samia { get; }
        public string Nabil { get; }
        public string Suspended { get; }
        public string RoleRetired { get; }
        public string Elsewhere { get; }

        private static Role NewRole(string code, bool active) => new()
        {
            RoleCode = code, Rank = Interlocked.Increment(ref _nextRank), LabelFr = "Caissier", LabelAr = "أمين الصندوق",
            IsActive = active, CreatedAt = Moment,
        };

        private static Store NewStore(string id) => new()
        {
            StoreId = id, StoreCode = id[..Math.Min(id.Length, 12)], StoreName = id, StoreType = "grocery", Currency = "DZD",
            RoundingPolicy = Rounding.HalfUp, CreatedAt = Moment, UpdatedAt = Moment,
        };

        private static string Add(WaymarkDbContext context, string id, string store, string name, string role, StaffStatus status, string hash)
        {
            context.Staff.Add(new Staff
            {
                StaffId = id, StoreId = store, StaffName = name, Role = role, PinHash = hash,
                JoinDate = new DateOnly(2026, 1, 1), Status = status, CreatedAt = Moment, UpdatedAt = Moment,
            });
            return id;
        }
    }

    private async Task<T> Read<T>(Shop shop, Func<StaffCredentials, Task<T>> read)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        return await read(new StaffCredentials(context));
    }

    private (string? Hash, DateTimeOffset UpdatedAt) Row(string staffId)
    {
        using var context = database.NewContext();
        var row = context.Staff.IgnoreQueryFilters().Single(s => s.StaffId == staffId);
        return (row.PinHash, row.UpdatedAt);
    }

    // ------------------------------------------------------------ the list

    [Fact]
    public async Task The_list_is_this_stores_active_people_with_an_active_role_by_name()
    {
        var shop = new Shop(database);

        var listed = await Read(shop, credentials => credentials.CandidatesAsync());

        Assert.Equal([shop.Nabil, shop.Samia], listed.Select(person => person.StaffId));
        Assert.Equal("Caissier", listed[0].RoleLabelFr);
    }

    [Fact]
    public async Task The_list_says_who_has_a_pin_and_never_what_it_is()
    {
        var shop = new Shop(database);

        var listed = await Read(shop, credentials => credentials.CandidatesAsync());

        Assert.False(listed.Single(person => person.StaffId == shop.Nabil).HasPin);
        Assert.True(listed.Single(person => person.StaffId == shop.Samia).HasPin);
        Assert.DoesNotContain(typeof(SignInCandidate).GetProperties(), property => property.Name.Contains("Hash", StringComparison.Ordinal));
    }

    // ------------------------------------------------------------ the hash

    [Fact]
    public async Task The_hash_is_read_for_an_active_person_of_this_store()
    {
        var shop = new Shop(database);

        Assert.Equal("test$1357", await Read(shop, credentials => credentials.PinHashAsync(shop.Samia)));
    }

    [Fact]
    public async Task Another_stores_person_has_no_hash_here()
    {
        // The global filter, on the one read that could sign somebody in (DPIA R9).
        var shop = new Shop(database);

        Assert.Null(await Read(shop, credentials => credentials.PinHashAsync(shop.Elsewhere)));
    }

    [Fact]
    public async Task A_suspended_person_or_a_retired_role_has_no_hash_whatever_the_pin()
    {
        var shop = new Shop(database);

        Assert.Null(await Read(shop, credentials => credentials.PinHashAsync(shop.Suspended)));
        Assert.Null(await Read(shop, credentials => credentials.PinHashAsync(shop.RoleRetired)));
    }

    // ------------------------------------------------------ setting a PIN

    private async Task<(int Exit, string Output)> SetPin(Shop shop, string staffId, params string?[] typed)
    {
        await using var context = database.NewContext(storeId: shop.StoreId);
        var ids = new UlidGenerator();
        var clock = new HeldClock(Later);
        var executor = new CommandExecutor(
            new WaymarkUnitOfWork(context), ids, new ProcessingLogWriter(context, ids, new FixedCurrentStore(shop.StoreId), clock));
        var handler = new SetStaffPinHandler(new StaffCredentials(context), new TestHasher(), clock);

        var answers = new Queue<string?>(typed);
        using var output = new StringWriter();
        var exit = await SetPinSwitch.RunAsync(staffId, _ => answers.Count > 0 ? answers.Dequeue() : null, output, executor, handler);
        return (exit, output.ToString());
    }

    [Fact]
    public async Task A_pin_typed_twice_is_stored_as_its_hash_and_committed()
    {
        var shop = new Shop(database);

        var (exit, output) = await SetPin(shop, shop.Nabil, "4821", "4821");

        Assert.Equal(0, exit);
        Assert.Equal(("test$4821", Later), Row(shop.Nabil));
        Assert.DoesNotContain("4821", output.Replace("test$4821", "", StringComparison.Ordinal), StringComparison.Ordinal);
    }

    [Fact]
    public async Task Two_different_pins_change_nothing()
    {
        var shop = new Shop(database);

        var (exit, _) = await SetPin(shop, shop.Samia, "4821", "4812");

        Assert.Equal(1, exit);
        Assert.Equal(("test$1357", Moment), Row(shop.Samia));
    }

    [Fact]
    public async Task No_input_changes_nothing()
    {
        var shop = new Shop(database);

        Assert.Equal(1, (await SetPin(shop, shop.Samia)).Exit);
        Assert.Equal(("test$1357", Moment), Row(shop.Samia));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("١٢٣٤")]
    [InlineData(" 1234")]
    public async Task A_pin_that_is_not_well_formed_changes_nothing_and_is_not_repeated(string pin)
    {
        var shop = new Shop(database);

        var (exit, output) = await SetPin(shop, shop.Samia, pin, pin);

        Assert.Equal(1, exit);
        Assert.Equal(("test$1357", Moment), Row(shop.Samia));
        Assert.DoesNotContain(pin.Trim(), output, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Nobody_outside_this_stores_active_staff_gets_a_pin()
    {
        var shop = new Shop(database);

        Assert.Equal(1, (await SetPin(shop, shop.Elsewhere, "4821", "4821")).Exit);
        Assert.Equal(1, (await SetPin(shop, shop.Suspended, "4821", "4821")).Exit);
        Assert.Equal(1, (await SetPin(shop, "no-such-person", "4821", "4821")).Exit);

        Assert.Equal(("test$1111", Moment), Row(shop.Elsewhere));
        Assert.Equal(("test$2468", Moment), Row(shop.Suspended));
    }

    [Fact]
    public async Task A_retired_role_gets_no_pin_because_it_could_never_sign_in_with_one()
    {
        // The switch said "It works at the next sign-in", and the list and the hash both leave a
        // retired role out: the person was told a PIN worked that no till would ever accept.
        var shop = new Shop(database);

        var (exit, output) = await SetPin(shop, shop.RoleRetired, "4821", "4821");

        Assert.Equal(1, exit);
        Assert.Contains("role is retired", output, StringComparison.Ordinal);
        Assert.Equal(("test$9753", Moment), Row(shop.RoleRetired));
    }
}
