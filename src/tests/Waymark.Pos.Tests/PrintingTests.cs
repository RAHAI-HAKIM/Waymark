using Waymark.Contracts.Pos;
using Waymark.Domain.Hardware;
using Waymark.Pos.Receipts;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// The printing rules (D2, D-126), with no window. The silent failures: a drawer that opens for a card
/// sale, or stays shut for a cash one; a failing printer hidden while printing is switched off, found
/// only when a customer asks for a ticket; a paper that failed told as printed.
/// </summary>
public sealed class PrintingTests
{
    private static SaleOutcome Sale(string? cashToCollect, params PaymentLine[] parts) =>
        new(SaleOutcomes.Completed, "t", "S-1", "143.00", "0.00", cashToCollect, "DZD", null, parts.Length == 0 ? null : parts);

    [Fact]
    public void The_drawer_opens_when_cash_moves_and_for_nothing_else()
    {
        Assert.True(DrawerKicks.AfterSale(Sale("145.00")));                                         // a server before B6: all cash
        Assert.True(DrawerKicks.AfterSale(Sale(null, new PaymentLine("card", "100.00", "A1"), new PaymentLine("cash", "43.00", null))));
        Assert.False(DrawerKicks.AfterSale(Sale(null, new PaymentLine("card", "143.00", "A1"))));
        Assert.False(DrawerKicks.AfterSale(Sale(null, new PaymentLine("on_account", "143.00", null))));
        Assert.False(DrawerKicks.AfterSale(Sale("0.00")));

        Assert.True(DrawerKicks.AfterRefund(new RefundAnswer(RefundOutcomes.Refunded, RefundTo: RefundDestinations.Cash, CashOut: "145.00")));
        Assert.False(DrawerKicks.AfterRefund(new RefundAnswer(RefundOutcomes.Refunded, RefundTo: RefundDestinations.StoreCredit, CashOut: null)));
        Assert.False(DrawerKicks.AfterRefund(new RefundAnswer(RefundOutcomes.Refunded, RefundTo: RefundDestinations.Cash, CashOut: "0.00"))); // all back on the tab

        Assert.True(DrawerKicks.AfterRepayment(new TabAnswer(CustomerOutcomes.Ok, null, "0.00", null, null, false, null, null, "DZD", null, CashCollected: "145.00")));
        Assert.False(DrawerKicks.AfterRepayment(new TabAnswer(CustomerOutcomes.Ok, null, "0.00", null, null, false, null, null, "DZD", null)));
    }

    [Fact]
    public void A_failing_printer_is_said_on_the_bar_even_with_printing_switched_off_and_nothing_when_all_is_well()
    {
        var text = TillText.French;

        Assert.Equal(new PrinterChip(text.PrinterFaultChip(PrinterFault.NotResponding), Tone.Warning),
            PrintScreen.Chip(text, new PrintingState(true, On: false, PrinterFault.NotResponding)));
        Assert.Equal(new PrinterChip(text.PrintingOffChip, Tone.Neutral), PrintScreen.Chip(text, new PrintingState(true, On: false, null)));
        Assert.Null(PrintScreen.Chip(text, new PrintingState(true, On: true, null)));
        Assert.Null(PrintScreen.Chip(text, new PrintingState(false, On: false, PrinterFault.NotResponding))); // no printer: nothing said
    }

    [Fact]
    public void A_paper_is_said_as_it_went_and_only_a_missing_one_offers_to_print()
    {
        var text = TillText.French;
        var printing = new PrintingState(true, true, null);

        var printed = PrintScreen.Block(text, printing, new Paper(PaperOutcome.Printed, DrawerOpened: true), PaperKind.Sale)!;
        Assert.Equal((text.Printed(PaperKind.Sale), text.DrawerOpenedLine, (PanelMessage?)null), (printed.Status, printed.Drawer, printed.Warning));

        // Printed, a sale's key becomes "Réimprimer" (D-127); a refund's ticket has the rail's own.
        Assert.Equal(new OperationKey(Operation.Reprint, text.ReprintKey, "F7", true), printed.Print);
        Assert.Null(PrintScreen.Block(text, printing, new Paper(PaperOutcome.Printed, DrawerOpened: false), PaperKind.Refund)!.Print);

        var off = PrintScreen.Block(text, printing, new Paper(PaperOutcome.SwitchedOff, DrawerOpened: false), PaperKind.Sale)!;
        Assert.Equal((text.TicketNotPrintedOff, (string?)null, Operation.PrintTicket), (off.Status, off.Drawer, off.Print!.Operation));

        var failed = PrintScreen.Block(text, printing, new Paper(PaperOutcome.Failed, DrawerOpened: true), PaperKind.Repayment)!;
        Assert.Null(failed.Status); // the warning says it, in the line's place
        Assert.Equal((text.NotPrintedTitle(PaperKind.Repayment), Tone.Warning, text.PrintReceiptKey, text.DrawerOpenedStash),
            (failed.Warning!.Title, failed.Warning.Tone, failed.Print!.Label, failed.Drawer));

        Assert.Null(PrintScreen.Block(text, printing, null, PaperKind.Sale));
        Assert.Null(PrintScreen.Block(text, new PrintingState(false, true, null), new Paper(PaperOutcome.Printed, false), PaperKind.Sale));
    }

    [Fact]
    public async Task A_printer_that_fails_says_so_until_a_print_goes_through()
    {
        var device = new RecordingPrinter { Fails = true };
        var printer = new TillPrinter(device, device, 42);
        var paper = new ReceiptDocument([new TextLine("x")]);

        Assert.False(await printer.PrintAsync(paper));
        Assert.Equal(PrinterFault.NotResponding, printer.Fault);
        Assert.False(await printer.OpenDrawerAsync(DrawerOpenReason.Sale));

        device.Fails = false;
        Assert.True(await printer.PrintAsync(paper));
        Assert.Null(printer.Fault);
    }
}

/// <summary>A printer and its drawer that keep what they were sent, in order, and can be made to fail.</summary>
public sealed class RecordingPrinter : IReceiptPrinter, ICashDrawer
{
    /// <summary>Everything sent: a document, or a drawer's reason.</summary>
    public List<object> Sent { get; } = [];

    public bool Fails { get; set; }

    public IEnumerable<ReceiptDocument> Papers => Sent.OfType<ReceiptDocument>();

    public IEnumerable<DrawerOpenReason> Opens => Sent.OfType<DrawerOpenReason>();

    public Task PrintAsync(ReceiptDocument document, CancellationToken cancellationToken = default)
    {
        if (Fails)
        {
            throw new ReceiptPrinterException("The fake printer is failing.");
        }

        Sent.Add(document);
        return Task.CompletedTask;
    }

    public Task OpenAsync(DrawerOpenReason reason, CancellationToken cancellationToken = default)
    {
        if (Fails)
        {
            throw new ReceiptPrinterException("The fake printer is failing.");
        }

        Sent.Add(reason);
        return Task.CompletedTask;
    }
}
