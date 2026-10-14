using System.Globalization;
using Waymark.Contracts.Pos;
using Waymark.Domain.Hardware;
using Waymark.Domain.Values;
using Waymark.Hardware;
using Waymark.Pos.Checkout;

namespace Waymark.Pos.Receipts;

/// <summary>
/// The receipt's paper (session D1, D-125): the board's layout, filled from what the server read off
/// the ticket's rows. Pure, so every rule is tested with no printer and no server.
///
/// <para><b>The rules <c>ReceiptLayoutTests</c> hold you to:</b></para>
/// <list type="number">
///   <item><description><b>Every figure is the wire's.</b> Nothing is worked out again but what two
///   recorded figures make together: a line before its discount (its total less what was taken off),
///   the cash handed over (the cash part with its rounding). No rate, no percent, no tax.</description></item>
///   <item><description><b>A ticket with no TVA says nothing of tax</b> (D-122): "TOTAL", no "HT", no
///   mention. One that has some says "TOTAL TTC", "Total HT" and "dont TVA", from its row.</description></item>
///   <item><description><b>Nothing of a customer</b>: no name, no balance (D-123); the wire carries none.</description></item>
///   <item><description><b>No cash handed over, no change</b>: nothing records them.</description></item>
///   <item><description><b>A duplicate says so twice</b>, reversed, above the store and above the legal
///   line, so either half of a torn ticket still does.</description></item>
/// </list>
///
/// <para>
/// French, as ASCII once the encoder folds it, until K1 proves a code page. A tenant whose receipts are
/// in Arabic is printed in French until K2 draws the paper as a picture (D-124).
/// </para>
/// </summary>
public static class ReceiptLayout
{
    /// <summary>A sale's or a refund's receipt.</summary>
    /// <param name="columns">The paper's width at single size: 32 for 58 mm, 42 for 80 mm.</param>
    /// <param name="reprint">The reprint being made (D3), for a duplicate; null for the first paper.</param>
    public static ReceiptDocument For(ReceiptWire receipt, int columns, ReprintMark? reprint = null)
    {
        ArgumentNullException.ThrowIfNull(receipt);
        var currency = receipt.Currency;
        var refund = receipt.Kind == ReceiptKinds.Refund;
        var tax = WireFigures.Money(receipt.TaxTotal, currency);
        var paper = new List<ReceiptLine>();

        if (reprint is not null)
        {
            paper.Add(Duplicate);
            paper.Add(new FeedLine());
        }

        Header(paper, receipt.Store);

        if (refund && receipt.Original is { } original)
        {
            paper.Add(new TextLine("REMBOURSEMENT", TextAlignment.Centre, Emphasised: true, DoubleHeight: true));
            Numbered(paper, "Ticket d'origine", original.InvoiceNumber, columns);
            paper.Add(new PairLine($"du {original.Day} {original.Time}", original.TerminalName));
            paper.Add(new SeparatorLine());
        }

        foreach (var line in receipt.Lines)
        {
            Item(paper, line, currency, columns, refund);
        }

        if (refund && receipt.Reason is { } reason)
        {
            paper.Add(new TextLine($"Motif : {reason}"));
        }

        paper.Add(new SeparatorLine());
        var articles = Articles(receipt.Lines);
        paper.Add(new TextLine(articles == 1 ? "1 article" : $"{articles} articles"));
        paper.Add(new AmountLine(
            refund ? "TOTAL REMBOURSE" : tax.IsZero ? "TOTAL" : "TOTAL TTC",
            WireFigures.Money(receipt.Total, currency),
            Emphasised: true,
            DoubleHeight: true,
            WithCurrency: true));

        var discounts = WireFigures.Money(receipt.DiscountTotal, currency);
        if (!refund && discounts.IsPositive)
        {
            paper.Add(new AmountLine("dont remises", discounts));
        }

        // D-122: a ticket that carries no TVA says nothing of it.
        if (!tax.IsZero)
        {
            paper.Add(new AmountLine("Total HT", WireFigures.Money(receipt.Subtotal, currency)));
            paper.Add(new AmountLine("dont TVA", tax));
        }

        paper.Add(new SeparatorLine());
        Tenders(paper, receipt, refund);
        paper.Add(new SeparatorLine());

        Numbered(paper, "Ticket", receipt.InvoiceNumber, columns);
        paper.Add(new PairLine(receipt.Day, receipt.Time));
        paper.Add(receipt.Cashier is { } cashier ? new PairLine(receipt.TerminalName, $"Caissier : {cashier}") : new TextLine(receipt.TerminalName));

        if (reprint is not null)
        {
            var by = reprint.By is { } person ? $" par {person}" : string.Empty;
            paper.Add(new TextLine($"Reimprime le {reprint.Day} {reprint.Time}{by}. Non valable pour un second remboursement.", TextAlignment.Centre, Emphasised: true));
        }

        if (refund)
        {
            paper.Add(new FeedLine());
            paper.Add(new TextLine("Signature client :"));
            paper.Add(new FeedLine());
            paper.Add(new SeparatorLine('.'));
        }

        paper.Add(new BarcodeLine(receipt.InvoiceNumber, BarcodeSymbology.Code128));
        paper.Add(new FeedLine());

        if (reprint is not null)
        {
            paper.Add(Duplicate);
            paper.Add(new FeedLine());
        }

        Foot(paper, receipt.Store, receipt.RefundWindowDays, signature: true);
        return new ReceiptDocument(paper);
    }

