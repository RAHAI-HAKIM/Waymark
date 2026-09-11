using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// Units are carried, never assumed. Adding a kilogram to five hundred grams is
/// the Mars Climate Orbiter bug, and it costs nothing to make impossible.
/// </summary>
public sealed class QuantityUnitTests
{
    private static Quantity Kg(long thousandths) => Quantity.FromThousandths(thousandths, "kg");

    private static Quantity Grams(long thousandths) => Quantity.FromThousandths(thousandths, "g");

    [Fact]
    public void Mixing_units_throws_rather_than_adding_the_numbers()
    {
        Assert.Throws<InvalidOperationException>(
            () => { _ = Kg(1_000) + QuantityDelta.FromThousandths(500, "g"); });

        Assert.Throws<InvalidOperationException>(() => { _ = Kg(1_000) - Grams(500); });

        Assert.Throws<InvalidOperationException>(
            () => { _ = QuantityDelta.FromThousandths(1, "kg") + QuantityDelta.FromThousandths(1, "g"); });

        Assert.Throws<InvalidOperationException>(() => { _ = Quantity.Sum("kg", [Kg(1), Grams(1)]); });
    }

    [Fact]
    public void Ordering_across_units_throws_because_the_question_has_no_answer()
    {
        Assert.Throws<InvalidOperationException>(() => { _ = Kg(1) < Grams(1); });
        Assert.Throws<InvalidOperationException>(() => { _ = Kg(1).CompareTo(Grams(1)); });
    }

    [Fact]
    public void Equality_across_units_answers_false_instead_of_throwing()
    {
        Assert.NotEqual(Kg(1_000), Grams(1_000));
        Assert.False(Kg(1_000) == Grams(1_000));

        var byQuantity = new Dictionary<Quantity, string>
        {
            [Kg(1_000)] = "a kilo",
            [Grams(1_000)] = "a gram",
        };

        Assert.Equal("a kilo", byQuantity[Kg(1_000)]);
        Assert.Equal("a gram", byQuantity[Grams(1_000)]);
    }

    [Fact]
    public void Unit_codes_are_matched_exactly_as_they_are_stored()
    {
        // SQLite compares TEXT case-sensitively by default, so "KG" and "kg"
        // are two different rows in units_of_measure and must not silently
        // merge here.
        Assert.Throws<InvalidOperationException>(
            () => { _ = Quantity.FromThousandths(1, "kg") - Quantity.FromThousandths(1, "KG"); });
    }

    [Fact]
    public void A_quantity_with_no_unit_cannot_be_built_or_used()
    {
        Assert.Throws<ArgumentException>(() => { _ = Quantity.FromThousandths(1, ""); });
        Assert.Throws<ArgumentException>(() => { _ = Quantity.FromThousandths(1, "   "); });
        Assert.Throws<ArgumentException>(() => { _ = QuantityDelta.FromThousandths(1, ""); });

        var undefined = default(Quantity);

        Assert.False(undefined.HasUnit);
        Assert.Throws<InvalidOperationException>(
            () => { _ = undefined + QuantityDelta.FromThousandths(1, "kg"); });
        Assert.Throws<InvalidOperationException>(() => { _ = undefined.Abs(); });
        Assert.Throws<InvalidOperationException>(() => { _ = default(QuantityDelta).Magnitude; });

        // Still equal to itself, so collections do not break.
        Assert.Equal(default(Quantity), undefined);
        Assert.NotEqual(Quantity.Zero("kg"), undefined);
    }

    [Fact]
    public void A_unit_sold_whole_refuses_a_fraction_at_construction()
    {
        // units_of_measure.decimal_places is 0 for a piece. A till that accepts
        // 1,5 of something sold whole prints a receipt nobody can fill.
        var pieces = UnitPrecision.For("piece", decimalPlaces: 0);

        Assert.Equal(3_000, pieces.Whole(3).Thousandths);
        Assert.Equal(3_000, pieces.Quantity(3_000).Thousandths);

        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = pieces.Quantity(1_500); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = pieces.Delta(-1_500); });
    }

    [Theory]
    [InlineData(0, 1_000)]   // whole units only
    [InlineData(1, 100)]
    [InlineData(2, 10)]
    [InlineData(3, 1)]       // to the gram
    public void The_step_a_unit_admits_follows_its_decimal_places(int decimalPlaces, long step)
    {
        var unit = UnitPrecision.For("u", decimalPlaces);

        Assert.Equal(step, unit.StepThousandths);
        Assert.True(unit.Allows(step));
        Assert.True(unit.Allows(step * 7));
        Assert.True(unit.Allows(-step * 7));
        Assert.True(unit.Allows(0));

        if (step > 1)
        {
            Assert.False(unit.Allows(step - 1));
        }
    }

    [Fact]
    public void A_unit_measured_to_the_gram_accepts_anything()
    {
        var kilos = UnitPrecision.For("kg", decimalPlaces: 3);

        Assert.Equal(1_234, kilos.Quantity(1_234).Thousandths);
        Assert.Equal(-1, kilos.Delta(-1).Thousandths);
    }

    [Fact]
    public void The_precision_bound_is_the_one_the_schema_already_enforces()
    {
        // CHECK (decimal_places BETWEEN 0 AND 3), which is also the limit of
        // storing quantities in thousandths.
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = UnitPrecision.For("u", -1); });
        Assert.Throws<ArgumentOutOfRangeException>(() => { _ = UnitPrecision.For("u", 4); });
        Assert.Throws<ArgumentException>(() => { _ = UnitPrecision.For("", 0); });
        Assert.Equal(3, UnitPrecision.MaxDecimalPlaces);
        Assert.Equal(1_000, Quantity.Scale);
    }

    [Fact]
    public void FitsIn_reports_whether_a_value_belongs_to_a_unit()
    {
        var pieces = UnitPrecision.For("piece", decimalPlaces: 0);

        Assert.True(Quantity.FromThousandths(2_000, "piece").FitsIn(pieces));
        Assert.False(Quantity.FromThousandths(2_500, "piece").FitsIn(pieces));

        // A value in a different unit does not fit, whatever its precision.
        Assert.False(Kg(2_000).FitsIn(pieces));
        Assert.True(QuantityDelta.FromThousandths(-2_000, "piece").FitsIn(pieces));
    }

    [Fact]
    public void A_default_precision_reads_as_undefined_and_refuses_to_build_anything()
    {
        Assert.Equal("(none)", default(UnitPrecision).Code);
        Assert.False(default(UnitPrecision).IsDefined);
        Assert.Throws<InvalidOperationException>(() => { _ = default(UnitPrecision).Quantity(1_000); });
    }
}
