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

        var line = Assert.Single(Paper(bytes), l => l.Contains("25.00", StringComparison.Ordinal));
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
        Assert.EndsWith("25.00", line, StringComparison.Ordinal);
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
        Assert.EndsWith("1234567.89", line, StringComparison.Ordinal);
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
    [InlineData(0, "0.00")]
    [InlineData(5, "0.05")]
    [InlineData(-5, "-0.05")]
    [InlineData(2500, "25.00")]
    [InlineData(-2500, "-25.00")]
    [InlineData(123456789, "1234567.89")]
    public void An_amount_is_formatted_from_its_minor_units(long minorUnits, string expected) =>
        Assert.Equal(expected, EscPosEncoder.Format(Money.FromMinorUnits(minorUnits, Currency.Dzd)));

    [Fact]
    public void A_negative_amount_under_one_unit_keeps_its_sign()
    {
        // -0.05 formatted from units and remainder separately loses the sign,
        // because -5 / 100 is 0 in integer arithmetic. On a refund line that
        // prints a credit as a charge.
        Assert.Equal("-0.05", EscPosEncoder.Format(Money.FromMinorUnits(-5, Currency.Dzd)));
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
    public void Double_height_text_wraps_to_half_the_columns()
    {
        // The printer does not know the text is twice as wide. It prints past
        // the edge of the paper and the rest is simply gone.
        var bytes = Standard.Encode(Document(
            new TextLine(new string('B', 40), DoubleHeight: true)));

        var line = Paper(bytes).First(l => l.Contains('B', StringComparison.Ordinal));

        Assert.Equal(21, line.Length);
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
