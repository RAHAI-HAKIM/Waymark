using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Persistence;

namespace Waymark.Integration.Tests;

/// <summary>
/// Proves the two worked-example mappings against a real database built from
/// the schema.
///
/// <para>
/// The round trip alone is not enough. A configuration can be wrong in a way
/// that survives writing and reading through EF — a converter that emits the
/// wrong timestamp format, or an enum written as "PaidIn" into a column whose
/// CHECK expects "paid_in" — because EF reads back whatever it wrote. So these
/// tests also read the raw column values with SQL, which is the only way to see
/// what actually landed on disk.
/// </para>
/// </summary>
public sealed class EntityMappingTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _schema;

    public EntityMappingTests(MigratedDatabaseFixture schema) => _schema = schema;

    private WaymarkDbContext NewContext() =>
        // Foreign keys off: cash_movements references cash_sessions,
        // reason_codes and staff, and seeding those to test a column mapping
        // would mean seeding half the catalogue.
        _schema.NewContext(enforceForeignKeys: false);

    [Fact]
    public void A_unit_of_measure_round_trips()
    {
        using (var context = NewContext())
        {
            context.UnitsOfMeasure.Add(new UnitOfMeasure
            {
                UnitCode = "g",
                NameAr = "غرام",
                NameFr = "gramme",
                Dimension = Dimension.Weight,
                BaseUnitCode = null,
                FactorToBase = 1_000_000_000,
                DecimalPlaces = 3,
                IsActive = true,
                CreatedAt = new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero)
            });
            context.SaveChanges();
        }

        using (var context = NewContext())
        {
            var unit = context.UnitsOfMeasure.Single(u => u.UnitCode == "g");

            Assert.Equal("gramme", unit.NameFr);
            Assert.Equal(Dimension.Weight, unit.Dimension);
            Assert.Equal(1_000_000_000L, unit.FactorToBase);
            Assert.Equal(3L, unit.DecimalPlaces);
            Assert.True(unit.IsActive);
            Assert.Equal(new DateTimeOffset(2026, 1, 1, 9, 30, 0, TimeSpan.Zero), unit.CreatedAt);
        }
    }

    [Fact]
    public void A_cash_movement_round_trips()
    {
        using (var context = NewContext())
        {
            context.CashMovements.Add(new CashMovement
            {
                MovementId = "01ROUNDTRIP",
                SessionId = "session-1",
                MovementType = CashMovementType.PaidOut,
                Amount = 1250,
                ReasonCode = "supplier-cash",
                Note = null,
                StaffId = "staff-1",
                AuthorisedBy = "manager-1",
                OccurredAt = new DateTimeOffset(2026, 3, 4, 17, 45, 12, TimeSpan.Zero)
            });
            context.SaveChanges();
        }

        using (var context = NewContext())
        {
            var movement = context.CashMovements.Single(m => m.MovementId == "01ROUNDTRIP");

            Assert.Equal(CashMovementType.PaidOut, movement.MovementType);
            Assert.Equal(1250L, movement.Amount);
            Assert.Null(movement.Note);
            Assert.Equal("manager-1", movement.AuthorisedBy);
            Assert.Equal(new DateTimeOffset(2026, 3, 4, 17, 45, 12, TimeSpan.Zero), movement.OccurredAt);
        }
    }

    [Fact]
    public void The_values_on_disk_match_the_schema_conventions()
    {
        using (var context = NewContext())
        {
            context.CashMovements.Add(new CashMovement
            {
                MovementId = "01ONDISK",
                SessionId = "session-1",
                MovementType = CashMovementType.FloatAdd,
                Amount = 50_000,
                ReasonCode = "opening-float",
                StaffId = "staff-1",
                OccurredAt = new DateTimeOffset(2026, 3, 4, 8, 0, 0, TimeSpan.Zero)
            });
            context.SaveChanges();
        }

        using var connection = _schema.Connect(enforceForeignKeys: false);
        using var command = connection.CreateCommand();
        command.CommandText = """
            SELECT movement_type, amount, occurred_at, typeof(amount)
            FROM cash_movements WHERE movement_id = '01ONDISK'
            """;

        using var reader = command.ExecuteReader();
        Assert.True(reader.Read());

        // The enum is spelled the database's way, not C#'s. "FloatAdd" would
        // violate the CHECK constraint, and EF would have read it back happily.
        Assert.Equal("float_add", reader.GetString(0));

        // Money is an integer number of centimes, and SQLite agrees it is an
        // integer rather than a float that happens to look whole.
        Assert.Equal(50_000L, reader.GetInt64(1));
        Assert.Equal("integer", reader.GetString(3));

        // The schema's timestamp convention: ISO-8601 UTC, space separator, no
        // zone suffix, so lexical order is chronological order.
        Assert.Equal("2026-03-04 08:00:00", reader.GetString(2));
    }

    [Fact]
    public void The_database_rejects_what_the_model_allows()
    {
        // AmountCentimes is a long, so nothing in C# stops a negative. The
        // CHECK constraint does — which is why it has to be declared in the
        // configuration as well as the schema, or a table rebuild loses it.
        using var context = NewContext();
        context.CashMovements.Add(new CashMovement
        {
            MovementId = "01NEGATIVE",
            SessionId = "session-1",
            MovementType = CashMovementType.PaidIn,
            Amount = -1,
            ReasonCode = "oops",
            StaffId = "staff-1",
            OccurredAt = DateTimeOffset.UnixEpoch
        });

        var error = Assert.Throws<DbUpdateException>(() => context.SaveChanges());
        Assert.IsType<SqliteException>(error.InnerException);
        Assert.Contains("CHECK constraint failed", error.InnerException!.Message, StringComparison.Ordinal);
    }
}
