using Waymark.Domain.Catalogue;

namespace Waymark.Domain.Tests;

/// <summary>
/// Reading a scale label (session B3, D-090). The silent failures: the weight read from the wrong
/// digits, a smudged label read anyway, any 2x code taken for a label, and PLU 537 missed because
/// the label prints it 00537.
/// </summary>
public sealed class ScaleLabelFormatTests
{
    // "21" + item 00537 + value 00556 + check digit.
    private const string WeightLabel = "2100537005566";

    [Fact]
    public void The_standard_format_reads_five_item_digits_then_five_value_digits()
    {
        var label = ScaleLabelFormat.Default.Read(WeightLabel);

        Assert.Equal(new ScaleLabel("537", 556), label);
    }

    [Fact]
    public void A_price_label_reads_the_same_way_and_the_product_says_what_the_value_is()
    {
        // 22 + 00537 + 00100: 100 dinars under the default, which counts dinars.
        Assert.Equal(new ScaleLabel("537", 100), ScaleLabelFormat.Default.Read("2200537001008"));
        Assert.Equal(LabelPriceUnit.Dinars, ScaleLabelFormat.Default.PriceUnit);
    }

    [Theory]
    [InlineData("2100537005567")] // the last digit off by one: a worn print head
    [InlineData("2100537005560")]
    public void A_label_whose_check_digit_does_not_hold_is_not_read(string code) =>
        Assert.Null(ScaleLabelFormat.Default.Read(code));

    [Theory]
    [InlineData("1900537005563")] // a manufacturer's code, not the in-store range
    [InlineData("210053700556")]  // twelve digits
    [InlineData("21005370055661")]
    [InlineData("21005A7005566")]
    [InlineData("")]
    [InlineData(null)]
    public void A_code_that_is_not_this_formats_label_is_not_read(string? code) =>
        Assert.Null(ScaleLabelFormat.Default.Read(code));

    [Fact]
    public void Every_in_store_prefix_20_to_29_is_a_label_by_default()
    {
        Assert.NotNull(ScaleLabelFormat.Default.Read("2700537005568"));
        Assert.NotNull(ScaleLabelFormat.Default.Read("2800537005565"));
    }

    [Fact]
    public void A_milk_barcode_in_the_20_range_also_reads_as_a_label_which_is_why_the_lookup_asks_the_barcode_first()
    {
        // seed-42's milk: prefix 20, item 0, value 1. The lookup matches the exact barcode before it
        // reads any label (D-090); this test pins why that order matters.
        Assert.Equal(new ScaleLabel("0", 1), ScaleLabelFormat.Default.Read("2000000000015"));
    }

    [Theory]
    [InlineData("00537", "537")]
    [InlineData("537", "537")]
    [InlineData(" 0537 ", "537")]
    [InlineData("0000", "0")]
    public void A_plu_is_compared_without_its_leading_zeros(string plu, string itemCode) =>
        Assert.Equal(itemCode, ScaleLabelFormat.ItemCodeOf(plu));

    [Theory]
    [InlineData("2000000000015", true)]
    [InlineData("2100537005566", true)]
    [InlineData("2100537005565", false)]
    [InlineData("210053700556", false)]
    public void The_ean13_check_digit(string code, bool holds) =>
        Assert.Equal(holds, ScaleLabelFormat.Ean13CheckDigitHolds(code));

    // ------------------------------------------------------------------ presets

    [Fact]
    public void A_store_with_no_format_reads_the_standard_one()
    {
        Assert.Same(ScaleLabelFormat.Default, ScaleLabelFormat.Parse(null));
        Assert.Same(ScaleLabelFormat.Default, ScaleLabelFormat.Parse("  "));
        Assert.Equal("standard", ScaleLabelFormat.Default.Name);
    }

    [Fact]
    public void Every_preset_is_found_by_its_name_and_is_a_valid_mask()
    {
        foreach (var preset in ScaleLabelFormat.Presets)
        {
            Assert.Same(preset, ScaleLabelFormat.Parse(preset.Name.ToUpperInvariant()));
            Assert.Equal(ScaleLabelFormat.Length, preset.Mask.Length);
            Assert.Equal(preset.Mask, ScaleLabelFormat.Parse(preset.Mask).Mask);
        }
    }

    [Fact]
    public void The_four_digit_item_preset_reads_past_the_verifier_digit()
    {
        // 21 + 0537 + verifier 0 + 00556.
        Assert.Equal(new ScaleLabel("537", 556), ScaleLabelFormat.Parse("item4-verifier").Read("2105370005568"));
    }

    [Fact]
    public void The_six_digit_item_preset_has_a_one_digit_prefix()
    {
        // 2 + 105379 + 00556: the reviewed schema's layout.
        Assert.Equal(new ScaleLabel("105379", 556), ScaleLabelFormat.Parse("item6").Read("2105379005569"));
    }

    [Fact]
    public void The_centimes_preset_counts_the_value_in_centimes() =>
        Assert.Equal(LabelPriceUnit.Centimes, ScaleLabelFormat.Parse("standard-centimes").PriceUnit);

    // ------------------------------------------------------------------ custom masks

    [Fact]
    public void A_custom_mask_with_its_own_prefixes_and_unit()
    {
        var format = ScaleLabelFormat.Parse("PPIIIIIVVVVVC; prefixes=27 ; price=centimes");

        Assert.Equal(["27"], format.Prefixes);
        Assert.Equal(LabelPriceUnit.Centimes, format.PriceUnit);
        Assert.NotNull(format.Read("2700537005568"));
        Assert.Null(format.Read(WeightLabel)); // 21 is not this store's prefix
    }

    [Theory]
    [InlineData("PPIIIIIVVVVV")]          // twelve letters
    [InlineData("PPIIIIIVVVVVCC")]
    [InlineData("PPIIIIIVVVVCV")]         // the check digit is not last
    [InlineData("PPIIIIIVVVVVX")]
    [InlineData("IPPIIIIVVVVVC")]         // does not start with its prefix
    [InlineData("PPIIVIIVVVVVC")]         // the value is broken in two
    [InlineData("PPIIIIIIIVVVC")]         // three value digits
    [InlineData("PPIIIIIVVVVVQ")]
    [InlineData("PPPIIIIVVVVVC")]         // three prefix digits and no prefixes listed
    [InlineData("PPIIIIIVVVVVC;prefixes=2")]
    [InlineData("PPIIIIIVVVVVC;price=euros")]
    [InlineData("PPIIIIIVVVVVC;colour=red")]
    public void A_mask_that_cannot_be_read_is_refused_with_its_reason(string text) =>
        Assert.Throws<FormatException>(() => ScaleLabelFormat.Parse(text));
}
