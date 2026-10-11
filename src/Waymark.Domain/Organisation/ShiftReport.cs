using System.Collections;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.Domain.Organisation;

/// <summary>What a ticket of the session is, as the report counts it.</summary>
public enum TicketKind
{
    /// <summary>A sale: completed, with its number.</summary>
    Sale,

    /// <summary>A refund: its own numbered ticket, its total and its parts negative (D-098).</summary>
    Refund,

    /// <summary>A cancelled ticket: recorded, priced as its sale would be, with no payment (D-097).</summary>
    Cancel,
}

/// <summary>A ticket of the session as the report reads it.</summary>
/// <param name="Total">The ticket's total as written: negative for a refund; what a cancel came to.</param>
/// <param name="PaymentOpened">A cancel: the payment panel had been opened on it.</param>
/// <param name="DiscountReason">The reason of a discount given on the whole ticket; null when none was.</param>
/// <param name="Policy">The rounding policy stamped on it (D-053).</param>
public sealed record ReportTicket(
    string TicketId, TicketKind Kind, string StaffId, DateTimeOffset At, Money Total, bool PaymentOpened, string? DiscountReason, Rounding Policy);

/// <summary>A <c>transaction_items</c> row as the report reads it: one batch's share of a line.</summary>
/// <param name="LineNumber">The line's place on its ticket; the rows of one line share it (D-104).</param>
/// <param name="Struck">The line was struck before the sale (<c>removed_at</c>): outside every total (D-097).</param>
/// <param name="Total">The row's total, its discount already off.</param>
/// <param name="Discount">What was taken off the row: the line's own and its share of the ticket's, together.</param>
/// <param name="DiscountReason">The line's own reason when it had one, else the ticket's (as <c>CompleteSale</c> writes it).</param>
/// <param name="ListPrice">The price in force a typed price replaced (D-092); null when none was typed.</param>
public sealed record ReportRow(
    string TicketId, int LineNumber, bool Struck, long QuantityThousandths, Money Total, Money Discount, string? DiscountReason, Money? ListPrice);

/// <summary>A <c>transaction_payments</c> row: negative on a refund.</summary>
public sealed record ReportPart(string TicketId, PaymentMethod Method, Money Amount);

/// <summary>One tender's line: how many parts, and how much, as a figure of zero or more.</summary>
public sealed record TenderLine(PaymentMethod Method, int Parts, Money Amount);

/// <summary>What the session took, by tender (C2, D-112).</summary>
/// <param name="Sales">The sales' parts by tender, in the enum's order, a tender nobody used left out.</param>
/// <param name="Refunds">The refunds' parts by tender, the same way, each amount above zero.</param>
/// <param name="Net">Sales less refunds: "Total encaissé".</param>
public sealed record Takings(IReadOnlyList<TenderLine> Sales, IReadOnlyList<TenderLine> Refunds, Money SalesTotal, Money RefundsTotal, Money Net);

/// <summary>How many, and what they come to, as a figure of zero or more.</summary>
public readonly record struct Tally(int Count, Money Value);

/// <summary>What happened at the till, counted.</summary>
public sealed record Activity(Tally Sold, Tally Refunded, Tally Cancelled, Tally StruckLines);

/// <summary>What was given away at the counter. Each value is what came off, above zero when the customer paid less.</summary>
public sealed record Reductions(Tally LineDiscounts, Tally TicketDiscounts, Tally Overrides, Tally Total);

/// <summary>Why the owner should look at a session.</summary>
public enum ShiftFlagKind
{
    /// <summary>A ticket cancelled after the payment panel was opened on it. Critical.</summary>
    CancelAfterEncaisser,

    /// <summary>The count is further from the drawer than the threshold in force at the close. Critical.</summary>
    VariancePastThreshold,

    /// <summary>One person cancelled more tickets in the session than the threshold. Warning.</summary>
    TooManyCancels,

    /// <summary>A cancelled ticket worth more than the threshold. Warning.</summary>
    CancelAboveValue,
}

/// <param name="StaffId">Who it is about; null for the variance, which is the drawer's.</param>
/// <param name="TicketId">The cancelled ticket; null for a count and for the variance.</param>
/// <param name="Value">The ticket's worth, or the variance; null for a count.</param>
/// <param name="Count">The person's cancels in the session; null otherwise.</param>
public sealed record ShiftFlag(ShiftFlagKind Kind, string? StaffId, string? TicketId, Money? Value, int? Count);

