using System.Text.RegularExpressions;
using Waymark.Contracts.Pos;
using Waymark.Domain.Hardware;
using Waymark.Hardware;
using Waymark.Pos.Receipts;

namespace Waymark.Pos.Tests;

/// <summary>
/// The receipt's paper (D1, D-125), checked as it comes out of the encoder: the bytes a printer would
/// take, read back as text. The specimens are the board's, with what our rules remove removed.
/// </summary>
public sealed class ReceiptLayoutTests
{
    private static readonly int[] Widths = [32, 42];

    private static readonly ReceiptStoreWire ElAmel = new(
        "ALIMENTATION EL AMEL", ["Cité 300 Logements, bloc C", "31000 Es Senia, Oran"], "041 00 00 00", "000031000000000", "Merci de votre visite !");

    /// <summary>Specimen 1: a store not subject to TVA, cash only, a weighed line, a discount, a long name.</summary>
    private static ReceiptWire NotSubject(int? window = 7) => new(
        ReceiptKinds.Sale, ElAmel, "S-2026-011542", "12/10/2026", "17:08", "Caisse 03", "Karim",
        [
            new("Lait UHT demi-écrémé 1 L", "6", "u", "140.00", "0.00", "840.00", false),
            new("Tomates", "0.742", "kg", "180.00", "0.00", "133.56", true),
            new("Café moulu 250 g", "1", "u", "420.00", "42.00", "378.00", false),
            new("Pâtes spaghetti 500 g", "3", "u", "85.00", "0.00", "255.00", false),
            new("Biscuits fourrés choco 12x40 g", "1", "u", "230.00", "0.00", "230.00", false),
        ],
        "1836.56", "0.00", "1836.56", "42.00", "DZD",
        [new("cash", "1836.56")], "-1.56", null, null, window, 0, "fr");

    /// <summary>Specimen 2: a store subject to TVA, card first then cash.</summary>
    private static ReceiptWire Subject() => NotSubject() with
    {
        Store = ElAmel with { Name = "SUPERETTE LES OLIVIERS" },
        Lines =
        [
            new("Café moulu 250 g", "1", "u", "420.00", "0.00", "420.00", false),
            new("Lessive poudre 3 kg", "1", "u", "950.00", "95.00", "855.00", false),
            new("Huile tournesol 5 L", "1", "u", "1150.00", "0.00", "1150.00", false),
        ],
        Subtotal = "2172.99", TaxTotal = "252.09", Total = "2425.08", DiscountTotal = "95.00",
        Payments = [new("card", "2000.00"), new("cash", "425.08")], CashRounding = "-0.08",
    };

    /// <summary>Specimen 3: a refund of one discounted line, in cash.</summary>
    private static ReceiptWire Refund() => NotSubject() with
    {
        Kind = ReceiptKinds.Refund, InvoiceNumber = "S-2026-002291", Cashier = "Nadia",
        Lines = [new("Lessive poudre 3 kg", "-1", "u", "950.00", "95.00", "-855.00", false)],
        Subtotal = "-718.49", TaxTotal = "-136.51", Total = "-855.00", DiscountTotal = "95.00",
        Payments = [new("cash", "-855.00")], CashRounding = null,
        Original = new("S-2026-004817", "11/10/2026", "18:42", "Caisse 02"), Reason = "Article défectueux",
    };

    /// <summary>The printable text the bytes would put on paper, a line each, commands removed.</summary>
    private static string[] Paper(ReceiptDocument document, int columns) =>
        [.. FileEscPosSink.Render(new EscPosEncoder(new PrinterProfile(Columns: columns)).Encode(document))
            .Split('\n')
            .Select(line => Regex.Replace(line, "<[^>]*>", string.Empty))];

    private static string Line(string[] paper, string starts) =>
        Assert.Single(paper, line => line.StartsWith(starts, StringComparison.Ordinal));

    // ------------------------------------------------------------ the width

    public static TheoryData<string, int> Specimens()
    {
        var data = new TheoryData<string, int>();
        foreach (var name in new[] { "not subject", "subject", "refund", "duplicate", "long", "repayment" })
        {
            foreach (var width in Widths)
            {
                data.Add(name, width);
            }
        }

        return data;
    }

