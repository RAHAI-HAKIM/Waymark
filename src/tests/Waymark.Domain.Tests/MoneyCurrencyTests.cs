using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Money carries its currency and refuses to mix (decisions.md D-035). Today
/// every store is DZD; the point is that the day a EUR supplier price meets a
/// DZD total, it is an exception rather than a plausible number.
/// </summary>
public sealed class MoneyCurrencyTests
{
    private static Money Dzd(long minorUnits) => new(minorUnits, Currency.Dzd);

    private static Money Eur(long minorUnits) => new(minorUnits, Currency.Eur);

    [Fact]
    public void Adding_two_currencies_throws_rather_than_converting()
    {
        // There is no implicit conversion anywhere, ever. A conversion is an
        // explicit event carrying the rate it used and the date it used.
        Assert.Throws<InvalidOperationException>(() => { _ = Dzd(100) + Eur(100); });
        Assert.Throws<InvalidOperationException>(() => { _ = Dzd(100) - Eur(100); });
    }

    [Fact]
    public void Ordering_two_currencies_throws_because_the_question_has_no_answer()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = Dzd(100) < Eur(100); });
        Assert.Throws<InvalidOperationException>(() => { _ = Dzd(100).CompareTo(Eur(100)); });
    }

    [Fact]
    public void Equality_answers_false_instead_of_throwing()
    {
        // Deliberately different from ordering: "are these the same value" has
        // an answer for 100 DZD and 100 EUR, and equality that threw would make
        // Money unusable as a dictionary key.
        Assert.NotEqual(Dzd(100), Eur(100));
        Assert.False(Dzd(100) == Eur(100));
        Assert.True(Dzd(100) != Eur(100));

        var byAmount = new Dictionary<Money, string>
        {
            [Dzd(100)] = "dinars",
            [Eur(100)] = "euros",
        };

        Assert.Equal("dinars", byAmount[Dzd(100)]);
        Assert.Equal("euros", byAmount[Eur(100)]);
    }

    [Fact]
    public void An_amount_with_no_currency_cannot_be_built_or_used()
    {
        Assert.Throws<ArgumentException>(() => { _ = new Money(100, default); });

        // default(Money) exists because it is a struct, so it must fail at use.
        var undefined = default(Money);

        Assert.False(undefined.Currency.IsDefined);
        Assert.Throws<InvalidOperationException>(() => { _ = undefined + Dzd(1); });
        Assert.Throws<InvalidOperationException>(() => { _ = Dzd(1) + undefined; });
        Assert.Throws<InvalidOperationException>(() => { _ = undefined.Times(1, 2, Rounding.HalfEven); });
        Assert.Throws<InvalidOperationException>(() => { _ = undefined.ToCashTender(); });
        Assert.Throws<InvalidOperationException>(() => { _ = undefined.Allocate(2); });

        // It still compares equal to itself, so collections do not break.
        Assert.Equal(default(Money), undefined);
        Assert.NotEqual(Money.Zero(Currency.Dzd), undefined);
    }

    [Fact]
    public void There_is_no_currency_less_zero()
    {
        // Money.Zero takes a currency, so an accumulator has to name one. A
        // currency-less zero would be a hole for a mismatch to slip through.
        Assert.Equal(Dzd(0), Money.Zero(Currency.Dzd));
        Assert.NotEqual(Money.Zero(Currency.Eur), Money.Zero(Currency.Dzd));
    }

    [Fact]
    public void Codes_resolve_case_insensitively_and_unknown_ones_are_refused()
    {
        Assert.Equal(Currency.Dzd, Currency.FromCode("DZD"));
        Assert.Equal(Currency.Dzd, Currency.FromCode("dzd"));
        Assert.Throws<ArgumentException>(() => { _ = Currency.FromCode("XYZ"); });

        Assert.True(Currency.TryFromCode("EUR", out var eur));
        Assert.Equal(Currency.Eur, eur);
        Assert.False(Currency.TryFromCode("XYZ", out var unknown));
        Assert.False(unknown.IsDefined);
    }

    [Fact]
    public void Every_supported_currency_fits_the_storage_scale()
    {
        // The schema's "scale 100" is a DZD fact, not a universal one. A
        // currency with three decimal places cannot be represented at 1/100 of
        // a unit, so the registry refuses to hold one and this is what says so.
        Assert.All(Currency.Supported, currency =>
            Assert.InRange(currency.MinorUnitExponent, 0, 2));

        Assert.Equal(100, Currency.StorageScale);
    }

    [Fact]
    public void The_default_currency_reads_as_undefined_rather_than_throwing()
    {
        // A property that threw would trip over a debugger watch window or a
        // log line, which is a poor place to discover an uninitialised value.
        Assert.Equal("(none)", default(Currency).Code);
        Assert.False(default(Currency).IsDefined);
        Assert.True(Currency.Dzd.IsDefined);
    }
}
