using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Domain.Hardware;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Ui;

namespace Waymark.Pos.Tests;

/// <summary>
/// The block D review (16/10): what Hakim's pass and the extreme cases found, kept as tests. The
/// silent failures: a reprint counted twice by a double press; F7 or F9 acting behind a panel that
/// freezes the ticket; F3 opening a ticket scanned long ago; a receipt the server never sent told as a
/// printer that does not answer; the Almanac card pushed past the bar by "Plus…".
/// </summary>
public sealed partial class TillWindowTests
{
    private static OperationKey ReprintKeyShown(Till till) =>
        (OperationKey)Operation(till, Screen.Operation.Reprint).Tag!;

    [Fact]
    public Task A_reprint_shows_its_press_and_a_second_press_counts_no_second_copy() => Headless.Run(() =>
    {
        var hold = new TaskCompletionSource();
        using var till = PrintingTill(server => server.HoldReprints = hold);
        till.TypeSlowly("S-2026-000142");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "t-142");

        till.Key(Key.F7, PhysicalKey.F7);
        till.WaitFor(() => ReprintKeyShown(till) is { Enabled: false });
        Assert.Equal(TillText.French.ReprintingKey, ReprintKeyShown(till).Label);

        till.Key(Key.F7, PhysicalKey.F7);                              // impatient
        till.Tap(Operation(till, Screen.Operation.Reprint), new Point(20, 20)); // and touched
        Assert.Single(till.Server.Reprints);

        hold.SetResult();
        till.WaitFor(() => till.Printer!.Papers.Any());
        till.WaitFor(() => ReprintKeyShown(till) is { Enabled: true });
        Assert.Equal(TillText.French.ReprintKey, ReprintKeyShown(till).Label);
        Assert.Single(till.Server.Reprints);
        Assert.Single(till.Printer!.Papers);
    });

    [Fact]
    public Task F7_and_f9_do_nothing_behind_a_refund_being_prepared_or_a_payment() => Headless.Run(() =>
    {
        using var till = PrintingTill(server => server.CustomerModule = true);

        // A past ticket, its refund panel open in the rail: F7 would reprint it under the cashier's hands.
        OpenRefund(till);
        till.Key(Key.F7, PhysicalKey.F7);
        till.Key(Key.F9, PhysicalKey.F9);
        till.Pump();
        Assert.Empty(till.Server.Reprints);
        Assert.Empty(till.Server.NoSales);
        Assert.DoesNotContain(till.Window.GetVisualDescendants().OfType<Control>(), control => control.Tag is FormRow { Id: "TIROIR-MONNAIE" });

        // A paid ticket, the client search floating over it (D-094): the same.
        till.Key(Key.Escape, PhysicalKey.Escape);
        till.Key(Key.Escape, PhysicalKey.Escape);
        till.Key(Key.Escape, PhysicalKey.Escape);
        till.ScanMany(1);
        till.Pay();
        till.WaitFor(() => till.Printer!.Papers.Any());
        var sent = till.Printer!.Sent.Count;
        till.Key(Key.F5, PhysicalKey.F5);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is FormPanel));
        till.Key(Key.F7, PhysicalKey.F7);
        till.Pump();
        Assert.Empty(till.Server.Reprints);
        Assert.Equal(sent, till.Printer.Sent.Count);
    });

    [Fact]
    public Task F3_opens_a_scanned_ticket_only_while_the_slot_still_offers_it() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(1);
        till.TypeAndEnter("S-2026-000142");
        till.WaitFor(() => till.Session.Notice?.Kind == TillNoticeKind.TicketScanned);

        // Something else happened since: a product scanned. F3 is "Attente" alone now.
        till.Scan(Till.Code(5));
        till.Key(Key.F3, PhysicalKey.F3);
        till.Pump();

        Assert.Single(till.Session.Parked);
        Assert.Null(till.Session.Viewing);
    });

    [Fact]
    public Task A_receipt_the_server_did_not_send_back_is_not_told_as_a_printer_that_does_not_answer() => Headless.Run(() =>
    {
        using var till = PrintingTill(server => server.ReceiptsUnread = true);
        till.ScanMany(1);
        till.Pay();
        till.WaitFor(() => PaperShown(till)?.Warning is not null);

        Assert.Equal(TillText.French.NotReadBody(PaperKind.Sale), PaperShown(till)!.Warning!.Body);
        Assert.Null(ChipShown(till)); // the printer is not at fault
        Assert.Equal(Screen.Operation.PrintTicket, PaperShown(till)!.Print!.Operation);

        till.Server.ReceiptsUnread = false;
        till.Key(Key.F7, PhysicalKey.F7);
        till.WaitFor(() => PaperShown(till)?.Status == TillText.French.Printed(PaperKind.Sale));
    });

    [Fact]
    public Task Plus_takes_the_rail_while_open_and_the_almanac_card_comes_back_after() => Headless.Run(() =>
    {
        using var till = PrintingTill();
        bool AlmanacShown() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is AlmanacSlot);
        Assert.True(AlmanacShown());

        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        Assert.False(AlmanacShown());

        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        Assert.True(AlmanacShown());
    });
}
