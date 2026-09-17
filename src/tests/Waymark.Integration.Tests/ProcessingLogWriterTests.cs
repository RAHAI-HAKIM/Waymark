using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Persistence;
using Waymark.Persistence.Privacy;

namespace Waymark.Integration.Tests;

/// <summary>
/// The <c>processing_log</c> helper against a real database (decisions.md
/// D-045).
///
/// <para>
/// <c>ProcessingLogContractTests</c> asserts the shape of the promise; this
/// asserts the row. The three columns a caller cannot set — id, time and store —
/// are the ones tested hardest, because a caller that could set them could
/// produce evidence of its own choosing.
/// </para>
/// <para>
/// Most subjects here are undefined, because the interesting half of the column
/// is the null one: the only route to a null <c>subject_id</c> is a subject that
/// was never defined. <c>A_computed_pseudonym_reaches_the_column_intact</c> is
/// the other half, and it could not be written until W7 built the pseudonymiser
/// — a <see cref="Pseudonym"/> cannot be constructed outside
/// <c>Waymark.Pseudonymisation</c>, so until that project existed nothing could
/// obtain one.
/// </para>
/// </summary>
public sealed class ProcessingLogWriterTests
    : IClassFixture<MigratedDatabaseFixture>, IClassFixture<TenantKeyFixture>
{
    private readonly MigratedDatabaseFixture _database;
    private readonly TenantKeyFixture _keys;

    public ProcessingLogWriterTests(MigratedDatabaseFixture database, TenantKeyFixture keys)
    {
        _database = database;
        _keys = keys;
    }

    private static readonly DateTimeOffset Noon =
        new(2026, 9, 12, 12, 0, 0, TimeSpan.Zero);

    /// <summary>A clock that does not move, so a test can assert on the instant.</summary>
    private sealed class FixedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    private sealed class FixedIds(string id) : IIdGenerator
    {
        public string NewId() => id;
    }

    private static ProcessingLogWriter WriterFor(
        WaymarkDbContext context,
        string id,
        string? storeId = null,
        DateTimeOffset? now = null) =>
        new(context, new FixedIds(id), new FixedCurrentStore(storeId), new FixedClock(now ?? Noon));

    /// <summary>A staff member looking a customer up: an operation about one person, so it names them (F-20).</summary>
    private ProcessingEvent AConsultation(string sourceModule = "Waymark.Application") => new(
        Operation.Consultation,
        ProcessingLogEntrySubjectType.Customer,
        Subject: _keys.Pseudonymiser.PseudonymFor(Waymark.Domain.Privacy.SubjectDomain.Customer, "01CONSULTED"),
        ActorType.Staff,
        ActorId: "staff-w9",
        ProcessingPurpose.LoyaltyLookup,
        ProcessingLegalBasis.Consent,
        sourceModule);

    // ---------------------------------------------------------------- the row

    [Fact]
    public void A_recorded_event_becomes_a_row_carrying_what_the_caller_said()
    {
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            WriterFor(context, "01WRITERBASIC").Record(AConsultation() with
            {
                Recipient = "ANPDP",
            });
            context.SaveChanges();
        }

        using var reader = _database.NewContext(enforceForeignKeys: false);
        var entry = reader.ProcessingLog.IgnoreQueryFilters()
            .Single(e => e.LogId == "01WRITERBASIC");

        Assert.Equal(Operation.Consultation, entry.Operation);
        Assert.Equal(ProcessingLogEntrySubjectType.Customer, entry.SubjectType);
        Assert.Equal(ActorType.Staff, entry.ActorType);
        Assert.Equal("staff-w9", entry.ActorId);
        Assert.Equal(ProcessingPurpose.LoyaltyLookup, entry.Purpose);
        Assert.Equal(ProcessingLegalBasis.Consent, entry.LegalBasis);
        Assert.Equal("ANPDP", entry.Recipient);
        Assert.Equal("Waymark.Application", entry.SourceModule);
    }

    [Fact]
    public void The_id_comes_from_the_generator_and_never_from_the_database()
    {
        // CLAUDE.md §3.2: no autoincrement, anywhere, ever. An id minted by the
        // machine that writes is what lets an offline till log without colliding.
        using var context = _database.NewContext(enforceForeignKeys: false);
        WriterFor(context, "01WRITERIDFROMPORT").Record(AConsultation());
        context.SaveChanges();

        Assert.Equal(
            ["01WRITERIDFROMPORT"],
            _database.Query("SELECT log_id FROM processing_log WHERE log_id = '01WRITERIDFROMPORT'"));
    }

    [Fact]
    public void The_time_comes_from_the_injected_clock_and_not_from_the_wall()
    {
        // Injected so W10's generator can write a year of history stamped with
        // the days it simulated, rather than with the moment it ran — and so
        // this assertion can be an equality rather than a tolerance.
        using var context = _database.NewContext(enforceForeignKeys: false);
        var simulated = new DateTimeOffset(2024, 3, 11, 8, 30, 0, TimeSpan.Zero);
        WriterFor(context, "01WRITERCLOCK", now: simulated).Record(AConsultation());
        context.SaveChanges();

        using var reader = _database.NewContext(enforceForeignKeys: false);
        var entry = reader.ProcessingLog.IgnoreQueryFilters().Single(e => e.LogId == "01WRITERCLOCK");

        Assert.Equal(simulated, entry.OccurredAt);
    }

    [Fact]
    public void The_store_comes_from_the_current_store_and_the_caller_cannot_say()
    {
        // processing_log is store-scoped behind a fail-closed filter (§3.3). A
        // row filed under another store is invisible to the process that wrote
        // it, which is an audit gap that looks like nothing at all.
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            WriterFor(context, "01WRITERSTORE", storeId: "store-w9").Record(AConsultation());
            context.SaveChanges();
        }

        Assert.Equal(
            ["store-w9"],
            _database.Query("SELECT store_id FROM processing_log WHERE log_id = '01WRITERSTORE'"));
    }

    [Theory]
    [InlineData(Operation.Consultation, ActorType.Staff)]
    [InlineData(Operation.Modification, ActorType.Staff)]
    [InlineData(Operation.Disclosure, ActorType.Staff)]
    [InlineData(Operation.Transmission, ActorType.Engine)]
    [InlineData(Operation.Erasure, ActorType.Staff)]
    [InlineData(Operation.ReIdentification, ActorType.Engine)]
    public void An_operation_about_one_person_by_a_person_or_the_engine_must_name_them(Operation operation, ActorType actor)
    {
        // F-20: default(Pseudonym) is also what a forgotten subject looks like. Only a declared
        // system task may leave it out, so a staff consultation with no subject is refused
        // rather than written as an entry that says nothing about whom.
        using var context = _database.NewContext(enforceForeignKeys: false);
        var writer = WriterFor(context, $"01WRITERNOSUBJECT{(int)operation}{(int)actor}");

        var error = Assert.Throws<ArgumentException>(() => writer.Record(AConsultation() with
        {
            Operation = operation,
            ActorType = actor,
            Subject = default,
        }));

        Assert.Contains("must name its subject", error.Message, StringComparison.Ordinal);
        Assert.Empty(context.ChangeTracker.Entries());
    }

    [Theory]
    [InlineData(Operation.Collection, ActorType.Staff)]
    [InlineData(Operation.Pseudonymisation, ActorType.Engine)]
    [InlineData(Operation.Consultation, ActorType.System)]
    public void A_task_over_many_subjects_or_a_declared_system_task_may_name_none(Operation operation, ActorType actor)
    {
        using var context = _database.NewContext(enforceForeignKeys: false);

        WriterFor(context, $"01WRITERMANY{(int)operation}{(int)actor}").Record(AConsultation() with
        {
            Operation = operation,
            ActorType = actor,
            Subject = default,
        });

        Assert.Single(context.ChangeTracker.Entries());
    }

    [Fact]
    public void An_operation_with_no_subject_writes_a_null_subject_id()
    {
        // A retention sweep has no subject. The route to NULL is a Pseudonym
        // that was never defined — never a null string — so "no subject" cannot
        // be confused with "a subject we failed to pseudonymise".
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            WriterFor(context, "01WRITERNOSUBJECT").Record(new ProcessingEvent(
                Operation.Erasure,
                ProcessingLogEntrySubjectType.Customer,
                Subject: default,
                ActorType.System,
                ActorId: null,
                ProcessingPurpose.RetentionExpiry,
                ProcessingLegalBasis.LegalObligation,
                SourceModule: "Waymark.Application.Retention"));
            context.SaveChanges();
        }

        Assert.Equal(
            ["<null>"],
            _database.Query("SELECT subject_id FROM processing_log WHERE log_id = '01WRITERNOSUBJECT'"));
    }

    [Fact]
    public void A_computed_pseudonym_reaches_the_column_intact()
    {
        // The half of D-045 that W9 could not test. What matters is that the
        // value in the column is the same 26 characters the pseudonymiser
        // produced — no truncation by a column width, no case change, no
        // round-trip through something that trims.
        var subject = _keys.Pseudonymiser.PseudonymFor(
            Waymark.Domain.Privacy.SubjectDomain.Customer, "01LOGGEDCUSTOMER");

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            WriterFor(context, "01WRITERREALSUBJECT").Record(AConsultation() with
            {
                Subject = subject,
            });
            context.SaveChanges();
        }

        var stored = _database.Query(
            "SELECT subject_id FROM processing_log WHERE log_id = '01WRITERREALSUBJECT'");

        Assert.Equal([subject.Value], stored);
        Assert.Equal(26, stored[0].Length);

        // And it is the pseudonym, not the customer id: the whole point of
        // D-045's reduction is that the log never held a direct identifier, so
        // erasure has nothing to purge here.
        Assert.DoesNotContain("01LOGGEDCUSTOMER", stored[0], StringComparison.Ordinal);
    }

    // -------------------------------------------------------- staged, not sent

    [Fact]
    public void Recording_stages_the_entry_and_writes_nothing_until_the_commit()
    {
        // D-045 puts the log row in the same transaction as the operation it
        // records. An operation that rolled back but logged claims processing
        // that never happened.
        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            WriterFor(context, "01WRITERSTAGED").Record(AConsultation());

            Assert.Empty(
                _database.Query("SELECT log_id FROM processing_log WHERE log_id = '01WRITERSTAGED'"));
        }

        // The context went out of scope without a commit, so the entry is gone.
        Assert.Empty(
            _database.Query("SELECT log_id FROM processing_log WHERE log_id = '01WRITERSTAGED'"));
    }

    [Fact]
    public async Task The_unit_of_work_is_what_commits_a_staged_entry()
    {
        using var context = _database.NewContext(enforceForeignKeys: false);
        WriterFor(context, "01WRITERUOW").Record(AConsultation());

        var written = await new WaymarkUnitOfWork(context).CommitAsync(CancellationToken.None);

        Assert.Equal(1, written);
        Assert.Equal(
            ["01WRITERUOW"],
            _database.Query("SELECT log_id FROM processing_log WHERE log_id = '01WRITERUOW'"));
    }

    // ------------------------------------------------------------ the refusal

    [Fact]
    public void An_entry_naming_no_source_module_is_refused_before_it_is_staged()
    {
        // An entry nobody can trace back to a code path is not evidence of
        // anything, and source_module is NOT NULL — so without this the failure
        // arrives at the commit, taking the whole unit of work with it.
        using var context = _database.NewContext(enforceForeignKeys: false);
        var writer = WriterFor(context, "01WRITERNOMODULE");

        Assert.Throws<ArgumentException>(() => writer.Record(AConsultation(sourceModule: "  ")));
        Assert.Empty(context.ChangeTracker.Entries<ProcessingLogEntry>());
    }

    // --------------------------------------------------------- the vocabulary

    [Fact]
    public void Every_legal_basis_the_enum_offers_survives_a_write_and_a_read()
    {
        // The column has no CHECK, by D-045, because amending one is a rebuild
        // (D-022). The converter is the validation, so it has to hold for every
        // member — including vital_interest, which is the reason this enum is
        // separate from LegalBasis at all.
        var bases = Enum.GetValues<ProcessingLegalBasis>();

        using (var context = _database.NewContext(enforceForeignKeys: false))
        {
            foreach (var basis in bases)
            {
                WriterFor(context, $"01WRITERBASIS{(int)basis}").Record(
                    AConsultation() with { LegalBasis = basis });
            }

            context.SaveChanges();
        }

        using var reader = _database.NewContext(enforceForeignKeys: false);
        var stored = reader.ProcessingLog.IgnoreQueryFilters()
            .Where(e => e.LogId.StartsWith("01WRITERBASIS"))
            .Select(e => e.LegalBasis)
            .ToList();

        Assert.Equal(bases.Order().ToList(), stored.Order().ToList());
    }
}
