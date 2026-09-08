// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Customers;

/// <summary>
/// Maps to <c>data_subject_requests</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>DataSubjectRequestConfiguration</c>.
/// </para>
/// </summary>
public sealed class DataSubjectRequest
{
    /// <summary>Primary key (<c>request_id</c>).</summary>
    public required string RequestId { get; init; }

    public required string CustomerId { get; init; }

    public required RequestType RequestType { get; init; }

    public required DateTimeOffset ReceivedAt { get; init; }

    public required DateTimeOffset DueAt { get; init; }

    public string? ReceivedBy { get; init; }

    public DataSubjectRequestStatus Status { get; init; } = DataSubjectRequestStatus.Open;

    public string? ResolutionNote { get; init; }

    public DateTimeOffset? ResolvedAt { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
