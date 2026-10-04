using System.Text;

namespace Waymark.Domain.Catalogue;

/// <summary>
/// Whether a product's name answers what the cashier typed, and how well (session B1, D-088).
///
/// <para>
/// <b>The rule:</b> every word typed must start a word of the name, case and accents ignored, so
/// "lait dem" finds "Lait UHT Candia Grand Lait Demi-écrémé" and "creme" finds "Crème". A name
/// that holds a typed word <b>whole</b> ranks above one that only starts with it: "lait" puts
/// "Lait Candia" before "Laitue". <b>Among names that answer equally well, the ones the first word
/// typed begins come first</b>: "lait" puts "Lait Candia" before "Biscuits au lait", since Entrée
/// sells the first result and it sold biscuits (block B review). Then the catalogue's order, by name.
/// </para>
/// <para>
/// Pure and in Domain, like <c>TvaRate</c>: the rule is argued with in a test that has no
/// database, and the Persistence read only hands it names.
/// </para>
/// </summary>
public static class NameSearch
{
    /// <summary>Fewer characters than this search nothing: one letter matches half the shop.</summary>
    public const int MinimumLength = 2;

    /// <summary>The most results a search answers with: a list the cashier can read at the counter.</summary>
    public const int MaximumResults = 20;

    /// <summary>
    /// The words of a text as the search compares them: lower case, accents taken off, and split at
    /// anything that is neither a letter nor a digit, so "Demi-écrémé" is "demi" and "ecreme".
    /// </summary>
    public static IReadOnlyList<string> Words(string? text)
    {
        var words = new List<string>();
        var word = new StringBuilder();
        foreach (var c in text ?? string.Empty)
        {
            var folded = Fold(c);
            if (folded is null)
            {
                Flush();
                continue;
            }

            word.Append(folded);
        }

        Flush();
        return words;

        void Flush()
        {
            if (word.Length > 0)
            {
                words.Add(word.ToString());
                word.Clear();
            }
        }
    }

    /// <summary>
    /// How well <paramref name="name"/> answers <paramref name="query"/>: null when it does not,
    /// otherwise the number of typed words it holds whole (higher is better).
    /// </summary>
    public static int? Score(string? query, string? name)
    {
        var typed = Words(query);
        if (typed.Count == 0 || typed.Sum(word => word.Length) < MinimumLength)
        {
            return null;
        }

        var words = Words(name);
        var whole = 0;
        foreach (var t in typed)
        {
            if (!words.Any(word => word.StartsWith(t, StringComparison.Ordinal)))
            {
                return null;
            }

            if (words.Contains(t, StringComparer.Ordinal))
            {
                whole++;
            }
        }

        return whole;
    }

    /// <summary>
    /// Whether the name begins with the first word typed: "lait" leads "Lait UHT Candia" and
    /// "Laitue", not "Biscuits au lait".
    /// </summary>
    public static bool Leads(string? query, string? name)
    {
        var typed = Words(query);
        var words = Words(name);
        return typed.Count > 0 && words.Count > 0 && words[0].StartsWith(typed[0], StringComparison.Ordinal);
    }

    /// <summary>
    /// The items whose name answers <paramref name="query"/>, best first, at most
    /// <see cref="MaximumResults"/>: by the typed words held whole, then the names the first typed
    /// word begins, then by name, then by the order given.
    /// </summary>
    public static IReadOnlyList<T> Rank<T>(string? query, IEnumerable<T> items, Func<T, string> name)
    {
        ArgumentNullException.ThrowIfNull(items);
        ArgumentNullException.ThrowIfNull(name);

        return [.. items
            .Select((item, index) => (Item: item, Index: index, Name: name(item), Score: Score(query, name(item))))
            .Where(hit => hit.Score is not null)
            .OrderByDescending(hit => hit.Score)
            .ThenByDescending(hit => Leads(query, hit.Name))
            .ThenBy(hit => hit.Name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(hit => hit.Index)
            .Take(MaximumResults)
            .Select(hit => hit.Item)];
    }

    /// <summary>
    /// One character as the search compares it: a lower-case letter without its accent, a digit, or
    /// null for a separator. A table rather than Unicode normalisation: the till runs with invariant
    /// globalisation (D-067), and the table says exactly which letters fold.
    /// </summary>
    private static string? Fold(char c)
    {
        if (c is >= 'a' and <= 'z' or >= '0' and <= '9')
        {
            return c.ToString();
        }

        if (c is >= 'A' and <= 'Z')
        {
            return ((char)(c + 32)).ToString();
        }

        return char.ToLowerInvariant(c) switch
        {
            'à' or 'â' or 'ä' or 'á' or 'ã' or 'å' => "a",
            'ç' => "c",
            'é' or 'è' or 'ê' or 'ë' => "e",
            'î' or 'ï' or 'í' or 'ì' => "i",
            'ô' or 'ö' or 'ó' or 'ò' or 'õ' => "o",
            'ù' or 'û' or 'ü' or 'ú' => "u",
            'ÿ' or 'ý' => "y",
            'ñ' => "n",
            'œ' => "oe",
            'æ' => "ae",
            var other when char.IsLetterOrDigit(other) => other.ToString(),
            _ => null,
        };
    }
}
