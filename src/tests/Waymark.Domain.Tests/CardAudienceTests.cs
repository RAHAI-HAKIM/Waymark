using Waymark.Domain.Engine;

namespace Waymark.Domain.Tests;

/// <summary>
/// Who may act on a card (hop 7, D-074).
///
/// </para>
/// <para>
/// Getting this wrong is silent in the worst direction. Too strict and a card is simply never
/// seen — a shopkeeper concludes the engine found nothing. Too loose and a cashier is shown
/// something the shop meant for a manager, which is the Integration Layer's whole job to
/// prevent. Neither shows up as an error anywhere.
/// </para>
/// </summary>
public sealed class CardAudienceTests
{
    private const long Cashier = 1;
    private const long Manager = 2;
    private const long Owner = 3;

    [Fact]
    public void A_manager_may_decide_a_card_meant_for_a_manager()
    {
        Assert.True(CardAudience.MayDecide(Manager, Manager));
    }

    [Fact]
    public void An_owner_may_decide_a_card_meant_for_a_manager()
    {
        // The rule is "this rank and anything above it". A shop where the person in charge is
        // locked out of what their manager can do is not a shop anybody would run.
        Assert.True(CardAudience.MayDecide(Owner, Manager));
    }

    [Fact]
    public void A_cashier_may_not()
    {
        Assert.False(CardAudience.MayDecide(Cashier, Manager));
    }
}
