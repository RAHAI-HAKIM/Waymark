using Microsoft.Extensions.Configuration;
using Waymark.Application.Commands;
using Waymark.Application.Organisation;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;

namespace Waymark.StoreServer.Organisation;

/// <summary>
/// The tenant's settings from the command line (B7, D-096), then exit, serving nothing: the
/// maintenance door until H2's settings screen, like <c>--scale-format</c>.
/// <list type="bullet">
///   <item><description><c>--customer-module=on|off</c></description></item>
///   <item><description><c>--max-credit-limit=50000.00|none</c></description></item>
///   <item><description><c>--credit-overdue-days=30|off</c></description></item>
///   <item><description><c>--tab-as-part=on|off</c></description></item>
///   <item><description><c>--void-alert-count=5|off</c>, <c>--void-alert-value=5000.00|off</c></description></item>
///   <item><description><c>--refund-min-rank=2|off</c></description></item>
///   <item><description><c>--credit-expiry-days=365|off</c></description></item>
///   <item><description><c>--paid-out-min-rank=2|off</c></description></item>
///   <item><description><c>--close-session-min-rank=1|off</c>, <c>--x-report-min-rank=1|off</c> (off: the ladder's rank)</description></item>
///   <item><description><c>--blind-close=on|off</c>, <c>--variance-alert-value=200.00|off</c></description></item>
///   <item><description><c>--publish-information-notice=&lt;path to the text&gt;</c>, with
///   <c>--notice-language=ar|fr|en</c> (Arabic when left out)</description></item>
/// </list>
/// Any of them together; the settings in force are printed after.
/// </summary>
public static class TenantSwitch
{
    public const string CustomerModule = "customer-module";

    public const string MaxCreditLimit = "max-credit-limit";

    public const string CreditOverdueDays = "credit-overdue-days";

    public const string TabAsPart = "tab-as-part";

    /// <summary>B8 (D-097): more cancels than this by one person in one cash session are flagged; <c>off</c> to stop.</summary>
    public const string VoidAlertCount = "void-alert-count";

    /// <summary>B8 (D-097): a cancelled ticket worth more than this is flagged; <c>off</c> to stop.</summary>
    public const string VoidAlertValue = "void-alert-value";

    /// <summary>B9 (D-098): the lowest rank that refunds alone; <c>off</c> for anyone with a rank.</summary>
    public const string RefundMinRank = "refund-min-rank";

    /// <summary>B9b (D-101): store credit unspent this many days after it was issued expires; <c>off</c> for never.</summary>
    public const string CreditExpiryDays = "credit-expiry-days";

    /// <summary>B10 (D-102): the lowest rank that takes cash out of the drawer alone; <c>off</c> for anyone with a rank.</summary>
    public const string PaidOutMinRank = "paid-out-min-rank";

    /// <summary>C1 (D-110): the lowest rank that closes the cash session alone, above the ladder's or below it; <c>off</c> for the ladder's.</summary>
    public const string CloseSessionMinRank = "close-session-min-rank";

    /// <summary>C2 (D-110): the lowest rank that reads an open session's figures; <c>off</c> for the ladder's.</summary>
    public const string XReportMinRank = "x-report-min-rank";

    /// <summary>C1 (D-111): whoever counts the drawer is not shown what it should hold.</summary>
    public const string BlindClose = "blind-close";

    /// <summary>C1 (D-111): a close whose variance is larger than this needs a note; <c>off</c> for never.</summary>
    public const string VarianceAlertValue = "variance-alert-value";

    public const string PublishNotice = "publish-information-notice";

    public const string NoticeLanguage = "notice-language";

    /// <summary>Whether any of the switches was given.</summary>
    public static bool Asked(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new[] { CustomerModule, MaxCreditLimit, CreditOverdueDays, TabAsPart, PublishNotice, VoidAlertCount, VoidAlertValue, RefundMinRank, CreditExpiryDays, PaidOutMinRank,
                CloseSessionMinRank, XReportMinRank, BlindClose, VarianceAlertValue }
            .Any(key => configuration[key] is not null);
    }

