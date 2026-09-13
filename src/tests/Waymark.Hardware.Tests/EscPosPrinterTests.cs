using Waymark.Domain.Hardware;
using Waymark.Domain.Values;
using Waymark.Hardware;

namespace Waymark.Hardware.Tests;

/// <summary>
/// The fake printer as a device: what lands on disk, and what the drawer does.
/// </summary>
public sealed class EscPosPrinterTests : IDisposable
{
    private readonly string _directory = Path.Combine(
        Path.GetTempPath(), "waymark-printer", Guid.NewGuid().ToString("N"));

    private FileEscPosSink NewSink()
    {
        Directory.CreateDirectory(_directory);
        return new FileEscPosSink(Path.Combine(_directory, "till.escpos"));
    }

    private static ReceiptDocument AReceipt() => new(
    [
        new TextLine("EPICERIE DU CENTRE", TextAlignment.Centre, Emphasised: true),
        new SeparatorLine(),
        new AmountLine("Pain x2", Money.FromMinorUnits(5000, Currency.Dzd)),
        new AmountLine("TOTAL", Money.FromMinorUnits(5000, Currency.Dzd), Emphasised: true),
        new BarcodeLine("INV-2026-0001", BarcodeSymbology.Code39),
        new FeedLine(2),
        new CutLine(),
    ]);

    // -------------------------------------------------------------- the files

    [Fact]
    public async Task Printing_writes_the_raw_stream_and_a_readable_rendering()
    {
        // Both, and the raw one matters most: a fake that only wrote a readable
        // transcript would let the encoder be wrong in every way the real
        // printer cares about, and the first real print would find all of it.
        var sink = NewSink();
        using var printer = new EscPosPrinter(sink, PrinterProfile.Standard80Mm, TimeProvider.System);

        await printer.PrintAsync(AReceipt(), CancellationToken.None);

        var raw = await File.ReadAllBytesAsync(sink.RawPath, CancellationToken.None);
        var readable = await File.ReadAllTextAsync(sink.ReadablePath, CancellationToken.None);

        Assert.Equal(0x1B, raw[0]);
        Assert.Equal(0x40, raw[1]);
        Assert.Contains("EPICERIE DU CENTRE", readable, StringComparison.Ordinal);
        Assert.Contains("<cut>", readable, StringComparison.Ordinal);
    }

    [Fact]
    public async Task A_second_receipt_is_appended_rather_than_replacing_the_first()
    {
        // A till prints all day. A sink that truncated would leave only the last
        // receipt, which is never the one being investigated.
        var sink = NewSink();
        using var printer = new EscPosPrinter(sink, PrinterProfile.Standard80Mm, TimeProvider.System);

        await printer.PrintAsync(AReceipt(), CancellationToken.None);
        var afterFirst = new FileInfo(sink.RawPath).Length;

        await printer.PrintAsync(AReceipt(), CancellationToken.None);
        var afterSecond = new FileInfo(sink.RawPath).Length;

        Assert.Equal(afterFirst * 2, afterSecond);
    }

    [Fact]
    public void A_sink_creates_the_directory_it_was_pointed_at()
    {
        var nested = Path.Combine(_directory, "deep", "deeper", "till.escpos");

        _ = new FileEscPosSink(nested);

        Assert.True(Directory.Exists(Path.GetDirectoryName(nested)));
    }

    // ------------------------------------------------------------- the drawer

