using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Contracts.Reference;
using Waymark.Domain.Hardware;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;

namespace Waymark.Pos.Tests;

/// <summary>
/// D2 (D-126): the paper and the drawer at the till, driven through the window with a printer that
/// keeps what it was sent. The silent failures: the drawer opened before the server wrote the sale, or
/// for a card; a receipt printed while printing was switched off, or not printed while it was on; a
/// failing printer that fails the sale, or hides; a no-sale whose drawer opens before its row exists.
/// </summary>
public sealed partial class TillWindowTests
{
    private static Till PrintingTill(Action<FakeStoreServer>? setup = null, RecordingPrinter? printer = null)
    {
        var server = new FakeStoreServer { DrawerAnswers = true, DrawerIsOpen = true };
        setup?.Invoke(server);
        return new Till(server, signedIn: true, printer ?? new RecordingPrinter());
    }

    private static PaperBlock? PaperShown(Till till) =>
        till.Window.GetVisualDescendants().OfType<StackPanel>().Select(panel => panel.Tag).OfType<PaperBlock>().SingleOrDefault();

    private static PrinterChip? ChipShown(Till till) =>
        till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<PrinterChip>().SingleOrDefault();

    [Fact]
    public Task A_cash_sale_opens_the_drawer_then_prints_its_ticket_and_the_paid_rail_says_both() => Headless.Run(() =>
    {
        using var till = PrintingTill();
        till.ScanMany(1);
        till.Pay();
        till.WaitFor(() => till.Printer!.Papers.Any());

        // The server first (one sale written), then the pulse, then the paper: the change before the receipt.
        Assert.Equal(1, till.Server.Sales);
        Assert.Equal(DrawerOpenReason.Sale, Assert.IsType<DrawerOpenReason>(till.Printer!.Sent[0]));
        Assert.IsType<ReceiptDocument>(till.Printer.Sent[1]);
        Assert.Equal("0142", till.Server.Receipts.Single());

        till.WaitFor(() => PaperShown(till) is not null);
        Assert.Equal((TillText.French.Printed(PaperKind.Sale), TillText.French.DrawerOpenedLine, (Screen.Operation?)Screen.Operation.Reprint),
            (PaperShown(till)!.Status, PaperShown(till)!.Drawer, PaperShown(till)!.Print?.Operation));
    });

    [Fact]
    public Task A_card_sale_prints_its_ticket_and_opens_nothing() => Headless.Run(() =>
    {
        using var till = PrintingTill(server => server.SaleAnswer = new SaleAnswer.Completed(new SaleOutcome(
            SaleOutcomes.Completed, "sale", "0142", "143.00", "22.83", null, "DZD", null, [new PaymentLine("card", "143.00", "A1")])));
        till.ScanMany(1);
        till.Pay();
        till.WaitFor(() => till.Printer!.Papers.Any());

        Assert.Empty(till.Printer!.Opens);
        till.WaitFor(() => PaperShown(till) is not null);
        Assert.Null(PaperShown(till)!.Drawer);
    });

    [Fact]
    public Task Printing_switched_off_prints_no_sale_ticket_until_asked_says_so_and_is_on_again_at_the_next_sign_in() => Headless.Run(() =>
    {
        using var till = PrintingTill();
        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.Printing), new Point(20, 20));

        // The list stays open to show the new state; the bar says printing is off, neutral.
        Assert.Contains(till.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == TillText.French.PrintingOff);
        Assert.Equal(new PrinterChip(TillText.French.PrintingOffChip, Tone.Neutral), ChipShown(till));

