using System.Reflection;
using System.Text.Json.Serialization;
using Waymark.Contracts.Intents;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Recommendations;
using Waymark.Contracts.Sync;

namespace Waymark.Integration.Tests;

/// <summary>
/// Every field of the contract is a column, and every column is a field —
/// unless somebody wrote down why not.
///
/// <para>
/// <c>ContractsMatchSchemaTests</c> compares <b>values</b>: that a contract enum
/// says exactly what its CHECK allows. This compares <b>shape</b>, and the two
/// catch different things. When it was first written it found two fields the
/// store could not persist at all: <c>projected_value_unit</c> on an option, and
/// <c>decided_by_cloud_user</c> on a decision. Both serialised happily, both
/// passed every vocabulary test, and both would have been discovered by a
/// handler failing to map them somewhere in W9.
/// </para>
/// <para>
/// D-044's rule is that the contract mirrors the tables rather than inventing a
/// shape, because a contract that diverges from the storage is a translation
/// layer nobody asked for. This is that rule, enforced.
/// </para>
/// <para>
/// <b>The exceptions are the interesting part of this file.</b> Each one is a
/// place where the wire and the table legitimately differ, and each carries the
/// reason. A list that grew without reasons would turn this test into
/// decoration, so keep it short and keep it explained.
/// </para>
/// </summary>
public sealed class ContractsMirrorTheSchemaTests : IClassFixture<MigratedDatabaseFixture>
{
    private readonly MigratedDatabaseFixture _database;

    public ContractsMirrorTheSchemaTests(MigratedDatabaseFixture database) => _database = database;

    /// <summary>
    /// A contract type, the table it mirrors, and what the two do not share.
    /// </summary>
    /// <param name="Contract">The record.</param>
    /// <param name="Table">The table it mirrors.</param>
    /// <param name="WireOnly">Wire fields with no column, each with its reason.</param>
    /// <param name="ColumnOnly">Columns that never cross, each with its reason.</param>
    /// <param name="Renamed">Wire name to column name, where the two differ.</param>
    private sealed record Mirror(
        Type Contract,
        string Table,
        IReadOnlyDictionary<string, string> WireOnly,
        IReadOnlyDictionary<string, string> ColumnOnly,
        IReadOnlyDictionary<string, string> Renamed);

    private static readonly Mirror[] Mirrors =
    [
        new(typeof(RecommendationEnvelope), "recommendations",
            WireOnly: new Dictionary<string, string>
            {
                ["options"] = "The options are rows in recommendation_options, joined on the way out.",
            },
            ColumnOnly: new Dictionary<string, string>(),
            Renamed: new Dictionary<string, string>
            {
                ["because"] = "because_json",
            }),

        new(typeof(RecommendationOption), "recommendation_options",
            WireOnly: new Dictionary<string, string>(),
            ColumnOnly: new Dictionary<string, string>
            {
                ["recommendation_id"] = "The parent link. Implied by nesting inside the envelope, so repeating it on the wire would be a second source of truth for the same fact.",
            },
            Renamed: new Dictionary<string, string>
            {
                ["payload"] = "payload_json",
            }),

        new(typeof(RecommendationDecisionMessage), "recommendation_decisions",
            WireOnly: new Dictionary<string, string>(),
            ColumnOnly: new Dictionary<string, string>(),
            Renamed: new Dictionary<string, string>
            {
                ["adjusted_payload"] = "adjusted_payload_json",
            }),

        new(typeof(IntentMessage), "intents",
            WireOnly: new Dictionary<string, string>(),
            ColumnOnly: new Dictionary<string, string>(),
            Renamed: new Dictionary<string, string>
            {
                ["payload"] = "payload_json",
                ["preconditions"] = "preconditions_json",
            }),

        new(typeof(OutboundEnvelope), "outbox",
            WireOnly: new Dictionary<string, string>(),
            ColumnOnly: new Dictionary<string, string>
            {
                ["attempts"] = "Local delivery state. The receiver has no business knowing how many times we tried.",
                ["last_attempt_at"] = "Local delivery state.",
                ["last_error"] = "Local delivery state, and it can carry a message from our own stack.",
            },
            Renamed: new Dictionary<string, string>
            {
                ["message_id"] = "outbox_id",
                ["payload"] = "payload_json",
            }),

        new(typeof(InboundEnvelope), "inbox",
            WireOnly: new Dictionary<string, string>(),
            ColumnOnly: new Dictionary<string, string>
            {
                ["inbox_id"] = "Our local primary key. The sender's message_id is what identifies the message.",
                ["received_at"] = "When we took delivery. Local.",
                ["applied_at"] = "When we acted on it. Local.",
                ["status"] = "What we decided about it. Trusting a status the sender wrote would defeat idempotent replay.",
                ["rejection_reason"] = "Ours, not theirs.",
            },
            Renamed: new Dictionary<string, string>
            {
                ["payload"] = "payload_json",
            }),
    ];

    private List<string> ColumnsOf(string table) =>
        [.. _database.Query($"SELECT name FROM pragma_table_info('{table}')")];

    private static List<string> WireNamesOf(Type contract) =>
        [.. contract.GetProperties(BindingFlags.Public | BindingFlags.Instance)
            .Where(property => property.Name != "EqualityContract")
            .Select(property => property.GetCustomAttribute<JsonPropertyNameAttribute>()!.Name)];

    public static TheoryData<string> MirrorNames()
    {
        var data = new TheoryData<string>();
        foreach (var mirror in Mirrors)
        {
            data.Add(mirror.Contract.Name);
        }

        return data;
    }

    private static Mirror MirrorFor(string name) =>
        Mirrors.Single(mirror => mirror.Contract.Name == name);

