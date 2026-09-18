using Waymark.Domain.Hardware;
using Waymark.Hardware;

namespace Waymark.Hardware.Tests;

/// <summary>
/// Telling a scan apart from a cashier typing, which is the whole job (D-063).
///
/// <para>
/// The clock is driven by hand here rather than by waiting, so these assert the
/// rule instead of the machine's mood on the day. Its timers fire inside
/// <c>Advance</c>, on the test thread, the way the silence timer's callback
/// reaches the UI thread in the POS.
/// </para>
/// </summary>
public sealed class KeyboardWedgeScannerTests
{
    /// <summary>A clock, and its timers, that only move when a test moves them.</summary>
    private sealed class HandCrankedClock : TimeProvider
    {
        private readonly List<Timer> _timers = [];
        private DateTimeOffset _now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period)
        {
            var timer = new Timer(this, callback, state);
            timer.Change(dueTime, period);
            _timers.Add(timer);
            return timer;
        }

        /// <summary>Moves time forward, firing each timer at the instant it falls due.</summary>
        public void Advance(TimeSpan by)
        {
            var target = _now + by;

            while (_timers.Where(t => t.DueAt <= target).MinBy(t => t.DueAt) is { } next)
            {
                _now = next.DueAt!.Value;
                next.Fire();
            }

            _now = target;
        }

        private sealed class Timer(HandCrankedClock clock, TimerCallback callback, object? state) : ITimer
        {
            public DateTimeOffset? DueAt { get; private set; }

            public bool Change(TimeSpan dueTime, TimeSpan period)
            {
                // One-shot only: the scanner never asks for a period.
                Assert.Equal(Timeout.InfiniteTimeSpan, period);
                DueAt = dueTime == Timeout.InfiniteTimeSpan ? null : clock._now + dueTime;
                return true;
            }

            public void Fire()
            {
                DueAt = null;
                callback(state);
            }

            public void Dispose() => DueAt = null;

            public ValueTask DisposeAsync()
            {
                Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan Human = TimeSpan.FromMilliseconds(200);

    private const string Ean13 = "6123456789012";

    private sealed record Rig(
        KeyboardWedgeScanner Scanner, HandCrankedClock Clock, List<BarcodeScanned> Scans, List<string> Typed)
    {
        /// <summary>What reached the text box: released keystrokes, in order.</summary>
        public string TextBox => string.Concat(Typed);
    }

    /// <summary>
    /// Builds a scanner under a chosen synchronisation context, since it
    /// captures the current one. xUnit runs tests under its own, which would
    /// send the silence callback to xUnit's pool; null runs it inline, on the
    /// test thread.
    /// </summary>
    private static KeyboardWedgeScanner Under(SynchronizationContext? context, Func<KeyboardWedgeScanner> build)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(context);

        try
        {
            return build();
        }
        finally
        {
            SynchronizationContext.SetSynchronizationContext(previous);
        }
    }

    private static Rig Build()
    {
        var clock = new HandCrankedClock();
        var scanner = Under(null, () => new KeyboardWedgeScanner(clock));
        var rig = new Rig(scanner, clock, [], []);
        scanner.Scanned += (_, scan) => rig.Scans.Add(scan);
        scanner.Typed += (_, text) => rig.Typed.Add(text);
        return rig;
    }

    /// <summary>
    /// Types characters at a given pace, collecting the ones the scanner let
    /// through to the text box.
    /// </summary>
    private static List<char> Type(Rig rig, string text, TimeSpan pace)
    {
        var passedThrough = new List<char>();

        foreach (var character in text)
        {
            rig.Clock.Advance(pace);
            if (!rig.Scanner.Accept(character))
            {
                passedThrough.Add(character);
            }
        }

        return passedThrough;
    }

    // -------------------------------------------------------------- the scan

    [Fact]
    public void A_burst_ending_in_enter_is_a_scan_at_once()
    {
        var rig = Build();

        Type(rig, Ean13, Fast);
        rig.Clock.Advance(Fast);
        Assert.True(rig.Scanner.Accept('\r'));

        // No silence has passed: Enter ended it without waiting.
        Assert.Equal([Ean13], rig.Scans.Select(scan => scan.Code));
    }

