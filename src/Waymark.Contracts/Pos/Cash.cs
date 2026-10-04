using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// Cash into the drawer or out of it with no sale behind it (B10, D-102): which way, how much, why.
/// Who records it is the session's; who authorised a paid-out is the server's to resolve.
/// </summary>
/// <param name="Direction">One of <see cref="Reference.CashDirections"/>.</param>
/// <param name="Amount">Exact decimal text, "500.00".</param>
/// <param name="Note">For a reason that asks for one; null otherwise.</param>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered for <see cref="Capabilities.PaidOut"/>, when one was needed.</param>
public sealed record CashMovementRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("direction")] string Direction,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("authorisation")] string? Authorisation = null);

/// <summary>The movement recorded, or why not.</summary>
/// <param name="Outcome">One of <see cref="CashMovementOutcomes"/>.</param>
public sealed record CashMovementAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("amount")] string? Amount = null,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Refusal? Refusal = null);

public static class CashMovementOutcomes
{
    public const string Recorded = "recorded";

    public const string Refused = "refused";

    public const string NotSignedIn = "not_signed_in";

    /// <summary>A paid-out below the shop's <c>paid_out_min_rank</c>: a PIN, then send it again with the authorisation.</summary>
    public const string PinRequired = "pin_required";
}

/// <summary>A person clocking in or out at this till (B10, D-102): who, and their PIN, checked by the server only.</summary>
public sealed record ClockRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("staff_id")] string StaffId,
    [property: JsonPropertyName("pin")] string Pin);

/// <summary>The clock's answer.</summary>
/// <param name="Outcome">One of <see cref="ClockOutcomes"/>, or a sign-in refusal (<see cref="SignInOutcomes"/>): a wrong PIN, a lockout.</param>
/// <param name="Since">For a clock-out, when the shift began.</param>
public sealed record ClockAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("at")] DateTimeOffset? At = null,
    [property: JsonPropertyName("since")] DateTimeOffset? Since = null,
    [property: JsonPropertyName("attempts_left")] int? AttemptsLeft = null,
    [property: JsonPropertyName("locked_until")] DateTimeOffset? LockedUntil = null);

public static class ClockOutcomes
{
    public const string ClockedIn = "clocked_in";

    public const string ClockedOut = "clocked_out";

    public const string NotSignedIn = "not_signed_in";
}
