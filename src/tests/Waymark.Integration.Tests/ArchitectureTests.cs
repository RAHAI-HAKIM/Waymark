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
    /// Only Waymark.Pseudonymisation may hold the tenant key.
    ///
    /// <para>
    /// Renamed 11/09/2026. This was
    /// <c>Only_Pseudonymisation_may_name_the_identity_database</c>, and D-039
    /// removed that database — so the test still passed while guarding nothing.
    /// What needs guarding now is the key: the pseudonym is a keyed hash, and
    /// the separation the DPIA relies on is that nobody else can compute one
    /// (CLAUDE.md §3.5).
    /// </para>
    /// </summary>
    [Fact]
    public void Only_Pseudonymisation_may_hold_the_tenant_key()
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

    /// <summary>
    /// Nobody below the hosts touches cryptography except Waymark.Pseudonymisation.
    ///
    /// <para>
    /// The pseudonym is <c>HMAC-SHA256(tenant_key, …)</c>. A second place that
    /// computes one is a second place that holds the key, and the first sign of
    /// it would be a compliance question rather than a test failure. This is the
    /// mechanical half of "only this project computes a pseudonym" — the
    /// reference test above catches the obvious route, this one catches
    /// somebody reimplementing it in place.
    /// </para>
    /// <para>
    /// Hosts are excluded: TLS is cryptography and they are entitled to it.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_Pseudonymisation_may_compute_a_pseudonym()
    {
        Assembly[] everyoneElse = [Domain, Contracts, Application, Persistence, Hardware, Sync];

        foreach (var assembly in everyoneElse)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOn("System.Security.Cryptography")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                Explain(result, $"{assembly.GetName().Name} reached for cryptography. Only Waymark.Pseudonymisation may."));
        }
    }

    /// <summary>
    /// The key that must never reach the cloud is not held by anything that
    /// talks to the cloud, in either direction.
    /// </summary>
    [Fact]
    public void Pseudonymisation_and_Sync_know_nothing_of_each_other()
    {
        var result = Types.InAssembly(Pseudonymisation)
            .ShouldNot()
            .HaveDependencyOn("Waymark.Sync")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            Explain(result, "Waymark.Pseudonymisation reached toward the outbox. The boundary is symmetric."));
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

    /// <summary>
    /// Contracts references nothing at all — not even Domain.
    ///
    /// <para>
    /// It is the shape shared with the TypeScript clients and the Python engine,
    /// so it has to be serialisable and free of behaviour. A reference to Domain
    /// would let <c>Money</c>, <c>Quantity</c> or an entity into the wire format,
    /// and the first consumer that could not represent one would find out at
    /// runtime in another language (D-044).
    /// </para>
    /// <para>
    /// This is why the contract mirrors the schema's vocabulary in its own enums
    /// rather than reusing Domain's, and why <c>ContractsMatchSchemaTests</c>
    /// exists to stop the two drifting.
    /// </para>
    /// </summary>
    [Fact]
    public void Contracts_depends_on_nothing()
    {
        string[] framework = ["System", "netstandard", "mscorlib"];

        var offenders = Contracts.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "(unnamed)")
            .Where(name => !framework.Any(
                prefix => name.Equals(prefix, StringComparison.Ordinal)
                          || name.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Waymark.Contracts took a dependency:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nContracts is the wire format, shared with TypeScript and Python. It "
            + "mirrors the schema and holds no behaviour, which is why it can afford to "
            + "depend on nothing.");
    }

    /// <summary>
    /// Domain references no package outside a named allowlist.
    ///
    /// <para>
    /// <see cref="Domain_depends_on_nothing"/> covers the eight sibling
    /// projects, which is the mistake somebody is most likely to make. This
    /// covers the one it cannot see: a NuGet package added to Domain. "Zero
    /// dependencies" was written about project references, and a rule whose
    /// value comes from being absolute needs the other half stated too
    /// (decisions.md D-038).
    /// </para>
    /// <para>
    /// <b>The allowlist is empty, and that is the intended state.</b> The
    /// <c>Ulid</c> package lives in <c>Waymark.Application</c>, because ids come
    /// from the <c>IIdGenerator</c> port and the port is all Domain needs to
    /// know. Adding a name here is an architecture change: say why in a decision
    /// entry first.
    /// </para>
    /// </summary>
    [Fact]
    public void Domain_references_no_package_outside_the_allowlist()
    {
        string[] allowlist = [];
        string[] framework = ["System", "netstandard", "mscorlib"];

        var offenders = Domain.GetReferencedAssemblies()
            .Select(reference => reference.Name ?? "(unnamed)")
            .Where(name => !framework.Any(
                prefix => name.Equals(prefix, StringComparison.Ordinal)
                          || name.StartsWith(prefix + ".", StringComparison.Ordinal)))
            .Where(name => !allowlist.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"""
            Waymark.Domain took a package dependency.

            Outside the allowlist:
              {string.Join(Environment.NewLine + "  ", offenders)}

            Domain has zero dependencies (CLAUDE.md §2.1). If this package really
            belongs there, add it to the allowlist above and write the decision
            entry that says why. Do not widen the framework prefixes.
            """);
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
