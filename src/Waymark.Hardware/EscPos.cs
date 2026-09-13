namespace Waymark.Hardware;

/// <summary>
/// The ESC/POS commands Waymark emits, and nothing else.
///
/// <para>
/// Epson's <i>ESC/POS Command Reference</i> is the source, and the name beside
/// each constant is the name it uses there — so a byte sequence in a hex dump
/// can be traced back to a documented command rather than to somebody's memory.
/// The Build Plan puts verification against that reference on Hakim; these names
/// are what makes that a reading task rather than a decoding one.
/// </para>
/// <para>
/// <b>Only the commands actually used.</b> A constants file listing the whole
/// command set would be a file nobody can tell the tested part of from the
/// untested part.
/// </para>
/// </summary>
internal static class EscPos
{
    private const byte Esc = 0x1B;
    private const byte Gs = 0x1D;
    internal const byte LineFeed = 0x0A;

    /// <summary><c>ESC @</c> — initialise. Clears whatever the last job left set.</summary>
    internal static ReadOnlySpan<byte> Initialise => [Esc, 0x40];

    /// <summary><c>ESC a n</c> — justification. 0 left, 1 centre, 2 right.</summary>
    internal static byte[] Align(byte n) => [Esc, 0x61, n];

    /// <summary><c>ESC E n</c> — emphasised mode on or off.</summary>
    internal static byte[] Emphasis(bool on) => [Esc, 0x45, on ? (byte)1 : (byte)0];

    /// <summary>
    /// <c>GS ! n</c> — character size. The low nibble is height, the high nibble
    /// width; <c>0x01</c> is double height at single width.
    /// </summary>
    internal static byte[] CharacterSize(byte n) => [Gs, 0x21, n];

    /// <summary><c>ESC t n</c> — select the character code table.</summary>
    internal static byte[] CodePage(byte n) => [Esc, 0x74, n];

    /// <summary>
    /// <c>GS V m n</c> — cut. <c>m = 66</c> is a partial cut after feeding
    /// <c>n</c> dots, which leaves the receipt attached by a tab so it does not
    /// fall on the floor.
    /// </summary>
    internal static byte[] PartialCut(byte feedDots = 3) => [Gs, 0x56, 66, feedDots];

    /// <summary><c>GS h n</c> — barcode height in dots.</summary>
    internal static byte[] BarcodeHeight(byte dots) => [Gs, 0x68, dots];

    /// <summary><c>GS w n</c> — barcode module width.</summary>
    internal static byte[] BarcodeWidth(byte width) => [Gs, 0x77, width];

    /// <summary><c>GS H n</c> — where to print the human-readable digits. 2 is below.</summary>
    internal static byte[] BarcodeTextPosition(byte position) => [Gs, 0x48, position];

    /// <summary>
    /// <c>GS k m n d1…dn</c> — print a barcode, length-prefixed form.
    /// <c>m = 69</c> is CODE39, <c>m = 67</c> is EAN13.
    /// </summary>
    internal static byte[] BarcodeHeader(byte symbology, byte length) => [Gs, 0x6B, symbology, length];

    /// <summary>CODE39 in the length-prefixed <c>GS k</c> form.</summary>
    internal const byte Code39 = 69;

    /// <summary>EAN13 in the length-prefixed <c>GS k</c> form.</summary>
    internal const byte Ean13 = 67;

    /// <summary>
    /// <c>ESC p m t1 t2</c> — generate a pulse on the drawer-kick connector.
    ///
    /// <para>
    /// This is the whole of "opening the cash drawer": the drawer has no
    /// interface of its own, it is a solenoid wired to the printer. <c>m</c>
    /// selects pin 2 or pin 5, and the two times are the on and off durations in
    /// 2 ms units. Too short and the solenoid does not throw; too long and it
    /// overheats, which is why these are not simply set to the maximum.
    /// </para>
    /// </summary>
    internal static byte[] DrawerKick(byte pin, byte onTime, byte offTime) =>
        [Esc, 0x70, pin, onTime, offTime];
}