    /// <summary>
    /// A tab repayment's slip (D-124, D-125): the customer's proof of what they paid. No name, no
    /// balance (D-123), no code: nothing scans it.
    /// </summary>
    public static ReceiptDocument Repayment(ReceiptStoreWire store, RepaymentSlip slip)
    {
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(slip);
        var paper = new List<ReceiptLine>();
        Header(paper, store);
        paper.Add(new TextLine("VERSEMENT", TextAlignment.Centre, Emphasised: true, DoubleHeight: true));
        paper.Add(new AmountLine("Verse sur le carnet", slip.Paid, Emphasised: slip.CashRounding is null));

        // A tab repaid in cash rounds like a sale (D-108): the tab clears exactly, the drawer takes the step.
        if (slip.CashRounding is { } rounding)
        {
            paper.Add(new AmountLine("Arrondi especes", rounding));
            paper.Add(new AmountLine("Paye en especes", slip.Paid + rounding, Emphasised: true));
        }

        paper.Add(new SeparatorLine());
        paper.Add(new PairLine(slip.Day, slip.Time));
        paper.Add(slip.Cashier is { } cashier ? new PairLine(slip.TerminalName, $"Caissier : {cashier}") : new TextLine(slip.TerminalName));
        paper.Add(new FeedLine());
        Foot(paper, store, refundWindowDays: null, signature: false);
        return new ReceiptDocument(paper);
    }

