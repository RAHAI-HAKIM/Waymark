using Waymark.Application.Commands;
using Waymark.Domain;
using Waymark.Domain.Customers;
using Waymark.Domain.Enums;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Persistence;
using Waymark.Persistence.Privacy;

namespace Waymark.Integration.Tests;

/// <summary>
/// The executor against a real context, where staged work survives a failed
/// command unless something throws it away (D-050).
///
/// <para>
/// The fake unit of work in <c>CommandExecutorTests</c> can only show that
/// <c>Discard</c> was called. This shows what happens without it: EF keeps the
/// failed command's entities tracked, and the next <c>SaveChanges</c> on the
/// same context writes them.
/// </para>
/// </summary>
public sealed class CommandExecutorPersistenceTests(MigratedDatabaseFixture database)
    : IClassFixture<MigratedDatabaseFixture>
{
    private sealed class SequentialIds : IIdGenerator
    {
        private int _next;

        public string NewId() => $"01EXECUTOR{++_next:D16}";
    }

    private sealed record LogOne(string SourceModule, bool Fail) : ICommand<NoResult>;

    /// <summary>The customer the handler consults: an operation about one person names them (F-20).</summary>
    private static readonly Pseudonym Subject = TenantKeyFixture.PseudonymiserFor(TenantKeyFixture.KeyBytes)
        .PseudonymFor(SubjectDomain.Customer, "01EXECUTORCUSTOMER");

    private sealed class LogOneHandler : ICommandHandler<LogOne, NoResult>
    {
        public Task<NoResult> HandleAsync(
            LogOne command,
            CommandContext context,
            CancellationToken cancellationToken = default)
        {
            context.Record(new ProcessingEvent(
                Operation.Consultation,
                ProcessingLogEntrySubjectType.Customer,
                Subject: Subject,
                ActorType.Staff,
                ActorId: "staff-executor",
                ProcessingPurpose.LoyaltyLookup,
                ProcessingLegalBasis.Consent,
                command.SourceModule));

            if (command.Fail)
            {
                throw new InvalidOperationException("the handler failed after staging");
            }

            return Task.FromResult(NoResult.Value);
        }
    }

    [Fact]
    public async Task A_failed_command_leaves_nothing_for_the_next_commit_to_write()
    {
        using var context = database.NewContext(enforceForeignKeys: false);
        var ids = new SequentialIds();
        var log = new ProcessingLogWriter(context, ids, new FixedCurrentStore(null), TimeProvider.System);
        var executor = new CommandExecutor(new WaymarkUnitOfWork(context), ids, log);

        await Assert.ThrowsAsync<InvalidOperationException>(() =>
            executor.ExecuteAsync(new LogOneHandler(), new LogOne("executor-failed", Fail: true)));

        await executor.ExecuteAsync(new LogOneHandler(), new LogOne("executor-succeeded", Fail: false));

        Assert.True(
            database.Query("SELECT source_module FROM processing_log WHERE log_id LIKE '01EXECUTOR%'")
                .SequenceEqual(["executor-succeeded"]),
            "The failed command's processing_log row was written by the next command's commit. "
            + "A log entry for an operation that rolled back is evidence of processing that "
            + "never happened (D-045, D-050).");
    }
}
