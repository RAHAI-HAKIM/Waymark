using System.Text.Json.Serialization;

namespace Waymark.Contracts.Reference;

/// <summary>
/// The reasons a shop accepts for one kind of action (session A3).
///
/// <para>
/// The kind is echoed so a client holding several lists can tell them apart without
/// remembering what it asked for.
/// </para>
/// </summary>
/// <param name="AppliesTo">One of <see cref="ReasonCodeAppliesTo"/>.</param>
/// <param name="ReasonCodes">In the order they should be offered. Possibly empty.</param>
public sealed record ReasonCodeList(
    [property: JsonPropertyName("applies_to")] string AppliesTo,
    [property: JsonPropertyName("reason_codes")] IReadOnlyList<ReasonCodeOption> ReasonCodes);

/// <summary>One reason, as it is offered.</summary>
/// <param name="Code">
/// What gets written to the row recording the action — the foreign key's value, never a
/// label. A client sends this back, not the text it showed.
/// </param>
/// <param name="LabelAr">The Arabic label.</param>
/// <param name="LabelFr">The French label.</param>
/// <param name="RequiresNote">The shop wants words as well as a code.</param>
/// <param name="RequiresManager">
/// The shop wants somebody senior to authorise this reason. <b>It is a statement of what the
/// shop asked for, not a decision</b>: who counts as senior is <c>StaffPermissions</c>, and a
/// client that treated this flag as the whole rule would be a second place deciding who may
/// do what.
/// </param>
public sealed record ReasonCodeOption(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("label_ar")] string LabelAr,
    [property: JsonPropertyName("label_fr")] string LabelFr,
    [property: JsonPropertyName("requires_note")] bool RequiresNote,
    [property: JsonPropertyName("requires_manager")] bool RequiresManager);

/// <summary>
/// The values of <see cref="ReasonCodeList.AppliesTo"/>, and what a client may ask for.
/// Exactly the CHECK on <c>reason_codes.applies_to</c>.
/// </summary>
public static class ReasonCodeAppliesTo
{
    /// <summary>Money taken off a line or a sale.</summary>
    public const string Discount = "discount";

    /// <summary>Selling at a price other than the one in force.</summary>
    public const string PriceOverride = "price_override";

    /// <summary>A stock level corrected by hand.</summary>
    public const string Adjustment = "adjustment";

    /// <summary>A line or a sale taken back out before it completed.</summary>
    public const string Void = "void";

    /// <summary>Goods brought back after the sale.</summary>
    public const string Return = "return";

    /// <summary>The drawer opened with no sale behind it.</summary>
    public const string NoSale = "no_sale";

    /// <summary>Cash in or out of the drawer other than by a sale: paid-in, paid-out, a drop, a repayment.</summary>
    public const string CashMovement = "cash_movement";

    /// <summary>Stock or a debt given up on.</summary>
    public const string WriteOff = "write_off";
}
