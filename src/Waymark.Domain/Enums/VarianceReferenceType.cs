namespace Waymark.Domain.Enums;

/// <summary>
/// What a row of <c>rounding_variance</c> is about — the other half of
/// <c>reference_id</c>.
///
/// <para>
/// The pair is a polymorphic reference and carries <b>no foreign key</b>, which
/// is the schema's established pattern rather than a shortcut:
/// <c>stock_movements.reference_type</c>/<c>reference_id</c> and
/// <c>recommendations.subject_type</c>/<c>subject_id</c> both work this way, and
/// both are indexed instead. SQLite cannot express a foreign key that points at
/// a different table depending on a sibling column, and the alternatives —
/// four nullable columns with four keys, or a supertype table every reference
/// must be registered in — cost more than the guarantee is worth on one till.
/// </para>
/// <para>
/// The cost is real and worth naming: nothing stops a <c>reference_id</c> that
/// matches no row. What limits the damage is that these rows are written by one
/// code path, in the same transaction as the thing they point at.
/// </para>
/// </summary>
public enum VarianceReferenceType
{
    /// <summary>A sale or a refund — where cash tender rounding happens.</summary>
    Transaction,

    /// <summary>A purchase order, for a conversion residual on a foreign-currency order.</summary>
    PurchaseOrder,

    /// <summary>A batch, where a foreign-currency unit cost is valued into the ledger currency.</summary>
    Batch,
}
