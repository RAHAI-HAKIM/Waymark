namespace Waymark.Domain.Privacy;

/// <summary>
/// A subject identified the only way the processing log is allowed to identify
/// one.
///
/// <para>
/// <b>It cannot be constructed outside <c>Waymark.Pseudonymisation</c>.</b> The
/// constructor is internal and that project is the only one Domain grants
/// <c>InternalsVisibleTo</c>. A call site that wants to log has to cross the
/// pseudonymisation boundary to obtain one, so the compiler enforces what review
/// otherwise would — which is what CLAUDE.md §4's "structural rather than
/// remembered" means in practice (decisions.md D-045).
/// </para>
/// <para>
/// There is no implicit conversion, no <c>string</c> overload and no public
/// parse. <c>ProcessingLogContractTests</c> fails if any of those appear, the
/// same device that keeps <c>operator *(Money, decimal)</c> from coming back.
/// </para>
/// <para>
/// <b>Why this type lives in Domain rather than beside the key.</b>
/// <c>Waymark.Application</c> has to name it in the log helper's signature, and
/// Application may not reference <c>Waymark.Pseudonymisation</c> — that missing
/// edge is a legal boundary enforced by an architecture test (§2.1), and it
/// matters more since D-039 put the tenant key in that project. Declaring the
/// type here and withholding its constructor gives the same guarantee without
/// putting the key's project on Application's dependency graph.
/// </para>
/// </summary>
public readonly struct Pseudonym : IEquatable<Pseudonym>
{
    private readonly string? _value;

    /// <summary>
    /// Wraps a computed pseudonym. Visible only to
    /// <c>Waymark.Pseudonymisation</c>, which is the point.
    /// </summary>
    internal Pseudonym(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            throw new ArgumentException("A pseudonym is required.", nameof(value));
        }

        _value = value;
    }

    /// <summary>
    /// The 26-character Crockford base32 text (D-039), or empty for
    /// <c>default(Pseudonym)</c>. Does not throw: an uninitialised value fails
    /// where it is used, not where it is read or logged.
    /// </summary>
    public string Value => _value ?? string.Empty;

    /// <summary>False only for <c>default(Pseudonym)</c>.</summary>
    public bool IsDefined => _value is not null;

    public bool Equals(Pseudonym other) =>
        string.Equals(_value, other._value, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Pseudonym other && Equals(other);

    public override int GetHashCode() =>
        _value is null ? 0 : StringComparer.Ordinal.GetHashCode(_value);

    public static bool operator ==(Pseudonym left, Pseudonym right) => left.Equals(right);

    public static bool operator !=(Pseudonym left, Pseudonym right) => !left.Equals(right);

    /// <summary>
    /// The pseudonym itself. Safe to print: it is what the log stores, and it
    /// names nobody without the tenant key.
    /// </summary>
    public override string ToString() => Value;
}
