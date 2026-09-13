namespace Waymark.Domain.Hardware;

/// <summary>
/// A barcode scanner.
///
/// <para>
/// Almost every retail scanner is a <b>keyboard wedge</b>: the operating system
/// sees a keyboard, and a scan arrives as characters typed very fast followed by
/// Enter. There is no device to open and no driver to call, which is why this
/// port exposes scans rather than a connection — and why the interesting part of
/// the implementation is telling a scan apart from a cashier typing.
/// </para>
/// </summary>
public interface IBarcodeScanner
{
    /// <summary>
    /// Raised once per complete scan.
    ///
    /// <para>
    /// Never raised for a partial code. A handler that receives half a barcode
    /// looks up half a product, and at a till that is a wrong price rather than
    /// an error.
    /// </para>
    /// </summary>
    event EventHandler<BarcodeScanned>? Scanned;
}

/// <summary>One completed scan.</summary>
/// <param name="Code">The characters between the start of the burst and the terminator.</param>
/// <param name="At">When the terminator arrived.</param>
public sealed record BarcodeScanned(string Code, DateTimeOffset At);
