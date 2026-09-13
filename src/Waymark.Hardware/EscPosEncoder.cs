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

        // Double height halves how much fits, and the printer does not wrap for
        // you — it prints off the edge of the paper.
        var width = line.DoubleHeight ? _profile.Columns / 2 : _profile.Columns;

        foreach (var wrapped in Wrap(line.Text, width))
        {
            Write(bytes, wrapped);
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

    private void EncodeAmount(AmountLine line, List<byte> bytes)
    {
        var amount = Format(line.Amount);

        // The figure is never what gets cut. A truncated label is a receipt
        // somebody can still read; a truncated total is a receipt that disagrees
        // with the till, and the customer is holding it.
        var room = _profile.Columns - amount.Length - 1;
        var label = room <= 0
            ? string.Empty
            : line.Label.Length <= room ? line.Label : line.Label[..room];

        var gap = _profile.Columns - label.Length - amount.Length;
        var text = label + new string(' ', Math.Max(gap, 1)) + amount;

        bytes.AddRange(EscPos.Align(0));

        if (line.Emphasised)
        {
            bytes.AddRange(EscPos.Emphasis(true));
        }

        Write(bytes, text);

        if (line.Emphasised)
        {
            bytes.AddRange(EscPos.Emphasis(false));
        }
    }

    private static void EncodeBarcode(BarcodeLine line, List<byte> bytes)
    {
        var (symbology, data) = line.Symbology switch
        {
            BarcodeSymbology.Code39 => (EscPos.Code39, ValidateCode39(line.Data)),
            BarcodeSymbology.Ean13 => (EscPos.Ean13, ValidateEan13(line.Data)),
            _ => throw new NotSupportedException($"Unknown symbology {line.Symbology}."),
        };

        bytes.AddRange(EscPos.Align(1));
        bytes.AddRange(EscPos.BarcodeHeight(64));
        bytes.AddRange(EscPos.BarcodeWidth(2));
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
    internal static string Format(Money amount)
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

        var units = amount.MinorUnits / Currency.StorageScale;
        var fraction = Math.Abs(amount.MinorUnits % Currency.StorageScale);
        var sign = amount.MinorUnits < 0 && units == 0 ? "-" : string.Empty;

        return string.Create(
            CultureInfo.InvariantCulture,
            $"{sign}{units}.{fraction.ToString("D2", CultureInfo.InvariantCulture)}");
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
        // ASCII with '?' for anything else. Accented French and Arabic both need
        // a code page the profile selects and a font the printer has, and Arabic
        // additionally needs shaping and right-to-left ordering that ESC/POS does
        // not do. That is a real gap, recorded rather than papered over — see
        // decisions.md D-052.
        bytes.AddRange(Encoding.ASCII.GetBytes(text));
        bytes.Add(EscPos.LineFeed);
    }
}
