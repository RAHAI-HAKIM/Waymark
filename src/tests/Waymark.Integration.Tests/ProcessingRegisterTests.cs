using System.Reflection;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Privacy;

namespace Waymark.Integration.Tests;

/// <summary>
/// The register Loi 25-11 article 41 bis 3 requires, as it reaches the database
/// (decisions.md D-045).
/// </summary>
public sealed class ProcessingRegisterTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _database;

    public ProcessingRegisterTests(MigratedDatabaseFixture database) => _database = database;

    // --------------------------------------------------- the compile-time rule

    [Fact]
    public void A_pseudonym_cannot_be_built_outside_Waymark_Pseudonymisation()
    {
        // The whole of D-045's "enforced by the type, not by the helper's body".
        // If a public constructor ever appears, a call site can log a customer_id
        // as a string and the log stops being pseudonymous — silently, because
        // the column is TEXT either way.
        var publicConstructors = typeof(Pseudonym)
            .GetConstructors(BindingFlags.Public | BindingFlags.Instance)
            .Where(constructor => constructor.GetParameters().Length > 0)
            .ToList();

        Assert.True(
            publicConstructors.Count == 0,
            "Pseudonym gained a public constructor. Only Waymark.Pseudonymisation may build one "
            + "(decisions.md D-045); the constructor is internal and Domain grants "
            + "InternalsVisibleTo to that project alone.");
    }

    [Fact]
    public void There_is_no_conversion_from_a_string_to_a_pseudonym()
    {
        // An implicit conversion would undo the constructor rule in one line,
        // and a public Parse would undo it in two.
        var conversions = typeof(Pseudonym)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.IsSpecialName
                             && (method.Name == "op_Implicit" || method.Name == "op_Explicit"))
            .ToList();

        Assert.Empty(conversions);

        var parses = typeof(Pseudonym)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name is "Parse" or "TryParse" or "From" or "FromString")
            .ToList();

        Assert.Empty(parses);
    }

    // ------------------------------------------------------------ the columns

    [Fact]
    public void A_log_row_records_its_purpose_and_its_legal_basis()
    {
        var occurred = new DateTimeOffset(2026, 6, 1, 9, 15, 0, TimeSpan.Zero);

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.ProcessingLog.Add(new ProcessingLogEntry
            {
                LogId = "01PROCLOG",
                OccurredAt = occurred,
                Operation = Operation.Consultation,
                SubjectType = ProcessingLogEntrySubjectType.Customer,
                SubjectId = "01PSEUDONYMVALUE0000000000",
                ActorType = ActorType.Staff,
                ActorId = "staff-1",
                Purpose = ProcessingPurpose.LoyaltyLookup,
                LegalBasis = ProcessingLegalBasis.Consent,
                SourceModule = "Waymark.Application",
                StoreId = null,
                TerminalId = null
            });
            context.SaveChanges();
        }

        using var reader = _database.NewContext(enforceForeignKeys: false);
        var entry = reader.ProcessingLog.IgnoreQueryFilters().Single(e => e.LogId == "01PROCLOG");

        Assert.Equal(ProcessingPurpose.LoyaltyLookup, entry.Purpose);
        Assert.Equal(ProcessingLegalBasis.Consent, entry.LegalBasis);
    }

    [Fact]
    public void The_purpose_and_legal_basis_are_stored_as_the_text_the_statute_uses()
    {
        // Not the C# member name. "LoyaltyLookup" in the column would be
        // unreadable to an examiner and unmatchable by a query.
        var stored = _database.Query(
            "SELECT purpose || '/' || legal_basis FROM processing_log WHERE log_id = '01PROCLOG'");

        Assert.Equal(["loyalty_lookup/consent"], stored);
    }

    [Fact]
    public void Vital_interest_is_representable_here_and_nowhere_else()
    {
        // The reason ProcessingLegalBasis is its own enum: customers.legal_basis
        // carries a CHECK with four values, and widening it is a table rebuild
        // (D-022), which D-045 avoids.
        Assert.Equal(5, Enum.GetValues<ProcessingLegalBasis>().Length);
        Assert.Equal(4, Enum.GetValues<LegalBasis>().Length);
        Assert.DoesNotContain("VitalInterest", Enum.GetNames<LegalBasis>(), StringComparer.Ordinal);
    }

    [Fact]
    public void Every_purpose_in_the_enum_round_trips_through_the_converter()
    {
        // The converter is the validation, since the column has no CHECK. A
        // member with no mapping throws on write; one the database holds but the
        // enum does not throws on read. Both are better than a silent default.
        using var context = _database.NewContext(enforceForeignKeys: false);
        var property = context.Model
            .FindEntityType(typeof(ProcessingLogEntry))!
            .FindProperty(nameof(ProcessingLogEntry.Purpose))!;

        var converter = property.GetValueConverter()!;

        foreach (var purpose in Enum.GetValues<ProcessingPurpose>())
        {
            var text = converter.ConvertToProvider(purpose);
            Assert.Equal(purpose, converter.ConvertFromProvider(text));
        }
    }

    // ----------------------------------------------------------- the counters

    [Fact]
    public void A_counter_replaces_a_days_detail_and_survives_a_round_trip()
    {
        var day = new DateOnly(2026, 6, 1);

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            context.ProcessingCounters.Add(new ProcessingCounter
            {
                CounterId = "01COUNTER",
                StoreId = null,
                Day = day,
                Operation = Operation.Consultation,
                Purpose = ProcessingPurpose.PosSale,
                EventCount = 1_483,
                RolledUpAt = new DateTimeOffset(2033, 6, 1, 3, 0, 0, TimeSpan.Zero)
            });
            context.SaveChanges();
        }

        using var reader = _database.NewContext(enforceForeignKeys: false);
        var counter = reader.ProcessingCounters.IgnoreQueryFilters().Single(c => c.CounterId == "01COUNTER");

        Assert.Equal(day, counter.Day);
        Assert.Equal(1_483, counter.EventCount);
        Assert.Equal(Operation.Consultation, counter.Operation);
    }

    [Fact]
    public void A_counter_of_nothing_is_refused()
    {
        // A roll-up with nothing to count writes no row. A zero would be
        // indistinguishable from "the purge ran and found nothing", which is a
        // different claim.
        using var context = _database.NewContext(enforceForeignKeys: false);
        context.ProcessingCounters.Add(new ProcessingCounter
        {
            CounterId = "01COUNTERZERO",
            StoreId = null,
            Day = new DateOnly(2026, 6, 2),
            Operation = Operation.Collection,
            Purpose = ProcessingPurpose.PosSale,
            EventCount = 0,
            RolledUpAt = DateTimeOffset.UnixEpoch
        });

        var error = Assert.Throws<DbUpdateException>(() => context.SaveChanges());

        Assert.Contains("ck_processing_counters_event_count", error.InnerException!.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void The_same_day_and_purpose_cannot_be_counted_twice_for_one_store()
    {
        using var seed = _database.NewContext(enforceForeignKeys: false);
        seed.ProcessingCounters.Add(new ProcessingCounter
        {
            CounterId = "01COUNTERA",
            StoreId = "store-counters",
            Day = new DateOnly(2026, 6, 3),
            Operation = Operation.Collection,
            Purpose = ProcessingPurpose.PosSale,
            EventCount = 10,
            RolledUpAt = DateTimeOffset.UnixEpoch
        });
        seed.SaveChanges();

        using var again = _database.NewContext(enforceForeignKeys: false);
        again.ProcessingCounters.Add(new ProcessingCounter
        {
            CounterId = "01COUNTERB",
            StoreId = "store-counters",
            Day = new DateOnly(2026, 6, 3),
            Operation = Operation.Collection,
            Purpose = ProcessingPurpose.PosSale,
            EventCount = 11,
            RolledUpAt = DateTimeOffset.UnixEpoch
        });

        Assert.Throws<DbUpdateException>(() => again.SaveChanges());
    }

    [Fact]
    public void The_counters_table_is_STRICT_like_every_other()
    {
        var strict = _database.Query(
            "SELECT strict FROM pragma_table_list WHERE schema = 'main' AND name = 'processing_counters'");

        Assert.Equal(["1"], strict);
    }

    // ----------------------------------------------------- the dedupe key

    [Fact]
    public void Supersession_has_an_index_to_match_on()
    {
        // D-044's dedupe key. Derived rather than stored, which only works if
        // finding the row it supersedes is cheap.
        var columns = _database.Query(
            "SELECT name FROM pragma_index_info('ix_recs_dedupe')");

        Assert.Equal(["store_id", "recommendation_type", "subject_type", "subject_id"], columns);
    }

    [Fact]
    public void Neither_added_column_forced_a_table_rebuild()
    {
        // ALTER TABLE ADD COLUMN appends to the stored SQL in place; a rebuild
        // reformats the whole definition. This asserts what the migration diff
        // showed — and that every CHECK and index the rebuild would have dropped
        // is still there (D-022).
        using var connection = _database.Connect();

        Assert.Equal(3, CountIn(connection, "processing_log", "CONSTRAINT \"ck_"));
        Assert.Equal(6, CountIn(connection, "recommendations", "CONSTRAINT \"ck_"));

        var recommendationIndexes = _database.Query(
            "SELECT count(*) FROM sqlite_schema WHERE type = 'index' AND tbl_name = 'recommendations' AND sql IS NOT NULL");

        Assert.Equal(["3"], recommendationIndexes);
    }

    private static int CountIn(SqliteConnection connection, string table, string needle)
    {
        using var command = connection.CreateCommand();
        command.CommandText = $"SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = '{table}'";
        var sql = (string)command.ExecuteScalar()!;

        var count = 0;
        var index = sql.IndexOf(needle, StringComparison.Ordinal);
        while (index >= 0)
        {
            count++;
            index = sql.IndexOf(needle, index + 1, StringComparison.Ordinal);
        }

        return count;
    }
}
