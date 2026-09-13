using Waymark.Application.Commands;
using Waymark.Domain.Enums;
using Waymark.Domain.Ids;
using Waymark.Domain.Privacy;
using Waymark.Domain.Work;

namespace Waymark.Application.Tests;

/// <summary>
/// The three rules the command skeleton exists to make mechanical: nothing is
/// written while a handler runs, everything commits once, and a context stops
/// working when its unit of work closes.
/// </summary>
public sealed class CommandExecutorTests
{
    // ------------------------------------------------------------------ fakes

    private sealed class CountingIds : IIdGenerator
    {
        private int _next;

        public int Minted { get; private set; }

        public string NewId()
        {
            Minted++;
            return $"ID{_next++:D26}";
        }
    }

    private sealed class RecordingLog : IProcessingLog
    {
        public List<ProcessingEvent> Entries { get; } = [];

        public void Record(in ProcessingEvent processingEvent) => Entries.Add(processingEvent);
    }

    /// <summary>
    /// Records when a commit happened relative to the handler's work, which is
    /// the only thing these tests actually need to observe.
    /// </summary>
    private sealed class RecordingUnitOfWork : IUnitOfWork
    {
        public int Commits { get; private set; }

        public Task<int> CommitAsync(CancellationToken cancellationToken = default)
        {
            Commits++;
            return Task.FromResult(0);
        }
    }

    private sealed record Probe(Func<CommandContext, Task> Work) : ICommand<string>;

    private sealed class ProbeHandler : ICommandHandler<Probe, string>
    {
        public CommandContext? Captured { get; private set; }

        public async Task<string> HandleAsync(
            Probe command,
            CommandContext context,
            CancellationToken cancellationToken = default)
        {
            Captured = context;
            await command.Work(context);
            return "done";
        }
    }

    private static ProcessingEvent AnEvent() => new(
        Operation.Consultation,
        ProcessingLogEntrySubjectType.Customer,
        default,
        ActorType.System,
        ActorId: null,
        ProcessingPurpose.LoyaltyLookup,
        ProcessingLegalBasis.LegitimateInterest,
        SourceModule: "tests");

    private static (CommandExecutor Executor, CountingIds Ids, RecordingLog Log, RecordingUnitOfWork Work) Build()
    {
        var ids = new CountingIds();
        var log = new RecordingLog();
        var work = new RecordingUnitOfWork();
        return (new CommandExecutor(work, ids, log), ids, log, work);
    }

    // ------------------------------------------------------------------ tests

    [Fact]
    public async Task Nothing_is_committed_while_the_handler_runs()
    {
        // CLAUDE.md §3.2 says every id for the unit of work is minted before
        // anything is written. That holds because a handler has no way to
        // write — not because handlers remember to mint first.
        var (executor, _, _, work) = Build();
        var commitsSeenInside = -1;

        var handler = new ProbeHandler();
        await executor.ExecuteAsync(handler, new Probe(context =>
        {
            context.NewId();
            context.Record(AnEvent());
            commitsSeenInside = work.Commits;
            return Task.CompletedTask;
        }));

        Assert.Equal(0, commitsSeenInside);
        Assert.Equal(1, work.Commits);
    }

    [Fact]
    public async Task A_handler_that_throws_commits_nothing()
    {
        // An operation that rolled back must not leave a processing_log entry
        // claiming it happened (D-045).
        var (executor, _, log, work) = Build();

        await Assert.ThrowsAsync<InvalidTimeZoneException>(() =>
            executor.ExecuteAsync(new ProbeHandler(), new Probe(context =>
            {
                context.Record(AnEvent());
                throw new InvalidTimeZoneException("the handler failed");
            })));

        Assert.Equal(0, work.Commits);

        // The entry was staged. It is the absent commit that discards it, which
        // is why IProcessingLog stages instead of writing.
        Assert.Single(log.Entries);
    }

    [Fact]
    public async Task The_result_is_whatever_the_handler_answered()
    {
        var (executor, _, _, _) = Build();

        var result = await executor.ExecuteAsync(
            new ProbeHandler(), new Probe(_ => Task.CompletedTask));

        Assert.Equal("done", result);
    }

    [Fact]
    public async Task Ids_come_from_the_generator_and_are_counted()
    {
        var (executor, ids, _, _) = Build();

        await executor.ExecuteAsync(new ProbeHandler(), new Probe(context =>
        {
            var first = context.NewId();
            var second = context.NewId();
            Assert.NotEqual(first, second);
            Assert.Equal(2, context.IdsMinted);
            return Task.CompletedTask;
        }));

        Assert.Equal(2, ids.Minted);
    }

    [Fact]
    public async Task A_context_stops_minting_ids_once_its_command_has_finished()
    {
        // The failure this catches: a handler stores the context, kicks off
        // background work and mints an id from it later. The id attaches to a
        // transaction that has already committed, and surfaces as a foreign key
        // violation days afterwards rather than as an exception at the call site.
        var (executor, _, _, _) = Build();
        var handler = new ProbeHandler();

        await executor.ExecuteAsync(handler, new Probe(_ => Task.CompletedTask));

        var escaped = handler.Captured!;
        var thrown = Assert.Throws<InvalidOperationException>(() => escaped.NewId());
        Assert.Contains("after the command had finished", thrown.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_context_stops_recording_once_its_command_has_finished()
    {
        var (executor, _, log, _) = Build();
        var handler = new ProbeHandler();

        await executor.ExecuteAsync(handler, new Probe(_ => Task.CompletedTask));

        Assert.Throws<InvalidOperationException>(() => handler.Captured!.Record(AnEvent()));
        Assert.Empty(log.Entries);
    }

    [Fact]
    public async Task Entries_recorded_during_the_command_reach_the_log()
    {
        var (executor, _, log, _) = Build();

        await executor.ExecuteAsync(new ProbeHandler(), new Probe(context =>
        {
            context.Record(AnEvent());
            context.Record(AnEvent() with { Operation = Operation.Disclosure });
            Assert.Equal(2, context.EventsRecorded);
            return Task.CompletedTask;
        }));

        Assert.Equal(
            [Operation.Consultation, Operation.Disclosure],
            log.Entries.Select(entry => entry.Operation));
    }

    [Fact]
    public async Task A_null_handler_or_command_is_refused_before_anything_runs()
    {
        var (executor, _, _, work) = Build();

        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.ExecuteAsync<Probe, string>(null!, new Probe(_ => Task.CompletedTask)));
        await Assert.ThrowsAsync<ArgumentNullException>(() =>
            executor.ExecuteAsync(new ProbeHandler(), null!));

        Assert.Equal(0, work.Commits);
    }
}
