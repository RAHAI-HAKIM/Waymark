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

    public const string PublishNotice = "publish-information-notice";

    public const string NoticeLanguage = "notice-language";

    /// <summary>Whether any of the switches was given.</summary>
    public static bool Asked(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new[] { CustomerModule, MaxCreditLimit, CreditOverdueDays, TabAsPart, PublishNotice, VoidAlertCount, VoidAlertValue, RefundMinRank, CreditExpiryDays, PaidOutMinRank }
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
                + $"cash out alone from {(now.PaidOutMinRank is { } outRank ? $"rank {outRank}" : "any rank (off)")}.");
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
            paidOutOff);
    }

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