        till.Key(Key.Escape, PhysicalKey.Escape);
        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20)); // closes the list
        till.ScanMany(1);
        till.Pay();
        till.WaitFor(() => PaperShown(till) is not null);

        // The drawer still opens: printing is the paper, not the cash.
        Assert.Equal([DrawerOpenReason.Sale], till.Printer!.Opens);
        Assert.Empty(till.Printer.Papers);
        Assert.Equal((TillText.French.TicketNotPrintedOff, Screen.Operation.PrintTicket), (PaperShown(till)!.Status, PaperShown(till)!.Print!.Operation));

        // The one customer who asks: F7 prints the original.
        till.Key(Key.F7, PhysicalKey.F7);
        till.WaitFor(() => till.Printer.Papers.Any());
        till.WaitFor(() => PaperShown(till)?.Status == TillText.French.Printed(PaperKind.Sale));

        // Somebody signs in: printing is on again (D-124).
        SignInAs(till, "samia", "1357");
        Assert.Null(ChipShown(till));
    });

    [Fact]
    public Task A_printer_that_fails_leaves_the_sale_paid_says_why_and_prints_from_the_rail_once_it_answers() => Headless.Run(() =>
    {
        var printer = new RecordingPrinter { Fails = true };
        using var till = PrintingTill(printer: printer);
        till.ScanMany(1);
        till.Pay();
        till.WaitFor(() => PaperShown(till)?.Warning is not null);

        Assert.Equal(1, till.Server.Sales);
        Assert.NotNull(till.Session.Paid); // the sale stands
        Assert.Equal((TillText.French.NotPrintedTitle(PaperKind.Sale), Tone.Warning), (PaperShown(till)!.Warning!.Title, PaperShown(till)!.Warning!.Tone));
        Assert.Equal(Tone.Warning, ChipShown(till)?.Tone);

        printer.Fails = false;
        till.Key(Key.F7, PhysicalKey.F7);
        till.WaitFor(() => printer.Papers.Any());
        till.WaitFor(() => PaperShown(till)?.Status == TillText.French.Printed(PaperKind.Sale));
        Assert.Null(ChipShown(till));
    });

    [Fact]
    public Task A_no_sale_is_recorded_first_then_the_drawer_opens_and_the_slot_says_who_allowed_it() => Headless.Run(() =>
    {
        using var till = PrintingTill();
        till.Key(Key.F9, PhysicalKey.F9);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is FormRow { Id: "TIROIR-MONNAIE" }));

        // A scan under the panel is ignored, and the slot says why.
        Assert.Contains(till.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == TillText.French.ScanIgnored);

        till.Tap(Keyed<FormRow>(till, row => row.Id == "TIROIR-MONNAIE"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter);

        // A cashier: nothing recorded, nothing opened, until a manager's PIN.
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));
        Assert.Equal((string?)null, Assert.Single(till.Server.NoSales).Authorisation);
        Assert.Empty(till.Printer!.Opens);

        till.Tap(Keyed<ApproverRow>(till, row => row.StaffId == "samia"), new Point(20, 20));
        till.Window.KeyTextInput("1357");
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is Screen.Approval { PinLength: 4 }));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Printer.Opens.Any());

        Assert.Equal(("TIROIR-MONNAIE", "auth-samia"), (till.Server.NoSales[^1].ReasonCode, till.Server.NoSales[^1].Authorisation));
        Assert.Equal([DrawerOpenReason.NoSale], till.Printer.Opens);
        Assert.Empty(till.Printer.Papers); // a no-sale prints nothing
        Assert.Contains(till.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == TillText.French.NoSaleOpened);
        Assert.Contains("autorisé par Samia K.", till.Session.Notice?.Detail ?? string.Empty, StringComparison.Ordinal);
    });

    [Fact]
    public Task A_no_sale_reason_that_asks_for_a_note_waits_for_one() => Headless.Run(() =>
    {
        using var till = PrintingTill(server => server.SellerMayNoSale = true);
        till.Key(Key.F9, PhysicalKey.F9);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is FormRow { Id: "TIROIR-AUTRE" }));
        till.Tap(Keyed<FormRow>(till, row => row.Id == "TIROIR-AUTRE"), new Point(20, 10));

        till.Key(Key.Enter, PhysicalKey.Enter);
        Assert.Empty(till.Server.NoSales);

        Keys(till, "Billet");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Printer!.Opens.Any());
        Assert.Equal(("TIROIR-AUTRE", "Billet"), (till.Server.NoSales.Single().ReasonCode, till.Server.NoSales.Single().Note));
    });

    [Fact]
    public Task A_refund_in_cash_opens_the_drawer_and_its_ticket_prints_even_with_printing_switched_off() => Headless.Run(() =>
    {
        using var till = PrintingTill();
        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.Printing), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));

        OpenRefund(till);
        Quote(till);
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Printer!.Papers.Any());

        // A refund's ticket is the shop's proof (D-126): the switch is for sale tickets only.
        Assert.Equal([DrawerOpenReason.Refund], till.Printer!.Opens);
        Assert.Equal("S-2026-000143", till.Server.Receipts.Single());
        till.WaitFor(() => PaperShown(till)?.Status == TillText.French.Printed(PaperKind.Refund));
    });

    [Fact]
    public void The_reports_no_sales_are_counted_for_every_reader_and_detailed_for_the_owner_alone()
    {
        var server = new FakeStoreServer { DrawerIsOpen = true, ReadsFlags = true };
        var openings = new NoSaleRowWire[]
        {
            new(new DateTimeOffset(2026, 9, 25, 9, 14, 0, TimeSpan.Zero), "Nabil B.", "Faire de la monnaie", "صرف", null, "Karim M."),
            new(new DateTimeOffset(2026, 9, 25, 15, 5, 0, TimeSpan.Zero), "Yacine R.", "Autre", "سبب آخر", "Billet tombé", "Karim M."),
        };
        Screen.ReportSection Section(NoSalesWire noSales) =>
            ReportScreen.Build(TillText.French, TimeZoneInfo.Utc, null, new ReportState(12, Report: server.SampleReport(12) with { NoSales = noSales }))
                .Sections.Single(section => section.Title == TillText.French.NoSaleTitle);

        var owner = Section(new NoSalesWire(2, openings));
        Assert.Equal((TillText.French.OwnerOnly, 3, "2"), (owner.Aside, owner.Lines.Count, owner.Lines[0].Value));
        // Words, not figures (block D review): who allowed it is said in full under the hour and the person.
        Assert.Equal(("09:14 · Nabil B.", (string?)null, "Faire de la monnaie · autorisé par Karim M.", true),
            (owner.Lines[1].Label, owner.Lines[1].Count, owner.Lines[1].Value, owner.Lines[1].Stacked));
        Assert.Equal("Autre · « Billet tombé » · autorisé par Karim M.", owner.Lines[2].Value);

        var manager = Section(new NoSalesWire(2, null));
        Assert.Equal((1, "2", TillText.French.NoSaleDetailReserved), (manager.Lines.Count, manager.Lines[0].Value, manager.Note));
        Assert.Null(Section(new NoSalesWire(0, null)).Note); // nothing to keep from anyone
    }

    /// <summary>Sign in as somebody else through the window's own path, as the PIN screen would.</summary>
    private static void SignInAs(Till till, string staffId, string pin)
    {
        var flow = (SignInFlow)(typeof(TillWindow).GetField("_signIn", BindingFlags.NonPublic | BindingFlags.Instance)?.GetValue(till.Window)
            ?? throw new InvalidOperationException("TillWindow._signIn was renamed: update this test."));
        var load = flow.LoadAsync();
        while (!load.IsCompleted)
        {
            Dispatcher.UIThread.RunJobs();
        }

        flow.Select(staffId);
        foreach (var digit in pin)
        {
            flow.Press(digit);
        }

        (typeof(TillWindow).GetMethod("OpenTill", BindingFlags.NonPublic | BindingFlags.Instance)
            ?? throw new InvalidOperationException("TillWindow.OpenTill was renamed: update this test.")).Invoke(till.Window, null);
        till.WaitFor(() => till.Session.SignedIn is not null);
        till.Pump();
    }

    private sealed partial class FakeStoreServer
    {
        /// <summary>Every receipt asked for, by number.</summary>
        public List<string> Receipts { get; } = [];

        /// <summary>Every no-sale sent, as sent.</summary>
        public List<NoSaleRequest> NoSales { get; } = [];

        /// <summary>The seller reaches rank 2: a no-sale needs nobody's PIN.</summary>
        public bool SellerMayNoSale { get; set; }

        /// <summary>The server cannot read a receipt back: it answers nothing.</summary>
        public bool ReceiptsUnread { get; set; }

        public Task<ReceiptAnswer?> ReceiptAsync(string idOrNumber, string sessionToken, string? authorisation, CancellationToken cancellationToken = default)
        {
            Receipts.Add(idOrNumber);
            return Task.FromResult<ReceiptAnswer?>(ReceiptsUnread ? null : new ReceiptAnswer(TicketOutcomes.Found, new ReceiptWire(
                ReceiptKinds.Sale, new ReceiptStoreWire("EL AMEL", [], null, null, null), idOrNumber, "25/09/2026", "10:05", "Caisse 1", "Nabil",
                [new ReceiptLineWire("Lait UHT Candia Brique 1L", "1", "pc", "143.00", "0.00", "143.00", false)],
                "143.00", "0.00", "143.00", "0.00", "DZD", [new PastPaymentWire("cash", "143.00")], "2.00", null, null, null, 0, "fr")));
        }

        public Task<ReasonCodeList?> NoSaleReasonsAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<ReasonCodeList?>(new ReasonCodeList("no_sale",
            [
                new ReasonCodeOption("TIROIR-MONNAIE", "صرف النقود", "Faire de la monnaie", RequiresNote: false, RequiresManager: true),
                new ReasonCodeOption("TIROIR-AUTRE", "سبب آخر", "Autre", RequiresNote: true, RequiresManager: true),
            ]));

        public Task<NoSaleAnswer?> NoSaleAsync(NoSaleRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            NoSales.Add(request);
            return Task.FromResult<NoSaleAnswer?>(!SellerMayNoSale && request.Authorisation is null
                ? new NoSaleAnswer(CashMovementOutcomes.PinRequired, Refusal: new Refusal(RefusalCodes.NoSaleNeedsManager))
                : new NoSaleAnswer(CashMovementOutcomes.Recorded, new DateTimeOffset(2026, 9, 25, 13, 36, 0, TimeSpan.Zero)));
        }
    }
}
