using System.Globalization;
using System.Text;
using Waymark.Domain.Hardware;
using Waymark.Domain.Values;

namespace Waymark.Hardware;

/// <summary>
/// Turns a <see cref="ReceiptDocument"/> into the bytes a printer takes.
///
/// <para>
/// <b>One encoder, two sinks.</b> The fake printer and the real one share every
/// line of this, differing only in where the bytes go. That is what makes
/// developing against a file worth anything: if the fake rendered the document
/// its own way, the first real print would be the first test of the encoding.
/// </para>
/// </summary>
public sealed class EscPosEncoder(PrinterProfile profile)
{
    private readonly PrinterProfile _profile = profile;

    /// <summary>Encodes a whole document, initialise to cut.</summary>
    public byte[] Encode(ReceiptDocument document)
    {
        ArgumentNullException.ThrowIfNull(document);

        var bytes = new List<byte>(512);

        // Always initialise. A printer holds emphasis, alignment and character
        // size across jobs, so a receipt that follows a bold one is bold unless
        // the previous job happened to reset — which is a bug that appears only
        // in the second print of a session.
        bytes.AddRange(EscPos.Initialise);
        bytes.AddRange(EscPos.CodePage(_profile.CodePage));

        foreach (var line in document.Lines)
        {
            EncodeLine(line, bytes);
        }

        return [.. bytes];
    }

    private void EncodeLine(ReceiptLine line, List<byte> bytes)
    {
        switch (line)
        {
            case TextLine text:
                EncodeText(text, bytes);
                break;

            case AmountLine amount:
                EncodeAmount(amount, bytes);
                break;

            case PairLine pair:
                EncodePair(Fold(pair.Left), Fold(pair.Right), pair.Emphasised, tall: false, bytes);
                break;

            case SeparatorLine separator:
                Write(bytes, new string(separator.Fill, _profile.Columns));
                break;

            case BarcodeLine barcode:
                EncodeBarcode(barcode, bytes);
                break;

            case FeedLine feed:
                if (feed.Count < 1)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(line), feed.Count, "A feed of fewer than one line is not a feed.");
                }

                bytes.AddRange(Enumerable.Repeat(EscPos.LineFeed, feed.Count));
                break;

            case CutLine:
                // A printer with no cutter is told nothing rather than sent a
                // command it will answer with garbage on the paper.
                if (_profile.CanCut)
                {
                    bytes.AddRange(EscPos.PartialCut());
                }

                break;