    /// <returns>The process exit code: 0 when set, 1 when refused and nothing was changed.</returns>
    public static async Task<int> RunAsync(
        IConfiguration configuration,
        TextWriter output,
        CommandExecutor executor,
        SetTenantSettingsHandler settings,
        PublishInformationNoticeHandler notices,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        ArgumentNullException.ThrowIfNull(output);
        ArgumentNullException.ThrowIfNull(executor);

        SetTenantSettings command;
        try
        {
            command = Read(configuration);
        }
        catch (FormatException refusal)
        {
            await output.WriteLineAsync($"{refusal.Message} Nothing was changed.");
            return 1;
        }

        try
        {
            // The notice read before anything is written, the settings next, the notice last: a
            // refusal at any step leaves nothing half done.
            var notice = configuration[PublishNotice] is { } path
                ? (Language(configuration[NoticeLanguage]), await File.ReadAllTextAsync(path, cancellationToken))
                : ((Language, string)?)null;
            var now = await executor.ExecuteAsync(settings, command, cancellationToken);
            if (notice is var (language, body))
            {
                var code = await executor.ExecuteAsync(notices, new PublishInformationNotice(language, body), cancellationToken);
                await output.WriteLineAsync($"Information notice {code} published, in force from today.");
            }

            await output.WriteLineAsync(
                $"Customer module {(now.CustomerModule ? "on" : "off")}; ceiling {now.MaxCreditLimit?.ToString() ?? "none"}; "
                + $"overdue after {(now.CreditOverdueDays is { } days ? $"{days} days" : "never (off)")}; "
                + $"tab as a part {(now.TabAsPart ? "on" : "off")}; "
                + $"cancels flagged above {(now.VoidAlertCount is { } count ? $"{count} a session" : "no count (off)")} "
                + $"and above {now.VoidAlertValue?.ToString() ?? "no value (off)"}; "
                + $"refunds alone from {(now.RefundMinRank is { } rank ? $"rank {rank}" : "any rank (off)")}; "
                + $"store credit expires {(now.CreditExpiryDays is { } expiry ? $"after {expiry} days" : "never (off)")}; "
                + $"cash out alone from {(now.PaidOutMinRank is { } outRank ? $"rank {outRank}" : "any rank (off)")}; "
                + $"the drawer closed alone from {(now.CloseSessionMinRank is { } closeRank ? $"rank {closeRank}" : "the ladder's rank (off)")}, "
                + $"read from {(now.XReportMinRank is { } xRank ? $"rank {xRank}" : "the ladder's rank (off)")}; "
                + $"blind close {(now.BlindClose ? "on" : "off")}; "
                + $"a note asked past a variance of {now.VarianceAlertValue?.ToString() ?? "nothing (off)"}.");
            return 0;
        }
        catch (Exception refusal) when (refusal is TenantSettingRefusedException or IOException or UnauthorizedAccessException)
        {
            await output.WriteLineAsync($"{refusal.Message} Nothing was changed.");
            return 1;
        }
    }

