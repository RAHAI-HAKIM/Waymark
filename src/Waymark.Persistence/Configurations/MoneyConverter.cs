using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Configurations;

/// <summary>
/// <see cref="Money"/> to the <c>INTEGER</c> count of minor units the schema
/// stores, and back.
///
/// <para>
/// Storing is lossless and obvious — a <see cref="Money"/> already <i>is</i> a
/// signed count of minor units. Reading is where the currency has to come from
/// somewhere, because the column does not carry one: see
/// <see cref="Waymark.Domain.ILedgerCurrency"/> for why that is, and what it
/// costs.
/// </para>
/// <para>
/// Applied centrally in <c>WaymarkDbContext.OnModelCreating</c> to every
/// property of type <see cref="Money"/>, rather than named in each of the 58
/// configurations. One rule thirty-one times is a rule that will be missed
/// once, and the one it is missed on would store a money column unwrapped
/// without anything noticing.
/// </para>
/// </summary>
internal sealed class MoneyConverter : ValueConverter<Money, long>
{
    public MoneyConverter(Currency currency)
        : base(
            money => money.MinorUnits,
            minorUnits => Money.FromMinorUnits(minorUnits, currency))
    {
    }
}

/// <summary>
/// The same, for a nullable money column. EF needs the nullable pair spelled
/// out; it does not lift a converter over <c>Nullable&lt;T&gt;</c> on its own
/// when the property is declared nullable and the store type is not.
/// </summary>
internal sealed class NullableMoneyConverter : ValueConverter<Money?, long?>
{
    public NullableMoneyConverter(Currency currency)
        : base(
            money => money == null ? null : money.Value.MinorUnits,
            minorUnits => minorUnits == null ? null : Money.FromMinorUnits(minorUnits.Value, currency))
    {
    }
}
