using System.Globalization;

namespace Waymark.Domain.Catalogue;

/// <summary>What the value digits of a price label count: centimes, or whole dinars.</summary>
public enum LabelPriceUnit
{
    /// <summary>1/100 of the currency unit: "01234" is 12,34 DA. Five digits stop at 999,99.</summary>
    Centimes,

    /// <summary>Whole units: "01234" is 1 234,00 DA. What a scale set for dinars prints.</summary>
    Dinars,
}

/// <summary>A scale label read by a <see cref="ScaleLabelFormat"/>: which product, and its value.</summary>
/// <param name="ItemCode">The item digits with their leading zeros taken off: "00537" is "537", matched to a PLU.</param>
/// <param name="Value">
/// The value digits as a number. For a weight label, thousandths of the selling unit (grams, for a
/// product sold by the kilo); for a price label, <see cref="ScaleLabelFormat.PriceUnit"/>.
/// </param>
public sealed record ScaleLabel(string ItemCode, long Value);

/// <summary>
/// How a store's scale labels are laid out (session B3, D-090): which of an EAN-13's digits name
/// the product and which carry its weight or price.
///
/// <para>
/// <b>The mask</b> has one letter per digit, thirteen in all: <c>P</c> the prefix, <c>I</c> the
/// item code, <c>V</c> the value, <c>X</c> a digit read past (a price verifier, or a fixed zero),
/// and <c>C</c> the EAN-13 check digit, last. <c>P</c>, <c>I</c> and <c>V</c> are each one run.
/// Whether the value is a weight or a price is not the label's to say: the product's
/// <c>barcode_type</c> says it.
/// </para>
/// <para>
/// <b>What a label must pass</b>, each learned from the scales' own documentation: thirteen digits;
/// a prefix this format owns (a POS that took any <c>2x</c> code sold milk as tomatoes); and the
/// check digit, which is what stops a label smudged by a worn print head from ringing up a different
/// weight. A price verifier (<c>X</c>) is read past, not checked: the check digit already covers it.
/// </para>
/// <para>
/// <b>The known formats ship as presets</b> (<see cref="Presets"/>); a scale that prints something
/// else is described by a custom mask, in the text <see cref="Parse"/> reads:
/// <c>MASK;prefixes=21,22;price=dinars</c>, where both options may be left out.
/// </para>
/// </summary>
public sealed class ScaleLabelFormat
{
    /// <summary>An EAN-13 has thirteen digits, and the mask one letter for each.</summary>
    public const int Length = 13;

    private ScaleLabelFormat(string name, string mask, IReadOnlyList<string> prefixes, LabelPriceUnit priceUnit)
    {
        Name = name;
        Mask = mask;
        Prefixes = prefixes;
        PriceUnit = priceUnit;
    }

    /// <summary>The preset's name, or the text a custom format was read from.</summary>
    public string Name { get; }

    /// <summary>Thirteen letters from <c>P I V X C</c>.</summary>
    public string Mask { get; }

    /// <summary>The prefixes this format reads; any other code is not one of its labels.</summary>
    public IReadOnlyList<string> Prefixes { get; }

    /// <summary>What a price label's value digits count.</summary>
    public LabelPriceUnit PriceUnit { get; }

    /// <summary>
    /// The known layouts. Dinars unless named otherwise: five digits of centimes stop at 999,99 DA,
    /// short of a kilo of meat, so scales sold here are set to dinars.
    /// </summary>
    public static IReadOnlyList<ScaleLabelFormat> Presets { get; } =
    [
        // CAS (CL5200, CL3000), Dibal, Mettler Toledo out of the box: 2x, five item digits, five value digits.
        new("standard", "PPIIIIIVVVVVC", RestrictedCirculation(), LabelPriceUnit.Dinars),
        new("standard-centimes", "PPIIIIIVVVVVC", RestrictedCirculation(), LabelPriceUnit.Centimes),

        // Four item digits, then a verifier or fixed digit before the value (Bizerba, some Dibal set-ups).
        new("item4-verifier", "PPIIIIXVVVVVC", RestrictedCirculation(), LabelPriceUnit.Dinars),

        // Five item digits, a price verifier, four value digits (Ishida's industry default).
        new("item5-verifier", "PPIIIIIXVVVVC", RestrictedCirculation(), LabelPriceUnit.Dinars),

        // One prefix digit, six item digits: the layout the reviewed schema described (v7.1).
        new("item6", "PIIIIIIVVVVVC", ["2"], LabelPriceUnit.Dinars),
    ];

    /// <summary>The format a store reads when it has chosen none: <c>standard</c>.</summary>
    public static ScaleLabelFormat Default => Presets[0];

