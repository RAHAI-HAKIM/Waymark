namespace Waymark.Pos.Screen;

/// <summary>What the text typed in the search field is (session B1, D-088).</summary>
public enum FieldKind
{
    /// <summary>Nothing to act on yet: empty, or one letter.</summary>
    Nothing,

    /// <summary>A code typed by hand: a barcode, else a PLU. Digits, or anything with no letter.</summary>
    Code,

    /// <summary>A name to search: at least two characters, one of them a letter.</summary>
    Name,

    /// <summary>A ticket number, "S-2026-000142": a dash, and at least four digits at the end.</summary>
    Ticket,

    /// <summary>"3*": the next scan or touch adds this many units.</summary>
    Multiplier,
}

/// <summary>The field's text, read (<see cref="FieldInput.Read"/>).</summary>
/// <param name="Text">The text, trimmed; for a multiplier, the digits alone.</param>
/// <param name="Count">For a multiplier, the count; 1 otherwise.</param>
public sealed record FieldEntry(FieldKind Kind, string Text, int Count = 1);

/// <summary>
/// One field scans, takes a code, searches a name and opens a ticket (D-088): this decides which,
/// from the text alone, so the rule is tested without a window. A scan never comes here: the scanner
/// recognises its burst and hands it over as a code.
/// </summary>
public static class FieldInput
{
    public static FieldEntry Read(string? text)
    {
        var trimmed = (text ?? string.Empty).Trim();
        if (trimmed.Length == 0)
        {
            return new FieldEntry(FieldKind.Nothing, trimmed);
        }

        // "3*": a count and a star, nothing else. "*3" too: in the right-to-left field the star is read
        // first (Hakim, 26/09). A count the stepper would refuse is no multiplier.
        if (trimmed.EndsWith('*') || trimmed.StartsWith('*'))
        {
            var digits = trimmed.Trim('*').Trim();
            return QuantityEntry.TryParse(digits, out var count)
                ? new FieldEntry(FieldKind.Multiplier, digits, count)
                : new FieldEntry(FieldKind.Nothing, trimmed);
        }

        // Invoice numbers are the store's code, a dash, and a sequence: "S-2026-000142",
        // "GDZ-001-2025-000001". A name with a dash has no run of four digits at its end.
        if (trimmed.Contains('-', StringComparison.Ordinal) && !trimmed.Contains(' ', StringComparison.Ordinal)
            && trimmed.Length >= 5 && trimmed[^4..].All(char.IsAsciiDigit))
        {
            return new FieldEntry(FieldKind.Ticket, trimmed);
        }

        if (trimmed.Any(char.IsLetter))
        {
            return trimmed.Length >= Domain.Catalogue.NameSearch.MinimumLength
                ? new FieldEntry(FieldKind.Name, trimmed)
                : new FieldEntry(FieldKind.Nothing, trimmed);
        }

        return new FieldEntry(FieldKind.Code, trimmed);
    }
}
