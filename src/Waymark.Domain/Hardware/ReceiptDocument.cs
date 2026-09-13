using Waymark.Domain.Values;

namespace Waymark.Domain.Hardware;

/// <summary>
/// What is to be printed, described rather than encoded.
///
/// <para>
/// <b>Not a byte array, and that is the whole point of this type.</b> If the
/// port took ESC/POS bytes, every caller that wanted a receipt would have to
/// know the command set, and the fake printer would be exercising a different
/// code path from the real one. Here the caller describes the paper, one
/// encoder turns that into bytes, and swapping a file sink for a serial port
/// changes nothing above it.
/// </para>
/// <para>
/// Amounts stay <see cref="Money"/> all the way down (CLAUDE.md §3.1). A
/// document holding pre-formatted strings would put currency formatting at
/// every call site, and the first one to get it wrong would print a receipt
/// that disagrees with the row it came from.
/// </para>
/// </summary>
/// <param name="Lines">The paper, top to bottom.</param>
public sealed record ReceiptDocument(IReadOnlyList<ReceiptLine> Lines);

/// <summary>
/// One line of a receipt.
///
/// <para>
/// A closed hierarchy: the constructor is <c>private protected</c>, so the only
/// subtypes are the ones below. That matters because the encoder switches over
/// them, and C# cannot make that switch exhaustive — a test enumerates the
/// subtypes instead and fails when one is added that the encoder does not
/// handle.
/// </para>
/// </summary>
public abstract record ReceiptLine
{
    private protected ReceiptLine()
    {
    }
}

/// <summary>Free text.</summary>
/// <param name="Text">What to print. Wrapped by the encoder to the printer's width.</param>
/// <param name="Alignment">Where on the paper.</param>
/// <param name="Emphasised">Bold. Used sparingly — a receipt where everything is bold says nothing.</param>
/// <param name="DoubleHeight">For the store name and the total, and nothing else.</param>
public sealed record TextLine(
    string Text,
    TextAlignment Alignment = TextAlignment.Left,
    bool Emphasised = false,
    bool DoubleHeight = false) : ReceiptLine;

/// <summary>
/// A label on the left and an amount on the right, filled to the paper's width.
/// The workhorse of a receipt: every line item, every subtotal, the TVA lines,
/// the total, the tender and the change.
/// </summary>
/// <param name="Label">What the amount is for.</param>
/// <param name="Amount">The amount. Formatted by the encoder from the currency's own minor-unit exponent.</param>
/// <param name="Emphasised">Bold. The total, normally.</param>
public sealed record AmountLine(string Label, Money Amount, bool Emphasised = false) : ReceiptLine;

/// <summary>A rule across the paper.</summary>
/// <param name="Fill">The character to repeat.</param>
public sealed record SeparatorLine(char Fill = '-') : ReceiptLine;

/// <summary>
/// A machine-readable code. On a receipt this is the invoice number, so a
/// returned item can be matched to its sale by scanning rather than typing.
/// </summary>
/// <param name="Data">What to encode.</param>
/// <param name="Symbology">How.</param>
public sealed record BarcodeLine(string Data, BarcodeSymbology Symbology) : ReceiptLine;

/// <summary>Blank paper.</summary>
/// <param name="Count">How many lines. The encoder refuses fewer than one.</param>
public sealed record FeedLine(int Count = 1) : ReceiptLine;

/// <summary>
/// Cut the paper.
///
/// <para>
/// Explicit rather than implied by the end of the document, because a kitchen
/// docket and a customer receipt can be one print job with a cut between them,
/// and because a printer with no cutter has to be told to ignore it rather than
/// guess.
/// </para>
/// </summary>
public sealed record CutLine : ReceiptLine;

/// <summary>Where text sits on the paper.</summary>
public enum TextAlignment
{
    Left,
    Centre,
    Right,
}

/// <summary>
/// Barcode symbologies the receipt uses.
///
/// <para>
/// Two, deliberately. <see cref="Code39"/> encodes anything alphanumeric and is
/// what an invoice number needs; <see cref="Ean13"/> is what products carry.
/// Adding a third is a decision about what receipts are scanned for, not a
/// formatting preference.
/// </para>
/// </summary>
public enum BarcodeSymbology
{
    /// <summary>Alphanumeric, variable length. For invoice numbers.</summary>
    Code39,

    /// <summary>Thirteen digits including the check digit. For products.</summary>
    Ean13,
}