    private static SetTenantSettings Read(IConfiguration configuration)
    {
        var ceiling = configuration[MaxCreditLimit];
        var days = configuration[CreditOverdueDays];
        var noCeiling = string.Equals(ceiling, "none", StringComparison.OrdinalIgnoreCase);
        var overdueOff = string.Equals(days, "off", StringComparison.OrdinalIgnoreCase);
        var voids = configuration[VoidAlertCount];
        var worth = configuration[VoidAlertValue];
        var voidsOff = string.Equals(voids, "off", StringComparison.OrdinalIgnoreCase);
        var worthOff = string.Equals(worth, "off", StringComparison.OrdinalIgnoreCase);
        var refund = configuration[RefundMinRank];
        var refundOff = string.Equals(refund, "off", StringComparison.OrdinalIgnoreCase);
        var expiry = configuration[CreditExpiryDays];
        var expiryOff = string.Equals(expiry, "off", StringComparison.OrdinalIgnoreCase);
        var paidOut = configuration[PaidOutMinRank];
        var paidOutOff = string.Equals(paidOut, "off", StringComparison.OrdinalIgnoreCase);
        var close = configuration[CloseSessionMinRank];
        var closeOff = string.Equals(close, "off", StringComparison.OrdinalIgnoreCase);
        var xReport = configuration[XReportMinRank];
        var xReportOff = string.Equals(xReport, "off", StringComparison.OrdinalIgnoreCase);
        var gap = configuration[VarianceAlertValue];
        var gapOff = string.Equals(gap, "off", StringComparison.OrdinalIgnoreCase);

        return new SetTenantSettings(
            OnOff(configuration[CustomerModule], CustomerModule),
            ceiling is null || noCeiling ? null
                : WireText.TryHundredths(ceiling, out var minor) ? minor : throw new FormatException($"--{MaxCreditLimit}: an amount, 50000.00, or none."),
            noCeiling,
            days is null || overdueOff ? null
                : int.TryParse(days, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var count) && count > 0
                    ? count
                    : throw new FormatException($"--{CreditOverdueDays}: a number of days, 30, or off."),
            overdueOff,
            OnOff(configuration[TabAsPart], TabAsPart),
            voids is null || voidsOff ? null
                : int.TryParse(voids, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var most) && most > 0
                    ? most
                    : throw new FormatException($"--{VoidAlertCount}: a number of cancels, 5, or off."),
            voidsOff,
            worth is null || worthOff ? null
                : WireText.TryHundredths(worth, out var cents) ? cents : throw new FormatException($"--{VoidAlertValue}: an amount, 5000.00, or off."),
            worthOff,
            refund is null || refundOff ? null
                : long.TryParse(refund, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rank) && rank > 0
                    ? rank
                    : throw new FormatException($"--{RefundMinRank}: a rank, 2, or off."),
            refundOff,
            expiry is null || expiryOff ? null
                : int.TryParse(expiry, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var expiryCount) && expiryCount > 0
                    ? expiryCount
                    : throw new FormatException($"--{CreditExpiryDays}: a number of days, 365, or off."),
            expiryOff,
            paidOut is null || paidOutOff ? null
                : long.TryParse(paidOut, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var outRank) && outRank > 0
                    ? outRank
                    : throw new FormatException($"--{PaidOutMinRank}: a rank, 2, or off."),
            paidOutOff,
            close is null || closeOff ? null : Rank(close, CloseSessionMinRank),
            closeOff,
            xReport is null || xReportOff ? null : Rank(xReport, XReportMinRank),
            xReportOff,
            OnOff(configuration[BlindClose], BlindClose),
            gap is null || gapOff ? null
                : WireText.TryHundredths(gap, out var gapCents) ? gapCents : throw new FormatException($"--{VarianceAlertValue}: an amount, 200.00, or off."),
            gapOff);
    }

    private static long Rank(string value, string key) =>
        long.TryParse(value, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out var rank) && rank > 0
            ? rank
            : throw new FormatException($"--{key}: a rank, 2, or off.");

    private static bool? OnOff(string? value, string key) => value?.Trim().ToLowerInvariant() switch
    {
        null => null,
        "on" or "true" => true,
        "off" or "false" => false,
        _ => throw new FormatException($"--{key}: on or off."),
    };

    private static Language Language(string? value) => value?.Trim().ToLowerInvariant() switch
    {
        null or "ar" => Domain.Enums.Language.Ar,
        "fr" => Domain.Enums.Language.Fr,
        "en" => Domain.Enums.Language.En,
        _ => throw new TenantSettingRefusedException($"--{NoticeLanguage}: ar, fr or en."),
    };
}
