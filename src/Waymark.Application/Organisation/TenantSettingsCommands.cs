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
/// <param name="RefundMinRank">B9 (D-098): a rank an active role has; with <paramref name="RefundRankOff"/> anyone with a rank refunds instead.</param>
/// <param name="CloseSessionMinRank">C1 (D-110): a rank an active role has; with <paramref name="CloseRankOff"/> the ladder's instead.</param>
/// <param name="XReportMinRank">C2 (D-110): a rank an active role has; with <paramref name="XReportRankOff"/> the ladder's instead.</param>
/// <param name="VarianceAlertValue">C1 (D-111): minor units; with <paramref name="VarianceAlertOff"/> no close asks for a note instead.</param>
/// <param name="RefundWindowDays">D-125: 1 to 30; with <paramref name="RefundWindowOff"/> no window instead.</param>
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
    bool VoidValueOff = false,
    long? RefundMinRank = null,
    bool RefundRankOff = false,
    int? CreditExpiryDays = null,
    bool CreditExpiryOff = false,
    long? PaidOutMinRank = null,
    bool PaidOutRankOff = false,
    long? CloseSessionMinRank = null,
    bool CloseRankOff = false,
    long? XReportMinRank = null,
    bool XReportRankOff = false,
    bool? BlindClose = null,
    long? VarianceAlertValue = null,
    bool VarianceAlertOff = false,
    Language? ReceiptLanguage = null,
    int? RefundWindowDays = null,
    bool RefundWindowOff = false,
    int? NoSaleAlertCount = null,
    bool NoSaleAlertOff = false) : ICommand<TenantSettings>;

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

        if (command.CreditExpiryDays is <= 0)
        {
            throw new TenantSettingRefusedException("Store credit expires after one day or more; switch it off instead.");
        }

        if (command.CreditOverdueDays is <= 0)
        {
            throw new TenantSettingRefusedException("Overdue days are one or more; switch the rule off instead.");
        }

        // A rank nobody holds would leave nobody to refund, nor to authorise a refund (D-098).
        if (command.RefundMinRank is { } refundRank && !(await configuration.ActiveRanksAsync(cancellationToken)).Contains(refundRank))
        {
            throw new TenantSettingRefusedException($"No active role has rank {refundRank}: nobody could refund.");
        }

        if (command.PaidOutMinRank is { } paidOutRank && !(await configuration.ActiveRanksAsync(cancellationToken)).Contains(paidOutRank))
        {
            throw new TenantSettingRefusedException($"No active role has rank {paidOutRank}: nobody could take cash out.");
        }

        // A rank nobody holds would leave a drawer nobody could close, nor read (D-110).
        foreach (var asked in new[] { command.CloseSessionMinRank, command.XReportMinRank })
        {
            if (asked is { } sessionRank && !(await configuration.ActiveRanksAsync(cancellationToken)).Contains(sessionRank))
            {
                throw new TenantSettingRefusedException($"No active role has rank {sessionRank}: nobody could close the drawer or read it.");
            }
        }

        if (command.VarianceAlertValue is < 0)
        {
            throw new TenantSettingRefusedException("A variance threshold is zero or more; switch it off instead.");
        }

        // A receipt is printed in French or in Arabic (D-124): the notices' third language is not one of them.
        if (command.ReceiptLanguage is not (null or Language.Fr or Language.Ar))
        {
            throw new TenantSettingRefusedException("A receipt is printed in French or in Arabic.");
        }

        if (command.RefundWindowDays is < 1 or > TenantSettings.MaxRefundWindowDays)
        {
            throw new TenantSettingRefusedException($"A refund window is 1 to {TenantSettings.MaxRefundWindowDays} days; switch it off instead.");
        }

        if (command.NoSaleAlertCount is < 1)
        {
            throw new TenantSettingRefusedException("A no-sale threshold is one or more; switch it off instead.");
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
            RefundMinRank = command.RefundRankOff ? null : command.RefundMinRank ?? current.RefundMinRank,
            CreditExpiryDays = command.CreditExpiryOff ? null : command.CreditExpiryDays ?? current.CreditExpiryDays,
            PaidOutMinRank = command.PaidOutRankOff ? null : command.PaidOutMinRank ?? current.PaidOutMinRank,
            CloseSessionMinRank = command.CloseRankOff ? null : command.CloseSessionMinRank ?? current.CloseSessionMinRank,
            XReportMinRank = command.XReportRankOff ? null : command.XReportMinRank ?? current.XReportMinRank,
            BlindClose = command.BlindClose ?? current.BlindClose,
            VarianceAlertValue = command.VarianceAlertOff ? null
                : command.VarianceAlertValue is { } gap ? Money.FromMinorUnits(gap, ledgerCurrency.Currency)
                : current.VarianceAlertValue,
            ReceiptLanguage = command.ReceiptLanguage ?? current.ReceiptLanguage,
            RefundWindowDays = command.RefundWindowOff ? null : command.RefundWindowDays ?? current.RefundWindowDays,
            NoSaleAlertCount = command.NoSaleAlertOff ? null : command.NoSaleAlertCount ?? current.NoSaleAlertCount,
        };

        // A blind close with the X handed below it is no blind close (D-121, block C review): whoever
        // reads an X reads "Attendu", then counts "blind". Judged on the settings as they would stand,
        // so the pair is refused whichever of the three keys completes it.
        var reads = next.XReportMinRank ?? StaffPermissions.RequiredRank(Capability.ReadXReport);
        var closes = next.CloseSessionMinRank ?? StaffPermissions.RequiredRank(Capability.CloseSession);
        if (next.BlindClose && reads < closes)
        {
            throw new TenantSettingRefusedException(
                $"A blind close with reports read from rank {reads} and the drawer closed alone from rank {closes}: "
                + "whoever counts blind could read what the drawer should hold. Raise the reports' rank to the close's, or switch the blind close off.");
        }

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