/// <summary>The thresholds a session is judged by: today's for an X, those frozen at its close for a Z (D-112).</summary>
public sealed record ReportThresholds(int? VoidCount, Money? VoidValue, Money? Variance);

/// <summary>
/// <b>Session C2</b> A cash session's figures (D-112): the X while it is open, the Z once
/// closed, the same rule for both. Pure and in Domain: the server reads the session's rows and asks
/// this; nothing here is stored, so a report is recomputed each time it is read.
///
/// <para><b>The rules <c>ShiftReportTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b><see cref="TakingsOf"/>:</b> a part of a <see cref="TicketKind.Sale"/> is a
///   sale's, a part of a <see cref="TicketKind.Refund"/> a refund's, shown as what it gave back (its
///   amount negated). Each list has one line per tender used, <b>in the enum's order</b>, with how many
///   parts and their sum; a tender nobody used is left out. <b>Cash is what the tickets say</b>, to the
///   centime: the tender rounding is the drawer's line, never here (Hakim, 08/10).
///   <see cref="Takings.Net"/> is sales less refunds. A part of a cancel throws
///   <see cref="ArgumentException"/>: a cancelled ticket holds no payment (D-097), and one that does is
///   a ledger nobody should total. So does a part of a ticket that is not among the session's. A sale's
///   part below zero, or a refund's above, throws too. No part at all: empty lists and zeros in
///   <paramref name="currency"/>.</description></item>
///   <item><description><b><see cref="Reconciles"/>:</b> the takings match the tickets: the sales' total
///   is the sum of the sale tickets' totals, the refunds' total the sum of what the refund tickets gave
///   back. False is a report that must not be shown as if it added up.</description></item>
///   <item><description><b><see cref="ActivityOf"/>:</b> sales, refunds and cancels counted by ticket,
///   each with the sum of its totals as a figure above zero (a refund's negated). <b>Struck lines are
///   counted by line</b>, the rows of one line once (same ticket, same line number), on whatever kind of
///   ticket, with the sum of their totals. A struck row is in no ticket's total and in no other
///   count.</description></item>
///   <item><description><b><see cref="ReductionsOf"/>, on sales only, struck rows left out:</b>
///   <list type="bullet">
///     <item><description>A row's discount is <b>the ticket's</b> when its ticket carries a ticket
///     discount and the row's reason is that ticket's reason; otherwise it is <b>the line's</b>.
///     (A line with both keeps its own reason, so its share of the ticket's is counted with the
///     line's: F-41.) Line discounts are counted by line, ticket discounts by ticket; a row with
///     nothing off counts nowhere.</description></item>
///     <item><description><b>An override</b> is a line with a list price. What it gave away is what the
///     line would have come to at the list price, <b>priced once for the whole line</b> (D-103: the
///     list price times the sum of its rows' quantities, by the ticket's policy), less what it came to
///     before its discount (the sum of its rows' total plus discount). Below zero when the price was
///     raised: it is summed as it is.</description></item>
///     <item><description><see cref="Reductions.Total"/> is the three added: counts and
///     values.</description></item>
///   </list></description></item>
///   <item><description><b><see cref="FlagsOf"/>, in this order:</b> every cancel after "Encaisser", in
///   time order; the variance, when <see cref="Drawer.NeedsNote"/> says so against
///   <see cref="ReportThresholds.Variance"/> (no variance, an open session: none); one
///   <see cref="ShiftFlagKind.TooManyCancels"/> per person past the count, with their count, people in
///   the order of their first cancel; every cancel above the value, in time order. <b>Whether a cancel
///   is flagged is <see cref="Voids.Flags"/>'s to say</b>, asked for each cancel with the person's
///   cancels so far, this one included: never a second copy of its comparisons.</description></item>
///   <item><description>Figures in different currencies throw, as <see cref="Money"/> always does.</description></item>
/// </list>
/// </summary>
public static class ShiftReport
{
    /// <param name="currency">The ledger's currency, for a session with nothing in it.</param>
    public static Takings TakingsOf(Currency currency, IReadOnlyList<ReportTicket> tickets, IReadOnlyList<ReportPart> parts)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(parts);

        // A part is read through its ticket: the kind says which side it is on.
        var kinds = tickets.ToDictionary(ticket => ticket.TicketId, ticket => ticket.Kind, StringComparer.Ordinal);
        var sales = new List<ReportPart>();
        var refunds = new List<ReportPart>();

