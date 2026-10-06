using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// The till's cash session as the till asks about it (C1, D-111): whether the drawer is open, since
/// when and by whom, and, to whoever may see them, what it should hold.
/// </summary>
/// <param name="Outcome">One of <see cref="CashSessionOutcomes"/>.</param>
/// <param name="Open">The session open on this till; null when there is none and the drawer is to be counted.</param>
/// <param name="LastClose">The till's latest close, for "Dernière clôture"; null when it never closed.</param>
/// <param name="Blind">The shop's <c>blind_close</c>: who counts is not shown what the drawer should hold.</param>
/// <param name="MayClose">The person signed in closes alone; otherwise a manager's PIN is asked at the end.</param>
/// <param name="NoteThreshold">
/// The variance past which a note is asked, exact decimal text; null when the shop asks for none, and
/// always null to someone counting blind, who could work the expected figure back from it.
/// </param>
public sealed record CashSessionState(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("open")] OpenCashSessionWire? Open = null,
    [property: JsonPropertyName("last_close")] LastCloseWire? LastClose = null,
    [property: JsonPropertyName("blind")] bool Blind = false,
    [property: JsonPropertyName("may_close")] bool MayClose = false,
    [property: JsonPropertyName("note_threshold")] string? NoteThreshold = null);

/// <param name="OpeningFloat">Exact decimal text.</param>
/// <param name="Tickets">Tickets sold or refunded so far; a cancel is not one.</param>
/// <param name="Drawer">What the drawer should hold, line by line; null to someone counting blind.</param>
public sealed record OpenCashSessionWire(
    [property: JsonPropertyName("opened_at")] DateTimeOffset OpenedAt,
    [property: JsonPropertyName("opened_by")] string? OpenedBy,
    [property: JsonPropertyName("opening_float")] string OpeningFloat,
    [property: JsonPropertyName("tickets")] int Tickets,
    [property: JsonPropertyName("drawer")] DrawerWire? Drawer = null);

/// <summary>
/// What the drawer should hold and why, each line exact decimal text and zero or more: the sign is the
/// line's name. <see cref="CashSales"/> is what the drawer took, the tender rounding in it, so the lines
/// add up to <see cref="Expected"/> with nothing left over.
/// </summary>
public sealed record DrawerWire(
    [property: JsonPropertyName("opening_float")] string OpeningFloat,
    [property: JsonPropertyName("cash_sales")] string CashSales,
    [property: JsonPropertyName("cash_refunds")] string CashRefunds,
    [property: JsonPropertyName("paid_in")] string PaidIn,
    [property: JsonPropertyName("paid_out")] string PaidOut,
    [property: JsonPropertyName("tab_repayments")] string TabRepayments,
    [property: JsonPropertyName("drops")] string Drops,
    [property: JsonPropertyName("expected")] string Expected);

public sealed record LastCloseWire(
    [property: JsonPropertyName("z_report_number")] long ZReportNumber,
    [property: JsonPropertyName("closed_at")] DateTimeOffset ClosedAt,
    [property: JsonPropertyName("closed_by")] string? ClosedBy);

/// <summary>The float counted into the drawer (C1). Who opens is the session's.</summary>
/// <param name="OpeningFloat">Exact decimal text, "5000.00".</param>
public sealed record OpenCashSessionRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("opening_float")] string OpeningFloat);

/// <summary>The drawer's count at the end (C1). Who counted is the session's.</summary>
/// <param name="Counted">Exact decimal text, the float included.</param>
/// <param name="Note">Asked past the shop's threshold; kept whenever written.</param>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered for <see cref="Capabilities.CloseSession"/>, when one was needed.</param>
public sealed record CloseCashSessionRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("counted")] string Counted,
    [property: JsonPropertyName("note")] string? Note = null,
    [property: JsonPropertyName("authorisation")] string? Authorisation = null);

/// <summary>What opening or closing did, or why not.</summary>
/// <param name="Outcome">One of <see cref="CashSessionOutcomes"/>.</param>
/// <param name="State">After an opening, the session as <see cref="CashSessionState"/> gives it.</param>
/// <param name="Closed">After a close, its result.</param>
public sealed record CashSessionAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("state")] CashSessionState? State = null,
    [property: JsonPropertyName("closed")] ClosedCashSessionWire? Closed = null,
    [property: JsonPropertyName("reason")] string? Reason = null,
    [property: JsonPropertyName("refusal"), JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)] Refusal? Refusal = null);

/// <summary>
/// A close as the till shows it. The three figures are null to someone who counted blind and may not
/// close alone: they are told the Z number and nothing else.
/// </summary>
/// <param name="Variance">Counted less expected, signed: a shortage is negative.</param>
/// <param name="PastThreshold">The variance passed the shop's threshold; null with the figures.</param>
/// <param name="ValidatedBy">Whose PIN allowed the close, or who closed alone.</param>
public sealed record ClosedCashSessionWire(
    [property: JsonPropertyName("z_report_number")] long ZReportNumber,
    [property: JsonPropertyName("closed_at")] DateTimeOffset ClosedAt,
    [property: JsonPropertyName("validated_by")] string? ValidatedBy,
    [property: JsonPropertyName("expected")] string? Expected = null,
    [property: JsonPropertyName("counted")] string? Counted = null,
    [property: JsonPropertyName("variance")] string? Variance = null,
    [property: JsonPropertyName("past_threshold")] bool? PastThreshold = null);

public static class CashSessionOutcomes
{
    public const string Ok = "ok";

    public const string Opened = "opened";

    public const string Closed = "closed";

    public const string Refused = "refused";

    public const string NotSignedIn = "not_signed_in";

    /// <summary>The person may not close alone: a manager's PIN, then send it again with the authorisation.</summary>
    public const string PinRequired = "pin_required";

    /// <summary>The count is past the shop's threshold: a note, then send it again. It says nothing of how far.</summary>
    public const string NoteRequired = "note_required";
}
