namespace Waymark.Domain.Sales;

/// <summary>
/// What one line of a ticket may hold. The till refuses a count typed with one digit too many
/// (D-087), and the server refuses it again: the till's cart is a preview (D-068), and a request is
/// not a till. Found in the block B review: the server sold 2 147 483 647 units of milk when asked.
/// </summary>
public static class SaleLimits
{
    /// <summary>The most units one counted line may hold: 9 999.</summary>
    public const int MaxUnitsPerLine = 9_999;

    /// <summary>
    /// The most one typed weighing may be, in thousandths of the unit: 99,999 kg, what five digits
    /// of a scale label can carry (D-090). A weight past it was typed with a digit too many.
    /// </summary>
    public const long MaxWeightThousandths = 99_999;

    /// <summary>
    /// The longest note a cashier may write, in characters: on a discount, a refund, cash in or out, a
    /// close (D-121). No note had a length, and one of two million characters was stored and sent
    /// back with every read (block C review). 500 until the consent screens say better (Hakim, 12/10).
    /// </summary>
    public const int MaxNoteLength = 500;

    /// <summary>Whether a note may be written as it is: none, or one of <see cref="MaxNoteLength"/> characters at most, its edges trimmed.</summary>
    public static bool NoteFits(string? note) => note is null || note.Trim().Length <= MaxNoteLength;
}