    private static ReceiptDocument Specimen(string name, int columns) => name switch
    {
        "not subject" => ReceiptLayout.For(NotSubject(), columns),
        "subject" => ReceiptLayout.For(Subject(), columns),
        "refund" => ReceiptLayout.For(Refund(), columns),
        "duplicate" => ReceiptLayout.For(NotSubject(), columns, new ReprintMark("12/10/2026", "17:31", "Karim")),
        "long" => ReceiptLayout.For(NotSubject() with
        {
            Store = ElAmel with { Name = "ALIMENTATION GENERALE ET PRODUITS LAITIERS EL AMEL", Footer = new string('x', 90) + " " + new string('y', 109) },
            Lines = [new(new string('N', 70), "1", "u", "1234567.89", "0.00", "1234567.89", false)],
        }, columns),
        "repayment" => ReceiptLayout.Repayment(ElAmel, new RepaymentSlip("12/10/2026", "17:40", "Caisse 03", "Karim", Dzd(143), Dzd(2))),
        _ => throw new ArgumentOutOfRangeException(nameof(name)),
    };

    [Theory]
    [MemberData(nameof(Specimens))]
    public void No_line_runs_past_the_paper(string specimen, int columns)
    {
        // A printer does not wrap: what passes the edge is lost, and a lost figure is a receipt that
        // disagrees with the till in the customer's hand.
        var paper = Paper(Specimen(specimen, columns), columns);

        Assert.All(paper, line => Assert.True(line.Length <= columns, $"'{line}' is {line.Length} wide on {columns} columns."));
    }

    // ------------------------------------------------------- the board, kept

    [Theory]
    [InlineData(32)]
    [InlineData(42)]
    public void A_sale_prints_its_lines_its_total_and_its_cash_as_the_board_draws_them(int columns)
    {
        var paper = Paper(ReceiptLayout.For(NotSubject(), columns), columns);

        Assert.EndsWith("840,00", Line(paper, "  6 x 140,00"), StringComparison.Ordinal);
        Assert.EndsWith("133,56", Line(paper, "  0,742 kg x 180,00/kg"), StringComparison.Ordinal);

        // One unit that fits beside its figure is one line; the figure is before the discount.
        Assert.EndsWith("420,00", Line(paper, "Cafe moulu 250 g"), StringComparison.Ordinal);
        Assert.EndsWith("-42,00", Line(paper, "  Remise"), StringComparison.Ordinal);

        Assert.Contains("12 articles", paper);
        Assert.EndsWith("1 836,56 DA", Line(paper, "TOTAL"), StringComparison.Ordinal);
        Assert.EndsWith("42,00", Line(paper, "dont remises"), StringComparison.Ordinal);
        Assert.EndsWith("-1,56", Line(paper, "Arrondi especes"), StringComparison.Ordinal);
        Assert.EndsWith("1 835,00", Line(paper, "Net a payer especes"), StringComparison.Ordinal);
        Assert.EndsWith("No S-2026-011542", Line(paper, "Ticket"), StringComparison.Ordinal);
        Assert.EndsWith("Caissier : Karim", Line(paper, "Caisse 03"), StringComparison.Ordinal);
        Assert.Contains("NIF 000031000000000", paper.Select(line => line.Trim()));
        Assert.Contains("Facture sur demande.", paper.Select(line => line.Trim()));
    }