    [Fact]
    public async Task Opening_the_drawer_sends_a_kick_through_the_printer()
    {
        // The drawer is a solenoid on the printer's kick connector, not a device
        // of its own. If this ever stops going through the sink, the model has
        // drifted from the machine.
        var sink = NewSink();
        using var printer = new EscPosPrinter(sink, PrinterProfile.Standard80Mm, TimeProvider.System);

        await printer.OpenAsync(DrawerOpenReason.Sale, CancellationToken.None);

        var raw = await File.ReadAllBytesAsync(sink.RawPath, CancellationToken.None);

        Assert.Equal(new byte[] { 0x1B, 0x70, 0x00, 50, 50 }, raw);
        Assert.Contains(
            "<DRAWER KICK>",
            await File.ReadAllTextAsync(sink.ReadablePath, CancellationToken.None),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task The_kick_goes_to_the_pin_the_profile_names()
    {
        // Shops are wired to pin 2 or pin 5, and the wrong one simply does
        // nothing at all — no error, no drawer.
        var sink = NewSink();
        using var printer = new EscPosPrinter(
            sink, PrinterProfile.Standard80Mm with { DrawerPin = 1 }, TimeProvider.System);

        await printer.OpenAsync(DrawerOpenReason.NoSale, CancellationToken.None);

        var raw = await File.ReadAllBytesAsync(sink.RawPath, CancellationToken.None);
        Assert.Equal(0x01, raw[2]);
    }

    [Fact]
    public async Task Every_open_is_recorded_with_its_reason()
    {
        // A drawer that opens without a recorded reason is the shrinkage-audit
        // hole the parameter exists to close (System_Architecture §2).
        var sink = NewSink();
        using var printer = new EscPosPrinter(sink, PrinterProfile.Standard80Mm, TimeProvider.System);

        await printer.OpenAsync(DrawerOpenReason.Sale, CancellationToken.None);
        await printer.OpenAsync(DrawerOpenReason.NoSale, CancellationToken.None);
        await printer.OpenAsync(DrawerOpenReason.PaidOut, CancellationToken.None);

        Assert.Equal(
            [DrawerOpenReason.Sale, DrawerOpenReason.NoSale, DrawerOpenReason.PaidOut],
            printer.Opens.Select(open => open.Reason));
    }

    [Fact]
    public async Task An_open_is_stamped_by_the_injected_clock()
    {
        // The synthetic day stamps drawer opens with the time it simulated, not
        // the time the generator happened to run.
        var simulated = new DateTimeOffset(2024, 3, 11, 18, 45, 0, TimeSpan.Zero);
        using var printer = new EscPosPrinter(
            NewSink(), PrinterProfile.Standard80Mm, new StoppedClock(simulated));

        await printer.OpenAsync(DrawerOpenReason.NoSale, CancellationToken.None);

        Assert.Equal(simulated, Assert.Single(printer.Opens).At);
    }

    private sealed class StoppedClock(DateTimeOffset now) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => now;
    }

    [Fact]
    public async Task The_drawer_refuses_to_open_for_a_reason_that_is_not_a_reason()
    {
        var sink = NewSink();
        using var printer = new EscPosPrinter(sink, PrinterProfile.Standard80Mm, TimeProvider.System);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            printer.OpenAsync((DrawerOpenReason)99, CancellationToken.None));

        Assert.Empty(printer.Opens);
        Assert.False(File.Exists(sink.RawPath));
    }

    // ------------------------------------------------------------ the failure

    [Fact]
    public async Task A_sink_that_cannot_write_raises_a_printer_exception()
    {
        // A jammed printer is a reprint, not a rollback — but the caller can only
        // make that choice if the failure arrives as something it can catch
        // narrowly.
        Directory.CreateDirectory(_directory);
        var path = Path.Combine(_directory, "locked.escpos");
        var sink = new FileEscPosSink(path);

        using var hold = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None);
        using var printer = new EscPosPrinter(sink, PrinterProfile.Standard80Mm, TimeProvider.System);

        await Assert.ThrowsAsync<ReceiptPrinterException>(() =>
            printer.PrintAsync(AReceipt(), CancellationToken.None));
    }

    public void Dispose()
    {
        try
        {
            if (Directory.Exists(_directory))
            {
                Directory.Delete(_directory, recursive: true);
            }
        }
        catch (IOException)
        {
            // A locked file on a build agent must not fail the run.
        }

        GC.SuppressFinalize(this);
    }
}
