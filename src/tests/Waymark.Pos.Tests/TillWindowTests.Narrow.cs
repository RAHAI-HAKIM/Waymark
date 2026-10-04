using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Pos.Screen;
using Waymark.Pos.Ui;

namespace Waymark.Pos.Tests;

/// <summary>
/// The till on a narrow screen, 1024 × 768, which many tills are (F-29, block B review). The silent
/// failures, each seen on a screenshot: a ticket whose lines show a stock warning and no article; the
/// key that removes a line pushed off the ticket's edge; tickets on hold out of sight with nothing to
/// say they exist.
/// </summary>
public sealed partial class TillWindowTests
{
    private static void Narrow(Till till)
    {
        till.Window.Width = 1024;
        till.Window.Height = 768;
        till.Pump();
    }

    private static Border TicketCard(Till till) =>
        till.Rows()[0].GetVisualAncestors().OfType<Border>().First(border => border.CornerRadius.TopLeft == TillSizes.CardRadius);

    [Fact]
    public Task A_narrow_till_gives_the_ticket_the_room_the_rail_can_spare() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(1);
        var wide = TicketCard(till).Bounds.Width;

        Narrow(till);

        // 1024 less the margins, the gap and the narrow rail: with the full rail it was 504.
        Assert.Equal(1024 - (2 * TillSizes.Margin) - TillSizes.Gap - TillSizes.RailNarrow, TicketCard(till).Bounds.Width, 1.0);
        Assert.True(wide > TicketCard(till).Bounds.Width);
    });

    private static TillKey RemoveKeyOf(Till till) => till.Window.GetVisualDescendants().OfType<TillKey>()
        .Single(key => key.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == TillText.French.RemoveLine));

    [Fact]
    public Task On_a_narrow_till_the_key_that_removes_a_line_stays_whole_and_on_the_ticket() => Headless.Run(() =>
    {
        // Kept in one row it got what the other keys left: off the ticket's edge with the full rail,
        // squeezed to "Retirer la l…" with the narrow one. It wraps onto a second row instead.
        using var till = Till.SignedIn();
        till.ScanMany(2);
        till.Tap(till.Rows()[0], new Point(200, 20));
        var whole = RemoveKeyOf(till).Bounds.Width; // on a wide till it has all the room it asks for

        Narrow(till);

        var card = TicketCard(till);
        var remove = RemoveKeyOf(till);
        var end = remove.TranslatePoint(new Point(remove.Bounds.Width, 0), card)!.Value.X;
        Assert.True(remove.Bounds.Width >= whole - 0.5, $"Retirer la ligne has {remove.Bounds.Width:0} px and needs {whole:0}.");
        Assert.True(end <= card.Bounds.Width + 0.5, $"Retirer la ligne ends at {end:0} on a ticket {card.Bounds.Width:0} wide.");
        Assert.True(remove.TranslatePoint(new Point(0, 0), card)!.Value.X >= 0);
    });

    [Fact]
    public Task A_line_with_a_chip_still_shows_its_article() => Headless.Run(() =>
    {
        // The chip kept its width beside the name whatever the room: the row read "AU-DELÀ DU STOCK
        // ENREGISTRÉ" and no article at all.
        using var till = Till.SignedIn(server => server.StockOnHand = "0");
        Narrow(till);
        till.ScanMany(1);

        var row = till.Rows()[0];
        var line = Assert.IsType<LineRow>(row.Tag);
        Assert.NotEmpty(line.Chips);
        var name = row.GetVisualDescendants().OfType<TextBlock>().First(text => text.Text == line.Article);

        Assert.True(name.Bounds.Width >= 100, $"The article has {name.Bounds.Width:0} px.");
    });

    [Fact]
    public Task Tickets_on_hold_are_counted_beside_their_tabs_and_the_count_brings_the_oldest_back() => Headless.Run(() =>
    {
        // A tab scrolled out of sight was a ticket nobody knew was there.
        using var till = Till.SignedIn();
        Narrow(till);
        till.ScanMany(1);
        till.Key(Key.F3, PhysicalKey.F3);
        till.ScanMany(2);
        till.Key(Key.F3, PhysicalKey.F3);
        Assert.Equal(2, till.Session.Parked.Count);
        var oldest = till.Session.Parked[0].Id;

        var count = till.Window.GetVisualDescendants().OfType<TillKey>().Single(key => Equals(key.Tag, TillViews.HeldCountTag));
        till.Tap(count, new Point(10, 10));

        Assert.DoesNotContain(till.Session.Parked, held => held.Id == oldest);
        Assert.Single(till.Session.Cart.ActiveLines);
    });
}
