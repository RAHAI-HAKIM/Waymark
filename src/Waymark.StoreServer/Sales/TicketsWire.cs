using System.Text;
using Waymark.Contracts.Pos;
using Waymark.Domain.Organisation;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;
using Waymark.StoreServer.Security;

namespace Waymark.StoreServer.Sales;

/// <summary>
/// Past tickets on the wire (session B1, D-088): who may see which, the store's day as instants, and
/// the mapping. Pure, so each rule is tested without a server.
/// </summary>
public static class TicketsWire
{
    /// <summary>
    /// Whether the person the session names may see the tickets of <paramref name="day"/> at
    /// <paramref name="terminalId"/> (null: every till). <b>Today at their own till needs no rank</b>:
    /// they sold them. Anything else needs <see cref="Capability.ViewOtherTickets"/>, and a person
    /// with no rank is refused (D-077).
    /// </summary>
    public static bool MaySee(SignedInTill session, long? rank, DateOnly today, DateOnly day, string? terminalId)
    {
        ArgumentNullException.ThrowIfNull(session);
        return (day == today && string.Equals(terminalId, session.TerminalId, StringComparison.Ordinal))
            || StaffPermissions.May(rank, Capability.ViewOtherTickets);
    }

    /// <summary>
    /// The store's day as a half-open window of instants, midnight to midnight in the store's zone:
    /// a sale at 23:30 in Algiers is 22:30 UTC, and belongs to the Algiers day.
    /// </summary>
    public static (DateTimeOffset Since, DateTimeOffset Until) Bounds(DateOnly day, TimeZoneInfo zone)
    {
        ArgumentNullException.ThrowIfNull(zone);
        return (Midnight(day, zone), Midnight(day.AddDays(1), zone));
    }

    /// <summary>The store's day a moment falls on.</summary>
    public static DateOnly DayOf(DateTimeOffset moment, TimeZoneInfo zone) =>
        DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, zone).DateTime);

    public static TicketList List(string outcome, DateOnly day, bool allTills, IReadOnlyList<PastTicketSummary> tickets) => new(
        outcome,
        day.ToString("yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture),
        allTills,
        [.. tickets.Select(ticket => new TicketSummary(
            ticket.TransactionId,
            ticket.InvoiceNumber,
            ticket.OccurredAt,
            ticket.TerminalId,
            Snake(ticket.Status.ToString()),
            ticket.LineCount,
            WireText.Figure(ticket.Total),
            ticket.Total.Currency.Code))]);

    /// <param name="customerModule">The tenant keeps customers (D-096): with the sale's customer, a refund may be store credit.</param>
    public static TicketAnswer Found(PastTicket ticket, bool customerModule = false)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        return new(
            TicketOutcomes.Found,
            new PastTicketDetail(
                ticket.TransactionId,
                ticket.InvoiceNumber,
                ticket.OccurredAt,
                ticket.TerminalId,
                ticket.StaffName,
                Snake(ticket.Status.ToString()),
                [.. ticket.Lines.Select(line => new PastTicketLineWire(
                    line.ProductName,
                    line.VariantName,
                    WireText.Figure(line.Quantity),
                    line.Quantity.Unit ?? string.Empty,
                    WireText.Figure(line.UnitPrice),
                    WireText.Figure(line.LineTotal),
                    line.VariantId,
                    WireText.Figure(line.Returned ?? Quantity.Zero(line.Quantity.Unit!)),
                    line.Weighed))],
                WireText.Figure(ticket.Subtotal),
                WireText.Figure(ticket.TaxTotal),
                WireText.Figure(ticket.Total),
                ticket.Total.Currency.Code,
                [.. ticket.Payments.Select(payment => new PastPaymentWire(Snake(payment.Method.ToString()), WireText.Figure(payment.Amount)))],
                ticket.OriginalInvoiceNumber,
                customerModule && ticket.HasCustomer));
    }

    public static TicketAnswer Refused(string outcome) => new(outcome, null);

    private static DateTimeOffset Midnight(DateOnly day, TimeZoneInfo zone)
    {
        var local = day.ToDateTime(TimeOnly.MinValue, DateTimeKind.Unspecified);
        return new DateTimeOffset(local, zone.GetUtcOffset(local));
    }

    /// <summary>"PartiallyRefunded" to "partially_refunded": the database's own spelling of the enum.</summary>
    private static string Snake(string name)
    {
        var text = new StringBuilder(name.Length + 4);
        foreach (var c in name)
        {
            if (char.IsUpper(c) && text.Length > 0)
            {
                text.Append('_');
            }

            text.Append(char.ToLowerInvariant(c));
        }

        return text.ToString();
    }
}
