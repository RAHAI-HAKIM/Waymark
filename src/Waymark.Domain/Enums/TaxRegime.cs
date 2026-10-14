namespace Waymark.Domain.Enums;

/// <summary>
/// Whether a store charges TVA (D-122). Stored as TEXT with a CHECK constraint: 'not_subject',
/// 'subject'. Most small retailers are under the flat tax (IFU) and collect none: their price is a
/// price, with no tax inside it to extract, show or print.
/// </summary>
public enum TaxRegime
{
    /// <summary>Stored as <c>not_subject</c>. The default: no TVA is charged, so none is recorded.</summary>
    NotSubject,

    /// <summary>Stored as <c>subject</c>. TVA is extracted from each TTC price by its category's rate (D-033, D-075).</summary>
    Subject,
}
