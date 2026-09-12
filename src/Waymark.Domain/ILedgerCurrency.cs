using Waymark.Domain.Values;

namespace Waymark.Domain;

/// <summary>
/// The currency this store's books are kept in.
///
/// <para>
/// Declared in Domain and implemented outside it, like <see cref="ICurrentStore"/>.
/// Set at commissioning and immutable: changing it would silently reinterpret
/// every historical row, so it is a migration event with a stated conversion,
/// never a settings toggle (decisions.md D-035).
/// </para>
/// <para>
/// <b>Why this exists at all.</b> Money columns are a bare <c>INTEGER</c> count
/// of minor units; the currency lives in a separate column, and on many tables
/// in no column at all. An EF value converter sees one property in isolation and
/// cannot read a sibling, so the currency it builds <see cref="Money"/> with has
/// to come from somewhere outside the row. This is that somewhere.
/// </para>
/// <para>
/// <b>The limitation this carries, named in D-035.</b> Four tables may
/// legitimately hold a document in another currency — <c>suppliers</c>,
/// <c>supplier_variant</c>, <c>purchase_orders</c>, <c>batch_items</c> — and
/// their rows would be read back as if they were in the ledger currency. Today
/// every row is DZD, and
/// <c>LedgerCurrencyTests.Every_currency_column_holds_the_ledger_currency</c>
/// fails the moment one is not. That failure is the start of the Stage 2 work,
/// not a bug to paper over.
/// </para>
/// </summary>
public interface ILedgerCurrency
{
    /// <summary>The store's currency. Never <c>default(Currency)</c>.</summary>
    Currency Currency { get; }
}

/// <summary>
/// A fixed answer, for a host that reads it from configuration and for tests.
/// </summary>
public sealed class FixedLedgerCurrency : ILedgerCurrency
{
    public FixedLedgerCurrency(Currency currency)
    {
        if (!currency.IsDefined)
        {
            throw new ArgumentException(
                "The ledger currency must be a real currency. Money cannot be read back without one.",
                nameof(currency));
        }

        Currency = currency;
    }

    /// <summary>Resolves an ISO code from configuration, defaulting to DZD.</summary>
    public static FixedLedgerCurrency FromCode(string? code) =>
        new(string.IsNullOrWhiteSpace(code) ? Currency.Dzd : Currency.FromCode(code));

    public Currency Currency { get; }
}
