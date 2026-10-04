using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Domain.Enums;
using Waymark.Pos.Screen;
using Waymark.Pos.Ui;

namespace Waymark.Pos.Tests;

/// <summary>
/// The block B review (03/10): defects of the window found by walking every screen of the block and
/// looking at it. None is visible to the screen model's tests: a panel that would not close, keys
/// pushed out of view or drawn over their own label, figures shown as the wire spells them.
/// </summary>
public sealed partial class TillWindowTests
{
    private static Screen.Approval ApprovalOf(Till till) =>
        till.Window.GetVisualDescendants().OfType<Border>().Select(border => border.Tag).OfType<Screen.Approval>().Single();

    // ================================================================ Échap

    [Fact]
    public Task Echap_closes_brouillons_as_it_closes_every_other_panel() => Headless.Run(() =>
    {
        // Only its own "Fermer" closed it: the rail's keys stayed hidden behind the list.
        using var till = Till.SignedIn();
        till.ScanMany(2);
        CancelWithAReason(till);
        till.Tap(Operation(till, Screen.Operation.Drafts), new Point(20, 20));
        Assert.DoesNotContain(till.Window.GetVisualDescendants().OfType<TillKey>(), key => key.Tag is OperationKey { Operation: Screen.Operation.Tickets });

        till.Key(Key.Escape, PhysicalKey.Escape);

        Assert.Contains(till.Window.GetVisualDescendants().OfType<TillKey>(), key => key.Tag is OperationKey { Operation: Screen.Operation.Tickets });
        Assert.Single(till.Session.Drafts); // closed, not emptied
    });

    // ================================================================ D-106: a line struck after "Encaisser"

    [Fact]
    public Task A_cashiers_strike_after_encaisser_asks_a_managers_pin_then_strikes_citing_it() => Headless.Run(() =>
    {
        // The cancel's own rule, one line at a time: take the cash, close the panel, strike the lines.
        using var till = Till.SignedIn();
        till.ScanMany(2);
        till.Key(Key.F12, PhysicalKey.F12);       // the payment panel opened...
        till.Key(Key.Escape, PhysicalKey.Escape); // ...and closed without paying
        till.Tap(till.Rows()[0], new Point(200, 20));

        till.Key(Key.F8, PhysicalKey.F8);

        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));
        Assert.Equal(TillText.French.StrikeApprovalTitle, ApprovalOf(till).Title);
        Assert.Equal(2, till.Session.Cart.ActiveLines.Count); // nothing is struck until somebody allows it

        SamiaAuthorises(till);

