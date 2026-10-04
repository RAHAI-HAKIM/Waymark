using Waymark.Domain.Values;

namespace Waymark.Domain.Sales;

/// <summary>Why a cancelled ticket is shown to the owner (B8, D-097). Shown in the Z-report (C2) and Admin's review queue, never on the till.</summary>
public enum VoidAlert
{
    /// <summary>Cancelled after the payment panel was opened on it: the customer may have paid. Always flagged.</summary>
    AfterEncaisser,

    /// <summary>More cancels by this person in this cash session than the owner allows (<c>void_alert_count</c>).</summary>
    CountPerShift,

    /// <summary>Worth more than the owner allows a ticket to be cancelled unremarked (<c>void_alert_value</c>).</summary>
    Value,
}

/// <summary>A cancelled ticket, as the flag rule reads it.</summary>
/// <param name="Value">What the ticket came to when it was cancelled, as the server prices it.</param>
/// <param name="PaymentOpened">The payment panel had been opened on it.</param>
public sealed record CancelledTicket(Money Value, bool PaymentOpened);

/// <summary>
/// <b>Session B8</b> A ticket cancelled at the till (D-097): whether it needs a manager, and why the
/// owner should look at it. Pure and in Domain: the server asks <see cref="NeedsAuthorisation"/>
/// before it records a cancel, and the Z-report (C2) and Admin ask <see cref="Flags"/>.
///
/// <para><b>The rules <c>VoidsTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="NeedsAuthorisation"/>:</b> a manager's PIN is needed only when
///   the seller may not cancel alone (their rank does not reach <c>VoidTransaction</c>, asked through
///   <c>StaffPermissions</c>, never here) <b>and</b> the payment panel had been opened on the ticket.
///   Everyone else cancels with a reason only; every cancel is recorded either way.</description></item>
///   <item><description><b><see cref="Flags"/>, in this order:</b>
///   <see cref="VoidAlert.AfterEncaisser"/> when the payment panel had been opened, always;
///   <see cref="VoidAlert.CountPerShift"/> when the person's cancels in this cash session, <b>this one
///   included</b>, are <b>more than</b> the count, so with a count of 3 the fourth is flagged;
///   <see cref="VoidAlert.Value"/> when the ticket is worth <b>more than</b> the value, exactly the
///   value is not. A threshold that is null flags nothing. None of them: an empty list.</description></item>
///   <item><description>A count of cancels below one throws: the cancel being judged is one of them.
///   A value in another currency than the threshold throws, as <see cref="Money"/> always does.</description></item>
/// </list>
/// </summary>
public static class Voids
{
    /// <param name="sellerMayVoid">The seller's rank reaches <c>VoidTransaction</c>, as <c>StaffPermissions.May</c> says.</param>
    /// <param name="paymentOpened">The payment panel had been opened on the ticket.</param>
    public static bool NeedsAuthorisation(bool sellerMayVoid, bool paymentOpened)
    {
        if(!sellerMayVoid && paymentOpened)
        {
            return true;
        }
        return false;
    }

    /// <summary>
    /// A line struck once the payment panel had been opened on its ticket (D-106): the same theft as
    /// a cancel at that stage, one line at a time, so the same rule. A manager's PIN is needed when
    /// the seller may not cancel alone <b>and</b> the line was struck at or after the moment
    /// "Encaisser" was first opened. A line struck before it, or on a ticket never opened for
    /// payment, needs nothing; it is recorded either way (D-097).
    /// </summary>
    /// <param name="sellerMayVoid">The seller's rank reaches <c>VoidTransaction</c>, as <c>StaffPermissions.May</c> says.</param>
    /// <param name="paymentOpenedAt">When the payment panel was first opened on the ticket; null when it never was.</param>
    /// <param name="removedAt">When the line was struck, by the same clock.</param>
    public static bool StrikeNeedsAuthorisation(bool sellerMayVoid, DateTimeOffset? paymentOpenedAt, DateTimeOffset removedAt) =>
        NeedsAuthorisation(sellerMayVoid, paymentOpenedAt is { } opened && removedAt >= opened);

    /// <param name="cancel">The cancelled ticket.</param>
    /// <param name="cancelsThisSession">The person's cancels in this cash session, this one included.</param>
    /// <param name="alertCount">The owner's <c>void_alert_count</c>; null: not flagged by count.</param>
    /// <param name="alertValue">The owner's <c>void_alert_value</c>; null: not flagged by value.</param>
    public static IReadOnlyList<VoidAlert> Flags(CancelledTicket cancel, int cancelsThisSession, int? alertCount, Money? alertValue)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(cancelsThisSession, 1);
        var Result = new List<VoidAlert>();
        if(cancel.PaymentOpened)
        {
            Result.Add(VoidAlert.AfterEncaisser);
        }
        if(!(alertCount is null) && cancelsThisSession > alertCount)
        {
            Result.Add(VoidAlert.CountPerShift);
        }
        if(!(alertValue is null) && cancel.Value > alertValue)
        {
            Result.Add(VoidAlert.Value);
        }

        return Result;
    }
}