    [Fact]
    public void A_burst_with_no_terminator_is_a_scan_once_the_window_is_silent()
    {
        // F-6: a scanner configured with no suffix, or one this code does not
        // know, still produces scans. Silence ends them, not a character.
        var rig = Build();

        Type(rig, Ean13, Fast);
        rig.Clock.Advance(rig.Scanner.Window - TimeSpan.FromMilliseconds(1));
        Assert.Empty(rig.Scans);

        rig.Clock.Advance(TimeSpan.FromMilliseconds(1));
        Assert.Equal([Ean13], rig.Scans.Select(scan => scan.Code));
    }

    [Fact]
    public void Any_control_character_ends_a_scan_without_waiting()
    {
        // F-6: the old custom terminator was appended to the buffer and never
        // ended anything. There is no terminator to configure now; Tab, or any
        // control character a scanner is set to send, is only a shortcut.
        var rig = Build();

        Type(rig, Ean13, Fast);
        rig.Clock.Advance(Fast);
        Assert.True(rig.Scanner.Accept('\t'));

        Assert.Equal([Ean13], rig.Scans.Select(scan => scan.Code));
    }

    [Fact]
    public void A_scan_ended_by_silence_carries_its_last_character_instant()
    {
        var rig = Build();
        var start = rig.Clock.GetUtcNow();

        Type(rig, Ean13, Fast);
        rig.Clock.Advance(Human);

        Assert.Equal(start + (Fast * Ean13.Length), rig.Scans.Single().At);
    }

    [Fact]
    public void A_scan_ended_by_enter_carries_the_instant_enter_arrived()
    {
        var rig = Build();
        var start = rig.Clock.GetUtcNow();

        Type(rig, Ean13, Fast);
        rig.Clock.Advance(Fast);
        rig.Scanner.Accept('\r');

        Assert.Equal(start + (Fast * (Ean13.Length + 1)), rig.Scans.Single().At);
    }

    // ------------------------------------------------- nothing leaks through

    [Fact]
    public void No_character_of_a_scan_reaches_the_text_box()
    {
        // F-6: the digits used to be passed through, so the barcode landed in
        // whichever box had focus as well as in the cart.
        var rig = Build();

        var passedThrough = Type(rig, Ean13 + "\r", Fast);
        rig.Clock.Advance(Human);

        Assert.Empty(passedThrough);
        Assert.Equal(string.Empty, rig.TextBox);
        Assert.Single(rig.Scans);
    }

    [Fact]
    public void A_crlf_scanner_does_not_leak_the_line_feed()
    {
        // F-6: CR ended the scan and LF went on to the text box.
        var rig = Build();

        var passedThrough = Type(rig, Ean13 + "\r\n", Fast);

        Assert.Empty(passedThrough);
        Assert.Single(rig.Scans);
    }

    [Fact]
    public void An_enter_long_after_a_scan_belongs_to_the_cashier()
    {
        var rig = Build();

        Type(rig, Ean13 + "\r", Fast);
        rig.Clock.Advance(Human);

        Assert.False(rig.Scanner.Accept('\r'));
        Assert.Single(rig.Scans);
    }

    // ------------------------------------------------------------ the typing

    [Fact]
    public void Characters_typed_slowly_reach_the_text_box_and_are_not_a_scan()
    {
        // A person cannot type at scanner speed. Without this rule, a cashier
        // typing digits into a quantity box produces a barcode lookup.
        var rig = Build();

        Type(rig, Ean13, Human);
        rig.Clock.Advance(Human);

        Assert.Empty(rig.Scans);
        Assert.Equal(Ean13, rig.TextBox);
    }

    [Fact]
    public void A_typed_character_is_held_for_at_most_the_window()
    {
        var rig = Build();

        Type(rig, "3", Human);
        Assert.Equal(string.Empty, rig.TextBox);

        rig.Clock.Advance(rig.Scanner.Window);
        Assert.Equal("3", rig.TextBox);
    }