            default:
                // ReceiptLine is a closed hierarchy, so this is unreachable
                // until somebody adds a line kind. EveryLineKindIsEncoded in the
                // test suite is what makes that a failing test rather than a
                // receipt with a line silently missing.
                throw new NotSupportedException(
                    $"{line.GetType().Name} is a ReceiptLine that EscPosEncoder does not handle.");
        }
    }

    private void EncodeText(TextLine line, List<byte> bytes)
    {
        bytes.AddRange(EscPos.Align(AlignmentByte(line.Alignment)));

        if (line.Emphasised)
        {
            bytes.AddRange(EscPos.Emphasis(true));
        }

        if (line.DoubleHeight)
        {
            bytes.AddRange(EscPos.CharacterSize(0x01));
        }

        if (line.Reversed)
        {
            bytes.AddRange(EscPos.Reverse(true));
        }

        // GS ! 1 is double height at single width: as many characters fit as on any line (D-125).
        // Double width would halve them; nothing here asks for it. The printer does not wrap for you —
        // it prints off the edge of the paper — so each paragraph is wrapped here.
        foreach (var paragraph in Fold(line.Text).Split('\n'))
        {
            foreach (var wrapped in Wrap(paragraph, _profile.Columns))
            {
                // Reversed text is a block, its own width and no wider, with a space either side.
                Write(bytes, line.Reversed ? $" {wrapped} " : wrapped);
            }
        }

        if (line.Reversed)
        {
            bytes.AddRange(EscPos.Reverse(false));
        }

        if (line.DoubleHeight)
        {
            bytes.AddRange(EscPos.CharacterSize(0x00));
        }

        if (line.Emphasised)
        {
            bytes.AddRange(EscPos.Emphasis(false));
        }

        bytes.AddRange(EscPos.Align(0));
    }

    private void EncodeAmount(AmountLine line, List<byte> bytes) =>
        EncodePair(Fold(line.Label), line.WithCurrency ? FormatWithCurrency(line.Amount) : Format(line.Amount), line.Emphasised, line.DoubleHeight, bytes);

    private void EncodePair(string left, string right, bool emphasised, bool tall, List<byte> bytes)
    {
        // What is on the right is never what gets cut. A truncated label is a
        // receipt somebody can still read; a truncated total is a receipt that
        // disagrees with the till, and the customer is holding it.
        right = right.Length <= _profile.Columns ? right : right[..(_profile.Columns)];
        var room = _profile.Columns - right.Length - 1;
        var label = room <= 0
            ? string.Empty
            : left.Length <= room ? left : left[..room];

        var gap = _profile.Columns - label.Length - right.Length;
        var text = label + new string(' ', Math.Max(gap, label.Length == 0 ? 0 : 1)) + right;

        bytes.AddRange(EscPos.Align(0));

        if (emphasised)
        {
            bytes.AddRange(EscPos.Emphasis(true));
        }

        if (tall)
        {
            bytes.AddRange(EscPos.CharacterSize(0x01));
        }

        Write(bytes, text);

        if (tall)
        {
            bytes.AddRange(EscPos.CharacterSize(0x00));
        }

        if (emphasised)
        {
            bytes.AddRange(EscPos.Emphasis(false));
        }
    }

    private void EncodeBarcode(BarcodeLine line, List<byte> bytes)
    {
        var (symbology, data) = line.Symbology switch
        {
            BarcodeSymbology.Code39 => (EscPos.Code39, ValidateCode39(line.Data)),
            BarcodeSymbology.Ean13 => (EscPos.Ean13, ValidateEan13(line.Data)),
            // Code set B, named in the data itself: the printer reads "{B" as the set, not as text.
            BarcodeSymbology.Code128 => (EscPos.Code128, "{B" + ValidateCode128(line.Data)),
            _ => throw new NotSupportedException($"Unknown symbology {line.Symbology}."),
        };

        bytes.AddRange(EscPos.Align(1));
        bytes.AddRange(EscPos.BarcodeHeight(64));
        bytes.AddRange(EscPos.BarcodeWidth(ModuleWidth(line)));
        bytes.AddRange(EscPos.BarcodeTextPosition(2));
        bytes.AddRange(EscPos.BarcodeHeader(symbology, (byte)data.Length));
        bytes.AddRange(Encoding.ASCII.GetBytes(data));
        bytes.Add(EscPos.LineFeed);
        bytes.AddRange(EscPos.Align(0));
    }

    private static string ValidateEan13(string data)
    {
        // The printer computes nothing. Hand it twelve digits and it prints a
        // barcode that scans as a different product, or refuses and prints
        // nothing — neither of which looks like an error at the till.
        if (data.Length != 13 || !data.All(char.IsAsciiDigit))
        {
            throw new ArgumentException(
                $"EAN-13 is thirteen digits including the check digit; got '{data}'.", nameof(data));
        }

        // Thirteen digits with a wrong last one is the same failure in disguise:
        // a mistyped digit anywhere still passes the length check. Weights
        // alternate 1 and 3 from the left over the first twelve digits.
        var sum = 0;
        for (var i = 0; i < 12; i++)
        {
            sum += (data[i] - '0') * (i % 2 == 0 ? 1 : 3);
        }

        var expected = (10 - (sum % 10)) % 10;
        if (data[12] - '0' != expected)
        {
            throw new ArgumentException(
                $"EAN-13 '{data}' fails its check digit (expected {expected}). A digit is wrong "
                + "somewhere, and printed as is it would scan as a different product or not at all.",
                nameof(data));
        }

        return data;
    }

    /// <summary>
    /// Two dots a module where the code fits the paper at two, else one. A Code 128 of set B is
    /// eleven modules a character, plus its start, check and stop (35); a printer is twelve dots a
    /// column at its standard font. A code wider than the paper is cut off and scans as nothing.
    /// Proven on paper in K1.
    /// </summary>
    private byte ModuleWidth(BarcodeLine line)
    {
        if (line.Symbology != BarcodeSymbology.Code128)
        {
            return 2;
        }

        var modules = (11 * (line.Data.Length + 3)) + 2;
        return modules * 2 <= _profile.Columns * 12 ? (byte)2 : (byte)1;
    }

    private static string ValidateCode128(string data)
    {
        // Set B is printable ASCII. The "{" that opens a set change is refused in the data, so a
        // number can never switch the printer to another set halfway through.
        if (data.Length == 0 || data.Length > 200 || data.Any(c => c is < ' ' or > '~' or '{'))
        {
            throw new ArgumentException(
                $"CODE128 set B encodes printable ASCII, 1 to 200 characters, and no '{{'; got '{data}'.", nameof(data));
        }

        return data;
    }

    private static string ValidateCode39(string data)
    {
        const string alphabet = "0123456789ABCDEFGHIJKLMNOPQRSTUVWXYZ-. $/+%";

        if (data.Length == 0 || data.Any(character => !alphabet.Contains(character, StringComparison.Ordinal)))
        {
            throw new ArgumentException(
                $"CODE39 encodes only uppercase alphanumerics and '-. $/+%'; got '{data}'.", nameof(data));
        }

        return data;
    }

    /// <summary>
    /// The amount as it appears on paper.
    ///
    /// <para>
    /// Formatted from <see cref="Currency.MinorUnitExponent"/>, which is what
    /// governs display — <b>not</b> from <see cref="Currency.StorageScale"/>,
    /// which is fixed at 100 for every currency so the value converter stays a
    /// pure function (CLAUDE.md §3.1, D-031, D-035).
    /// </para>
    /// <para>
    /// <c>Money.ToString()</c> is deliberately not reused: it is a debug form
    /// that always prints two decimals and appends the currency code, and a
    /// receipt is a legal document rather than a log line.
    /// </para>
    /// </summary>
    public static string Format(Money amount)
    {
        var exponent = amount.Currency.MinorUnitExponent;

        if (exponent != 2)
        {
            // Every currency Waymark supports is exponent 2, and storage is
            // fixed at 1/100. A currency with three decimals cannot be
            // represented at that scale, and one with none would have to round
            // at display, which needs a rounding policy and therefore a
            // decision — not a default chosen inside a printer driver.
            throw new NotSupportedException(
                $"{amount.Currency.Code} has minor-unit exponent {exponent}. Storage is fixed at "
                + "1/100 (D-031), so printing it needs a decision about display rounding first.");
        }

        // French, as the shop reads it (D-125): "1 836,56". A space between thousands, a comma
        // before the centimes. Built from the integer, never through a culture the till's
        // Windows could change.
        var units = Math.Abs(amount.MinorUnits / Currency.StorageScale);
        var fraction = Math.Abs(amount.MinorUnits % Currency.StorageScale);
        var sign = amount.MinorUnits < 0 ? "-" : string.Empty;
        var digits = units.ToString(CultureInfo.InvariantCulture);
        var grouped = new StringBuilder(digits.Length + (digits.Length / 3));
        for (var index = 0; index < digits.Length; index++)
        {
            if (index > 0 && (digits.Length - index) % 3 == 0)
            {
                grouped.Append(' ');
            }

            grouped.Append(digits[index]);
        }

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{grouped},{fraction.ToString("D2", CultureInfo.InvariantCulture)}");
    }

    /// <summary>The amount and what it is counted in: "1 836,56 DA". The dinar as the shop writes it; any other currency by its code.</summary>
    public static string FormatWithCurrency(Money amount) =>
        $"{Format(amount)} {(amount.Currency.Code == "DZD" ? "DA" : amount.Currency.Code)}";

    /// <summary>
    /// French as ASCII, until a code page is proven on paper (K1): an accent dropped, "é" to "e",
    /// "°" to "o", a ligature spelt out. What has no ASCII form, Arabic among it, is left for
    /// <see cref="Write"/> to print as '?', one for one.
    /// </summary>
    internal static string Fold(string text)
    {
        // A table, not Unicode decomposition: the hosts run with invariant globalization, where
        // string.Normalize leaves an accented letter as it is.
        var folded = new StringBuilder(text.Length);
        foreach (var character in text)
        {
            folded.Append(character switch
            {
                'à' or 'á' or 'â' or 'ã' or 'ä' or 'å' => "a",
                'À' or 'Á' or 'Â' or 'Ã' or 'Ä' or 'Å' => "A",
                'ç' => "c",
                'Ç' => "C",
                'è' or 'é' or 'ê' or 'ë' => "e",
                'È' or 'É' or 'Ê' or 'Ë' => "E",
                'ì' or 'í' or 'î' or 'ï' => "i",
                'Ì' or 'Í' or 'Î' or 'Ï' => "I",
                'ñ' => "n",
                'Ñ' => "N",
                'ò' or 'ó' or 'ô' or 'õ' or 'ö' => "o",
                'Ò' or 'Ó' or 'Ô' or 'Õ' or 'Ö' => "O",
                'ù' or 'ú' or 'û' or 'ü' => "u",
                'Ù' or 'Ú' or 'Û' or 'Ü' => "U",
                'ý' or 'ÿ' => "y",
                'Ý' or 'Ÿ' => "Y",
                'œ' => "oe",
                'Œ' => "OE",
                'æ' => "ae",
                'Æ' => "AE",
                '°' or 'º' => "o",
                '\u2019' or '\u2018' => "'",
                '«' or '»' or '\u201C' or '\u201D' => "\"",
                '\u00A0' or '\u202F' => " ",
                _ => character.ToString(),
            });
        }

        return folded.ToString();
    }

    private static byte AlignmentByte(TextAlignment alignment) => alignment switch
    {
        TextAlignment.Left => 0,
        TextAlignment.Centre => 1,
        TextAlignment.Right => 2,
        _ => throw new NotSupportedException($"Unknown alignment {alignment}."),
    };

    /// <summary>
    /// Wraps at word boundaries, breaking a word only when it is longer than the
    /// paper. A printer does not wrap; it prints past the edge and the rest is
    /// gone.
    /// </summary>
    internal static IEnumerable<string> Wrap(string text, int width)
    {
        if (text.Length == 0)
        {
            yield return string.Empty;
            yield break;
        }

        var line = new StringBuilder(width);

        foreach (var word in text.Split(' ', StringSplitOptions.RemoveEmptyEntries))
        {
            var candidate = word;

            while (candidate.Length > width)
            {
                if (line.Length > 0)
                {
                    yield return line.ToString();
                    line.Clear();
                }

                yield return candidate[..width];
                candidate = candidate[width..];
            }

            if (line.Length == 0)
            {
                line.Append(candidate);
            }
            else if (line.Length + 1 + candidate.Length <= width)
            {
                line.Append(' ').Append(candidate);
            }
            else
            {
                yield return line.ToString();
                line.Clear().Append(candidate);
            }
        }

        if (line.Length > 0)
        {
            yield return line.ToString();
        }
    }

    private static void Write(List<byte> bytes, string text)
    {
        // Printable ASCII, and '?' for anything else, one for one so the layout
        // already computed still holds. Accented French and Arabic both need a
        // code page the profile selects and a font the printer has, and Arabic
        // additionally needs shaping and right-to-left ordering that ESC/POS does
        // not do. That is a real gap, recorded rather than papered over — see
        // decisions.md D-052.
        //
        // Control characters are replaced too, not passed through as ASCII would:
        // this text is product and customer names from the store's data, and an
        // ESC or GS inside one is a printer command. A name could otherwise open
        // the drawer with no reason recorded (Phase 0 final test).
        foreach (var character in text)
        {
            bytes.Add(character is >= ' ' and < '\u007F' ? (byte)character : (byte)'?');
        }

        bytes.Add(EscPos.LineFeed);
    }
}
