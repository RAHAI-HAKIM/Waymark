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

    public const string PublishNotice = "publish-information-notice";

    public const string NoticeLanguage = "notice-language";

    /// <summary>Whether any of the switches was given.</summary>
    public static bool Asked(IConfiguration configuration)
    {
        ArgumentNullException.ThrowIfNull(configuration);
        return new[] { CustomerModule, MaxCreditLimit, CreditOverdueDays, TabAsPart, PublishNotice }.Any(key => configuration[key] is not null);
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
                + $"tab as a part {(now.TabAsPart ? "on" : "off")}.");
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
            OnOff(configuration[TabAsPart], TabAsPart));
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