    /// <summary>
    /// How many articles the ticket counts (D-125): the units of a counted line, one for a weighed
    /// line. A refund counts what it brings back.
    /// </summary>
    public static long Articles(IReadOnlyList<ReceiptLineWire> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);
        return lines.Sum(line =>
        {
            var thousandths = Math.Abs(WireFigures.Quantity(line.Quantity, line.UnitCode).Thousandths);
            return line.Weighed || thousandths % Quantity.Scale != 0 ? 1 : thousandths / Quantity.Scale;
        });
    }

    /// <summary>
    /// A label and a ticket's number on one line: "Ticket d'origine   No S-2026-004817". A label is
    /// never cut: where both do not fit, the "No" goes, and where they still do not, the number takes
    /// a line of its own, on the right.
    /// </summary>
    private static void Numbered(List<ReceiptLine> paper, string label, string number, int columns)
    {
        if (label.Length + 1 + 3 + number.Length <= columns)
        {
            paper.Add(new PairLine(label, $"No {number}"));
        }
        else if (label.Length + 1 + number.Length <= columns)
        {
            paper.Add(new PairLine(label, number));
        }
        else
        {
            paper.Add(new TextLine(label));
            paper.Add(new PairLine(string.Empty, $"No {number}"));
        }
    }

    private static TextLine Duplicate => new("DUPLICATA", TextAlignment.Centre, Emphasised: true, DoubleHeight: true, Reversed: true);

    private static void Header(List<ReceiptLine> paper, ReceiptStoreWire store)
    {
        paper.Add(new TextLine(store.Name, TextAlignment.Centre, Emphasised: true, DoubleHeight: true));
        foreach (var line in store.AddressLines)
        {
            paper.Add(new TextLine(line, TextAlignment.Centre));
        }

        if (store.Phone is { } phone)
        {
            paper.Add(new TextLine($"Tel. {phone}", TextAlignment.Centre));
        }

        paper.Add(new SeparatorLine());
    }

    /// <summary>
    /// An article: one line when it is one unit and fits beside its figure; else its name, then its
    /// quantity and price under it. Its discount below, as taken off ("-42,00") on a sale and as given
    /// back ("95,00") on a refund. The figure is the line before its discount.
    /// </summary>
    private static void Item(List<ReceiptLine> paper, ReceiptLineWire line, string currency, int columns, bool refund)
    {
        var total = WireFigures.Money(line.LineTotal, currency);
        var recorded = WireFigures.Money(line.Discount, currency);
        var discount = refund ? recorded : -recorded;
        var gross = total - discount;
        var quantity = WireFigures.Quantity(line.Quantity, line.UnitCode);
        var price = WireFigures.Money(line.UnitPrice, currency);
        var whole = Math.Abs(quantity.Thousandths);

        if (!line.Weighed && whole == Quantity.Scale && line.Name.Length + 1 + EscPosEncoder.Format(gross).Length <= columns)
        {
            paper.Add(new AmountLine(line.Name, gross));
        }
        else
        {
            paper.Add(new TextLine(line.Name));
            paper.Add(new AmountLine(
                line.Weighed
                    ? $"  {Weight(whole)} {line.UnitCode} x {EscPosEncoder.Format(price)}/{line.UnitCode}"
                    : $"  {Units(whole)} x {EscPosEncoder.Format(price)}",
                gross));
        }

        if (!recorded.IsZero)
        {
            paper.Add(new AmountLine(refund ? "  Remise d'origine" : "  Remise", discount));
        }
    }

    /// <summary>
    /// How it was paid or given back, in the order the rows were taken. The cash part shows what the
    /// cash step did to it and what changed hands; before it, what the other parts left to pay.
    /// </summary>
    private static void Tenders(List<ReceiptLine> paper, ReceiptWire receipt, bool refund)
    {
        var currency = receipt.Currency;
        var rounding = receipt.CashRounding is { } text ? WireFigures.Money(text, currency) : (Money?)null;
        var others = false;
        foreach (var payment in receipt.Payments)
        {
            var amount = WireFigures.Money(payment.Amount, currency);
            if (payment.Method != "cash")
            {
                paper.Add(new AmountLine(Method(payment.Method, refund), amount));
                others = true;
                continue;
            }

            if (others && !refund)
            {
                paper.Add(new AmountLine("Reste a payer", amount));
            }

            if (rounding is { } step)
            {
                paper.Add(new AmountLine("Arrondi especes", step));
            }

            paper.Add(new AmountLine(refund ? "Rembourse en especes" : "Net a payer especes", amount + (rounding ?? Money.Zero(amount.Currency)), Emphasised: true));
        }
    }

    private static string Method(string method, bool refund) => method switch
    {
        "card" => refund ? "Rembourse sur carte" : "Carte",
        "mobile_wallet" => "BaridiMob",
        "store_credit" => refund ? "Rendu en avoir" : "Avoir",
        "on_account" => refund ? "Rendu au carnet" : "Carnet",
        _ => method,
    };

    /// <summary>
    /// The store's legal line, its own text, then Waymark's signature: the refund window when the shop
    /// has one, and the facture the consumer may ask for (Law 04-02 art. 10).
    /// </summary>
    private static void Foot(List<ReceiptLine> paper, ReceiptStoreWire store, int? refundWindowDays, bool signature)
    {
        if (store.TaxNumber is { } nif)
        {
            paper.Add(new TextLine($"NIF {nif}", TextAlignment.Centre));
            paper.Add(new FeedLine());
        }

        if (store.Footer is { } footer)
        {
            paper.Add(new TextLine(footer, TextAlignment.Centre, Emphasised: true));
        }

        if (signature)
        {
            if (refundWindowDays is { } days)
            {
                paper.Add(new TextLine($"Echange ou remboursement sous {days} jours avec ce ticket.", TextAlignment.Centre));
            }

            paper.Add(new TextLine("Facture sur demande.", TextAlignment.Centre));
        }

        paper.Add(new FeedLine(3));
        paper.Add(new CutLine());
    }

    private static string Weight(long thousandths) =>
        string.Create(CultureInfo.InvariantCulture, $"{thousandths / Quantity.Scale},{thousandths % Quantity.Scale:D3}");

    private static string Units(long thousandths) =>
        thousandths % Quantity.Scale == 0
            ? (thousandths / Quantity.Scale).ToString(CultureInfo.InvariantCulture)
            : Weight(thousandths);
}

/// <summary>A reprint being made (D3): its moment and who asked, which only the paper keeps (D-123).</summary>
/// <param name="Day">"dd/MM/yyyy", the store's.</param>
/// <param name="Time">"HH:mm", the store's.</param>
/// <param name="By">The first name of who reprinted it.</param>
public sealed record ReprintMark(string Day, string Time, string? By);

/// <summary>What a tab repayment's slip says (D-125).</summary>
/// <param name="Paid">What went on the tab, exactly.</param>
/// <param name="CashRounding">What the cash step added or took off the cash handed over; null when nothing.</param>
public sealed record RepaymentSlip(string Day, string Time, string TerminalName, string? Cashier, Money Paid, Money? CashRounding);
