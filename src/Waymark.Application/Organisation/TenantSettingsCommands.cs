using System.Globalization;
using Waymark.Application.Commands;
using Waymark.Domain;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Reference;
using Waymark.Domain.Values;
using Waymark.Domain.Work;

namespace Waymark.Application.Organisation;

/// <summary>A tenant setting refused; nothing was changed.</summary>
public sealed class TenantSettingRefusedException(string reason) : Exception(reason);

/// <summary>
/// Change some of the tenant's settings (B7, D-096); a null leaves that one as it is. The maintenance
/// door until H2's settings screen, through <c>--customer-module</c> and its siblings.
/// </summary>
/// <param name="MaxCreditLimit">Minor units; with <paramref name="NoCeiling"/> the ceiling is removed instead.</param>
/// <param name="CreditOverdueDays">With <paramref name="OverdueOff"/> the rule is switched off instead.</param>
public sealed record SetTenantSettings(
    bool? CustomerModule = null,
    long? MaxCreditLimit = null,
    bool NoCeiling = false,
    int? CreditOverdueDays = null,
    bool OverdueOff = false,
    bool? TabAsPart = null,
    int? VoidAlertCount = null,
    bool VoidCountOff = false,
    long? VoidAlertValue = null,
    bool VoidValueOff = false) : ICommand<TenantSettings>;

public sealed class SetTenantSettingsHandler(ITenantConfiguration configuration, ILedgerCurrency ledgerCurrency, TimeProvider clock)
    : ICommandHandler<SetTenantSettings, TenantSettings>
{
    public async Task<TenantSettings> HandleAsync(SetTenantSettings command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        if (command.MaxCreditLimit is < 0)
        {
            throw new TenantSettingRefusedException("A ceiling is zero or more.");
        }

        if (command.VoidAlertCount is <= 0 || command.VoidAlertValue is < 0)
        {
            throw new TenantSettingRefusedException("A cancel threshold is one or more, an amount zero or more; switch it off instead.");
        }

        if (command.CreditOverdueDays is <= 0)
        {
            throw new TenantSettingRefusedException("Overdue days are one or more; switch the rule off instead.");
        }

        var current = await configuration.CurrentAsync(cancellationToken);
        var next = current with
        {
            CustomerModule = command.CustomerModule ?? current.CustomerModule,
            MaxCreditLimit = command.NoCeiling ? null
                : command.MaxCreditLimit is { } minor ? Money.FromMinorUnits(minor, ledgerCurrency.Currency)
                : current.MaxCreditLimit,
            CreditOverdueDays = command.OverdueOff ? null : command.CreditOverdueDays ?? current.CreditOverdueDays,
            TabAsPart = command.TabAsPart ?? current.TabAsPart,
            VoidAlertCount = command.VoidCountOff ? null : command.VoidAlertCount ?? current.VoidAlertCount,
            VoidAlertValue = command.VoidValueOff ? null
                : command.VoidAlertValue is { } worth ? Money.FromMinorUnits(worth, ledgerCurrency.Currency)
                : current.VoidAlertValue,
        };

        await configuration.StageAsync(next, null, clock.GetUtcNow(), cancellationToken);
        return next;
    }
}

/// <summary>
/// Publish a new information notice (Art. 32), in force from today (B7, D-096): the one piece of G2
/// that creating a customer at the till needs. The text is the retailer's; Waymark stores it and
/// records, on each customer created, which version they were handed. A notice is never edited: a
/// new text is a new version.
/// </summary>
public sealed record PublishInformationNotice(Language Language, string Body) : ICommand<string>;

public sealed class PublishInformationNoticeHandler(IStaging staging, IStoreCalendar calendar, TimeProvider clock)
    : ICommandHandler<PublishInformationNotice, string>
{
    public Task<string> HandleAsync(PublishInformationNotice command, CommandContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(command);
        var body = (command.Body ?? string.Empty).Trim();
        if (body.Length == 0)
        {
            throw new TenantSettingRefusedException("A notice has a text.");
        }

        var now = clock.GetUtcNow();
        var today = calendar.Today.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        var code = string.Create(CultureInfo.InvariantCulture, $"information-{command.Language.ToString().ToLowerInvariant()}-{now:yyyyMMddHHmmss}");
        staging.Add(new NoticeVersion
        {
            VersionCode = code,
            NoticeType = NoticeType.Information,
            Language = command.Language,
            BodyText = body,
            EffectiveFrom = today,
            PublishedAt = now,
        });

        return Task.FromResult(code);
    }
}
