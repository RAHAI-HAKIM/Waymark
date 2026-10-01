using Waymark.Domain.Values;

namespace Waymark.Domain.Organisation;

/// <summary>
/// What the retailer switched on, once, for every store of the tenant (B7, D-096), kept as four
/// keys of <c>system_config</c>: the installation's settings, not a store's. Customers are the
/// tenant's (<c>customers</c> has no <c>store_id</c>), so a customer and the ceiling on their tab
/// are the same in every shop of a chain. A key that is not there is its default: a tenant never
/// configured keeps no customers.
/// </summary>
/// <param name="CustomerModule">Customers and their tabs are kept (DPIA P6): the retailer's choice. Off, every customer endpoint refuses and the till shows no "Carnet".</param>
/// <param name="MaxCreditLimit">No customer's limit may exceed it, whoever sets it. Null: no ceiling but the owner's judgement.</param>
/// <param name="CreditOverdueDays">After this many days the oldest unpaid charge stops new charges until something is repaid. Null: off.</param>
/// <param name="TabAsPart">A ticket may be part on the tab, part cash or card (D-095's parts). Off: the tab takes a ticket whole or not at all.</param>
/// <param name="VoidAlertCount">B8 (D-097): more cancels than this by one person in one cash session are flagged for the owner. Null: not flagged.</param>
/// <param name="VoidAlertValue">B8 (D-097): a cancelled ticket worth more than this is flagged for the owner. Null: not flagged.</param>
public sealed record TenantSettings(
    bool CustomerModule, Money? MaxCreditLimit, int? CreditOverdueDays, bool TabAsPart, int? VoidAlertCount = null, Money? VoidAlertValue = null)
{
    /// <summary><c>system_config.config_key</c> of each setting.</summary>
    public const string CustomerModuleKey = "customer_module";

    public const string MaxCreditLimitKey = "max_credit_limit";

    public const string CreditOverdueDaysKey = "credit_overdue_days";

    public const string TabAsPartKey = "tab_as_part";

    public const string VoidAlertCountKey = "void_alert_count";

    public const string VoidAlertValueKey = "void_alert_value";

    /// <summary>A tenant never configured: no customers kept; the tab, once switched on, may be a part.</summary>
    public static TenantSettings Defaults { get; } = new(false, null, null, true);
}

/// <summary>Reads and stages the tenant's configuration (B7, D-096).</summary>
public interface ITenantConfiguration
{
    /// <summary>The configuration in force: <see cref="TenantSettings.Defaults"/> for each key that is not there.</summary>
    Task<TenantSettings> CurrentAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Stages the whole configuration: each key written, a null one removed so it reads as its
    /// default. Written when the executor commits (D-050).
    /// </summary>
    /// <param name="updatedBy">The staff member who changed it; null from the command line.</param>
    Task StageAsync(TenantSettings settings, string? updatedBy, DateTimeOffset at, CancellationToken cancellationToken = default);
}
