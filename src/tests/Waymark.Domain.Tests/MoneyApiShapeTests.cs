using System.Reflection;
using Waymark.Domain.Values;

namespace Waymark.Domain.Tests;

/// <summary>
/// The shape of the API is itself a rule, so it is asserted rather than
/// reviewed.
///
/// <para>
/// CLAUDE.md §3.1 says money is never a float and that no arithmetic which can
/// round exists as an operator. Both are the kind of rule that survives while
/// someone remembers it and dies quietly the first time it is convenient —
/// adding <c>operator *(Money, decimal)</c> would make every call site read
/// perfectly and destroy the ability to enumerate where rounding happens
/// (decisions.md D-031).
/// </para>
/// </summary>
public sealed class MoneyApiShapeTests
{
    private static readonly Type[] Forbidden = [typeof(double), typeof(float), typeof(decimal)];

    private static IEnumerable<MethodInfo> OperatorsNamed(string name) =>
        typeof(Money)
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.IsSpecialName && method.Name == name);

    [Fact]
    public void Multiplication_is_only_ever_by_a_whole_number()
    {
        var signatures = OperatorsNamed("op_Multiply")
            .Select(method => string.Join(", ", method.GetParameters().Select(p => p.ParameterType.Name)))
            .Order(StringComparer.Ordinal)
            .ToList();

        Assert.Equal(["Int32, Money", "Money, Int32"], signatures);
    }

    [Fact]
    public void There_is_no_division_operator()
    {
        // Division always rounds, so it always needs a policy, so it cannot be
        // an operator. DivideBy and TryDivideBy are the only ways in.
        Assert.Empty(OperatorsNamed("op_Division"));
    }

    [Fact]
    public void There_is_no_conversion_to_or_from_a_number()
    {
        // An implicit conversion would let a bare long be added to a Money and
        // silently acquire whatever currency was nearby.
        Assert.Empty(OperatorsNamed("op_Implicit"));
        Assert.Empty(OperatorsNamed("op_Explicit"));
    }

    [Fact]
    public void No_floating_point_or_decimal_appears_anywhere_in_the_value_types()
    {
        // Checked across the whole namespace rather than Money alone, so the
        // rule still holds when Quantity and QuantityDelta land beside it.
        var offenders = new List<string>();

        var valueTypes = typeof(Money).Assembly
            .GetExportedTypes()
            .Where(type => type.Namespace == typeof(Money).Namespace)
            .OrderBy(type => type.Name, StringComparer.Ordinal);

        foreach (var type in valueTypes)
        {
            foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.Static))
            {
                if (Forbidden.Contains(method.ReturnType))
                {
                    offenders.Add($"{type.Name}.{method.Name} returns {method.ReturnType.Name}");
                }

                foreach (var parameter in method.GetParameters().Where(p => Forbidden.Contains(p.ParameterType)))
                {
                    offenders.Add($"{type.Name}.{method.Name} takes a {parameter.ParameterType.Name}");
                }
            }

            foreach (var constructor in type.GetConstructors())
            {
                foreach (var parameter in constructor.GetParameters().Where(p => Forbidden.Contains(p.ParameterType)))
                {
                    offenders.Add($"{type.Name}'s constructor takes a {parameter.ParameterType.Name}");
                }
            }
        }

        Assert.True(
            offenders.Count == 0,
            "Money is never a float (CLAUDE.md §3.1). These members let one in:\n  "
            + string.Join("\n  ", offenders));
    }

    [Fact]
    public void Every_operation_that_can_round_asks_which_policy_to_use()
    {
        // The claim behind "grep for these three names and you have every place
        // a centime can be created or destroyed". If a rounding method ever
        // appears without a Rounding parameter, it has a hidden default.
        string[] roundingMethods = ["Times", "Percent", "DivideBy", "TryDivideBy", "SplitTaxInclusive", "AddTaxExclusive"];

        foreach (var name in roundingMethods)
        {
            var overloads = typeof(Money)
                .GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(method => method.Name == name)
                .ToList();

            Assert.NotEmpty(overloads);

            foreach (var overload in overloads)
            {
                Assert.Contains(overload.GetParameters(), p => p.ParameterType == typeof(Rounding));
                Assert.DoesNotContain(overload.GetParameters(), p => p.ParameterType == typeof(Rounding) && p.HasDefaultValue);
            }
        }
    }

    [Fact]
    public void Allocation_takes_no_policy_because_it_cannot_round()
    {
        var overloads = typeof(Money)
            .GetMethods(BindingFlags.Public | BindingFlags.Instance)
            .Where(method => method.Name == nameof(Money.Allocate))
            .ToList();

        Assert.Equal(2, overloads.Count);

        foreach (var overload in overloads)
        {
            Assert.DoesNotContain(overload.GetParameters(), p => p.ParameterType == typeof(Rounding));
        }
    }

    [Fact]
    public void The_rounding_policy_offers_only_the_two_the_retailer_may_choose()
    {
        // Truncation is deliberately absent: biased downward on every line
        // without exception, so its drift never cancels (decisions.md D-032).
        Assert.Equal(
            ["HalfEven", "HalfUp"],
            Enum.GetNames<Rounding>().Order(StringComparer.Ordinal).ToArray());
    }
}