        till.WaitFor(() => till.Session.Cart.ActiveLines.Count == 1);
        Assert.Equal("auth-samia", till.Session.Cart.Lines.Single(line => line.IsRemoved).RemovedAuthorisation);
    });

    [Fact]
    public Task A_line_struck_before_encaisser_asks_nobody() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(2);
        till.Tap(till.Rows()[0], new Point(200, 20));

        till.Key(Key.F8, PhysicalKey.F8);

        Assert.Single(till.Session.Cart.ActiveLines);
        Assert.Null(till.Session.Cart.Lines.Single(line => line.IsRemoved).RemovedAuthorisation);
    });

    [Fact]
    public Task A_ticket_struck_empty_can_be_cancelled_and_the_cancel_is_sent_with_its_struck_lines() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.ScanMany(1);
        till.Tap(till.Rows()[0], new Point(200, 20));
        till.Key(Key.F8, PhysicalKey.F8);

        CancelWithAReason(till);

        till.WaitFor(() => till.Server.Voids.Count == 1);
        Assert.Empty(till.Server.Voids[0].Lines);
        Assert.Single(till.Server.Voids[0].Removed!);
        till.WaitFor(() => till.Session.Cart.Lines.Count == 0);
    });

    // ================================================================ the rail's panel

    [Fact]
    public Task Continuer_stays_in_view_however_many_reasons_the_shop_has() => Headless.Run(() =>
    {
        // The keys were the last thing in a scrolling list: with a refund's three reasons at 768 px,
        // Continuer was below the fold, and only Entrée reached it.
        using var till = Till.SignedIn(server => server.ExtraDiscountReasons = 10);
        till.ScanMany(1);
        till.Tap(till.Rows()[0], new Point(200, 20));
        till.Key(Key.F4, PhysicalKey.F4);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Count(control => control.Tag is ReasonRow) == 12);

        var card = till.Window.GetVisualDescendants().OfType<Border>().Single(border => border.Tag is Rail.Discount);
        var goOn = till.Window.GetVisualDescendants().OfType<TillKey>()
            .Single(key => key.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == TillText.French.ContinueKey));
        var foot = goOn.TranslatePoint(new Point(0, goOn.Bounds.Height), card)!.Value.Y;

        Assert.True(foot <= card.Bounds.Height + 0.5, $"Continuer ends at {foot:0} in a card {card.Bounds.Height:0} high.");
        Assert.True(goOn.TranslatePoint(new Point(0, 0), card)!.Value.Y >= 0);
    });

    [Fact]
    public Task With_a_discount_to_take_off_continuer_is_still_written_in_full() => Headless.Run(() =>
    {
        // Three keys on one row left "Conti…" of the third.
        using var till = Till.SignedIn(server => server.SellerMayDiscount = true);
        till.ScanMany(1);
        till.Key(Key.F6, PhysicalKey.F6);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ReasonRow));
        Keys(till, "10");
        till.Tap(Keyed<ReasonRow>(till, row => row.Code == "geste_commercial"), new Point(20, 20));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Session.Cart.TicketDiscount is not null);

        till.Key(Key.F6, PhysicalKey.F6); // the discount opened again: Revenir, Retirer la remise, Continuer
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == TillText.French.RemoveDiscount));

        var label = till.Window.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == TillText.French.ContinueKey);
        label.Measure(Size.Infinity);
        Assert.True(label.Bounds.Width >= label.DesiredSize.Width - 0.5, $"Continuer has {label.Bounds.Width:0} px of the {label.DesiredSize.Width:0} it needs.");
    });

    // ================================================================ a key's F key

    [Theory]
    [InlineData("F4")]
    [InlineData("F8")]
    public Task Under_a_line_a_keys_f_key_sits_beside_its_label_not_on_it(string functionKey) => Headless.Run(() =>
    {
        // A key only as wide as its label had its F key drawn over the label's end.
        using var till = Till.SignedIn();
        till.ScanMany(1);
        till.Tap(till.Rows()[0], new Point(200, 20));

        var bar = ((StackPanel)till.Scroll.Content!).Children.Single(child => child.Tag is LineActions);
        var key = bar.GetVisualDescendants().OfType<TillKey>().Single(candidate => candidate.GetVisualDescendants().OfType<TextBlock>().Any(text => text.Text == functionKey));
        var hint = key.GetVisualDescendants().OfType<TextBlock>().Single(text => text.Text == functionKey);
        var face = key.GetVisualDescendants().OfType<StackPanel>().First(panel => panel.Orientation == Avalonia.Layout.Orientation.Horizontal);

        var hintStarts = hint.TranslatePoint(new Point(0, 0), key)!.Value.X;
        var faceEnds = face.TranslatePoint(new Point(face.Bounds.Width, 0), key)!.Value.X;
        Assert.True(hintStarts >= faceEnds - 0.5, $"{functionKey} starts at {hintStarts:0}, the label ends at {faceEnds:0}.");
    });

    // ================================================================ the manager step's summary

    [Fact]
    public Task The_owners_step_past_the_limit_shows_what_is_available_as_the_till_shows_money() => Headless.Run(() =>
    {
        // "Disponible 100.00" beside "143,00 DA": the wire's text, as it came.
        using var till = WithCustomers(server =>
        {
            server.TabAsPart = false;
            server.TabBalance = "7900.00";
            server.TabAvailable = "100.00";
        });
        till.ScanMany(1);
        AttachSamira(till);
        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.OnAccount) == true);
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.OnAccount), new Point(20, 20));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));

        var summary = ApprovalOf(till).Summary;
        Assert.Contains("100,00 DA", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("100.00", summary, StringComparison.Ordinal);
    });

    [Fact]
    public Task The_owners_step_for_a_new_limit_shows_both_limits_as_the_till_shows_money() => Headless.Run(() =>
    {
        using var till = WithCustomers();
        AttachSamira(till);
        till.Key(Key.F5, PhysicalKey.F5);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Border>().Any(border => border.Tag is CarnetView));
        till.Tap(till.Window.GetVisualDescendants().OfType<TillKey>().Single(key => key.Tag is FormKey { Id: CustomerScreen.ChangeKey }), new Point(20, 20));
        Keys(till, "12000");
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));

        var summary = ApprovalOf(till).Summary;
        Assert.Contains("8 000,00 DA → 12 000,00 DA", summary, StringComparison.Ordinal);
    });

    [Fact]
    public Task The_managers_step_for_cash_out_names_the_reason_and_the_amount_not_a_code() => Headless.Run(() =>
    {
        // "MONTANT 500 · caisse-depot": a reason's code, and an amount with no currency.
        using var till = Till.SignedIn();
        till.Server.PaidOutNeedsManager = true;
        OpenPettyCash(till, CustomerScreen.CashOutKey);
        till.WaitFor(() => FormOf(till)?.Rows.Count > 0);
        Keys(till, "500");
        till.Tap(Keyed<FormRow>(till, row => row.Id == "caisse-depot"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => till.Window.GetVisualDescendants().OfType<Control>().Any(control => control.Tag is ApproverRow));

        var summary = ApprovalOf(till).Summary;
        Assert.Contains("500,00 DA · Dépôt en banque", summary, StringComparison.Ordinal);
        Assert.DoesNotContain("caisse-depot", summary, StringComparison.Ordinal);
    });

    // ================================================================ words

    [Fact]
    public Task A_tab_part_is_not_told_what_a_card_does() => Headless.Run(() =>
    {
        // "Une carte ne rend pas de monnaie" under "CARNET DE Samira B.".
        using var till = WithCustomers();
        till.ScanMany(1);
        AttachSamira(till);
        till.Key(Key.F12, PhysicalKey.F12);
        till.WaitFor(() => PaymentOf(till)?.Methods.Any(method => method.Method == PaymentMethod.OnAccount) == true);
        till.Tap(Keyed<MethodChoice>(till, choice => choice.Method == PaymentMethod.OnAccount), new Point(20, 20));

        Assert.Equal(TillText.French.TabPartExact, PaymentOf(till)!.Entry!.NoChange);
        Assert.DoesNotContain(till.Window.GetVisualDescendants().OfType<TextBlock>(), block => block.Text == TillText.French.NoChangeOnCard);
    });
}
