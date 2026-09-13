namespace Waymark.Domain.Hardware;

/// <summary>
/// Somewhere to print a receipt.
///
/// <para>
/// Declared in Domain and implemented in <c>Waymark.Hardware</c>, so development
/// runs with nothing plugged in: the fake writes to a file and the real one
/// writes to a serial port, and neither the POS nor the tests can tell
/// (`docs/diagrams/05-solution-dependencies.md`).
/// </para>
/// </summary>
public interface IReceiptPrinter
{
    /// <summary>
    /// Prints one document.
    ///
    /// <para>
    /// <b>A failure here must never fail the sale.</b> The money has changed
    /// hands, the rows are committed, and a jammed printer is a reprint, not a
    /// rollback. Callers catch <see cref="ReceiptPrinterException"/> and carry
    /// on; the sale is complete before this is called at all
    /// (`docs/diagrams/06-sequence-sale.md`).
    /// </para>
    /// </summary>
    /// <exception cref="ReceiptPrinterException">The document could not be sent.</exception>
    Task PrintAsync(ReceiptDocument document, CancellationToken cancellationToken = default);
}

/// <summary>
/// The printer could not take the document.
///
/// <para>
/// Its own type so a caller can catch <i>this</i> rather than
/// <see cref="Exception"/>. Catching broadly around a print is how a genuine
/// bug — a null document, a cancelled token — ends up silently swallowed on the
/// grounds that "the printer is allowed to fail".
/// </para>
/// </summary>
public sealed class ReceiptPrinterException : Exception
{
    public ReceiptPrinterException()
    {
    }

    public ReceiptPrinterException(string message)
        : base(message)
    {
    }

    public ReceiptPrinterException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
