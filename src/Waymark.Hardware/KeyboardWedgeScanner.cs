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
/// <see cref="Window"/> of each other form one <i>burst</i>, and a burst ends
/// either on silence (nothing for <see cref="Window"/>) or on a control
/// character such as Enter. Only then is it classified (D-063):
/// <see cref="MinimumLength"/> characters or more is a scan and raises
/// <see cref="Scanned"/>; anything shorter was typed and is handed back through
/// <see cref="Typed"/>.
/// </para>
/// <para>
/// <b>Every printable keystroke is held, never passed through.</b> The first
/// character of a scan looks exactly like a keystroke, so a scanner that lets
/// characters through and decides later has already put half a barcode in the
/// quantity box. Holding costs a typed character at most <see cref="Window"/>
/// of delay, below what a person notices.
/// </para>
/// <para>
/// No terminator is required and none is configured: silence ends a scan
/// whatever suffix the scanner is set to send. A control character is only a
/// shortcut that ends the burst without waiting, and the rest of a two-character
/// suffix (CR LF) arriving just after a scan is swallowed with it.
/// </para>
/// <para>
/// The failure this prevents is specific and expensive. Without a timing rule,
/// a cashier typing a quantity while the buffer still holds a stale digit gets
/// a barcode built out of both, which looks up a real but wrong product — a
/// wrong price on a real receipt, with nothing anywhere reporting an error.
/// </para>
/// <para>
/// The clock and its timer are injected so that behaviour is a test rather than
/// a stopwatch. Not thread-safe: construct it on the UI thread, which is where
/// keystrokes arrive. The silence timer fires elsewhere, so its callback is
/// posted back to the <see cref="SynchronizationContext"/> current at
/// construction, and every event is raised on that thread.
/// </para>
/// </summary>
public sealed class KeyboardWedgeScanner : IBarcodeScanner, IDisposable
{
    /// <summary>
    /// Longest gap between two characters of one scan, and the silence that
    /// ends one. 50 ms is comfortably above what a scanner takes between
    /// characters and comfortably below a fast typist's 100–150 ms.
    /// </summary>
    public static readonly TimeSpan DefaultWindow = TimeSpan.FromMilliseconds(50);

    /// <summary>
    /// Shortest burst that counts as a scan: EAN-8, the shortest retail code.
    /// Two keys rolled over by a fast typist can land inside the window; eight
    /// cannot, and a PLU typed by hand (four or five digits) stays typing.
    /// </summary>
    public const int DefaultMinimumLength = 8;

    private readonly TimeProvider _clock;
    private readonly SynchronizationContext? _context;
    private readonly StringBuilder _buffer = new(16);
    private ITimer? _silence;
    private DateTimeOffset _lastCharacterAt;
    private DateTimeOffset? _scanEndedAt;

    public KeyboardWedgeScanner(
        TimeProvider clock, TimeSpan? window = null, int minimumLength = DefaultMinimumLength)
    {
        ArgumentNullException.ThrowIfNull(clock);
        ArgumentOutOfRangeException.ThrowIfLessThan(minimumLength, 1);

        _clock = clock;
        _context = SynchronizationContext.Current;
        Window = window ?? DefaultWindow;
        MinimumLength = minimumLength;

        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(Window, TimeSpan.Zero, nameof(window));
    }

    /// <summary>The inter-character gap this scanner tolerates, and the silence that ends a burst.</summary>
    public TimeSpan Window { get; }

    /// <summary>The shortest burst classified as a scan.</summary>
    public int MinimumLength { get; }

    /// <inheritdoc />
    public event EventHandler<BarcodeScanned>? Scanned;

    /// <summary>
    /// Raised with keystrokes that were held and turned out to be typing. The
    /// caller puts them where they would have gone: the focused text box.
    /// </summary>
    public event EventHandler<string>? Typed;

    /// <summary>
    /// Feeds one keystroke in. Returns true when the scanner has taken the
    /// character, either holding it or consuming it as part of a scan; the
    /// caller must not also put it in the text box. Returns false for a control
    /// character that belongs to the cashier, such as an Enter that ends no
    /// scan, and by then anything held before it has already been handed back
    /// through <see cref="Typed"/>, so the order of keystrokes survives.
    /// </summary>
    public bool Accept(char character)
    {
        var now = _clock.GetUtcNow();
        var gap = now - _lastCharacterAt;
        _lastCharacterAt = now;

        // A gap means the burst before it ended; the silence timer was only
        // late to say so. Classify it before this character starts anything.
        if (_buffer.Length > 0 && gap > Window)
        {
            EndBurst(now - gap);
        }

        if (char.IsControl(character))
        {
            if (_buffer.Length > 0)
            {
                return EndBurst(now);
            }

            // The second half of a CR LF suffix, or any control character the
            // scanner sends right after a scan it has already ended.
            return _scanEndedAt is { } ended && now - ended <= Window;
        }

        _buffer.Append(character);
        _silence ??= _clock.CreateTimer(_ => OnSilenceTimer(), null, Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
        _silence.Change(Window, Timeout.InfiniteTimeSpan);
        return true;
    }

    /// <summary>
    /// Feeds a whole code in as a scanner would, Enter included. For tests and
    /// for the synthetic day, where there is no keyboard.
    /// </summary>
    public void Scan(string code)
    {
        ArgumentException.ThrowIfNullOrEmpty(code);

        foreach (var character in code)
        {
            Accept(character);
        }

        Accept('\r');
    }

    /// <summary>
    /// Ends the burst now and classifies it. Call it before a key that is not
    /// text (Backspace, an arrow) reaches the text box, so a held character
    /// lands before the key that was pressed after it.
    /// </summary>
    public void Flush() => EndBurst(_lastCharacterAt);

    /// <summary>
    /// Throws away anything held, scan or typing. Call it when focus moves: a
    /// scan interrupted by a click should not finish itself in the next control.
    /// </summary>
    public void Reset()
    {
        _buffer.Clear();
        _silence?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);
    }

    /// <inheritdoc />
    public void Dispose() => _silence?.Dispose();

    private void OnSilenceTimer()
    {
        if (_context is null)
        {
            OnSilence();
        }
        else
        {
            _context.Post(_ => OnSilence(), null);
        }
    }

    private void OnSilence()
    {
        // A posted callback can arrive after a keystroke that extended the
        // burst and rearmed the timer. Only real silence ends it.
        if (_buffer.Length > 0 && _clock.GetUtcNow() - _lastCharacterAt >= Window)
        {
            EndBurst(_lastCharacterAt);
        }
    }

    /// <summary>Classifies the burst. True when it was a scan.</summary>
    private bool EndBurst(DateTimeOffset at)
    {
        _silence?.Change(Timeout.InfiniteTimeSpan, Timeout.InfiniteTimeSpan);

        if (_buffer.Length == 0)
        {
            return false;
        }

        var burst = _buffer.ToString();
        _buffer.Clear();

        if (burst.Length >= MinimumLength)
        {
            _scanEndedAt = at;
            Scanned?.Invoke(this, new BarcodeScanned(burst, at));
            return true;
        }

        Typed?.Invoke(this, burst);
        return false;
    }
}
