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
        TenantSettings.VoidAlertCountKey, TenantSettings.VoidAlertValueKey, TenantSettings.RefundMinRankKey, TenantSettings.CreditExpiryDaysKey,
        TenantSettings.PaidOutMinRankKey, TenantSettings.CloseSessionMinRankKey, TenantSettings.XReportMinRankKey, TenantSettings.BlindCloseKey,
        TenantSettings.VarianceAlertValueKey,
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
            values.TryGetValue(TenantSettings.TabAsPartKey, out var part) && bool.TryParse(part, out var asPart) ? asPart : defaults.TabAsPart,
            values.TryGetValue(TenantSettings.VoidAlertCountKey, out var voids)
                && int.TryParse(voids, NumberStyles.None, CultureInfo.InvariantCulture, out var most) && most > 0
                ? most
                : defaults.VoidAlertCount,
            values.TryGetValue(TenantSettings.VoidAlertValueKey, out var worth)
                && long.TryParse(worth, NumberStyles.None, CultureInfo.InvariantCulture, out var cents)
                ? Money.FromMinorUnits(cents, ledgerCurrency.Currency)
                : defaults.VoidAlertValue,
            // A rank that cannot be read is the ladder's: no setting at all.
            values.TryGetValue(TenantSettings.RefundMinRankKey, out var refund)
                && long.TryParse(refund, NumberStyles.None, CultureInfo.InvariantCulture, out var rank) && rank > 0
                ? rank
                : defaults.RefundMinRank,
            values.TryGetValue(TenantSettings.CreditExpiryDaysKey, out var expiry)
                && int.TryParse(expiry, NumberStyles.None, CultureInfo.InvariantCulture, out var expiryDays) && expiryDays > 0
                ? expiryDays
                : defaults.CreditExpiryDays,
            values.TryGetValue(TenantSettings.PaidOutMinRankKey, out var paidOut)
                && long.TryParse(paidOut, NumberStyles.None, CultureInfo.InvariantCulture, out var paidOutRank) && paidOutRank > 0
                ? paidOutRank
                : defaults.PaidOutMinRank,
            // A rank that cannot be read is the ladder's: a key nobody can read hands closing to nobody new.
            values.TryGetValue(TenantSettings.CloseSessionMinRankKey, out var close)
                && long.TryParse(close, NumberStyles.None, CultureInfo.InvariantCulture, out var closeRank) && closeRank > 0
                ? closeRank
                : defaults.CloseSessionMinRank,
            values.TryGetValue(TenantSettings.XReportMinRankKey, out var xReport)
                && long.TryParse(xReport, NumberStyles.None, CultureInfo.InvariantCulture, out var xRank) && xRank > 0
                ? xRank
                : defaults.XReportMinRank,
            values.TryGetValue(TenantSettings.BlindCloseKey, out var blind) && bool.TryParse(blind, out var isBlind) ? isBlind : defaults.BlindClose,
            // NumberStyles.None: a threshold below zero is not one, and reads as none.
            values.TryGetValue(TenantSettings.VarianceAlertValueKey, out var gap)
                && long.TryParse(gap, NumberStyles.None, CultureInfo.InvariantCulture, out var gapCents)
                ? Money.FromMinorUnits(gapCents, ledgerCurrency.Currency)
                : defaults.VarianceAlertValue);
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
        Put(TenantSettings.VoidAlertCountKey, settings.VoidAlertCount?.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Integer,
            "More cancels than this by one person in one cash session are flagged; absent: not flagged (D-097).");
        Put(TenantSettings.VoidAlertValueKey, settings.VoidAlertValue?.MinorUnits.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Money,
            "A cancelled ticket worth more than this, in minor units, is flagged; absent: not flagged (D-097).");
        Put(TenantSettings.RefundMinRankKey, settings.RefundMinRank?.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Integer,
            "The lowest roles.rank that refunds alone; below it a PIN is asked; absent: anyone with a rank (D-098).");
        Put(TenantSettings.CreditExpiryDaysKey, settings.CreditExpiryDays?.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Integer,
            "Days after which unspent store credit expires; absent: it never does (D-101).");
        Put(TenantSettings.PaidOutMinRankKey, settings.PaidOutMinRank?.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Integer,
            "The lowest roles.rank that takes cash out of the drawer alone; below it a PIN is asked; absent: anyone with a rank (D-102).");
        Put(TenantSettings.CloseSessionMinRankKey, settings.CloseSessionMinRank?.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Integer,
            "The lowest roles.rank that counts the drawer and closes the cash session alone; absent: the ladder's (D-110, D-111).");
        Put(TenantSettings.XReportMinRankKey, settings.XReportMinRank?.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Integer,
            "The lowest roles.rank that reads an open cash session's figures; absent: the ladder's (D-110).");
        Put(TenantSettings.BlindCloseKey, settings.BlindClose ? "true" : "false", SystemConfigEntryDataType.Bool,
            "Whoever counts the drawer is not shown what it should hold (D-111).");
        Put(TenantSettings.VarianceAlertValueKey, settings.VarianceAlertValue?.MinorUnits.ToString(CultureInfo.InvariantCulture), SystemConfigEntryDataType.Money,
            "A close whose variance is larger than this, in minor units, needs a note and is flagged; absent: never (D-111).");
    }

    public async Task<IReadOnlyList<long>> ActiveRanksAsync(CancellationToken cancellationToken = default) =>
        await context.Roles.AsNoTracking()
            .Where(role => role.IsActive)
            .Select(role => role.Rank)
            .Distinct()
            .OrderBy(rank => rank)
            .ToListAsync(cancellationToken);
}
