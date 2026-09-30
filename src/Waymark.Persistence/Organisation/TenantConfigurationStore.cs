using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;

namespace Waymark.Persistence.Organisation;

/// <summary>
/// The tenant's configuration (B7, D-096) as four <c>system_config</c> keys. A value is text, as the
/// column is: a bool is <c>true</c> or <c>false</c>, an integer its digits, money its minor units
/// (so <c>5000000</c> is 50 000,00), never a decimal that could be read two ways. A value that
/// cannot be read is its default, the safe one: the module off, no ceiling added, the rule off.
/// </summary>
public sealed class TenantConfigurationStore(WaymarkDbContext context, ILedgerCurrency ledgerCurrency) : ITenantConfiguration
{
    private static readonly string[] Keys =
    [
        TenantSettings.CustomerModuleKey, TenantSettings.MaxCreditLimitKey, TenantSettings.CreditOverdueDaysKey, TenantSettings.TabAsPartKey,
    ];

    public async Task<TenantSettings> CurrentAsync(CancellationToken cancellationToken = default)
    {
        var values = await context.SystemConfig.AsNoTracking()
            .Where(entry => Keys.Contains(entry.ConfigKey))
            .ToDictionaryAsync(entry => entry.ConfigKey, entry => entry.ConfigValue, StringComparer.Ordinal, cancellationToken);
        var defaults = TenantSettings.Defaults;

        return new TenantSettings(
            values.TryGetValue(TenantSettings.CustomerModuleKey, out var module) && bool.TryParse(module, out var on) ? on : defaults.CustomerModule,
            values.TryGetValue(TenantSettings.MaxCreditLimitKey, out var ceiling)
                && long.TryParse(ceiling, NumberStyles.None, CultureInfo.InvariantCulture, out var minor)
                ? Money.FromMinorUnits(minor, ledgerCurrency.Currency)
                : defaults.MaxCreditLimit,
            values.TryGetValue(TenantSettings.CreditOverdueDaysKey, out var days)
                && int.TryParse(days, NumberStyles.None, CultureInfo.InvariantCulture, out var count) && count > 0
                ? count
                : defaults.CreditOverdueDays,
            values.TryGetValue(TenantSettings.TabAsPartKey, out var part) && bool.TryParse(part, out var asPart) ? asPart : defaults.TabAsPart);
    }

    public async Task StageAsync(TenantSettings settings, string? updatedBy, DateTimeOffset at, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var existing = await context.SystemConfig
            .Where(entry => Keys.Contains(entry.ConfigKey))
            .ToDictionaryAsync(entry => entry.ConfigKey, StringComparer.Ordinal, cancellationToken);

        void Put(string key, string? value, SystemConfigEntryDataType type, string description)
        {
            existing.TryGetValue(key, out var entry);
            if (value is null)
            {
                if (entry is not null)
                {
                    context.SystemConfig.Remove(entry);
                }

                return;
            }

            if (entry is null)
            {
                context.SystemConfig.Add(new SystemConfigEntry
                {
                    ConfigKey = key, ConfigValue = value, DataType = type, Description = description, UpdatedBy = updatedBy, UpdatedAt = at,
                });
                return;
            }

            // Init-only properties: the change goes through the tracked entry (StoreSettings).
            var tracked = context.Entry(entry);
            tracked.Property(row => row.ConfigValue).CurrentValue = value;
            tracked.Property(row => row.UpdatedBy).CurrentValue = updatedBy;
            tracked.Property(row => row.UpdatedAt).CurrentValue = at;
        }

        Put(TenantSettings.CustomerModuleKey, settings.CustomerModule ? "true" : "false", SystemConfigEntryDataType.Bool,
            "Customers and their tabs are kept (DPIA P6, D-096).");
        Put(TenantSettings.MaxCreditLimitKey, settings.MaxCreditLimit?.MinorUnits.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Money,
            "No customer's tab limit may exceed it, in minor units; absent: no ceiling (D-096).");
        Put(TenantSettings.CreditOverdueDaysKey, settings.CreditOverdueDays?.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Integer,
            "Days after which the oldest unpaid charge stops new charges; absent: off (D-096).");
        Put(TenantSettings.TabAsPartKey, settings.TabAsPart ? "true" : "false", SystemConfigEntryDataType.Bool,
            "A ticket may be part on the tab (D-095, D-096).");
    }
}
