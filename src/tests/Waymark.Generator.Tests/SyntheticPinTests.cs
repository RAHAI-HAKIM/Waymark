using Waymark.Domain.Organisation;
using Waymark.Generator.Simulation;

namespace Waymark.Generator.Tests;

/// <summary>
/// The synthetic PIN hash the generator writes is the one Domain refuses (session A2).
///
/// <para>
/// Nothing that ships may reference <c>Waymark.Generator</c> (D-054), so the string exists in
/// two places on purpose. This test is the only thing holding them together. If they drift,
/// every staff row in every generated store becomes an account somebody could try to log into
/// — and the generated store is what the demo, the walkthroughs and the integration tests all
/// run against.
/// </para>
/// </summary>
public sealed class SyntheticPinTests
{
    [Fact]
    public void The_generator_writes_exactly_the_hash_domain_refuses()
    {
        Assert.Equal(StaffPin.NeverUsable, ReferenceData.SyntheticPinHash);
    }

    [Fact]
    public void And_domain_refuses_it()
    {
        Assert.False(StaffPin.IsUsable(ReferenceData.SyntheticPinHash));
    }
}
