using System.Reflection;
using Waymark.Domain.Privacy;

namespace Waymark.Application.Tests;

/// <summary>
/// D-045's "enforced by the type, not by the helper's body", asserted rather
/// than described.
///
/// <para>
/// <c>Pseudonym</c> and <c>IProcessingLog</c> both promise this class exists.
/// What it guards is a mistake that costs nothing at write time: <c>subject_id</c>
/// is <c>TEXT</c>, so a customer id assigned to it stores perfectly, passes every
/// constraint, and is discovered by a regulator rather than by a test. The only
/// thing standing between a call site and that row is the absence of a way to
/// say it — so the absence is what gets tested.
/// </para>
/// <para>
/// These are reflection tests because the thing being asserted is a shape, and a
/// shape is what a future contributor changes in a hurry. A compile-time
/// equivalent would only prove today's call sites are fine.
/// </para>
/// </summary>
public sealed class ProcessingLogContractTests
{
    private static readonly Type Log = typeof(IProcessingLog);
    private static readonly Type Event = typeof(ProcessingEvent);

    /// <summary>
    /// Strips the <c>&amp;</c> that <c>in</c>, <c>ref</c> and <c>out</c> put on a
    /// parameter type. Without this the subject parameter reads as
    /// <c>ProcessingEvent&amp;</c> and every comparison below silently fails to
    /// match — which would be a test that passes by never finding anything.
    /// </summary>
    private static Type Unwrap(Type type) => type.IsByRef ? type.GetElementType()! : type;

    [Fact]
    public void The_log_has_exactly_one_way_in()
    {
        var methods = Log.GetMethods().Select(method => method.Name).Order(StringComparer.Ordinal).ToList();

        Assert.Equal(["Record"], methods);
    }

    [Fact]
    public void Record_takes_a_processing_event_and_nothing_else()
    {
        var overloads = Log.GetMethods().Where(method => method.Name == "Record").ToList();

        Assert.Single(overloads);

        var parameters = overloads[0].GetParameters();
        Assert.Single(parameters);
        Assert.Equal(Event, Unwrap(parameters[0].ParameterType));
    }

    [Fact]
    public void No_member_of_the_log_accepts_a_string()
    {
        // The overload that would undo the whole design is Record(string
        // customerId, ...). It would be added in good faith, by somebody who
        // had a customer id in hand and a deadline.
        var offenders = Log.GetMethods()
            .SelectMany(method => method.GetParameters()
                .Where(parameter => Unwrap(parameter.ParameterType) == typeof(string))
                .Select(parameter => $"{method.Name}({parameter.Name}: string)"))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"""
            IProcessingLog gained a string parameter:
              {string.Join(Environment.NewLine + "  ", offenders)}

            A subject is named with a Pseudonym, which cannot be built outside
            Waymark.Pseudonymisation (D-045). A string parameter is a route to a
            log row carrying a direct identifier, and subject_id is TEXT — so
            nothing downstream would notice.
            """);
    }

    [Fact]
    public void The_subject_of_an_event_is_a_pseudonym()
    {
        var subject = Event.GetProperty(nameof(ProcessingEvent.Subject));

        Assert.NotNull(subject);
        Assert.Equal(typeof(Pseudonym), subject.PropertyType);
    }

    [Fact]
    public void An_event_cannot_be_built_with_a_string_subject()
    {
        // Belt and braces for the record's own constructors: a second
        // constructor taking (string subject, ...) would be just as effective a
        // hole as an overload on the interface.
        var offenders = Event.GetConstructors()
            .Select(constructor => constructor.GetParameters())
            .Where(parameters => parameters.Any(
                parameter => parameter.Name is "subject" or "subjectId"
                             && Unwrap(parameter.ParameterType) == typeof(string)))
            .Select(parameters => string.Join(", ", parameters.Select(p => $"{p.ParameterType.Name} {p.Name}")))
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "ProcessingEvent gained a constructor taking a string subject:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void An_event_cannot_choose_its_own_id_time_or_store()
    {
        // D-045 puts log_id, occurred_at and store_id under the writer's
        // control. A caller able to set them can backdate a row, collide an id,
        // or file the operation under another store — where the fail-closed
        // query filter (§3.3) makes it invisible to the process that wrote it.
        string[] writerOwned = ["LogId", "OccurredAt", "StoreId"];

        var declared = Event.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Select(property => property.Name)
            .Intersect(writerOwned, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            declared.Count == 0,
            $"""
            ProcessingEvent lets the caller set: {string.Join(", ", declared)}.

            Those three columns belong to ProcessingLogWriter — the id from
            IIdGenerator, the time from the injected TimeProvider, the store from
            ICurrentStore. Evidence whose timestamps the audited party chose is
            worth less than evidence whose timestamps it did not (D-045).
            """);
    }

    [Fact]
    public void A_subjectless_event_is_expressible_and_is_the_only_way_to_write_no_subject()
    {
        // subject_id is nullable because a retention sweep has no subject. The
        // route to NULL has to be a Pseudonym that was never defined, not a
        // null string — otherwise "no subject" and "subject we failed to
        // pseudonymise" look identical in the table.
        var subjectless = default(Pseudonym);

        Assert.False(subjectless.IsDefined);
        Assert.Equal(string.Empty, subjectless.Value);
    }
}
