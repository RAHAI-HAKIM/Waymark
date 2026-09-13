using Waymark.Domain.Enums;
using Waymark.Domain.Values;

namespace Waymark.Domain.Pricing;

/// <summary>
/// Maps to <c>rounding_variance</c>. Every minor unit created or destroyed by
/// rounding, and what created it.
///
/// <para>
/// A plain class: no attributes, no EF Core, nothing that leaves this project
/// (CLAUDE.md §2.1). How it reaches SQLite lives in
/// <c>RoundingVarianceConfiguration</c>.
/// </para>
/// <para>
/// The reason it is a ledger and not a memo: <c>cash_sessions.variance</c>
/// already exists, and it exists to detect theft and miscounting. A few dinars
/// of tender rounding every day would teach the shopkeeper to ignore that
/// number, and the control would be dead. Keeping rounding here is what keeps
/// that column meaning what it says (decisions.md D-034).
/// </para>
/// <para>
/// Append-only, guarded by triggers in <c>triggers.sql</c> like the other
/// ledgers. A correction is a new row, never an edit.
/// </para>
/// </summary>
public sealed class RoundingVariance : IStoreScoped
{
    /// <summary>Primary key (<c>variance_id</c>). A ULID from <c>IIdGenerator</c>.</summary>
    public required string VarianceId { get; init; }

    public required string StoreId { get; init; }

    /// <summary>When the rounding happened — not when the row was written.</summary>
    public required DateTimeOffset OccurredAt { get; init; }

    /// <summary>
    /// What this is about. Required, with <see cref="ReferenceId"/>: a variance
    /// nobody can attribute is useless to an auditor, and there is no case where
    /// one arises with nothing to point at.
    /// </summary>
    public required VarianceReferenceType ReferenceType { get; init; }

    /// <summary>
    /// The id of that thing. Deliberately not a foreign key — see
    /// <see cref="VarianceReferenceType"/> for why.
    /// </summary>
    public required string ReferenceId { get; init; }

    public required VarianceSource Source { get; init; }

    /// <summary>
    /// Signed. Negative means the shop received less than the invoice said.
    /// Stored as the INTEGER count of minor units the schema declares; the
    /// currency comes from <see cref="ILedgerCurrency"/>, because the column
    /// does not carry one.
    /// </summary>
    public required Money Amount { get; init; }

    /// <summary>
    /// The rounding policy in force when this happened — the same vocabulary as
    /// <c>stores.rounding_policy</c> and <c>transactions.rounding_policy</c>, so
    /// one word means one thing across the schema (D-032). Those two columns are
    /// still to be added (O-22).
    /// </summary>
    public required Rounding Policy { get; init; }

    public required DateTimeOffset CreatedAt { get; init; }
}
