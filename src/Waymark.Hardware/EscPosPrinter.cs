using Waymark.Domain.Hardware;

namespace Waymark.Hardware;

/// <summary>
/// The receipt printer, and the cash drawer wired to it.
///
/// <para>
/// <b>One class for both, because it is one device.</b> The drawer is a solenoid
/// on the printer's kick connector, not something the software can reach on its
/// own — so a design with an independent <c>ICashDrawer</c> implementation would
/// be modelling a machine that does not exist, and would let a store be
/// configured with a working drawer and no printer.
/// </para>
/// <para>
/// Register it once and hand out both interfaces. Two instances over one sink
/// would interleave a kick into the middle of a receipt.
/// </para>
/// <para>
/// The clock is injected because a drawer open is an audit record: the
/// synthetic generator has to stamp it with the day it simulated rather than
/// the minute it ran, and a test has to assert the instant rather than a
/// tolerance — the same reason <c>processing_log.occurred_at</c> takes one
/// (D-050).
/// </para>
/// </summary>
public sealed class EscPosPrinter(IEscPosSink sink, PrinterProfile profile, TimeProvider clock)
    : IReceiptPrinter, ICashDrawer, IDisposable
{
    private readonly EscPosEncoder _encoder = new(profile);
    private readonly SemaphoreSlim _oneAtATime = new(1, 1);

    /// <summary>Every drawer open this instance performed, in order.</summary>
    /// <remarks>
    /// The audit record proper belongs in the database, against a cash movement
    /// or a no-sale row. This is the development answer to "did the drawer
    /// actually fire, and why" — see <c>System_Architecture</c> §2 on no-sale
    /// opens being a shrinkage-audit point.
    /// </remarks>
    public IReadOnlyList<DrawerOpen> Opens => _opens;

    private readonly List<DrawerOpen> _opens = [];

    public async Task PrintAsync(ReceiptDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bytes = _encoder.Encode(document);

        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await sink.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    public async Task OpenAsync(DrawerOpenReason reason, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(reason))
        {
            // A drawer opening for a reason nobody can name is the audit hole
            // the parameter exists to close, so an unmapped value is refused
            // rather than logged as itself.
            throw new ArgumentOutOfRangeException(
                nameof(reason), reason, "The drawer does not open for an unknown reason.");
        }

        // 100 ms on, 100 ms off, in the 2 ms units the command takes. Long
        // enough for the solenoid to throw, short enough not to cook it.
        var kick = EscPos.DrawerKick(profile.DrawerPin, onTime: 50, offTime: 50);

        await _oneAtATime.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await sink.WriteAsync(kick, cancellationToken).ConfigureAwait(false);
            _opens.Add(new DrawerOpen(reason, clock.GetUtcNow()));
        }
        finally
        {
            _oneAtATime.Release();
        }
    }

    /// <summary>
    /// Releases the print lock. The sink is not disposed here: it was handed in,
    /// it may be shared, and a class that disposes what it was given is a class
    /// that closes a serial port somebody else is still using.
    /// </summary>
    public void Dispose() => _oneAtATime.Dispose();
}

/// <summary>One drawer opening, as the fake printer saw it.</summary>
public sealed record DrawerOpen(DrawerOpenReason Reason, DateTimeOffset At);
