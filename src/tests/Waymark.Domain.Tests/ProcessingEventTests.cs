using Waymark.Domain.Enums;
using Waymark.Domain.Privacy;

namespace Waymark.Domain.Tests;

/// <summary>
/// D-061 and D-115: which logbook operations must name the one person they are about, and when a row
/// may carry a count. The silent failure is an operation added to the list and placed nowhere: it is
/// then written by anybody with no subject at all, and the logbook says something happened to nobody.
/// </summary>
public sealed class ProcessingEventTests
{
    /// <summary>About one person (D-061; D-115, Hakim 10/10).</summary>
    private static readonly Operation[] OnePerson =
    [
        Operation.Consultation, Operation.Modification, Operation.Disclosure, Operation.Transmission, Operation.Erasure, Operation.ReIdentification,
        Operation.Extraction, Operation.Dissemination, Operation.Locking, Operation.Destruction,
    ];

    /// <summary>May run over many subjects at once.</summary>
    private static readonly Operation[] Many =
    [
        Operation.Collection, Operation.Pseudonymisation,
        Operation.Recording, Operation.Organisation, Operation.Storage, Operation.Use, Operation.Alignment, Operation.Interconnection, Operation.Encryption,
    ];

    private static ProcessingEvent An(Operation operation, ActorType actor, int? count = null) => new(
        operation, ProcessingLogEntrySubjectType.Customer, default, actor, "someone", ProcessingPurpose.LoyaltyLookup, ProcessingLegalBasis.Consent,
        "Waymark.Domain.Tests", SubjectCount: count);

    [Fact]
    public void Every_operation_of_the_logbook_is_placed_on_one_side_or_the_other()
    {
        // A new member of the enum fails here until somebody decides which it is.
        Assert.Equal(Enum.GetValues<Operation>().Order(), OnePerson.Concat(Many).Order());
        Assert.Empty(OnePerson.Intersect(Many));
    }

    [Fact]
    public void The_operations_about_one_person_are_exactly_these()
    {
        Assert.All(OnePerson, operation => Assert.True(ProcessingEvent.NamesOnePerson(operation), $"{operation} may be logged with no subject."));
        Assert.All(Many, operation => Assert.False(ProcessingEvent.NamesOnePerson(operation), $"{operation} cannot be logged over many subjects."));
    }

    [Theory]
    [InlineData(ActorType.Staff)]
    [InlineData(ActorType.Engine)]
    [InlineData(ActorType.Processor)] // a technician is held to the rule as staff are (D-114)
    public void Whoever_acts_names_the_one_person_and_only_a_system_task_may_not(ActorType actor)
    {
        Assert.All(OnePerson, operation => Assert.False(An(operation, actor).IsComplete, $"{actor} logged {operation} about nobody."));
        Assert.All(OnePerson, operation => Assert.True(An(operation, ActorType.System).IsComplete));
        Assert.All(Many, operation => Assert.True(An(operation, actor).IsComplete));
    }

    [Theory]
    [InlineData(null, true)]
    [InlineData(1, true)]
    [InlineData(4_000, true)]
    [InlineData(0, false)]
    [InlineData(-1, false)]
    public void A_count_is_none_or_above_zero_on_an_entry_that_names_no_one(int? count, bool valid)
    {
        Assert.Equal(valid, An(Operation.Use, ActorType.Engine, count).CountIsValid);
    }
}
