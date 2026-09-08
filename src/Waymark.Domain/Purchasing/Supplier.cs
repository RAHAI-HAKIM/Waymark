// Bootstrapped from schema_v7_1.sql by tools/generate-model.
//
// Hand-maintained from here on. The generator was a one-shot; re-running it
// would overwrite anything edited since. It deliberately carries no generated-
// code marker: that marker switches off the nullable context and the analysers
// on exactly the code that most needs them.

using Waymark.Domain.Enums;

namespace Waymark.Domain.Purchasing;

/// <summary>
/// Maps to <c>suppliers</c>.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this
/// project. <c>Waymark.Domain</c> has zero dependencies (CLAUDE.md §2.1),
/// and how this reaches SQLite lives in <c>SupplierConfiguration</c>.
/// </para>
/// </summary>
public sealed class Supplier
{
    /// <summary>Primary key (<c>supplier_id</c>).</summary>
    public required string SupplierId { get; init; }

    public required string SupplierCode { get; init; }

    public required string CompanyName { get; init; }

    public string? ResponsibleName { get; init; }

    public string? ContactPhone { get; init; }

    public string? Email { get; init; }

    public string? Website { get; init; }

    public string? ShippingAddress { get; init; }

    public long NetDays { get; init; }

    public long? CreditLimit { get; init; }

    public string Currency { get; init; } = "DZD";

    public SupplierStatus Status { get; init; } = SupplierStatus.Active;

    public required DateTimeOffset CreatedAt { get; init; }

    public required DateTimeOffset UpdatedAt { get; init; }
}
