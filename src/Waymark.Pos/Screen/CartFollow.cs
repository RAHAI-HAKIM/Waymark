using Waymark.Pos.Checkout;

namespace Waymark.Pos.Screen;

/// <summary>
/// Which cart line the window brings into view after it draws a frame, if any (G1 kit §9: "la
/// géométrie du panier ne change jamais"). Decided here, like everything the till shows, so it is
/// tested without a window (D-082).
///
/// <para>
/// <b>The ticket keeps the cashier's place.</b> A clock tick, a health check, a board refresh, a
/// notice, a line taken out: each redraws the ticket, and none may move it. Before this rule every
/// redraw put a long ticket back at its first line, the fifteen-second clock included, while the
/// line just scanned sat out of sight at the foot. Two things move it, because the cashier has to
/// see what they did: <b>a scan</b> brings its line into view — a new line at the foot, or a second
/// unit on a line further up — and <b>touching a line</b> brings its actions into view, since they
/// open beneath it.
/// </para>
/// </summary>
public static class CartFollow
{
    /// <param name="scannedBefore">The cart's last-added line when the window last drew.</param>
    /// <param name="scannedNow">
    /// The cart's last-added line now. A scan always makes a new line object, so a different object
    /// is a scan, even of the product scanned just before.
    /// </param>
    /// <param name="selectedBefore">The line selected when the window last drew.</param>
    /// <param name="selectedNow">The line selected now.</param>
    /// <returns>The line to bring into view, or null to leave the ticket where the cashier left it.</returns>
    public static CartTarget? After(CartLine? scannedBefore, CartLine? scannedNow, string? selectedBefore, string? selectedNow)
    {
        if (scannedNow is not null && !ReferenceEquals(scannedNow, scannedBefore))
        {
            return new CartTarget(scannedNow.LineId, WithActions: scannedNow.LineId == selectedNow);
        }

        return selectedNow is not null && selectedNow != selectedBefore
            ? new CartTarget(selectedNow, WithActions: true)
            : null;
    }
}

/// <summary>A line of the sale to bring into view.</summary>
/// <param name="LineId">The line, which is one still in the sale: a struck line is never followed.</param>
/// <param name="WithActions">Its action bar too, because the line is the selected one.</param>
public sealed record CartTarget(string LineId, bool WithActions);
