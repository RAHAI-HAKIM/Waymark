using Waymark.Contracts.Pos;
using Waymark.Domain.Hardware;
using Waymark.Hardware;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Receipts;

/// <summary>
/// The till's printer and the drawer wired to it (D2, D-126). One device, so one of these: the
/// receipt and the drawer's pulse go through the same <see cref="EscPosPrinter"/>, and never
/// interleave. <b>A failure never fails what was written</b>: the server has the row, the paper is a
/// reprint (<see cref="IReceiptPrinter"/>). It says so in <see cref="Fault"/>, until a print or a pulse
/// goes through.
/// </summary>
public sealed class TillPrinter(IReceiptPrinter printer, ICashDrawer drawer, int columns)
{
    /// <summary>The paper's width at single size: 32 for 58 mm, 42 for 80 mm.</summary>
    public int Columns { get; } = columns;

    /// <summary>What went wrong with the last print or pulse; null when the last one went through.</summary>
    public PrinterFault? Fault { get; private set; }

    /// <summary>Sends a paper. False when the printer did not take it.</summary>
    public async Task<bool> PrintAsync(ReceiptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);
        try
        {
            await printer.PrintAsync(document).ConfigureAwait(true);
            Fault = null;
            return true;
        }
        catch (ReceiptPrinterException)
        {
            // Its cause (no paper, the cover open) is read from the device: K1. A sink that did not
            // take the bytes is all the fake can say.
            Fault = PrinterFault.NotResponding;
            return false;
        }
    }

    /// <summary>Pulses the drawer, for its reason. False when the printer did not take the pulse.</summary>
    public async Task<bool> OpenDrawerAsync(DrawerOpenReason reason)
    {
        try
        {
            await drawer.OpenAsync(reason).ConfigureAwait(true);
            Fault = null;
            return true;
        }
        catch (ReceiptPrinterException)
        {
            Fault = PrinterFault.NotResponding;
            return false;
        }
    }

    /// <summary>
    /// The printer the switches name (D2): <c>--printer=&lt;folder&gt;</c> for the fake, which writes each
    /// paper's bytes to <c>receipts.escpos</c> there and a readable <c>receipts.txt</c> beside it;
    /// <c>--paper=58|80</c>; <c>--no-cutter</c>. Without <c>--printer=</c>, the till's own data folder.
    /// A real printer's port is K1's.
    /// </summary>
    public static TillPrinter Fake(IReadOnlyList<string> args, TimeProvider clock)
    {
        ArgumentNullException.ThrowIfNull(args);
        var folder = App.Setting(args, "--printer=", "WAYMARK_PRINTER")
            ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Waymark", "printer");
        var narrow = App.Setting(args, "--paper=", "WAYMARK_PAPER") == "58";
        var profile = (narrow ? PrinterProfile.Narrow58Mm : PrinterProfile.Standard80Mm) with { CanCut = !args.Contains("--no-cutter") };
        var device = new EscPosPrinter(new FileEscPosSink(Path.Combine(folder, "receipts.escpos")), profile, clock);
        return new TillPrinter(device, device, profile.Columns);
    }
}

/// <summary>
/// When the drawer opens (D-126), as one rule: when cash moves, and to count it. Pure, so it is
/// tested with no printer. The pulse follows what the server wrote; it is never asked before.
/// </summary>
public static class DrawerKicks
{
    /// <summary>A sale with a cash part. A card, wallet, tab or store-credit sale opens nothing.</summary>
    public static bool AfterSale(SaleOutcome outcome)
    {
        ArgumentNullException.ThrowIfNull(outcome);
        return outcome.Payments is { } parts
            ? parts.Any(part => part.Method == TenderMethods.Cash && !IsZero(part.Amount))
            : !IsZero(outcome.CashToCollect);
    }

    /// <summary>A refund paid out of the drawer. One to store credit or back on the tab opens nothing.</summary>
    public static bool AfterRefund(RefundAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return answer.RefundTo != RefundDestinations.StoreCredit && !IsZero(answer.CashOut);
    }

    /// <summary>A tab repaid in cash.</summary>
    public static bool AfterRepayment(TabAnswer answer)
    {
        ArgumentNullException.ThrowIfNull(answer);
        return !IsZero(answer.CashCollected);
    }

    /// <summary>Absent is not a figure (D-037), and nothing to hand over: no pulse either way.</summary>
    private static bool IsZero(string? figure) =>
        figure is null || (decimal.TryParse(figure, System.Globalization.NumberStyles.AllowLeadingSign | System.Globalization.NumberStyles.AllowDecimalPoint,
            System.Globalization.CultureInfo.InvariantCulture, out var value) && value == 0);
}
