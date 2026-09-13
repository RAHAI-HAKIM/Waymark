using Waymark.Domain.Hardware;
using Waymark.Hardware;

namespace Waymark.Hardware.Tests;

/// <summary>
/// Telling a scan apart from a cashier typing, which is the whole job.
///
/// <para>
/// The clock is driven by hand here rather than by waiting, so these assert the
/// rule instead of the machine's mood on the day.
/// </para>
/// </summary>
public sealed class KeyboardWedgeScannerTests
{
    /// <summary>A clock that only moves when a test moves it.</summary>
    private sealed class HandCrankedClock : TimeProvider
    {
        private DateTimeOffset _now = new(2026, 9, 13, 10, 0, 0, TimeSpan.Zero);

        public override DateTimeOffset GetUtcNow() => _now;

        public void Advance(TimeSpan by) => _now += by;
    }

    private static readonly TimeSpan Fast = TimeSpan.FromMilliseconds(5);
    private static readonly TimeSpan Human = TimeSpan.FromMilliseconds(200);

    private static (KeyboardWedgeScanner Scanner, HandCrankedClock Clock, List<BarcodeScanned> Scans) Build()
    {
        var clock = new HandCrankedClock();
        var scanner = new KeyboardWedgeScanner(clock);
        var scans = new List<BarcodeScanned>();
        scanner.Scanned += (_, scan) => scans.Add(scan);
        return (scanner, clock, scans);
    }

    /// <summary>Types characters at a given pace.</summary>
    private static void Type(
        KeyboardWedgeScanner scanner, HandCrankedClock clock, string text, TimeSpan pace)
    {
        foreach (var character in text)
        {
            clock.Advance(pace);
            scanner.Accept(character);
        }
    }

    // -------------------------------------------------------------- the scan

    [Fact]
    public void A_burst_of_characters_ending_in_enter_is_a_scan()
    {
        var (scanner, clock, scans) = Build();

        Type(scanner, clock, "6123456789012", Fast);
        clock.Advance(Fast);
        scanner.Accept('\r');

        Assert.Single(scans);
        Assert.Equal("6123456789012", scans[0].Code);
    }

    [Fact]
    public void The_scan_carries_the_instant_the_terminator_arrived()
    {
        var (scanner, clock, scans) = Build();
        var start = clock.GetUtcNow();

        Type(scanner, clock, "612345", Fast);
        clock.Advance(Fast);
        scanner.Accept('\r');

        Assert.Equal(start + Fast * 7, scans[0].At);
    }

    [Fact]
    public void Accept_reports_whether_it_consumed_the_keystroke()
    {
        // The other half of the same bug: a terminator the scanner consumed must
        // not also reach the text box, or the cart gets a stray newline.
        var (scanner, clock, _) = Build();

        clock.Advance(Fast);
        Assert.False(scanner.Accept('6'));

        clock.Advance(Fast);
        Assert.True(scanner.Accept('\r'));
    }

    // ------------------------------------------------------------ the typing

    [Fact]
    public void Characters_typed_slowly_are_not_a_scan()
    {
        // A person cannot type at scanner speed. Without this rule, a cashier
        // typing digits into a quantity box produces a barcode lookup.
        var (scanner, clock, scans) = Build();

        Type(scanner, clock, "6123456789012", Human);
        clock.Advance(Human);
        scanner.Accept('\r');

        Assert.Empty(scans);
    }

    [Fact]
    public void A_pause_mid_burst_abandons_what_came_before_it()
    {
        // The expensive failure: a stale digit left in the buffer prepends
        // itself to the next scan, and the till looks up a real but different
        // product. A wrong price on a real receipt, with nothing reporting an
        // error anywhere.
        var (scanner, clock, scans) = Build();

        Type(scanner, clock, "999", Fast);
        clock.Advance(Human);
        Type(scanner, clock, "6123456789012", Fast);
        clock.Advance(Fast);
        scanner.Accept('\r');

        Assert.Single(scans);
        Assert.Equal("6123456789012", scans[0].Code);
    }

    [Fact]
    public void An_enter_after_a_pause_terminates_nothing()
    {
        var (scanner, clock, scans) = Build();

        Type(scanner, clock, "6123456789012", Fast);
        clock.Advance(Human);
        Assert.False(scanner.Accept('\r'));

        Assert.Empty(scans);
    }

    [Fact]
    public void An_enter_on_an_empty_buffer_is_not_a_scan()
    {
        var (scanner, clock, scans) = Build();

        clock.Advance(Fast);
        Assert.False(scanner.Accept('\r'));

        Assert.Empty(scans);
    }

    // ------------------------------------------------------------- the reset

    [Fact]
    public void Resetting_throws_away_a_half_finished_scan()
    {
        // Focus moved. A scan interrupted by a click must not finish itself in
        // whatever control the cashier landed on.
        var (scanner, clock, scans) = Build();

        Type(scanner, clock, "612345", Fast);
        scanner.Reset();
        Type(scanner, clock, "789012", Fast);
        clock.Advance(Fast);
        scanner.Accept('\r');

        Assert.Single(scans);
        Assert.Equal("789012", scans[0].Code);
    }

    // ------------------------------------------------------------ the helper

    [Fact]
    public void Scan_feeds_a_whole_code_as_a_scanner_would()
    {
        // What the synthetic day and the POS tests will use — there is no
        // keyboard in either.
        var (scanner, _, scans) = Build();

        scanner.Scan("6123456789012");

        Assert.Single(scans);
        Assert.Equal("6123456789012", scans[0].Code);
    }

    [Fact]
    public void Scan_refuses_an_empty_code() =>
        Assert.Throws<ArgumentException>(() => Build().Scanner.Scan(string.Empty));

    [Fact]
    public void Two_scans_in_a_row_both_arrive()
    {
        var (scanner, _, scans) = Build();

        scanner.Scan("6123456789012");
        scanner.Scan("6009876543210");

        Assert.Equal(["6123456789012", "6009876543210"], scans.Select(scan => scan.Code));
    }
}
