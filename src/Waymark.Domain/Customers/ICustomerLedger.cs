using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Values;

namespace Waymark.Domain.Customers;

/// <summary>
/// The customers and their tabs, as the till's commands read and change them (B7, D-096). Declared
/// here, implemented in Persistence. New rows are staged through <c>IStaging</c>; the one change to
/// an existing row, a customer's limit and freeze, goes through <see cref="StageTabAsync"/>.
/// </summary>
public interface ICustomerLedger
{
    /// <summary>Active customers with this number, in its one form (<see cref="PhoneNumber"/>), by name.</summary>
    Task<IReadOnlyList<Customer>> FindByPhoneAsync(string normalisedPhone, CancellationToken cancellationToken = default);

    /// <summary>The customer, if there is one and it is not erased.</summary>
    Task<Customer?> FindAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>Every movement of the customer's tab at this store, oldest first.</summary>
    Task<IReadOnlyList<ReceivableMovement>> MovementsAsync(string customerId, CancellationToken cancellationToken = default);

    /// <summary>The <c>notice_versions</c> row of this type in force on <paramref name="day"/>, the latest to take effect; null when none is.</summary>
    Task<string?> NoticeInForceAsync(NoticeType type, DateOnly day, CancellationToken cancellationToken = default);

    /// <summary>Stages the customer's limit and freeze. False when there is no such customer.</summary>
    Task<bool> StageTabAsync(string customerId, Money? limit, DateTimeOffset? frozenAt, DateTimeOffset at, CancellationToken cancellationToken = default);
}
