using System.Text;
using System.Text.RegularExpressions;
using Waymark.Domain.Hardware;
using Waymark.Domain.Values;
using Waymark.Hardware;

namespace Waymark.Hardware.Tests;

/// <summary>
/// The encoder, which is the part of the fake printer that has to be right for
/// the real one to work. Everything else is a file handle.
/// </summary>
public sealed class EscPosEncoderTests
{
    private static readonly EscPosEncoder Standard = new(PrinterProfile.Standard80Mm);

    private static ReceiptDocument Document(params ReceiptLine[] lines) => new(lines);

    /// <summary>The printable text the bytes would put on paper, one line per line.</summary>
    private static string[] Paper(byte[] bytes) =>
        [.. Rendering(bytes).Split('\n').Select(line => Regex.Replace(line, "<[^>]*>", string.Empty))];

    /// <summary>The sink's own readable rendering — text plus named commands.</summary>
    private static string Rendering(byte[] bytes) => FileEscPosSink.Render(bytes);

    // --------------------------------------------------------- hostile text

    /// <summary>ESC p 0 50 50: kick the drawer open. Text that carries it must print it, not do it.</summary>
    private const string DrawerKick = "\u001Bp\u0000\u0032\u0032";

    public static TheoryData<string, ReceiptLine> LinesCarryingCommands() => new()
    {
        { "a product name", new TextLine("Lait " + DrawerKick + "1L") },
        { "an amount's label", new AmountLine("Pain" + DrawerKick, Money.FromMinorUnits(2500, Currency.Dzd)) },
        { "a customer's name", new TextLine("Client " + DrawerKick, Emphasised: true, DoubleHeight: true) },
        { "a separator", new SeparatorLine('\u001B') },
        { "a reset in a name", new TextLine("Caf\u001B@e") },
        { "a cut in a name", new TextLine("Th\u001DVBe") },
    };