    [Fact]
    public void A_short_fast_burst_is_typing_not_a_scan()
    {
        // Two keys rolled over by a fast typist land inside the window, and a
        // PLU is four or five digits. Neither is a barcode.
        var rig = Build();

        Type(rig, "4011", Fast);
        rig.Clock.Advance(Human);

        Assert.Empty(rig.Scans);
        Assert.Equal("4011", rig.TextBox);
    }

    [Fact]
    public void The_shortest_scan_is_the_minimum_length()
    {
        var rig = Build();

        Type(rig, "9638507", Fast);
        rig.Clock.Advance(Human);
        Type(rig, "96385074", Fast);
        rig.Clock.Advance(Human);

        Assert.Equal(["96385074"], rig.Scans.Select(scan => scan.Code));
        Assert.Equal("9638507", rig.TextBox);
    }

    [Fact]
    public void Typed_characters_then_enter_arrive_before_the_enter()
    {
        // The cashier types a quantity and presses Enter quickly. The held
        // digits must reach the box before the Enter is handled, or the Enter
        // confirms an empty box.
        var rig = Build();
        var order = new List<string>();
        rig.Scanner.Typed += (_, text) => order.Add(text);

        Type(rig, "12", Fast);
        rig.Clock.Advance(Fast);
        var enterPassedThrough = !rig.Scanner.Accept('\r');
        if (enterPassedThrough)
        {
            order.Add("<enter>");
        }

        Assert.Equal(["12", "<enter>"], order);
        Assert.Empty(rig.Scans);
    }

    [Fact]
    public void A_pause_mid_burst_hands_back_what_came_before_it()
    {
        // The expensive failure: a stale digit left in the buffer prepends
        // itself to the next scan, and the till looks up a real but different
        // product. A wrong price on a real receipt, with nothing reporting an
        // error anywhere. The digit was typed, so it goes to the box instead.
        var rig = Build();

        Type(rig, "999", Fast);
        rig.Clock.Advance(Human);
        Type(rig, Ean13 + "\r", Fast);

        Assert.Equal([Ean13], rig.Scans.Select(scan => scan.Code));
        Assert.Equal("999", rig.TextBox);
    }

    [Fact]
    public void A_late_silence_timer_still_splits_the_bursts()
    {
        // The UI thread can be busy when the timer falls due. The gap itself,
        // seen on the next keystroke, ends the burst; the timer only saves the
        // wait when nothing follows.
        var clock = new HandCrankedClock();
        using var scanner = Under(null, () => new KeyboardWedgeScanner(new FrozenTimers(clock)));
        var scans = new List<string>();
        var typed = new List<string>();
        scanner.Scanned += (_, scan) => scans.Add(scan.Code);
        scanner.Typed += (_, text) => typed.Add(text);

        foreach (var character in "999")
        {
            clock.Advance(Fast);
            scanner.Accept(character);
        }

        clock.Advance(Human);
        foreach (var character in Ean13 + "\r")
        {
            clock.Advance(Fast);
            scanner.Accept(character);
        }

        Assert.Equal([Ean13], scans);
        Assert.Equal(["999"], typed);
    }

    /// <summary>The same clock, with timers that never fire.</summary>
    private sealed class FrozenTimers(HandCrankedClock clock) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => clock.GetUtcNow();

        public override ITimer CreateTimer(
            TimerCallback callback, object? state, TimeSpan dueTime, TimeSpan period) => new Never();

        private sealed class Never : ITimer
        {
            public bool Change(TimeSpan dueTime, TimeSpan period) => true;

            public void Dispose()
            {
            }

            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    [Fact]
    public void An_enter_on_an_empty_buffer_belongs_to_the_cashier()
    {
        var rig = Build();

        rig.Clock.Advance(Human);
        Assert.False(rig.Scanner.Accept('\r'));

        Assert.Empty(rig.Scans);
    }

    // ------------------------------------------------- flush and reset

    [Fact]
    public void Flush_hands_back_a_held_character_at_once()
    {
        // Backspace is not text and never reaches Accept. Flushed first, the
        // held digit lands before the Backspace deletes it, as the cashier
        // expects.
        var rig = Build();

        Type(rig, "7", Human);
        rig.Scanner.Flush();

        Assert.Equal("7", rig.TextBox);
        rig.Clock.Advance(Human);
        Assert.Equal("7", rig.TextBox);
    }

