using System.Text;
using Waymark.Domain.Hardware;

namespace Waymark.Hardware;

/// <summary>
/// A barcode scanner, which to the operating system is a keyboard that types
/// very fast.
///
/// <para>
/// <b>The hard part is not reading the scanner. It is telling a scan apart from
/// a cashier typing</b>, because both arrive as keystrokes on the same focused
/// control. The rule here is timing: a scanner emits its characters a few
/// milliseconds apart, a person cannot. Characters arriving inside
/// <see cref="Window"/> of each other accumulate; a gap longer than that
/// abandons what was accumulating and starts again.
/// </para>
/// <para>
/// The failure this prevents is specific and expensive. Without a timing rule,
/// a cashier typing a quantity while the buffer still holds a stale digit gets
/// a barcode built out of both, which looks up a real but wrong product — a
/// wrong price on a real receipt, with nothing anywhere reporting an error.
/// </para>
/// <para>
/// The clock is injected so that behaviour is a test rather than a stopwatch.
/// Not thread-safe: keystrokes arrive on the UI thread, and making it safe would
/// only invite feeding it from somewhere that is not a keyboard.
/// </para>
/// </summary>
public sealed class KeyboardWedgeScanner(TimeProvider clock, TimeSpan? window = null, char terminator = '\r')
    : IBarcodeScanner
{
    /// <summary>
    /// Longest gap between two characters of one scan. 50 ms is comfortably
    /// above what a scanner takes between characters and comfortably below a
    /// fast typist's 100–150 ms.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(50);

    private readonly StringBuilder _buffer = new(16);
    private DateTimeOffset _lastCharacterAt;

    /// <summary>The inter-character gap this scanner tolerates.</summary>
    public TimeSpan Window { get; } = window ?? DefaultWindow;

    /// <summary>
    /// What ends a scan. Scanners are configured to send Enter; some send
    /// carriage return, some line feed, and both are accepted.
    /// </summary>
    public char Terminator { get; } = terminator;

    /// <inheritdoc />
    public event EventHandler<BarcodeScanned>? Scanned;

    /// <summary>
    /// Feeds one keystroke in. Returns true when the character was consumed as
    /// part of a scan, so the caller knows not to also put it in the text box —
    /// which is the other half of the same bug.
    /// </summary>
    public bool Accept(char character)
    {
        var now = clock.GetUtcNow();
        var gap = now - _lastCharacterAt;
        _lastCharacterAt = now;

        if (character is '\r' or '\n')
        {
            if (character != Terminator && Terminator is not ('\r' or '\n'))
            {
                return false;
            }

            // A terminator after a pause terminates nothing: the characters
            // before it were typed, not scanned.
            if (_buffer.Length == 0 || gap > Window)
            {
                _buffer.Clear();
                return false;
            }

            var code = _buffer.ToString();
            _buffer.Clear();
            Scanned?.Invoke(this, new BarcodeScanned(code, now));
            return true;
        }

        // A gap means whatever was accumulating was not a scan. Start over
        // rather than prepending it to what follows.
        if (_buffer.Length > 0 && gap > Window)
        {
            _buffer.Clear();
        }

        _buffer.Append(character);
        return false;
    }

    /// <summary>
    /// Feeds a whole code in as a scanner would, terminator included. For tests
    /// and for the synthetic day, where there is no keyboard.
    /// </summary>
    public void Scan(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        foreach (var character in code)
        {
            Accept(character);
        }

        Accept(Terminator);
    }

    /// <summary>
    /// Throws away anything half-accumulated. Call it when focus moves: a scan
    /// interrupted by a click should not finish itself in the next control.
    /// </summary>
    public void Reset() => _buffer.Clear();
}