        foreach (var part in parts)
        {
            if (!kinds.TryGetValue(part.TicketId, out var kind))
            {
                throw new ArgumentException($"A payment names ticket '{part.TicketId}', which is not the session's.", nameof(parts));
            }

            switch (kind)
            {
                case TicketKind.Sale when !part.Amount.IsNegative:
                    sales.Add(part);
                    break;

                // Shown as what it gave back: the row is negative, the line is not.
                case TicketKind.Refund when !part.Amount.IsPositive:
                    refunds.Add(part with { Amount = -part.Amount });
                    break;

                case TicketKind.Cancel:
                    throw new ArgumentException($"Cancelled ticket '{part.TicketId}' holds a payment: nothing was sold on it (D-097).", nameof(parts));

                default:
                    throw new ArgumentException($"A payment of {part.Amount} on ticket '{part.TicketId}' has the wrong sign for a {kind}.", nameof(parts));
            }
        }

        var saleLines = Lines(currency, sales);
        var refundLines = Lines(currency, refunds);
        var salesTotal = Sum(currency, saleLines.Select(line => line.Amount));
        var refundsTotal = Sum(currency, refundLines.Select(line => line.Amount));

        return new Takings(saleLines, refundLines, salesTotal, refundsTotal, salesTotal - refundsTotal);
    }

    public static bool Reconciles(Takings takings, IReadOnlyList<ReportTicket> tickets)
    {
        ArgumentNullException.ThrowIfNull(takings);
        ArgumentNullException.ThrowIfNull(tickets);
        var currency = takings.SalesTotal.Currency;

        // A cancel is on neither side: it holds no payment.
        var sold = Sum(currency, tickets.Where(ticket => ticket.Kind == TicketKind.Sale).Select(ticket => ticket.Total));
        var givenBack = Sum(currency, tickets.Where(ticket => ticket.Kind == TicketKind.Refund).Select(ticket => -ticket.Total));

        return takings.SalesTotal == sold && takings.RefundsTotal == givenBack;
    }

    public static Activity ActivityOf(Currency currency, IReadOnlyList<ReportTicket> tickets, IReadOnlyList<ReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(rows);

        Tally Of(TicketKind kind)
        {
            var ofKind = tickets.Where(ticket => ticket.Kind == kind).ToList();

            // A refund's total is negative on its row; every tally here is a figure above zero.
            return new Tally(ofKind.Count, Sum(currency, ofKind.Select(ticket => kind == TicketKind.Refund ? -ticket.Total : ticket.Total)));
        }

        // By line, not by row: the rows of one line share its ticket and its number (D-104).
        var struck = rows.Where(row => row.Struck).ToList();
        var struckLines = struck.Select(row => (row.TicketId, row.LineNumber)).Distinct().Count();

        return new Activity(
            Of(TicketKind.Sale), Of(TicketKind.Refund), Of(TicketKind.Cancel),
            new Tally(struckLines, Sum(currency, struck.Select(row => row.Total))));
    }

    public static Reductions ReductionsOf(Currency currency, IReadOnlyList<ReportTicket> tickets, IReadOnlyList<ReportRow> rows)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(rows);

        // Sales only, and nothing struck: a refund, a cancel and a struck line gave nothing away.
        var sales = tickets.Where(ticket => ticket.Kind == TicketKind.Sale).ToDictionary(ticket => ticket.TicketId, StringComparer.Ordinal);
        var sold = rows.Where(row => !row.Struck && sales.ContainsKey(row.TicketId)).ToList();

        // A row's discount is the ticket's when it carries the ticket's reason; else the line's (F-41).
        bool IsTickets(ReportRow row) => sales[row.TicketId].DiscountReason is { } reason && row.DiscountReason == reason;

        var discounted = sold.Where(row => !row.Discount.IsZero).ToList();
        var ofLines = discounted.Where(row => !IsTickets(row)).ToList();
        var ofTickets = discounted.Where(IsTickets).ToList();

        var lineDiscounts = new Tally(
            ofLines.Select(row => (row.TicketId, row.LineNumber)).Distinct().Count(), Sum(currency, ofLines.Select(row => row.Discount)));
        var ticketDiscounts = new Tally(
            ofTickets.Select(row => row.TicketId).Distinct(StringComparer.Ordinal).Count(), Sum(currency, ofTickets.Select(row => row.Discount)));

        // An override, line by line: what the whole line would have come to at its list price, priced
        // once by its ticket's policy (D-103), less what it came to before its discount.
        var overridden = 0;
        var givenAway = Money.Zero(currency);
        foreach (var line in sold.Where(row => row.ListPrice is not null).GroupBy(row => (row.TicketId, row.LineNumber)))
        {
            var listPrice = line.First().ListPrice!.Value;
            var quantity = line.Sum(row => row.QuantityThousandths);
            var atList = listPrice.Times(quantity, Quantity.Scale, sales[line.Key.TicketId].Policy);
            var charged = Sum(currency, line.Select(row => row.Total + row.Discount));

            overridden++;
            givenAway += atList - charged;
        }

        var overrides = new Tally(overridden, givenAway);
        return new Reductions(
            lineDiscounts, ticketDiscounts, overrides,
            new Tally(
                lineDiscounts.Count + ticketDiscounts.Count + overrides.Count,
                lineDiscounts.Value + ticketDiscounts.Value + overrides.Value));
    }

    /// <param name="variance">Counted less expected, for a closed session; null while it is open.</param>
    public static IReadOnlyList<ShiftFlag> FlagsOf(IReadOnlyList<ReportTicket> tickets, ReportThresholds thresholds, Money? variance)
    {
        ArgumentNullException.ThrowIfNull(tickets);
        ArgumentNullException.ThrowIfNull(thresholds);

        var afterEncaisser = new List<ShiftFlag>();
        var aboveValue = new List<ShiftFlag>();

        // Each person's cancels so far, and who went past the count, in the order of their first cancel.
        var cancelsBy = new Dictionary<string, int>(StringComparer.Ordinal);
        var pastCount = new List<string>();

        foreach (var cancel in tickets.Where(ticket => ticket.Kind == TicketKind.Cancel).OrderBy(ticket => ticket.At))
        {
            var soFar = cancelsBy[cancel.StaffId] = cancelsBy.GetValueOrDefault(cancel.StaffId) + 1;

            // The one rule (B8): whether this cancel is flagged, and why, is Voids.Flags's to say.
            var alerts = Voids.Flags(new CancelledTicket(cancel.Total, cancel.PaymentOpened), soFar, thresholds.VoidCount, thresholds.VoidValue);

            if (alerts.Contains(VoidAlert.AfterEncaisser))
            {
                afterEncaisser.Add(new ShiftFlag(ShiftFlagKind.CancelAfterEncaisser, cancel.StaffId, cancel.TicketId, cancel.Total, null));
            }

            if (alerts.Contains(VoidAlert.Value))
            {
                aboveValue.Add(new ShiftFlag(ShiftFlagKind.CancelAboveValue, cancel.StaffId, cancel.TicketId, cancel.Total, null));
            }

            if (alerts.Contains(VoidAlert.CountPerShift) && !pastCount.Contains(cancel.StaffId))
            {
                pastCount.Add(cancel.StaffId);
            }
        }

        var flags = new List<ShiftFlag>(afterEncaisser);

        // The drawer's own rule, as the close asked it; an open session has no variance yet.
        if (variance is { } gap && Drawer.NeedsNote(gap, thresholds.Variance))
        {
            flags.Add(new ShiftFlag(ShiftFlagKind.VariancePastThreshold, null, null, gap, null));
        }

        // One flag a person, with the count they ended on.
        flags.AddRange(pastCount.Select(staff => new ShiftFlag(ShiftFlagKind.TooManyCancels, staff, null, null, cancelsBy[staff])));
        flags.AddRange(aboveValue);
        return flags;
    }

    /// <summary>One line per tender used, in the enum's order, with its parts and their sum.</summary>
    private static List<TenderLine> Lines(Currency currency, List<ReportPart> parts) =>
        [.. parts
            .GroupBy(part => part.Method)
            .OrderBy(group => group.Key)
            .Select(group => new TenderLine(group.Key, group.Count(), Sum(currency, group.Select(part => part.Amount))))];

    /// <summary>A sum that starts in the ledger's currency, so nothing adds up to "no currency", and a second currency throws.</summary>
    private static Money Sum(Currency currency, IEnumerable<Money> amounts) =>
        amounts.Aggregate(Money.Zero(currency), (total, amount) => total + amount);
}
