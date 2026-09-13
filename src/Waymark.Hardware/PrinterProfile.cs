namespace Waymark.Hardware;

/// <summary>
/// What a particular printer can do. The encoder needs this and nothing else
/// about the device.
///
/// <para>
/// <b>Column width is not cosmetic.</b> An amount line is a label on the left
/// and a figure on the right, padded to the paper's width — get the width wrong
/// and every total on every receipt is misaligned or wrapped. 58 mm paper is 32
/// columns and 80 mm is 42 or 48 depending on the font, and shops buy whichever
/// was in the box.
/// </para>
/// </summary>
/// <param name="Columns">Characters per line at single width.</param>
/// <param name="CanCut">Whether the printer has a cutter. A <c>CutLine</c> is ignored when it has not.</param>
/// <param name="CodePage">
/// The <c>ESC t</c> character table. 0 is PC437, the default nearly every
/// printer starts in.
/// </param>
/// <param name="DrawerPin">
/// Which kick connector the drawer is on — 0 for pin 2, 1 for pin 5. Shops are
/// wired both ways and the wrong one simply does nothing.
/// </param>
public sealed record PrinterProfile(
    int Columns = 42,
    bool CanCut = true,
    byte CodePage = 0,
    byte DrawerPin = 0)
{
    /// <summary>58 mm paper: the cheap countertop printer, 32 columns.</summary>
    public static PrinterProfile Narrow58Mm { get; } = new(Columns: 32);

    /// <summary>80 mm paper at 42 columns. The usual till printer, and the default.</summary>
    public static PrinterProfile Standard80Mm { get; } = new(Columns: 42);

    /// <summary>
    /// A profile with no cutter, for a printer whose tear bar is manual.
    /// </summary>
    public static PrinterProfile Standard80MmNoCutter { get; } = new(Columns: 42, CanCut: false);
}