    /// <summary>
    /// The format named or described by <paramref name="text"/>: null or blank is <see cref="Default"/>,
    /// a preset's name is that preset, anything else is read as a custom mask.
    /// </summary>
    /// <exception cref="FormatException">The text is neither a preset nor a mask this class can read; the message says why.</exception>
    public static ScaleLabelFormat Parse(string? text)
    {
        var trimmed = text?.Trim();
        if (string.IsNullOrEmpty(trimmed))
        {
            return Default;
        }

        var preset = Presets.FirstOrDefault(p => string.Equals(p.Name, trimmed, StringComparison.OrdinalIgnoreCase));
        if (preset is not null)
        {
            return preset;
        }

        var parts = trimmed.Split(';', StringSplitOptions.TrimEntries);
        var mask = parts[0].ToUpperInvariant();
        CheckMask(mask);

        var prefixLength = mask.Count(c => c == 'P');
        List<string>? prefixes = null;
        var priceUnit = LabelPriceUnit.Dinars;
        foreach (var option in parts.Skip(1).Where(o => o.Length > 0))
        {
            var pair = option.Split('=', 2, StringSplitOptions.TrimEntries);
            switch (pair[0].ToUpperInvariant())
            {
                case "PREFIXES" when pair.Length == 2:
                    prefixes = [.. pair[1].Split(',', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries)];
                    break;
                case "PRICE" when pair.Length == 2 && pair[1].Equals("dinars", StringComparison.OrdinalIgnoreCase):
                    priceUnit = LabelPriceUnit.Dinars;
                    break;
                case "PRICE" when pair.Length == 2 && pair[1].Equals("centimes", StringComparison.OrdinalIgnoreCase):
                    priceUnit = LabelPriceUnit.Centimes;
                    break;
                default:
                    throw new FormatException(
                        $"'{option}' is not an option of a label format: write prefixes=21,22 or price=dinars|centimes.");
            }
        }

        // With no prefixes given, a two-digit prefix is GS1's in-store range, 20 to 29, and a
        // one-digit prefix is its first digit, 2.
        prefixes ??= prefixLength switch
        {
            1 => ["2"],
            2 => RestrictedCirculation(),
            _ => throw new FormatException($"A mask with {prefixLength} prefix digits needs its prefixes listed: prefixes=…"),
        };

        if (prefixes.Count == 0 || prefixes.Any(p => p.Length != prefixLength || !p.All(char.IsAsciiDigit)))
        {
            throw new FormatException($"Each prefix must be {prefixLength} digits, as many as the mask's P.");
        }

        return new ScaleLabelFormat(trimmed, mask, prefixes, priceUnit);
    }

    /// <summary>
    /// The label in <paramref name="code"/>, or null when it is not one of this format's labels:
    /// not thirteen digits, a prefix this format does not own, or a check digit that does not hold.
    /// </summary>
    public ScaleLabel? Read(string? code)
    {
        var trimmed = code?.Trim();
        if (trimmed is null || trimmed.Length != Length || !trimmed.All(char.IsAsciiDigit))
        {
            return null;
        }

        if (!Prefixes.Contains(Digits(trimmed, 'P'), StringComparer.Ordinal) || !Ean13CheckDigitHolds(trimmed))
        {
            return null;
        }

        var item = Digits(trimmed, 'I').TrimStart('0');
        var value = long.Parse(Digits(trimmed, 'V'), NumberStyles.None, CultureInfo.InvariantCulture);
        return new ScaleLabel(item.Length == 0 ? "0" : item, value);
    }

    /// <summary>
    /// Whether the last digit of a thirteen-digit code is its EAN-13 check digit: from the left, the
    /// first twelve digits weighted 1, 3, 1, 3…, and the check digit brings the sum to a multiple of ten.
    /// </summary>
    public static bool Ean13CheckDigitHolds(string code)
    {
        ArgumentNullException.ThrowIfNull(code);
        if (code.Length != Length || !code.All(char.IsAsciiDigit))
        {
            return false;
        }

        var sum = 0;
        for (var i = 0; i < Length - 1; i++)
        {
            sum += (code[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        return (10 - (sum % 10)) % 10 == code[^1] - '0';
    }

    /// <summary>A PLU as a label's item code is compared: leading zeros off, so "00537" and "537" are one.</summary>
    public static string ItemCodeOf(string plu)
    {
        ArgumentNullException.ThrowIfNull(plu);
        var trimmed = plu.Trim().TrimStart('0');
        return trimmed.Length == 0 ? "0" : trimmed;
    }

    private string Digits(string code, char letter) =>
        new([.. code.Where((_, i) => Mask[i] == letter)]);

    private static List<string> RestrictedCirculation() =>
        [.. Enumerable.Range(20, 10).Select(p => p.ToString(CultureInfo.InvariantCulture))];

    /// <summary>A mask this class can read, or the reason it cannot, in words a manager can act on.</summary>
    private static void CheckMask(string mask)
    {
        if (mask.Length != Length)
        {
            throw new FormatException($"A label mask has {Length} letters, one per digit; '{mask}' has {mask.Length}.");
        }

        if (mask.Any(c => c is not ('P' or 'I' or 'V' or 'X' or 'C')))
        {
            throw new FormatException($"A label mask is written with P, I, V, X and C only: '{mask}'.");
        }

        if (mask[^1] != 'C' || mask.Count(c => c == 'C') != 1)
        {
            throw new FormatException("A label mask ends with its check digit, C, and has only that one.");
        }

        if (mask[0] != 'P')
        {
            throw new FormatException("A label mask starts with its prefix, P.");
        }

        foreach (var letter in "PIV")
        {
            var first = mask.IndexOf(letter, StringComparison.Ordinal);
            var last = mask.LastIndexOf(letter);
            if (first < 0 || mask[first..(last + 1)].Any(c => c != letter))
            {
                throw new FormatException($"A label mask has one unbroken run of {letter}.");
            }
        }

        if (mask.Count(c => c == 'V') is < 4 or > 6)
        {
            throw new FormatException("A label's value takes 4 to 6 digits.");
        }
    }
}
