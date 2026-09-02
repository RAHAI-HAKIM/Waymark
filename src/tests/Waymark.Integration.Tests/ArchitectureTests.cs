using System.Reflection;
using NetArchTest.Rules;

namespace Waymark.Integration.Tests;

/// <summary>
/// CLAUDE.md §2.1 as executable rules.
///
/// These are not style tests. Each one names a boundary that is silently
/// wrong if crossed: the code compiles, the screen looks right, and the
/// damage shows up in a regulator's question months later.
///
/// If one of these fails, the fix is to remove the reference — not to
/// relax the test.
/// </summary>
public sealed class ArchitectureTests
{
    private static readonly Assembly Domain = typeof(Waymark.Domain.AssemblyMarker).Assembly;
    private static readonly Assembly Contracts = typeof(Waymark.Contracts.AssemblyMarker).Assembly;
    private static readonly Assembly Application = typeof(Waymark.Application.AssemblyMarker).Assembly;
    private static readonly Assembly Persistence = typeof(Waymark.Persistence.AssemblyMarker).Assembly;
    private static readonly Assembly Hardware = typeof(Waymark.Hardware.AssemblyMarker).Assembly;
    private static readonly Assembly Pseudonymisation = typeof(Waymark.Pseudonymisation.AssemblyMarker).Assembly;
    private static readonly Assembly Sync = typeof(Waymark.Sync.AssemblyMarker).Assembly;

    // -----------------------------------------------------------------------
    // The legal boundary
    // -----------------------------------------------------------------------

    /// <summary>
    /// The single most important test in the repository.
    ///
    /// Pseudonymisation happens before the outbox, so what Sync carries is
    /// already tier-2 shaped. Sync being structurally unable to reach the
    /// mapping is what makes that a fact rather than a promise
    /// (CLAUDE.md §2.1, §4; DPIA risk R9).
    /// </summary>
    [Fact]
    public void Sync_must_not_reference_Pseudonymisation()
    {
        var result = Types.InAssembly(Sync)
            .ShouldNot()
            .HaveDependencyOn("Waymark.Pseudonymisation")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain(result, "Waymark.Sync reached into the pseudonymisation boundary."));
    }

    /// <summary>
    /// Only Waymark.Pseudonymisation may name the identity database. Any
    /// other project holding that path or opening that connection breaks
    /// CLAUDE.md §3.4.
    /// </summary>
    [Fact]
    public void Only_Pseudonymisation_may_name_the_identity_database()
    {
        Assembly[] everyoneElse = [Domain, Contracts, Application, Persistence, Hardware, Sync];

        foreach (var assembly in everyoneElse)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("Waymark.Pseudonymisation")
                .GetResult();

            Assert.True(result.IsSuccessful, Explain(result, $"{assembly.GetName().Name} referenced Waymark.Pseudonymisation."));
        }
    }

    // -----------------------------------------------------------------------
    // Dependencies point inward
    // -----------------------------------------------------------------------

    /// <summary>
    /// Domain has zero dependencies. Not Persistence, not Contracts, not
    /// EF Core. It declares the interfaces; everyone else implements them.
    /// </summary>
    [Fact]
    public void Domain_depends_on_nothing()
    {
        var result = Types.InAssembly(Domain)
            .ShouldNot()
            .HaveDependencyOnAny(
                "Waymark.Contracts",
                "Waymark.Application",
                "Waymark.Persistence",
                "Waymark.Hardware",
                "Waymark.Pseudonymisation",
                "Waymark.Sync",
                "Waymark.StoreServer",
                "Waymark.Pos")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain(result, "Waymark.Domain acquired a dependency."));
    }

    /// <summary>
    /// No SQL and no ORM outside Persistence. Domain never names SQLite.
    /// </summary>
    [Fact]
    public void Persistence_technology_stays_in_Persistence()
    {
        Assembly[] mustBeIgnorantOfTheDatabase = [Domain, Contracts, Application, Hardware, Sync];

        foreach (var assembly in mustBeIgnorantOfTheDatabase)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Microsoft.EntityFrameworkCore",
                    "Microsoft.Data.Sqlite",
                    "SQLitePCL")
                .GetResult();

            Assert.True(result.IsSuccessful, Explain(result, $"{assembly.GetName().Name} named the database directly."));
        }
    }

    /// <summary>
    /// No HTTP below the hosts. A use case that calls out over the network
    /// is a use case that cannot be tested without one.
    /// </summary>
    [Fact]
    public void No_HTTP_below_the_hosts()
    {
        Assembly[] mustNotSpeakHttp = [Domain, Contracts, Application, Persistence, Hardware, Pseudonymisation];

        foreach (var assembly in mustNotSpeakHttp)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("System.Net.Http", "Microsoft.AspNetCore")
                .GetResult();

            Assert.True(result.IsSuccessful, Explain(result, $"{assembly.GetName().Name} took a dependency on HTTP."));
        }
    }

    /// <summary>
    /// Infrastructure points up into Domain and no further sideways. A
    /// repository implementation that knows about use cases has inverted the
    /// layering.
    /// </summary>
    [Fact]
    public void Infrastructure_does_not_depend_on_Application_or_the_hosts()
    {
        Assembly[] infrastructure = [Persistence, Hardware, Pseudonymisation, Sync];

        foreach (var assembly in infrastructure)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny("Waymark.Application", "Waymark.StoreServer", "Waymark.Pos")
                .GetResult();

            Assert.True(result.IsSuccessful, Explain(result, $"{assembly.GetName().Name} depends upward."));
        }
    }

    // -----------------------------------------------------------------------

    private static string Explain(TestResult result, string headline)
    {
        var offenders = result.FailingTypeNames is null
            ? "(no type names reported)"
            : string.Join(Environment.NewLine + "  ", result.FailingTypeNames);

        return $"""
            {headline}

            Offending types:
              {offenders}

            This boundary is in CLAUDE.md §2.1. Remove the reference. Do not
            relax this test.
            """;
    }
}
