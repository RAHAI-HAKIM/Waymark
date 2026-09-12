using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Waymark.Contracts.Intents;
using Waymark.Contracts.Recommendations;
using Waymark.Contracts.Sync;

namespace Waymark.Integration.Tests;

/// <summary>
/// <c>Waymark.Contracts</c> does not invent a shape; it mirrors the tables the
/// engine writes and the UI reads (decisions.md D-044). A mirror is only worth
/// having if it stays one, so these tests compare the contract against the
/// constraints the database actually enforces — not against a second copy of the
/// same list.
///
/// <para>
/// The failure this prevents: a value is added to a CHECK in a migration, the
/// contract is not updated, and the store rejects a message the cloud considers
/// valid. Nothing in either codebase would notice until a message failed in
/// production, and the error would surface far from its cause.
/// </para>
/// </summary>
public sealed partial class ContractsMatchSchemaTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _database;

    public ContractsMatchSchemaTests(MigratedDatabaseFixture database) => _database = database;

    [GeneratedRegex(@"'([A-Za-z0-9_]+)'")]
    private static partial Regex QuotedValue();

    /// <summary>The values a CHECK constraint permits, read from the live database.</summary>
    private List<string> AllowedBy(string table, string constraint)
    {
        var sql = _database.Query(
            $"SELECT sql FROM sqlite_schema WHERE type = 'table' AND name = '{table}'").Single();

        var start = sql.IndexOf($"CONSTRAINT \"{constraint}\" CHECK (", StringComparison.Ordinal);
        Assert.True(start >= 0, $"{table} has no constraint named {constraint}.");

        var end = sql.IndexOf("),", start, StringComparison.Ordinal);
        var body = sql[start..(end < 0 ? sql.Length : end)];

        return [.. QuotedValue().Matches(body).Select(m => m.Groups[1].Value).Order(StringComparer.Ordinal)];
    }

    /// <summary>The wire values an enum serialises to, in order.</summary>
    private static List<string> WireValuesOf(Type enumType) =>
        [.. Enum.GetNames(enumType)
            .Select(name => enumType.GetField(name)!
                .GetCustomAttribute<JsonStringEnumMemberNameAttribute>()?.Name
                ?? throw new InvalidOperationException(
                    $"{enumType.Name}.{name} has no [JsonStringEnumMemberName]. Without it the wire "
                    + "value is the C# member name, which no CHECK constraint allows."))
            .Order(StringComparer.Ordinal)];

    public static TheoryData<string, string, string> Mirrors => new()
    {
        { nameof(Department), "recommendations", "ck_recommendations_department" },
        { nameof(Urgency), "recommendations", "ck_recommendations_urgency" },
        { nameof(ActionType), "recommendations", "ck_recommendations_action_type" },
        { nameof(RecommendationSubject), "recommendations", "ck_recommendations_subject_type" },
        { nameof(RecommendationStatus), "recommendations", "ck_recommendations_status" },
        { nameof(DecisionKind), "recommendation_decisions", "ck_recommendation_decisions_decision" },
        { nameof(DecisionOrigin), "recommendation_decisions", "ck_recommendation_decisions_origin" },
        { nameof(IntentType), "intents", "ck_intents_intent_type" },
        { nameof(IntentStatus), "intents", "ck_intents_status" },
        { nameof(OutboundChannel), "outbox", "ck_outbox_channel" },
        { nameof(InboundChannel), "inbox", "ck_inbox_channel" },
        { nameof(InboundStatus), "inbox", "ck_inbox_status" },
    };

    [Theory]
    [MemberData(nameof(Mirrors))]
    public void A_contract_enum_says_exactly_what_its_CHECK_constraint_allows(
        string enumName, string table, string constraint)
    {
        var enumType = typeof(RecommendationEnvelope).Assembly.GetTypes()
            .Concat(typeof(IntentMessage).Assembly.GetTypes())
            .First(type => type.IsEnum && type.Name == enumName);

        Assert.Equal(AllowedBy(table, constraint), WireValuesOf(enumType));
    }

    [Fact]
    public void Every_mirrored_vocabulary_is_covered_by_the_theory()
    {
        // The other half. A new enum added to Contracts with no row above would
        // never be compared to anything, and the test above would keep passing.
        string[] notMirrored = [nameof(FactorDirection), nameof(Comparison)];

        var declared = typeof(RecommendationEnvelope).Assembly.GetExportedTypes()
            .Where(type => type.IsEnum)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        var covered = Mirrors.Select(row => (string)row[0]!)
            .Concat(notMirrored)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(covered, declared);
    }

    [Fact]
    public void The_two_enums_with_no_CHECK_behind_them_are_deliberate()
    {
        // FactorDirection and Comparison are contract-only vocabulary: neither
        // is stored, so neither has a constraint to mirror. Pinned so that
        // "it has no CHECK" stays a decision rather than an oversight.
        Assert.Equal(
            ["context", "opposes", "supports"],
            WireValuesOf(typeof(FactorDirection)));

        Assert.Equal(6, Enum.GetValues<Comparison>().Length);
    }

    // ------------------------------------------------------------ the wire

    private static IEnumerable<Type> ContractRecords() =>
        typeof(RecommendationEnvelope).Assembly.GetExportedTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null);

    [Fact]
    public void Every_contract_property_names_its_own_wire_field()
    {
        // Relying on a serializer naming policy would mean the wire format is
        // decided by whoever configures the serializer — and this contract is
        // read by C#, TypeScript and Python, each configured separately.
        var unnamed = new List<string>();

        foreach (var record in ContractRecords())
        {
            foreach (var property in record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            {
                if (property.Name == "EqualityContract")
                {
                    continue;
                }

                if (property.GetCustomAttribute<JsonPropertyNameAttribute>() is null)
                {
                    unnamed.Add($"{record.Name}.{property.Name}");
                }
            }
        }

        Assert.True(
            unnamed.Count == 0,
            "These contract properties have no [JsonPropertyName], so their wire name depends on "
            + "how each consumer configured its serializer:\n  " + string.Join("\n  ", unnamed));
    }

    [Fact]
    public void Wire_names_are_snake_case()
    {
        var offenders = ContractRecords()
            .SelectMany(record => record.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(property => (record, property)))
            .Select(pair => (pair.record.Name, Name: pair.property.GetCustomAttribute<JsonPropertyNameAttribute>()?.Name))
            .Where(pair => pair.Name is not null && !SnakeCase().IsMatch(pair.Name))
            .Select(pair => $"{pair.Name} on {pair.Item1}")
            .ToList();

        Assert.True(offenders.Count == 0, string.Join("\n  ", offenders));
    }

    [GeneratedRegex("^[a-z][a-z0-9_]*$")]
    private static partial Regex SnakeCase();

    [Fact]
    public void An_envelope_survives_a_round_trip_and_reads_as_the_schema_spells_it()
    {
        var envelope = new RecommendationEnvelope(
            RecommendationId: "01REC",
            StoreId: "01STORE",
            RecommendationType: "reorder_suggestion",
            Department: Department.SalesDemand,
            Urgency: Urgency.Warning,
            ActionType: ActionType.Menu,
            SubjectType: RecommendationSubject.Variant,
            SubjectId: "01VARIANT",
            Headline: "Suggested reorder: 240 units",
            Because: new BecauseBlock(
                Key: "reorder.below_safety_stock",
                Params: new Dictionary<string, string> { ["variant"] = "01VARIANT" },
                Factors:
                [
                    new BecauseFactor("factor.days_of_cover", "4.2", "days", FactorDirection.Supports),
                    new BecauseFactor("factor.lead_time", "9", "days", FactorDirection.Supports),
                    new BecauseFactor("factor.recent_spoilage", "3.5", "percent", FactorDirection.Opposes),
                ]),
            IntervalLow: "210",
            IntervalHigh: "270",
            ComputedAt: new DateTimeOffset(2026, 9, 12, 2, 0, 0, TimeSpan.Zero),
            ParameterVersion: 7,
            Source: "engine",
            MinimumRequiredRole: "manager",
            Status: RecommendationStatus.Pending,
            IssuedAt: new DateTimeOffset(2026, 9, 12, 2, 5, 0, TimeSpan.Zero),
            DeliveredAt: null,
            ExpiresAt: new DateTimeOffset(2026, 9, 15, 0, 0, 0, TimeSpan.Zero),
            Options:
            [
                new RecommendationOption(
                    "01OPT1", "Order 240", 0,
                    new IntentPayload("CreatePurchaseOrder", new Dictionary<string, string> { ["quantity"] = "240000" }),
                    "18400"),
            ]);

        var json = JsonSerializer.Serialize(envelope);

        // The schema's spelling, not C#'s.
        Assert.Contains("\"recommendation_type\":\"reorder_suggestion\"", json, StringComparison.Ordinal);
        Assert.Contains("\"department\":\"sales_demand\"", json, StringComparison.Ordinal);
        Assert.Contains("\"urgency\":\"warning\"", json, StringComparison.Ordinal);
        Assert.Contains("\"label_key\":\"factor.days_of_cover\"", json, StringComparison.Ordinal);

        // An enum must never cross as a number. A reader on the other side would
        // take 1 for the first member of whatever its own copy declares.
        Assert.DoesNotContain("\"department\":1", json, StringComparison.Ordinal);

        var restored = JsonSerializer.Deserialize<RecommendationEnvelope>(json)!;

        // Re-serialised rather than compared with Assert.Equal: a record's
        // generated equality compares IReadOnlyList by reference, so two
        // identical envelopes come out unequal. The claim that matters is that
        // the wire form survives the trip unchanged, which is what this says.
        Assert.Equal(json, JsonSerializer.Serialize(restored));

        Assert.Equal(3, restored.Because.Factors.Count);
        Assert.Equal(FactorDirection.Opposes, restored.Because.Factors[2].Direction);
        Assert.Equal("240000", restored.Options[0].Payload.Arguments["quantity"]);
        Assert.Equal(Urgency.Warning, restored.Urgency);
    }

    [Fact]
    public void Figures_cross_as_text_so_they_do_not_become_doubles()
    {
        // A JSON number is a double in both TypeScript and Python. A quantity in
        // thousandths or a price in centimes that round-trips through one comes
        // back approximately right, which is the silent error CLAUDE.md §3.1
        // exists to prevent.
        string[] figureFields = ["value", "interval_low", "interval_high", "quantity", "line_value", "projected_value"];

        var textual = ContractRecords()
            .SelectMany(record => record.GetProperties(BindingFlags.Public | BindingFlags.Instance))
            .Where(property => property.GetCustomAttribute<JsonPropertyNameAttribute>() is { } name
                               && figureFields.Contains(name.Name, StringComparer.Ordinal))
            .ToList();

        Assert.NotEmpty(textual);

        foreach (var property in textual)
        {
            Assert.True(
                property.PropertyType == typeof(string),
                $"{property.DeclaringType!.Name}.{property.Name} is a figure and must cross as text, "
                + $"not as {property.PropertyType.Name}.");
        }
    }

    [Fact]
    public void A_because_block_is_capped_at_three_factors()
    {
        // CLAUDE.md §5. Stated as a constant because these types are free of
        // behaviour; this is what a producer and a validator compare against.
        Assert.Equal(3, BecauseBlock.MaxFactors);
    }
}