    [Theory]
    [MemberData(nameof(MirrorNames))]
    public void Every_wire_field_is_a_column(string contractName)
    {
        var mirror = MirrorFor(contractName);
        var columns = ColumnsOf(mirror.Table);

        var orphans = WireNamesOf(mirror.Contract)
            .Where(wire => !mirror.WireOnly.ContainsKey(wire))
            .Select(wire => mirror.Renamed.TryGetValue(wire, out var column) ? column : wire)
            .Where(column => !columns.Contains(column, StringComparer.Ordinal))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            orphans.Count == 0,
            $"""
            {mirror.Contract.Name} has fields that {mirror.Table} cannot store:
              {string.Join(Environment.NewLine + "  ", orphans)}

            The contract mirrors the tables (D-044). Either add the column in a
            migration, or drop the field — but do not leave the store unable to
            persist something the wire promises.
            """);
    }

    [Theory]
    [MemberData(nameof(MirrorNames))]
    public void Every_column_is_a_wire_field(string contractName)
    {
        // The other direction, and the one that catches an omission rather than
        // an invention: a column nobody put on the contract is data the UI and
        // the engine cannot see, and nothing else would say so.
        var mirror = MirrorFor(contractName);

        var wireColumns = WireNamesOf(mirror.Contract)
            .Where(wire => !mirror.WireOnly.ContainsKey(wire))
            .Select(wire => mirror.Renamed.TryGetValue(wire, out var column) ? column : wire)
            .ToHashSet(StringComparer.Ordinal);

        var missing = ColumnsOf(mirror.Table)
            .Where(column => !wireColumns.Contains(column))
            .Where(column => !mirror.ColumnOnly.ContainsKey(column))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.True(
            missing.Count == 0,
            $"""
            {mirror.Table} has columns {mirror.Contract.Name} does not carry:
              {string.Join(Environment.NewLine + "  ", missing)}

            Either add them to the contract, or add them to this mirror's
            ColumnOnly list with the reason they never cross.
            """);
    }

    [Fact]
    public void Every_exception_names_a_field_or_column_that_still_exists()
    {
        // An exceptions list rots the moment the thing it excuses is renamed or
        // removed: the entry stops matching, silently excuses nothing, and the
        // list reads longer than the real set of differences.
        var stale = new List<string>();

        foreach (var mirror in Mirrors)
        {
            var wire = WireNamesOf(mirror.Contract).ToHashSet(StringComparer.Ordinal);
            var columns = ColumnsOf(mirror.Table).ToHashSet(StringComparer.Ordinal);

            stale.AddRange(mirror.WireOnly.Keys
                .Where(name => !wire.Contains(name))
                .Select(name => $"{mirror.Contract.Name}: WireOnly '{name}' is not a field any more"));

            stale.AddRange(mirror.ColumnOnly.Keys
                .Where(name => !columns.Contains(name))
                .Select(name => $"{mirror.Contract.Name}: ColumnOnly '{name}' is not a column any more"));

            stale.AddRange(mirror.Renamed
                .Where(pair => !wire.Contains(pair.Key))
                .Select(pair => $"{mirror.Contract.Name}: Renamed '{pair.Key}' is not a field any more"));

            stale.AddRange(mirror.Renamed
                .Where(pair => !columns.Contains(pair.Value))
                .Select(pair => $"{mirror.Contract.Name}: Renamed to '{pair.Value}', which is not a column"));
        }

        Assert.True(stale.Count == 0, string.Join(Environment.NewLine + "  ", stale));
    }

    [Fact]
    public void Every_exception_carries_a_reason()
    {
        var unexplained = Mirrors
            .SelectMany(mirror => mirror.WireOnly.Concat(mirror.ColumnOnly)
                .Where(pair => string.IsNullOrWhiteSpace(pair.Value))
                .Select(pair => $"{mirror.Contract.Name}: {pair.Key}"))
            .ToList();

        Assert.True(
            unexplained.Count == 0,
            "An exception with no reason is how this test stops meaning anything:\n  "
            + string.Join("\n  ", unexplained));
    }

    [Fact]
    public void Every_contract_record_that_mirrors_a_table_is_listed_here()
    {
        // Without this, adding a record and forgetting to mirror it is invisible
        // — the theories above only check what they were given.
        string[] notTableBacked =
        [
            nameof(BecauseBlock),          // parsed out of recommendations.because_json
            nameof(BecauseFactor),         // an element of that document
            nameof(IntentPayload),         // parsed out of payload_json, in three places
            nameof(Precondition),          // an element of intents.preconditions_json
            nameof(AnonymousBasketRecord), // D-043 outbox payload; shaped at emit, stored nowhere
            nameof(BasketLine),            // an element of that payload
            nameof(CustomerPeriodRecord),  // D-043 outbox payload; monthly grain, stored nowhere
            nameof(ProductLookup),         // till ↔ StoreServer answer; a read over several tables
            nameof(ProductForSale),        // an element of that answer
            nameof(SaleRequest),           // till → StoreServer; codes and counts, never a row
            nameof(SaleRequestLine),       // an element of that request
            nameof(SaleOutcome),           // StoreServer → till; a summary over several tables
        ];

        var declared = typeof(RecommendationEnvelope).Assembly.GetExportedTypes()
            .Where(type => type.IsClass && !type.IsAbstract)
            .Where(type => type.GetMethod("<Clone>$", BindingFlags.Public | BindingFlags.Instance) is not null)
            .Select(type => type.Name)
            .Order(StringComparer.Ordinal)
            .ToList();

        var accounted = Mirrors.Select(mirror => mirror.Contract.Name)
            .Concat(notTableBacked)
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(accounted, declared);
    }
}
