using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Domain.Enums;
using Waymark.Domain.Sales;
using Waymark.Domain.Values;

namespace Waymark.StoreServer.Sales;

/// <summary>A receipt on the wire (session D1, D-125). Pure, so each rule is tested without a server.</summary>
public static class ReceiptsWire
{
    /// <param name="zone">The store's zone: the paper's day and hour are the store's.</param>
    /// <param name="refundWindowDays">The tenant's window (D-125), printed in the signature.</param>
    /// <param name="language">The tenant's receipt language (D-124).</param>
    /// <param name="reprintedAt">For a reprint (D3), when it was asked; null for the first paper.</param>
    /// <param name="reprintedBy">Who asked for it, their full name: the paper prints the first.</param>
    public static ReceiptAnswer Found(
        ReceiptFacts receipt, TimeZoneInfo zone, int? refundWindowDays, Language language, DateTimeOffset? reprintedAt = null, string? reprintedBy = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        ArgumentNullException.ThrowIfNull(zone);
        var (day, time) = DayAndTime(receipt.OccurredAt, zone);
        return new(
            TicketOutcomes.Found,
            new ReceiptWire(
                receipt.Original is null ? ReceiptKinds.Sale : ReceiptKinds.Refund,
                Store(receipt.Store),
                receipt.InvoiceNumber,
                day,
                time,
                receipt.TerminalName,
                FirstName(receipt.StaffName),
                [.. receipt.Lines.Select(line => new ReceiptLineWire(
                    string.IsNullOrWhiteSpace(line.VariantName) ? line.ProductName : $"{line.ProductName} {line.VariantName}",
                    WireText.Figure(line.Quantity),
                    line.Quantity.Unit ?? string.Empty,
                    WireText.Figure(line.UnitPrice),
                    WireText.Figure(line.Discount),
                    WireText.Figure(line.LineTotal),
                    line.Weighed))],
                WireText.Figure(receipt.Subtotal),
                WireText.Figure(receipt.TaxTotal),
                WireText.Figure(receipt.Total),
                WireText.Figure(receipt.DiscountTotal),
                receipt.Total.Currency.Code,
                [.. receipt.Payments.Select(payment => new PastPaymentWire(Snake(payment.Method), WireText.Figure(payment.Amount)))],
                receipt.CashRounding is { } rounding ? WireText.Figure(rounding) : null,
                receipt.Original is { } original ? Original(original, zone) : null,
                receipt.Reason,
                refundWindowDays,
                receipt.ReprintCount,
                language == Language.Ar ? "ar" : "fr",
                reprintedAt is { } at ? Reprint(at, zone, reprintedBy) : null));
    }

    public static ReceiptAnswer Refused(string outcome) => new(outcome, null);

    private static ReprintWire Reprint(DateTimeOffset at, TimeZoneInfo zone, string? by)
    {
        var (day, time) = DayAndTime(at, zone);
        return new ReprintWire(day, time, FirstName(by));
    }

    public static ReceiptStoreWire Store(ReceiptStore store)
    {
        ArgumentNullException.ThrowIfNull(store);
        return new(
            store.Name,
            [.. (store.Address ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)],
            Blank(store.Phone),
            Blank(store.TaxNumber),
            Blank(store.Footer));
    }

    /// <summary>
    /// The first word of a name (D-125): "Karim Benali" prints "Karim". A receipt goes home with a
    /// stranger; the cashier's full name need not.
    /// </summary>
    public static string? FirstName(string? name) =>
        name?.Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries) is [var first, ..] ? first : null;

    /// <summary>An instant as the store's day and hour, as the paper prints them.</summary>
    public static (string Day, string Time) DayAndTime(DateTimeOffset moment, TimeZoneInfo zone)
    {
        var local = TimeZoneInfo.ConvertTime(moment, zone);
        return (local.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture), local.ToString("HH:mm", CultureInfo.InvariantCulture));
    }

    private static ReceiptOriginalWire Original(ReceiptOriginal original, TimeZoneInfo zone)
    {
        var (day, time) = DayAndTime(original.OccurredAt, zone);
        return new(original.InvoiceNumber, day, time, original.TerminalName);
    }

    private static string? Blank(string? text) => string.IsNullOrWhiteSpace(text) ? null : text.Trim();

    /// <summary>The database's own spelling of a payment method: "mobile_wallet".</summary>
    private static string Snake(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.Card => "card",
        PaymentMethod.MobileWallet => "mobile_wallet",
        PaymentMethod.StoreCredit => "store_credit",
        PaymentMethod.OnAccount => "on_account",
        _ => throw new ArgumentOutOfRangeException(nameof(method), method, "A payment method the receipt does not know."),
    };
}