    [Fact]
    public void Resetting_throws_away_a_half_finished_scan()
    {
        // Focus moved. A scan interrupted by a click must not finish itself in
        // whatever control the cashier landed on.
        var rig = Build();

        Type(rig, "612345", Fast);
        rig.Scanner.Reset();
        Type(rig, "78901234\r", Fast);
        rig.Clock.Advance(Human);

        Assert.Equal(["78901234"], rig.Scans.Select(scan => scan.Code));
        Assert.Equal(string.Empty, rig.TextBox);
    }

    // ------------------------------------------------------------ the helper

    [Fact]
    public void Scan_feeds_a_whole_code_as_a_scanner_would()
    {
        // What the synthetic day and the POS tests will use: there is no
        // keyboard in either.
        var rig = Build();

        rig.Scanner.Scan(Ean13);

        Assert.Equal([Ean13], rig.Scans.Select(scan => scan.Code));
    }

    [Fact]
    public void Scan_refuses_an_empty_code() =>
        Assert.Throws<ArgumentException>(() => Build().Scanner.Scan(string.Empty));

    [Fact]
    public void Two_scans_in_a_row_both_arrive()
    {
        var rig = Build();

        rig.Scanner.Scan(Ean13);
        rig.Scanner.Scan("6009876543210");

        Assert.Equal([Ean13, "6009876543210"], rig.Scans.Select(scan => scan.Code));
    }

    // ---------------------------------------------------- the silence timer

    [Fact]
    public void The_silence_timer_is_posted_to_the_thread_that_built_the_scanner()
    {
        // In the POS the timer fires on a pool thread while keystrokes arrive on
        // the UI thread. Posting its callback keeps the scanner single-threaded.
        var context = new QueueingContext();
        var clock = new HandCrankedClock();
        using var scanner = Under(context, () => new KeyboardWedgeScanner(clock));
        var scans = new List<string>();
        scanner.Scanned += (_, scan) => scans.Add(scan.Code);

        foreach (var character in Ean13)
        {
            clock.Advance(Fast);
            scanner.Accept(character);
        }

        clock.Advance(Human);
        Assert.Empty(scans);

        context.RunPosted();
        Assert.Equal([Ean13], scans);
    }

    [Fact]
    public void A_posted_silence_that_arrives_after_more_keys_ends_nothing()
    {
        // The timer fell due and was posted, then the scanner's next character
        // arrived before the UI thread ran the callback. The burst is still
        // going; ending it would cut the barcode in two.
        var context = new QueueingContext();
        var clock = new HandCrankedClock();
        using var scanner = Under(context, () => new KeyboardWedgeScanner(clock));
        var scans = new List<string>();
        scanner.Scanned += (_, scan) => scans.Add(scan.Code);

        foreach (var character in "6123")
        {
            clock.Advance(Fast);
            scanner.Accept(character);
        }

        clock.Advance(scanner.Window);
        foreach (var character in "456789012")
        {
            scanner.Accept(character);
            clock.Advance(Fast);
        }

        context.RunPosted();
        Assert.Empty(scans);

        scanner.Accept('\r');
        Assert.Equal([Ean13], scans);
    }

    /// <summary>Holds posted callbacks until the test runs them, as a busy UI thread would.</summary>
    private sealed class QueueingContext : SynchronizationContext
    {
        private readonly Queue<(SendOrPostCallback Callback, object? State)> _posted = new();

        public override void Post(SendOrPostCallback d, object? state) => _posted.Enqueue((d, state));

        public void RunPosted()
        {
        while (_posted.TryDequeue(out var item))
        {
            item.Callback(item.State);
        }
        }
    }

    // ------------------------------------------------------- configuration

    [Fact]
    public void A_window_of_zero_or_less_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new KeyboardWedgeScanner(new HandCrankedClock(), TimeSpan.Zero));

    [Fact]
    public void A_minimum_length_below_one_is_refused() =>
        Assert.Throws<ArgumentOutOfRangeException>(
            () => new KeyboardWedgeScanner(new HandCrankedClock(), minimumLength: 0));
}