    [Fact]
    public void A_long_name_takes_two_lines_on_narrow_paper_and_one_on_wide()
    {
        var narrow = Paper(ReceiptLayout.For(NotSubject(), 32), 32);
        var wide = Paper(ReceiptLayout.For(NotSubject(), 42), 42);

        Assert.Contains("Biscuits fourres choco 12x40 g", narrow);
        Assert.EndsWith("230,00", Line(narrow, "  1 x 230,00"), StringComparison.Ordinal);
        Assert.EndsWith("230,00", Line(wide, "Biscuits fourres choco 12x40 g"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_subject_ticket_prints_its_tax_from_its_row_and_what_the_card_left_in_cash()
    {
        var paper = Paper(ReceiptLayout.For(Subject(), 42), 42);

        Assert.EndsWith("2 425,08 DA", Line(paper, "TOTAL TTC"), StringComparison.Ordinal);
        Assert.EndsWith("2 172,99", Line(paper, "Total HT"), StringComparison.Ordinal);
        Assert.EndsWith("252,09", Line(paper, "dont TVA"), StringComparison.Ordinal);
        Assert.EndsWith("2 000,00", Line(paper, "Carte"), StringComparison.Ordinal);
        Assert.EndsWith("425,08", Line(paper, "Reste a payer"), StringComparison.Ordinal);
        Assert.EndsWith("425,00", Line(paper, "Net a payer especes"), StringComparison.Ordinal);
    }

    [Fact]
    public void A_refund_names_its_sale_and_reason_and_gives_back_the_discount_with_the_article()
    {
        var paper = Paper(ReceiptLayout.For(Refund(), 42), 42);

        Assert.Contains("REMBOURSEMENT", paper.Select(line => line.Trim()));
        Assert.EndsWith("No S-2026-004817", Line(paper, "Ticket d'origine"), StringComparison.Ordinal);
        Assert.EndsWith("Caisse 02", Line(paper, "du 11/10/2026 18:42"), StringComparison.Ordinal);
        Assert.EndsWith("-950,00", Line(paper, "Lessive poudre 3 kg"), StringComparison.Ordinal);
        Assert.EndsWith(" 95,00", Line(paper, "  Remise d'origine"), StringComparison.Ordinal);
        Assert.Contains("Motif : Article defectueux", paper);
        Assert.Contains("1 article", paper);
        Assert.EndsWith("-855,00 DA", Line(paper, "TOTAL REMBOURSE"), StringComparison.Ordinal);
        Assert.EndsWith("-855,00", Line(paper, "Rembourse en especes"), StringComparison.Ordinal);
        Assert.Contains("Signature client :", paper);
        Assert.DoesNotContain(paper, line => line.StartsWith("dont remises", StringComparison.Ordinal));
    }

    [Theory]
    [InlineData("S-2026-004817", 32, "Ticket d'origine   S-2026-004817")]
    [InlineData("S-2026-004817", 42, "Ticket d'origine          No S-2026-004817")]
    [InlineData("EPICERIE-2026-004817", 32, "Ticket d'origine")]
    public void A_label_beside_a_number_is_never_cut(string number, int columns, string first)
    {
        // On 58 mm, "Ticket d'origine" and "No S-2026-004817" are 33 characters: the label was cut.
        var paper = Paper(ReceiptLayout.For(Refund() with { Original = new(number, "11/10/2026", "18:42", "Caisse 02") }, columns), columns);

        var at = Array.FindIndex(paper, line => line.StartsWith("Ticket d'origine", StringComparison.Ordinal));
        Assert.Equal(first, paper[at]);
        Assert.Contains(number, paper[at] + paper[at + 1], StringComparison.Ordinal);
    }

    [Fact]
    public void A_duplicate_says_so_reversed_above_the_store_and_above_the_legal_line()
    {
        var document = ReceiptLayout.For(NotSubject(), 42, new ReprintMark("12/10/2026", "17:31", "Karim"));
        var marks = document.Lines.OfType<TextLine>().Where(line => line.Text == "DUPLICATA").ToList();

        Assert.Equal(2, marks.Count);
        Assert.All(marks, mark => Assert.True(mark.Reversed && mark.DoubleHeight));
        Assert.Same(marks[0], document.Lines[0]);
        Assert.True(
            document.Lines.ToList().IndexOf(marks[1]) < document.Lines.ToList().FindIndex(line => line is TextLine { Text: var text } && text.StartsWith("NIF", StringComparison.Ordinal)),
            "The second mark is not above the legal line.");
        Assert.Contains(Paper(document, 42), line => line.Contains("Reimprime le 12/10/2026 17:31 par Karim.", StringComparison.Ordinal));

        // The first paper carries neither.
        Assert.DoesNotContain(ReceiptLayout.For(NotSubject(), 42).Lines, line => line is TextLine { Text: "DUPLICATA" });
    }

    [Fact]
    public void The_number_is_a_code128_and_the_window_is_printed_only_when_the_shop_has_one()
    {
        var with = ReceiptLayout.For(NotSubject(window: 7), 42);
        var without = ReceiptLayout.For(NotSubject(window: null), 42);

        Assert.Equal(new BarcodeLine("S-2026-011542", BarcodeSymbology.Code128), Assert.Single(with.Lines.OfType<BarcodeLine>()));
        // A sentence wider than the paper wraps, as the board has it: read the foot as one text.
        Assert.Contains("Echange ou remboursement sous 7 jours avec ce ticket.", string.Join(' ', Paper(with, 42).Select(line => line.Trim())), StringComparison.Ordinal);
        Assert.DoesNotContain(Paper(without, 42), line => line.Contains("remboursement sous", StringComparison.Ordinal));
        Assert.Contains("Facture sur demande.", Paper(without, 42).Select(line => line.Trim()));
    }

    // ----------------------------------------------------- what our rules remove

    [Fact]
    public void A_ticket_with_no_tva_says_nothing_of_tax()
    {
        // D-122 rejected "Non assujetti a la TVA": a ticket with no TVA in it says nothing of TVA.
        var text = string.Join('\n', Paper(ReceiptLayout.For(NotSubject(), 42), 42));

        Assert.DoesNotContain("TVA", text, StringComparison.Ordinal);
        Assert.DoesNotContain("Total HT", text, StringComparison.Ordinal);
        Assert.DoesNotContain("TTC", text, StringComparison.Ordinal);
    }

    [Fact]
    public void No_cash_handed_over_no_change_no_percent_and_no_customer()
    {
        foreach (var receipt in new[] { NotSubject(), Subject(), Refund() })
        {
            var text = string.Join('\n', Paper(ReceiptLayout.For(receipt, 42), 42));

            Assert.DoesNotContain("recues", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Rendu\n", text + "\n", StringComparison.Ordinal);
            Assert.DoesNotContain("%", text, StringComparison.Ordinal);
            Assert.DoesNotContain("Solde", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("Client", text, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void A_figure_on_paper_is_the_wires_and_what_two_of_them_make_together()
    {
        // The paper works out only two things: a line before its discount, and the cash that
        // changed hands. Each is a sum of two recorded figures, checked here against the board.
        var paper = Paper(ReceiptLayout.For(NotSubject() with { CashRounding = "3.44" }, 42), 42);

        Assert.EndsWith("1 840,00", Line(paper, "Net a payer especes"), StringComparison.Ordinal);
        Assert.EndsWith("3,44", Line(paper, "Arrondi especes"), StringComparison.Ordinal);

        var exact = Paper(ReceiptLayout.For(NotSubject() with { CashRounding = null }, 42), 42);
        Assert.DoesNotContain(exact, line => line.StartsWith("Arrondi", StringComparison.Ordinal));
        Assert.EndsWith("1 836,56", Line(exact, "Net a payer especes"), StringComparison.Ordinal);
    }

    [Fact]
    public void Articles_count_units_and_a_weighed_line_as_one()
    {
        Assert.Equal(12, ReceiptLayout.Articles(NotSubject().Lines));
        Assert.Equal(1, ReceiptLayout.Articles(Refund().Lines));
    }

    // ------------------------------------------------------------ the slip

    [Fact]
    public void A_repayment_slip_says_what_was_paid_with_no_name_no_balance_and_no_code()
    {
        var document = ReceiptLayout.Repayment(ElAmel, new RepaymentSlip("12/10/2026", "17:40", "Caisse 03", "Karim", Dzd(143), Dzd(2)));
        var paper = Paper(document, 32);

        Assert.Contains("VERSEMENT", paper.Select(line => line.Trim()));
        Assert.EndsWith("143,00", Line(paper, "Verse sur le carnet"), StringComparison.Ordinal);
        Assert.EndsWith("2,00", Line(paper, "Arrondi especes"), StringComparison.Ordinal);
        Assert.EndsWith("145,00", Line(paper, "Paye en especes"), StringComparison.Ordinal);
        Assert.Empty(document.Lines.OfType<BarcodeLine>());
        Assert.DoesNotContain(paper, line => line.Contains("remboursement", StringComparison.OrdinalIgnoreCase));
    }

    private static Domain.Values.Money Dzd(decimal amount) =>
        Domain.Values.Money.FromMinorUnits((long)(amount * 100), Domain.Values.Currency.Dzd);
}
