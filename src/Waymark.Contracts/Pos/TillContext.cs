using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// Who and where the till is, for its top bar: the store, the till and the person selling
/// (session A4). A terminal this store does not have is an answer, not an error.
/// </summary>
/// <param name="Outcome">One of <see cref="TillContextOutcome"/>.</param>
/// <param name="StoreName">The store's name, when found.</param>
/// <param name="TerminalName">The till's name, when found.</param>
/// <param name="Currency">The store's currency code: what an empty ticket totals in.</param>
/// <param name="StaffName">
/// The person selling, when they are active staff with an active role; null otherwise, and the
/// till then shows nobody rather than a guess.
/// </param>
/// <param name="RoleLabelFr">Their role, in French.</param>
/// <param name="RoleLabelAr">Their role, in Arabic.</param>
public sealed record TillContext(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("store_name")] string? StoreName,
    [property: JsonPropertyName("terminal_name")] string? TerminalName,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("staff_name")] string? StaffName,
    [property: JsonPropertyName("role_label_fr")] string? RoleLabelFr,
    [property: JsonPropertyName("role_label_ar")] string? RoleLabelAr);

/// <summary>The values of <see cref="TillContext.Outcome"/>.</summary>
public static class TillContextOutcome
{
    public const string Found = "found";

    /// <summary>This store has no terminal with that id: the till is misconfigured, or in the wrong store.</summary>
    public const string UnknownTerminal = "unknown_terminal";
}
