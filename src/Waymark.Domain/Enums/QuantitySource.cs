namespace Waymark.Domain.Enums;

/// <summary>
/// Where a sale row's quantity came from (session B3, D-090). Stored as TEXT with a CHECK
/// constraint: 'count', 'typed_weight', 'label_weight', 'label_price'.
///
/// <para>
/// It answers two questions a row could not answer before. <b>Which figure is exact</b>: for
/// <see cref="LabelPrice"/> the line total is the label's and the quantity was derived from it,
/// so the row recomputes as quantity = total ÷ price; for every other source it is the other way
/// round (O-26). <b>Who vouches for the weight</b>: a scale printed <see cref="LabelWeight"/>; a
/// person typed <see cref="TypedWeight"/>, which nothing can check afterwards.
/// </para>
/// </summary>
public enum QuantitySource
{
    /// <summary>Stored as <c>count</c>: whole units, scanned or counted.</summary>
    Count,

    /// <summary>Stored as <c>typed_weight</c>: a weight typed at the till.</summary>
    TypedWeight,

    /// <summary>Stored as <c>label_weight</c>: the weight printed in a scale label.</summary>
    LabelWeight,

    /// <summary>Stored as <c>label_price</c>: the price printed in a scale label, the quantity derived from it.</summary>
    LabelPrice,
}
