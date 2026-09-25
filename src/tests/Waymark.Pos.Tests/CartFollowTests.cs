using Waymark.Contracts.Pos;
using Waymark.Pos.Checkout;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// When the ticket moves (G1 kit §9, "la géométrie du panier ne change jamais"). The failures are
/// the two a cashier sees at once and nobody reports as a bug: a long ticket jumping back to its
/// first line on a redraw the cashier did not cause, and the line just scanned drawn out of sight.
/// </summary>
public sealed class CartFollowTests
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 13, 32, 0, TimeSpan.Zero);

    private static ProductForSale Product(string id) =>
        new(id, "p-" + id, "Soummam", "nature 1 L", "pc", 0, 900, TvaRateSource.FromCategory, "143.00", "DZD", false, "40");

    [Fact]
    public void A_scan_brings_its_line_into_view()
    {
        var cart = new Cart();
        cart.Add(Product("a"), "613a");
        var before = cart.LastAdded;
        cart.Add(Product("b"), "613b");

        Assert.Equal(new CartTarget(cart.LineOf("b"), WithActions: false), CartFollow.After(before, cart.LastAdded, null, null));
    }

    [Fact]
    public void A_second_scan_of_the_same_product_brings_its_line_into_view_again()
    {
        // The line is the same line, further up the ticket; the scan is a new one, and the cashier
        // must see its count go up.
        var cart = new Cart();
        cart.Add(Product("a"), "613a");
        cart.Add(Product("b"), "613b");
        cart.Add(Product("a"), "613a");
        var before = cart.LastAdded;
        cart.Add(Product("a"), "613a");

        Assert.Equal(new CartTarget(cart.LineOf("a"), WithActions: false), CartFollow.After(before, cart.LastAdded, null, null));
    }

    [Fact]
    public void A_redraw_that_is_not_a_scan_leaves_the_ticket_where_the_cashier_left_it()
    {
        // The clock, a health check, the Almanac board, a notice dismissed: the same line object,
        // the same selection. Following anything here is what sent a long ticket back to the top.
        var cart = new Cart();
        cart.Add(Product("a"), "613a");

        Assert.Null(CartFollow.After(cart.LastAdded, cart.LastAdded, null, null));
        Assert.Null(CartFollow.After(cart.LastAdded, cart.LastAdded, "a", "a"));
    }

    [Fact]
    public void Touching_a_line_brings_its_actions_into_view()
    {
        var cart = new Cart();
        cart.Add(Product("a"), "613a");
        cart.Add(Product("b"), "613b");

        Assert.Equal(new CartTarget("a", WithActions: true), CartFollow.After(cart.LastAdded, cart.LastAdded, null, "a"));
        Assert.Equal(new CartTarget("b", WithActions: true), CartFollow.After(cart.LastAdded, cart.LastAdded, "a", "b"));
    }

    [Fact]
    public void Letting_go_of_a_line_or_taking_it_out_moves_nothing()
    {
        var cart = new Cart();
        cart.Add(Product("a"), "613a");
        var before = cart.LastAdded;

        Assert.Null(CartFollow.After(before, before, "a", null));

        cart.Remove(cart.LineOf("a"), Now);
        Assert.Null(CartFollow.After(before, cart.LastAdded, "a", null));
    }

    [Fact]
    public void A_scan_of_the_selected_line_keeps_its_actions_in_view_too()
    {
        var cart = new Cart();
        cart.Add(Product("a"), "613a");
        var before = cart.LastAdded;
        cart.Add(Product("a"), "613a");

        var line = cart.LineOf("a");
        Assert.Equal(new CartTarget(line, WithActions: true), CartFollow.After(before, cart.LastAdded, line, line));
    }

    [Fact]
    public void A_new_sale_moves_nothing_until_its_first_scan()
    {
        var cart = new Cart();
        cart.Add(Product("a"), "613a");
        var before = cart.LastAdded;
        cart.Clear();

        Assert.Null(CartFollow.After(before, cart.LastAdded, null, null));
    }
}
