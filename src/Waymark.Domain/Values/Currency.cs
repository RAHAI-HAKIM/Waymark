namespace Waymark.Domain.Values;

/// <summary>
/// A currency, and the two local facts that go with it.
///
/// <para>
/// <b>The storage scale is not the currency's minor unit.</b> One Waymark minor
/// unit is 1/100 of a currency unit, for every currency, because an EF value
/// converter sees one property in isolation and cannot read the row's
/// <c>currency</c> column to learn an exponent. Fixing the scale keeps the
/// converter a pure function (decisions.md D-031).
/// </para>
/// <para>
/// <see cref="MinorUnitExponent"/> is the currency's own exponent and governs
/// display, not storage. Carrying it now is cheap insurance: the schema's
/// "scale 100" is a DZD fact, and if a currency with a different minor unit
/// ever appears, every INTEGER money column changes meaning at once with no
/// migration able to say which rows were which (D-035). An exponent above 2 is
/// rejected rather than silently truncated, so that day arrives loudly.
/// </para>
/// <para>
/// These are ISO facts and local practice facts, identical for every store, so
/// they are code rather than a <c>currencies</c> table. A table would cost a
/// migration, foreign keys from eight tables, and seed data, to hold values
/// that never vary per store.
/// </para>
/// </summary>
public readonly struct Currency : IEquatable<Currency>
{
    /// <summary>
    /// Waymark minor units per currency unit. Always 100 — see the remarks on
    /// <see cref="Currency"/> for why this is not <see cref="MinorUnitExponent"/>.
    /// </summary>
    public const int StorageScale = 100;

    private const string UndefinedCode = "(none)";

    private readonly string? _code;
    private readonly int _minorUnitExponent;
    private readonly long _cashRoundingStep;

    private Currency(string code, int minorUnitExponent, long cashRoundingStep)
    {
        _code = code;
        _minorUnitExponent = minorUnitExponent;
        _cashRoundingStep = cashRoundingStep;
    }

    /// <summary>
    /// Algerian dinar. The cash step is 5,00 DZD — 500 minor units — because
    /// the smallest coin in practical circulation is five dinars (D-034).
    /// </summary>
    public static Currency Dzd { get; } = Define("DZD", minorUnitExponent: 2, cashRoundingStep: 500);

    /// <summary>Euro. Defined for supplier documents; no cash rounding.</summary>
    public static Currency Eur { get; } = Define("EUR", minorUnitExponent: 2, cashRoundingStep: 1);

    /// <summary>US dollar. Defined for supplier documents; no cash rounding.</summary>
    public static Currency Usd { get; } = Define("USD", minorUnitExponent: 2, cashRoundingStep: 1);

    private static readonly Currency[] All = [Dzd, Eur, Usd];

    /// <summary>Every currency this build knows about.</summary>
    public static IReadOnlyList<Currency> Supported => All;

    /// <summary>
    /// The ISO 4217 code, or <c>(none)</c> for <c>default(Currency)</c>. This
    /// property does not throw; the arithmetic that would be meaningless
    /// throws instead, so a default value fails at use rather than at read.
    /// </summary>
    public string Code => _code ?? UndefinedCode;

    /// <summary>False for <c>default(Currency)</c>, true for every real one.</summary>
    public bool IsDefined => _code is not null;

    /// <summary>
    /// The currency's own minor-unit exponent — 2 for DZD, meaning centimes.
    /// Governs display. Not the storage scale; see <see cref="StorageScale"/>.
    /// </summary>
    public int MinorUnitExponent => _minorUnitExponent;

    /// <summary>
    /// The smallest amount that can actually change hands in cash, in Waymark
    /// minor units. 500 for DZD; 1 — meaning no rounding — everywhere else.
    /// This is why the number 500 never appears in a handler (D-034).
    /// </summary>
    public long CashRoundingStep => _cashRoundingStep;

    /// <summary>True when cash tender has to be rounded for this currency.</summary>
    public bool RoundsCash => _cashRoundingStep > 1;

    private static Currency Define(string code, int minorUnitExponent, long cashRoundingStep)
    {
        if (minorUnitExponent is < 0 or > 2)
        {
            throw new ArgumentOutOfRangeException(
                nameof(minorUnitExponent),
                minorUnitExponent,
                $"Waymark stores money at 1/{StorageScale} of a currency unit, so a currency "
                + "with an exponent above 2 cannot be represented.");
        }

        if (cashRoundingStep < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(cashRoundingStep), cashRoundingStep, "The cash step is at least 1 minor unit.");
        }

        return new Currency(code, minorUnitExponent, cashRoundingStep);
    }

    /// <summary>The supported currency with this ISO code.</summary>
    /// <exception cref="ArgumentException">The code is not supported.</exception>
    public static Currency FromCode(string code)
    {
        if (TryFromCode(code, out var currency))
        {
            return currency;
        }

        throw new ArgumentException(
            $"'{code}' is not a supported currency. Supported: {string.Join(", ", All.Select(c => c.Code))}.",
            nameof(code));
    }

    /// <summary>The supported currency with this ISO code, or false.</summary>
    public static bool TryFromCode(string code, out Currency currency)
    {
        foreach (var candidate in All)
        {
            if (string.Equals(candidate.Code, code, StringComparison.OrdinalIgnoreCase))
            {
                currency = candidate;
                return true;
            }
        }

        currency = default;
        return false;
    }

    public bool Equals(Currency other) =>
        string.Equals(_code, other._code, StringComparison.Ordinal);

    public override bool Equals(object? obj) => obj is Currency other && Equals(other);

    public override int GetHashCode() =>
        _code is null ? 0 : StringComparer.Ordinal.GetHashCode(_code);

    public static bool operator ==(Currency left, Currency right) => left.Equals(right);

    public static bool operator !=(Currency left, Currency right) => !left.Equals(right);

    public override string ToString() => Code;
}
