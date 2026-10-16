using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Domain.Hardware;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// D3 (D-127): the reprint and the scanned ticket, through the window. The silent failures: a reprint
/// printed as the original, so two papers each look like the only one; a reprint nobody counted; a
/// receipt's number scanned during a sale and sold as a product, or dropping the sale on screen.
/// </summary>
public sealed partial class TillWindowTests
{
    private static string? ReprintedLine(Till till) =>
        till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<string>()
            .SingleOrDefault(tag => tag.StartsWith("Réimprimé", StringComparison.Ordinal));

    [Fact]
    public Task A_past_ticket_offers_reprint_beside_refund_and_f7_prints_a_counted_duplicate() => Headless.Run(() =>
    {
        using var till = PrintingTill();
        till.TypeSlowly("S-2026-000142");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "t-142");

        // Both keys at the rail's foot; no count before the first reprint.
        Assert.Equal("F7", Operation(till, Screen.Operation.Reprint).Tag is OperationKey { Key: var key } ? key : null);
        Assert.NotNull(Operation(till, Screen.Operation.Refund));
        Assert.Null(ReprintedLine(till));

        till.Key(Key.F7, PhysicalKey.F7);
        till.WaitFor(() => till.Printer!.Papers.Any());

        // The server counted it, the paper says it is a copy, the ticket says how many are about.
        Assert.Equal("S-2026-000142", till.Server.Reprints.Single().Ticket);
        var paper = till.Printer!.Papers.Single();
        Assert.Equal(2, paper.Lines.OfType<TextLine>().Count(line => line is { Text: "DUPLICATA", Reversed: true }));
        Assert.Empty(till.Printer.Opens); // a reprint moves no cash
        till.WaitFor(() => ReprintedLine(till) == "Réimprimé 1 fois");
    });

    [Fact]
    public Task Once_the_paid_ticket_has_printed_its_key_is_reprint_and_its_copy_is_a_duplicate() => Headless.Run(() =>
    {
        using var till = PrintingTill();
        till.ScanMany(1);
        till.Pay();
        till.WaitFor(() => PaperShown(till)?.Status == TillText.French.Printed(PaperKind.Sale));
        Assert.Equal(Screen.Operation.Reprint, PaperShown(till)!.Print!.Operation);

        till.Key(Key.F7, PhysicalKey.F7);
        till.WaitFor(() => till.Printer!.Papers.Count() == 2);

        Assert.Equal("0142", till.Server.Reprints.Single().Ticket);
        Assert.Contains(till.Printer!.Papers.Last().Lines.OfType<TextLine>(), line => line.Text == "DUPLICATA");
        Assert.DoesNotContain(till.Printer.Papers.First().Lines.OfType<TextLine>(), line => line.Text == "DUPLICATA");
    });

    [Fact]
    public Task A_receipts_number_scanned_on_an_empty_ticket_opens_it_and_is_never_looked_up_as_a_product() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        var lookups = till.Server.Lookups.Count;

        till.TypeAndEnter("S-2026-000142");
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "t-142");

        Assert.Equal(lookups, till.Server.Lookups.Count);
        Assert.Empty(till.Session.Cart.Lines);
    });

    [Fact]
    public Task A_receipts_number_scanned_during_a_sale_adds_nothing_and_f3_puts_the_sale_on_hold_then_opens_it() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(2);
        var lookups = till.Server.Lookups.Count;

        till.TypeAndEnter("S-2026-000142");
        till.WaitFor(() => till.Session.Notice?.Kind == TillNoticeKind.TicketScanned);

        Assert.Equal(lookups, till.Server.Lookups.Count);
        Assert.Equal(2, till.Session.Cart.ActiveLines.Count); // the sale is left as it was
        Assert.Null(till.Session.Viewing);
        Assert.Contains(till.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == TillText.French.TicketScanned);

        till.Key(Key.F3, PhysicalKey.F3);
        till.WaitFor(() => till.Session.Viewing?.TransactionId == "t-142");
        Assert.Equal(2, Assert.Single(till.Session.Parked).Cart.ActiveLines.Count);
    });

    [Fact]
    public Task An_unknown_receipts_number_scanned_says_so() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.TypeAndEnter("S-2026-999999");
        till.WaitFor(() => till.Session.Notice?.Kind == TillNoticeKind.TicketUnknown);
        Assert.Null(till.Session.Viewing);
    });

    private sealed partial class FakeStoreServer
    {
        /// <summary>Every reprint asked for, as sent.</summary>
        public List<ReprintRequest> Reprints { get; } = [];

        public async Task<ReceiptAnswer?> ReprintAsync(ReprintRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Reprints.Add(request);
            var receipt = (await ReceiptAsync(request.Ticket, sessionToken, request.Authorisation, cancellationToken))!.Receipt!;
            return new ReceiptAnswer(TicketOutcomes.Found, receipt with
            {
                ReprintCount = Reprints.Count(sent => sent.Ticket == request.Ticket),
                Reprint = new ReprintWire("25/09/2026", "10:20", "Nabil"),
            });
        }
    }
}
