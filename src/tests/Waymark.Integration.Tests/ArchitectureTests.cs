using System.Reflection;
using System.Text.Json;
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
    /// already pseudonymised. Sync being structurally unable to reach the
    /// tenant key is what makes that a fact rather than a promise
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
    /// Sync cannot reach the pseudonymisation boundary through the <i>port</i>
    /// either.
    ///
    /// <para>
    /// Added 12/09/2026 with W7, because until then there was nothing to reach.
    /// <see cref="Sync_must_not_reference_Pseudonymisation"/> closes the obvious
    /// route — a project reference — but <c>IPseudonymiser</c> is declared in
    /// Domain, and Domain is referenced by everybody. Injecting the port would
    /// hand Sync the ability to turn a customer id into the pseudonym the cloud
    /// stores, without adding a single forbidden reference.
    /// </para>
    /// <para>
    /// The whole namespace, not just that one interface: pseudonymisation
    /// happens <i>before</i> the outbox, so what Sync carries is already tier-2
    /// shaped text (CLAUDE.md §4). Sync has no honest use for <c>Pseudonym</c>,
    /// <c>ProcessingEvent</c> or the key-check value either, and a rule that
    /// admits exceptions is a rule somebody argues with.
    /// </para>
    /// </summary>
    [Fact]
    public void Sync_knows_nothing_of_the_privacy_types()
    {
        var result = Types.InAssembly(Sync)
            .ShouldNot()
            .HaveDependencyOn("Waymark.Domain.Privacy")
            .GetResult();

        Assert.True(
            result.IsSuccessful,
            Explain(result, "Waymark.Sync reached for a privacy type. What reaches the outbox is already pseudonymised."));
    }

    /// <summary>
    /// Only Application and the hosts may hold the ability to pseudonymise.
    ///
    /// <para>
    /// Holding <c>IPseudonymiser</c> is a capability: it turns a direct
    /// identifier into the cloud's name for the same person, which is half of
    /// re-identification. Application needs it, because pseudonymisation happens
    /// there, before the outbox. Nothing else does — Persistence stores whatever
    /// it is handed, Contracts is a wire shape, Hardware prints receipts.
    /// </para>
    /// </summary>
    [Fact]
    public void Only_Application_and_the_hosts_may_pseudonymise()
    {
        Assembly[] mustNotBeAbleTo = [Contracts, Persistence, Hardware, Sync];

        foreach (var assembly in mustNotBeAbleTo)
        {
            var result = Types.InAssembly(assembly)
                .ShouldNot()
                .HaveDependencyOnAny(
                    "Waymark.Domain.Privacy.IPseudonymiser",
                    "Waymark.Domain.Privacy.ITenantKeyCheck")
                .GetResult();

            Assert.True(
                result.IsSuccessful,
                Explain(result, $"{assembly.GetName().Name} acquired the ability to pseudonymise."));
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
    /// <para>
    /// <b>This test alone is not enough.</b> It reads the compiled assembly's
    /// references, and the compiler emits a reference only for a package Domain
    /// actually <i>uses</i>. An unused one is dropped from the IL and passes
    /// here while still being restored, resolved and shipped — the same blind
    /// spot D-049 recorded for an unused <c>ProjectReference</c>.
    /// <see cref="Domain_ships_no_package_outside_the_allowlist"/> is the half
    /// that catches it.
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

    /// <summary>
    /// Domain ships no package outside the allowlist — used or not.
    ///
    /// <para>
    /// Written 12/09/2026, after <c>Waymark.Domain.csproj</c> was found carrying
    /// <c>Microsoft.EntityFrameworkCore.SqlServer</c>. It had been there since
    /// 04/09/2026, added in passing by a commit about EF tooling that never
    /// mentioned Domain, and
    /// <see cref="Domain_references_no_package_outside_the_allowlist"/> passed
    /// the whole time: Domain never used an EF type, so the compiler emitted no
    /// reference and there was nothing in the IL to find. The package was
    /// nonetheless restored, resolved and copied beside the assembly — the
    /// project with zero dependencies shipped a SQL Server driver.
    /// </para>
    /// <para>
    /// The deps file is the right place to look because it is what the runtime
    /// reads: it lists what a library was built against whether or not a single
    /// line of code touched it. A test that only inspects IL is a test that
    /// cannot see the mistake most likely to be made — one <c>dotnet add
    /// package</c> in the wrong directory.
    /// </para>
    /// </summary>
    [Fact]
    public void Domain_ships_no_package_outside_the_allowlist()
    {
        string[] allowlist = [];

        var offenders = ShippedDependenciesOf("Waymark.Domain")
            .Where(name => !allowlist.Contains(name, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            $"""
            Waymark.Domain was built against a package it does not use:
              {string.Join(Environment.NewLine + "  ", offenders)}

            Zero dependencies means the project file too, not only the IL
            (CLAUDE.md §2.1). Remove the PackageReference from
            Waymark.Domain.csproj. If it truly belongs there, add it to both
            allowlists and write the decision entry that says why.
            """);
    }

    /// <summary>
    /// What <paramref name="library"/> was built against, from this test
    /// assembly's own deps file.
    ///
    /// <para>
    /// It throws rather than returning nothing when the library is missing. A
    /// lookup that quietly finds no entry is how the test above would go back to
    /// passing while guarding nothing, which is the failure it was written to
    /// end.
    /// </para>
    /// </summary>
    private static IReadOnlyList<string> ShippedDependenciesOf(string library)
    {
        var deps = Path.ChangeExtension(typeof(ArchitectureTests).Assembly.Location, ".deps.json");

        using var document = JsonDocument.Parse(File.ReadAllText(deps));

        foreach (var target in document.RootElement.GetProperty("targets").EnumerateObject())
        {
            foreach (var entry in target.Value.EnumerateObject())
            {
                if (!entry.Name.StartsWith(library + "/", StringComparison.Ordinal))
                {
                    continue;
                }

                return entry.Value.TryGetProperty("dependencies", out var dependencies)
                    ? [.. dependencies.EnumerateObject().Select(dependency => dependency.Name)]
                    : [];
            }
        }

        throw new InvalidOperationException(
            $"{library} is not in {deps}. The deps file is how this suite sees what a "
            + "project was built against; without the entry the check proves nothing.");
    }

    /// <summary>
    /// The POS never opens the store database (CLAUDE.md §2.2).
    ///
    /// <para>
    /// It talks HTTP to StoreServer even at Basic tier, where both run on one
    /// machine: one code path, not two. The rule is checked on the project graph
    /// rather than the IL for two reasons. This test project cannot reference
    /// <c>Waymark.Pos</c> (an Avalonia <c>WinExe</c>); and an unused reference is
    /// dropped from the IL anyway, which is the blind spot D-049 and D-050 recorded.
    /// </para>
    /// <para>
    /// The walk is <b>transitive</b>, because Pos reaching Persistence through
    /// Application is the same violation as a direct reference. SQLite itself is
    /// not forbidden: the POS will legitimately own its Level-2 cache. EF Core and
    /// the store's own projects are.
    /// </para>
    /// </summary>
    [Fact]
    public void Pos_cannot_reach_the_store_database()
    {
        string[] forbiddenProjects = ["Waymark.Persistence", "Waymark.Application", "Waymark.StoreServer", "Waymark.Pseudonymisation"];
        const string forbiddenPackagePrefix = "Microsoft.EntityFrameworkCore";

        var pos = Path.Combine(SourceRoot(), "Waymark.Pos", "Waymark.Pos.csproj");
        var reachable = ProjectClosure(pos);

        var offenders = reachable.Keys
            .Where(project => forbiddenProjects.Contains(project, StringComparer.Ordinal))
            .Concat(reachable
                .SelectMany(entry => entry.Value.Select(package => $"{package} (package, via {entry.Key})"))
                .Where(package => package.StartsWith(forbiddenPackagePrefix, StringComparison.Ordinal)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Waymark.Pos can reach the store database:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nThe POS talks HTTP to StoreServer, even on one machine (CLAUDE.md §2.2). "
            + "Its Level-2 cache is its own SQLite file with its own schema, never the store's.");
    }

    // -----------------------------------------------------------------------
    // The synthetic store generator
    // -----------------------------------------------------------------------

    /// <summary>
    /// Nothing that ships depends on the generator (D-046).
    ///
    /// <para>
    /// The generator is a simulator that writes rows the application never would —
    /// a year of history in one run, entities built without handlers. A production
    /// project that could call into it could fabricate store data. Only test
    /// projects may reference it.
    /// </para>
    /// </summary>
    [Fact]
    public void Nothing_that_ships_references_the_generator()
    {
        var root = SourceRoot();

        var offenders = Directory.EnumerateFiles(root, "*.csproj", SearchOption.AllDirectories)
            .Where(path => !IsUnder(path, Path.Combine(root, "tests")))
            .Where(path => !string.Equals(Path.GetFileNameWithoutExtension(path), "Waymark.Generator", StringComparison.Ordinal))
            .Where(path => !IsUnder(path, Path.Combine(root, "Waymark.Generator", "bin"))
                           && !IsUnder(path, Path.Combine(root, "Waymark.Generator", "obj")))
            .Where(path => ProjectClosure(path).ContainsKey("Waymark.Generator"))
            .Select(Path.GetFileNameWithoutExtension)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "A production project can reach Waymark.Generator:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nThe generator fabricates store history for tests and demos. Only test "
            + "projects may reference it (D-046).");
    }

    /// <summary>
    /// The generator reaches only what a simulated store needs.
    ///
    /// <para>
    /// It writes a store database, so Domain, Application, Persistence and Contracts.
    /// It must not hold the tenant key (Pseudonymisation), drive sync (Sync), open
    /// hardware, or embed either host. Checked transitively on the project graph, as
    /// for the POS rule above.
    /// </para>
    /// </summary>
    [Fact]
    public void The_generator_reaches_nothing_beyond_a_store_database()
    {
        string[] forbidden = ["Waymark.Pseudonymisation", "Waymark.Sync", "Waymark.Hardware", "Waymark.StoreServer", "Waymark.Pos"];

        var generator = Path.Combine(SourceRoot(), "Waymark.Generator", "Waymark.Generator.csproj");

        var offenders = ProjectClosure(generator).Keys
            .Where(project => forbidden.Contains(project, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            offenders.Count == 0,
            "Waymark.Generator can reach:\n  "
            + string.Join("\n  ", offenders)
            + "\n\nIt simulates a store database and nothing else (D-046).");
    }

    /// <summary>
    /// The generator's randomness is not cryptography.
    ///
    /// <para>
    /// <c>Draw(stream, coords)</c> is a plain mixing function, deliberately. A hash
    /// primitive from <c>System.Security.Cryptography</c> would put a second
    /// HMAC-capable place in the codebase next to the one that holds the tenant key,
    /// which <see cref="Only_Pseudonymisation_may_compute_a_pseudonym"/> exists to
    /// prevent everywhere else.
    /// </para>
    /// </summary>
    [Fact]
    public void The_generator_uses_no_cryptography()
    {
        var result = Types.InAssembly(typeof(Waymark.Generator.AssemblyMarker).Assembly)
            .ShouldNot()
            .HaveDependencyOn("System.Security.Cryptography")
            .GetResult();

        Assert.True(result.IsSuccessful, Explain(result, "Waymark.Generator reached for cryptography. Its randomness is a plain mixing function."));
    }

    private static bool IsUnder(string path, string directory) =>
        Path.GetFullPath(path).StartsWith(
            Path.GetFullPath(directory) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);

    /// <summary>
    /// Every project reachable from <paramref name="csproj"/> by
    /// <c>ProjectReference</c>, itself included, mapped to the packages it references.
    /// </summary>
    private static Dictionary<string, List<string>> ProjectClosure(string csproj)
    {
        var found = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var pending = new Stack<string>([Path.GetFullPath(csproj)]);

        while (pending.Count > 0)
        {
            var path = pending.Pop();
            var name = Path.GetFileNameWithoutExtension(path);
            if (found.ContainsKey(name))
            {
                continue;
            }

            var project = System.Xml.Linq.XDocument.Load(path);
            found[name] = [.. project.Descendants("PackageReference")
                .Select(reference => (string?)reference.Attribute("Include") ?? string.Empty)];

            foreach (var reference in project.Descendants("ProjectReference"))
            {
                var include = ((string?)reference.Attribute("Include") ?? string.Empty).Replace('\\', Path.DirectorySeparatorChar);
                pending.Push(Path.GetFullPath(Path.Combine(Path.GetDirectoryName(path)!, include)));
            }
        }

        return found;
    }

    /// <summary>The <c>src</c> directory, found by walking up to <c>Waymark.sln</c>.</summary>
    private static string SourceRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "Waymark.sln")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException(
            "Waymark.sln was not found above the test output. Without the source tree this "
            + "rule would check nothing, which is worse than failing.");
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
