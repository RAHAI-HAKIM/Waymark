using System.Reflection;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// The operator set <i>is</i> the specification (decisions.md D-036), so it is
/// asserted rather than reviewed.
///
/// <para>
/// The rule that matters most here cannot be tested by calling anything:
/// <c>Quantity + Quantity</c> does not compile. An absence is invisible to an
/// ordinary test and would come back the first time someone found it
/// convenient, so it is checked by reflection instead.
/// </para>
/// </summary>
public sealed class QuantityApiShapeTests
{
    private static List<string> OperatorSignatures(Type type, string name) =>
        [.. type
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.IsSpecialName && method.Name == name)
            .Select(method =>
                string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name))
                + " -> " + method.ReturnType.Name)
            .Order(StringComparer.Ordinal)];

    [Fact]
    public void A_level_can_only_be_added_to_a_change()
    {
        // Quantity + Quantity is deliberately missing. Adding two levels is
        // meaningless, and the mistake is invisible because both are plausible
        // integers — which is exactly why the compiler is doing this job and
        // not a test.
        Assert.Equal(
            [
                "Quantity, QuantityDelta -> Quantity",
                "QuantityDelta, Quantity -> Quantity",
            ],
            OperatorSignatures(typeof(Quantity), "op_Addition"));
    }

    [Fact]
    public void Subtracting_two_levels_gives_a_change_not_a_level()
    {
        // The return type is the interesting half. If this ever returns
        // Quantity, the reconciliation algebra silently stops type-checking.
        Assert.Equal(
            [
                "Quantity, Quantity -> QuantityDelta",
                "Quantity, QuantityDelta -> Quantity",
            ],
            OperatorSignatures(typeof(Quantity), "op_Subtraction"));
    }

    [Fact]
    public void Changes_only_combine_with_changes()
    {
        Assert.Equal(
            ["QuantityDelta, QuantityDelta -> QuantityDelta"],
            OperatorSignatures(typeof(QuantityDelta), "op_Addition"));

        Assert.Equal(
            ["QuantityDelta, QuantityDelta -> QuantityDelta"],
            OperatorSignatures(typeof(QuantityDelta), "op_Subtraction"));

        // The reversing movement. A level has no unary minus, because the
        // opposite of a level is not a thing anyone means.
        Assert.Equal(
            ["QuantityDelta -> QuantityDelta"],
            OperatorSignatures(typeof(QuantityDelta), "op_UnaryNegation"));

        Assert.Empty(OperatorSignatures(typeof(Quantity), "op_UnaryNegation"));
    }

    [Fact]
    public void There_is_no_conversion_between_a_level_and_a_change()
    {
        // An implicit conversion would quietly restore every operator the two
        // types exist to keep apart, including Quantity + Quantity.
        Assert.Empty(OperatorSignatures(typeof(Quantity), "op_Implicit"));
        Assert.Empty(OperatorSignatures(typeof(Quantity), "op_Explicit"));
        Assert.Empty(OperatorSignatures(typeof(QuantityDelta), "op_Implicit"));
        Assert.Empty(OperatorSignatures(typeof(QuantityDelta), "op_Explicit"));
    }

    [Fact]
    public void Multiplication_is_only_ever_by_a_whole_number()
    {
        Assert.Equal(
            ["Int32, Quantity -> Quantity", "Quantity, Int32 -> Quantity"],
            OperatorSignatures(typeof(Quantity), "op_Multiply"));

        Assert.Equal(
            ["Int32, QuantityDelta -> QuantityDelta", "QuantityDelta, Int32 -> QuantityDelta"],
            OperatorSignatures(typeof(QuantityDelta), "op_Multiply"));
    }

    [Fact]
    public void There_is_no_division_of_a_quantity()
    {
        // Dividing a quantity rounds, so it would need a policy, so it cannot
        // be an operator — the same rule Money follows. Nothing in Phase 0
        // divides a quantity, so nothing exists yet.
        Assert.Empty(OperatorSignatures(typeof(Quantity), "op_Division"));
        Assert.Empty(OperatorSignatures(typeof(QuantityDelta), "op_Division"));
    }

    [Fact]
    public void Nothing_in_quantity_arithmetic_takes_a_rounding_policy()
    {
        // Because none of it rounds. Every operation is exact integer
        // arithmetic, and a Rounding parameter appearing here would mean
        // something had started losing thousandths.
        foreach (var type in new[] { typeof(Quantity), typeof(QuantityDelta), typeof(UnitPrecision) })
        {
            var rounding = type
                .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static)
                .Where(method => method.GetParameters().Any(p => p.ParameterType == typeof(Rounding)))
                .Select(method => $"{type.Name}.{method.Name}")
                .ToList();

            Assert.True(
                rounding.Count == 0,
                "Quantity arithmetic is exact. These take a rounding policy:\n  "
                + string.Join("\n  ", rounding));
        }
    }
}