    [Theory]
    [MemberData(nameof(LinesCarryingCommands))]
    public void Text_from_the_database_cannot_smuggle_a_printer_command(string _, ReceiptLine line)
    {
        // Product and customer names come from the store's data. ASCII encoding keeps control
        // characters, so an ESC or GS inside one reached the printer as a command: a name could
        // open the drawer with no reason recorded, which D-052 makes the shrinkage audit point.
        var bytes = Standard.Encode(Document(line));
        var header = EscPos.Initialise.Length + EscPos.CodePage(PrinterProfile.Standard80Mm.CodePage).Length;
        var body = bytes.AsSpan(header);

        Assert.True(body.IndexOf("\u001Bp"u8) < 0, "A drawer kick in the text reached the printer.");
        Assert.True(body.IndexOf("\u001B@"u8) < 0, "A reset in the text reached the printer.");
        Assert.True(body.IndexOf("\u001DV"u8) < 0, "A cut in the text reached the printer.");
        Assert.DoesNotContain("drawer", Rendering(bytes), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_control_character_prints_as_a_mark_in_its_own_place_so_the_layout_holds()
    {
        var bytes = Standard.Encode(Document(new AmountLine("Pa\u0007in\nX", Money.FromMinorUnits(2500, Currency.Dzd))));

        var line = Assert.Single(Paper(bytes), l => l.Contains("25,00", StringComparison.Ordinal));
        Assert.Equal(42, line.Length);
        Assert.StartsWith("Pa?in?X", line, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------- the paper

    [Fact]
    public void An_amount_line_puts_the_label_left_and_the_figure_right()
    {
        var bytes = Standard.Encode(Document(
            new AmountLine("Pain", Money.FromMinorUnits(2500, Currency.Dzd))));

        var line = Paper(bytes).First(l => l.Contains("Pain", StringComparison.Ordinal));

        Assert.Equal(42, line.Length);
        Assert.StartsWith("Pain", line, StringComparison.Ordinal);
        Assert.EndsWith("25,00", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_label_too_long_for_the_paper_is_cut_and_the_figure_never_is()
    {
        // A truncated label is readable; a truncated total is a receipt that
        // disagrees with the till, in the customer's hand.
        var bytes = Standard.Encode(Document(
            new AmountLine(new string('X', 200), Money.FromMinorUnits(123456789, Currency.Dzd))));

        var line = Paper(bytes).First(l => l.Contains('X', StringComparison.Ordinal));

        Assert.Equal(42, line.Length);
        Assert.EndsWith("1 234 567,89", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_narrow_printer_lays_out_to_its_own_width()
    {
        var narrow = new EscPosEncoder(PrinterProfile.Narrow58Mm);

        var line = Paper(narrow.Encode(Document(
                new AmountLine("Lait", Money.FromMinorUnits(9000, Currency.Dzd)))))
            .First(l => l.Contains("Lait", StringComparison.Ordinal));

        Assert.Equal(32, line.Length);
    }

    [Fact]
    public void A_separator_fills_the_paper()
    {
        var line = Paper(Standard.Encode(Document(new SeparatorLine())))
            .First(l => l.Contains('-', StringComparison.Ordinal));

        Assert.Equal(new string('-', 42), line);
    }

    // ------------------------------------------------------------ the amount

    [Theory]
    [InlineData(0, "0,00")]
    [InlineData(5, "0,05")]
    [InlineData(-5, "-0,05")]
    [InlineData(2500, "25,00")]
    [InlineData(-2500, "-25,00")]
    [InlineData(100000, "1 000,00")]
    [InlineData(183656, "1 836,56")]
    [InlineData(-95000, "-950,00")]
    [InlineData(123456789, "1 234 567,89")]
    public void An_amount_is_formatted_from_its_minor_units_as_the_shop_writes_it(long minorUnits, string expected) =>
        Assert.Equal(expected, EscPosEncoder.Format(Money.FromMinorUnits(minorUnits, Currency.Dzd)));

    [Fact]
    public void A_negative_amount_under_one_unit_keeps_its_sign()
    {
        // -0.05 formatted from units and remainder separately loses the sign,
        // because -5 / 100 is 0 in integer arithmetic. On a refund line that
        // prints a credit as a charge.
        Assert.Equal("-0,05", EscPosEncoder.Format(Money.FromMinorUnits(-5, Currency.Dzd)));
    }

    [Fact]
    public void A_total_says_what_it_is_counted_in_and_stands_tall_at_the_full_width()
    {
        var narrow = new EscPosEncoder(PrinterProfile.Narrow58Mm);
        var rendering = Rendering(narrow.Encode(Document(
            new AmountLine("TOTAL", Money.FromMinorUnits(183656, Currency.Dzd), DoubleHeight: true, WithCurrency: true))));

        var line = Paper(narrow.Encode(Document(
            new AmountLine("TOTAL", Money.FromMinorUnits(183656, Currency.Dzd), DoubleHeight: true, WithCurrency: true))))
            .First(l => l.Contains("TOTAL", StringComparison.Ordinal));
        Assert.Equal(("TOTAL", "1 836,56 DA", 32), (line[..5], line[^11..], line.Length));
        Assert.True(
            rendering.IndexOf("<double height>", StringComparison.Ordinal) < rendering.IndexOf("TOTAL", StringComparison.Ordinal)
            && rendering.IndexOf("<normal height>", StringComparison.Ordinal) > rendering.IndexOf("TOTAL", StringComparison.Ordinal),
            "The total is not tall, or what follows it is.");
    }

    // -------------------------------------------------------------- the pair

    [Fact]
    public void A_pair_puts_text_left_and_right_and_the_right_is_never_cut()
    {
        var narrow = new EscPosEncoder(PrinterProfile.Narrow58Mm);

        var line = Paper(narrow.Encode(Document(new PairLine("Caisse principale du fond", "Caissier : Karim"))))
            .First(l => l.Contains("Karim", StringComparison.Ordinal));

        Assert.Equal(32, line.Length);
        Assert.EndsWith(" Caissier : Karim", line, StringComparison.Ordinal);
        Assert.StartsWith("Caisse principa ", line, StringComparison.Ordinal);
    }

    // ------------------------------------------------------------ the french

    [Theory]
    [InlineData("Lait UHT demi-écrémé 1 L", "Lait UHT demi-ecreme 1 L")]
    [InlineData("N° 03-011542", "No 03-011542")]
    [InlineData("Œufs frais, bœuf", "OEufs frais, boeuf")]
    [InlineData("Ticket d’origine", "Ticket d'origine")]
    [InlineData("Échange sous 7 jours", "Echange sous 7 jours")]
    public void French_is_folded_to_ascii_until_a_code_page_is_proven(string text, string paper) =>
        Assert.Equal(paper, EscPosEncoder.Fold(text));

    [Fact]
    public void What_has_no_ascii_form_is_a_mark_and_the_layout_still_holds()
    {
        // Arabic is K2's, drawn as a picture; printed as text today it is a row of marks, not a
        // printer command and not a broken line.
        var bytes = Standard.Encode(Document(new AmountLine("حليب", Money.FromMinorUnits(9000, Currency.Dzd))));

        var line = Paper(bytes).First(l => l.Contains("90,00", StringComparison.Ordinal));
        Assert.Equal(42, line.Length);
        Assert.StartsWith("????", line, StringComparison.Ordinal);
    }

    [Fact]
    public void A_line_break_in_text_starts_a_new_line_and_reversed_text_is_switched_back()
    {
        var rendering = Rendering(Standard.Encode(Document(
            new TextLine("Merci de votre visite !\nOuvert 7j/7", TextAlignment.Centre),
            new TextLine("DUPLICATA", TextAlignment.Centre, DoubleHeight: true, Reversed: true),
            new TextLine("after"))));

        Assert.Contains("Merci de votre visite !\nOuvert 7j/7", rendering, StringComparison.Ordinal);
        Assert.True(
            rendering.IndexOf("<reverse on>", StringComparison.Ordinal) < rendering.IndexOf(" DUPLICATA ", StringComparison.Ordinal)
            && rendering.IndexOf("<reverse off>", StringComparison.Ordinal) < rendering.IndexOf("after", StringComparison.Ordinal),
            "Reverse printing was still on when the next line printed.");
    }

    // ----------------------------------------------------------- the control

    [Fact]
    public void Every_document_starts_by_initialising_the_printer()
    {
        // A printer holds emphasis and alignment between jobs, so the receipt
        // after a bold one is bold — a bug that never appears in the first print
        // of a session.
        var bytes = Standard.Encode(Document(new TextLine("x")));

        Assert.Equal(0x1B, bytes[0]);
        Assert.Equal(0x40, bytes[1]);
    }

    [Fact]
    public void Emphasis_is_turned_off_again()
    {
        var rendering = Rendering(Standard.Encode(Document(
            new TextLine("TOTAL", Emphasised: true),
            new TextLine("plain"))));

        Assert.Contains("<bold on>", rendering, StringComparison.Ordinal);
        Assert.Contains("<bold off>", rendering, StringComparison.Ordinal);
        Assert.True(
            rendering.IndexOf("<bold off>", StringComparison.Ordinal)
            < rendering.IndexOf("plain", StringComparison.Ordinal),
            "Emphasis was still on when the next line printed.");
    }

    [Fact]
    public void A_printer_with_no_cutter_is_not_sent_a_cut()
    {
        var noCutter = new EscPosEncoder(PrinterProfile.Standard80MmNoCutter);

        Assert.DoesNotContain(
            "<cut>",
            Rendering(noCutter.Encode(Document(new CutLine()))),
            StringComparison.Ordinal);

        Assert.Contains(
            "<cut>",
            Rendering(Standard.Encode(Document(new CutLine()))),
            StringComparison.Ordinal);
    }

    // ---------------------------------------------------------- the barcodes

    [Fact]
    public void An_ean13_that_is_not_thirteen_digits_is_refused()
    {
        // The printer computes no check digit. Hand it twelve digits and it
        // prints something that scans as another product, or prints nothing —
        // and neither looks like an error at the till.
        Assert.Throws<ArgumentException>(() =>
            Standard.Encode(Document(new BarcodeLine("612345678901", BarcodeSymbology.Ean13))));

        Assert.Throws<ArgumentException>(() =>
            Standard.Encode(Document(new BarcodeLine("61234567890AB", BarcodeSymbology.Ean13))));
    }

    [Fact]
    public void An_ean13_with_a_wrong_check_digit_is_refused()
    {
        // 4006381333931 is valid; change only the last digit and every length
        // and character check still passes.
        Standard.Encode(Document(new BarcodeLine("4006381333931", BarcodeSymbology.Ean13)));

        var error = Assert.Throws<ArgumentException>(() =>
            Standard.Encode(Document(new BarcodeLine("4006381333932", BarcodeSymbology.Ean13))));
        Assert.Contains("check digit", error.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void A_valid_ean13_is_length_prefixed_and_carries_its_digits()
    {
        var bytes = Standard.Encode(Document(
            new BarcodeLine("6123456789016", BarcodeSymbology.Ean13)));

        var header = new byte[] { 0x1D, 0x6B, 67, 13 };
        Assert.True(bytes.AsSpan().IndexOf(header) >= 0, "GS k header with symbology 67 and length 13 is missing.");
        Assert.True(bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes("6123456789016")) >= 0);
    }

    [Fact]
    public void Code39_refuses_what_it_cannot_encode()
    {
        Assert.Throws<ArgumentException>(() =>
            Standard.Encode(Document(new BarcodeLine("inv-01", BarcodeSymbology.Code39))));

        // Uppercase, digits and the punctuation subset are fine.
        Standard.Encode(Document(new BarcodeLine("INV-01", BarcodeSymbology.Code39)));
    }

    [Fact]
    public void A_receipts_number_is_a_code128_of_set_b_carrying_its_text()
    {
        var bytes = Standard.Encode(Document(new BarcodeLine("S-2026-000142", BarcodeSymbology.Code128)));

        // GS k 73, the length counting the "{B" that names the set, then the set and the number.
        var header = new byte[] { 0x1D, 0x6B, 73, 15 };
        Assert.True(bytes.AsSpan().IndexOf(header) >= 0, "GS k header with symbology 73 and length 15 is missing.");
        Assert.True(bytes.AsSpan().IndexOf(Encoding.ASCII.GetBytes("{BS-2026-000142")) >= 0);
    }

    [Theory]
    [InlineData("")]
    [InlineData("S-2026-{A0001")]
    [InlineData("N°142")]
    [InlineData("S\u001Bp")]
    public void Code128_refuses_what_set_b_cannot_carry_or_would_switch_set(string data) =>
        Assert.Throws<ArgumentException>(() => Standard.Encode(Document(new BarcodeLine(data, BarcodeSymbology.Code128))));

    [Fact]
    public void A_code_too_wide_at_two_dots_a_module_is_printed_at_one()
    {
        // 58 mm is 384 dots: a 13-character number is 178 modules, 356 dots at two; a 20-character
        // one is 255 modules, too wide at two and printed at one rather than cut off.
        var narrow = new EscPosEncoder(PrinterProfile.Narrow58Mm);

        Assert.True(narrow.Encode(Document(new BarcodeLine("S-2026-000142", BarcodeSymbology.Code128))).AsSpan().IndexOf(new byte[] { 0x1D, 0x77, 2 }) >= 0);
        Assert.True(narrow.Encode(Document(new BarcodeLine("EPICERIE-2026-000142", BarcodeSymbology.Code128))).AsSpan().IndexOf(new byte[] { 0x1D, 0x77, 1 }) >= 0);
    }

    // ------------------------------------------------------------ the wrapping

    [Fact]
    public void Text_wraps_at_words_rather_than_running_off_the_paper()
    {
        var wrapped = EscPosEncoder.Wrap("the quick brown fox jumps over the lazy dog", 16).ToList();

        Assert.All(wrapped, line => Assert.True(line.Length <= 16, $"'{line}' is {line.Length} wide."));
        Assert.Equal("the quick brown fox jumps over the lazy dog", string.Join(' ', wrapped));
    }

    [Fact]
    public void A_word_longer_than_the_paper_is_broken_rather_than_lost()
    {
        var wrapped = EscPosEncoder.Wrap(new string('A', 20) + " tail", 8).ToList();

        Assert.All(wrapped, line => Assert.True(line.Length <= 8));
        Assert.Equal(new string('A', 20) + " tail", string.Join(string.Empty, wrapped[..3]) + " " + wrapped[3]);
    }

    [Fact]
    public void Double_height_text_keeps_the_full_width_and_wraps_past_it()
    {
        // GS ! 1 is double height at single width (D-125): the board's tall store name and total
        // fill the line. What is longer than the paper still wraps, since the printer prints past
        // the edge and the rest is simply gone.
        var bytes = Standard.Encode(Document(
            new TextLine(new string('B', 40) + " " + new string('C', 10), DoubleHeight: true)));

        var paper = Paper(bytes);

        Assert.Equal(40, paper.First(l => l.Contains('B', StringComparison.Ordinal)).Length);
        Assert.Equal(10, paper.First(l => l.Contains('C', StringComparison.Ordinal)).Length);
    }

    // -------------------------------------------------- the closed hierarchy

    [Fact]
    public void Every_line_kind_is_encoded()
    {
        // ReceiptLine is closed, and the encoder switches over it. C# cannot
        // make that switch exhaustive, so this does: a new line kind nobody
        // taught the encoder about would otherwise print as nothing at all.
        var kinds = typeof(ReceiptLine).Assembly.GetTypes()
            .Where(type => type.IsSubclassOf(typeof(ReceiptLine)) && !type.IsAbstract)
            .ToList();

        Assert.NotEmpty(kinds);

        ReceiptLine[] samples =
        [
            new TextLine("x"),
            new AmountLine("x", Money.Zero(Currency.Dzd)),
            new PairLine("x", "y"),
            new SeparatorLine(),
            new BarcodeLine("INV1", BarcodeSymbology.Code39),
            new FeedLine(),
            new CutLine(),
        ];

        var covered = samples.Select(sample => sample.GetType()).ToHashSet();
        var missing = kinds.Where(kind => !covered.Contains(kind)).Select(kind => kind.Name).ToList();

        Assert.True(
            missing.Count == 0,
            $"ReceiptLine kinds with no sample here, so the encoder is untested for them: "
            + string.Join(", ", missing));

        foreach (var sample in samples)
        {
            Standard.Encode(Document(sample));
        }
    }

    [Fact]
    public void A_feed_of_nothing_is_refused()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Standard.Encode(Document(new FeedLine(0))));
    }
}
