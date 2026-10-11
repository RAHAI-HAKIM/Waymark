using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// A cash session's X or Z as the till shows it (C2, D-112). Every amount is exact decimal text and,
/// unless its name says otherwise, zero or more: the sign is the line's name.
/// </summary>
/// <param name="Kind">One of <see cref="ShiftReportKinds"/>.</param>
/// <param name="ZReportNumber">A Z's number; null on an X.</param>
/// <param name="ReadAt">When the figures were worked out: an X's "instantané".</param>
/// <param name="Flags">What the report flags, to who may read them; null otherwise, with <see cref="FlagCount"/> still said.</param>
/// <param name="Reconciles">The takings come to the tickets, and a Z's rows still come to its stored expected cash.</param>
public sealed record ShiftReportWire(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("z_report_number")] long? ZReportNumber,
    [property: JsonPropertyName("currency")] string Currency,
    [property: JsonPropertyName("opened_at")] DateTimeOffset OpenedAt,
    [property: JsonPropertyName("opened_by")] string? OpenedBy,
    [property: JsonPropertyName("closed_at")] DateTimeOffset? ClosedAt,
    [property: JsonPropertyName("closed_by")] string? ClosedBy,
    [property: JsonPropertyName("read_at")] DateTimeOffset ReadAt,
    [property: JsonPropertyName("takings")] TakingsWire Takings,
    [property: JsonPropertyName("activity")] ActivityWire Activity,
    [property: JsonPropertyName("reductions")] ReductionsWire Reductions,
    [property: JsonPropertyName("cash_moves")] CashMovesWire CashMoves,
    [property: JsonPropertyName("drawer")] ReportDrawerWire Drawer,
    [property: JsonPropertyName("flag_count")] int FlagCount,
    [property: JsonPropertyName("flags")] IReadOnlyList<ShiftFlagWire>? Flags,
    [property: JsonPropertyName("reconciles")] bool Reconciles);

public static class ShiftReportKinds
{
    /// <summary>The open session, read now.</summary>
    public const string X = "x";

    /// <summary>A closed session, under its number.</summary>
    public const string Z = "z";
}

/// <param name="Method">One of <see cref="TenderMethods"/>.</param>
public sealed record TenderLineWire(
    [property: JsonPropertyName("method")] string Method,
    [property: JsonPropertyName("parts")] int Parts,
    [property: JsonPropertyName("amount")] string Amount);

/// <summary>What the session took by tender: cash as the tickets say it, the refunds apart, the net.</summary>
/// <param name="Net">Sales less refunds, signed: below zero on a day of refunds alone.</param>
public sealed record TakingsWire(
    [property: JsonPropertyName("sales")] IReadOnlyList<TenderLineWire> Sales,
    [property: JsonPropertyName("refunds")] IReadOnlyList<TenderLineWire> Refunds,
    [property: JsonPropertyName("sales_total")] string SalesTotal,
    [property: JsonPropertyName("refunds_total")] string RefundsTotal,
    [property: JsonPropertyName("net")] string Net);

public sealed record TallyWire(
    [property: JsonPropertyName("count")] int Count,
    [property: JsonPropertyName("value")] string Value);

public sealed record ActivityWire(
    [property: JsonPropertyName("sold")] TallyWire Sold,
    [property: JsonPropertyName("refunded")] TallyWire Refunded,
    [property: JsonPropertyName("cancelled")] TallyWire Cancelled,
    [property: JsonPropertyName("struck_lines")] TallyWire StruckLines);

/// <summary>What was given away at the counter. An override's value is signed: below zero when prices were raised.</summary>
public sealed record ReductionsWire(
    [property: JsonPropertyName("line_discounts")] TallyWire LineDiscounts,
    [property: JsonPropertyName("ticket_discounts")] TallyWire TicketDiscounts,
    [property: JsonPropertyName("overrides")] TallyWire Overrides,
    [property: JsonPropertyName("total")] TallyWire Total);

public sealed record CashMovesWire(
    [property: JsonPropertyName("paid_in")] TallyWire PaidIn,
    [property: JsonPropertyName("paid_out")] TallyWire PaidOut,
    [property: JsonPropertyName("tab_repayments")] TallyWire TabRepayments);

/// <summary>
/// The drawer's lines, which add up to <see cref="Expected"/>: float + cash sales + rounding − cash
/// refunds + paid-in − paid-out + tab repayments − drops.
/// </summary>
/// <param name="CashSales">The tickets' cash, to the centime: the same figure as the takings' cash line.</param>
/// <param name="Rounding">What the cash step added or took off, signed (D-034): never part of the variance.</param>
/// <param name="Counted">A Z's count; null on an X, whose drawer nobody counted.</param>
/// <param name="Variance">Counted less expected, signed; null on an X.</param>
/// <param name="Threshold">The variance threshold the Z is judged by, frozen at its close; null when there was none.</param>
public sealed record ReportDrawerWire(
    [property: JsonPropertyName("opening_float")] string OpeningFloat,
    [property: JsonPropertyName("cash_sales")] string CashSales,
    [property: JsonPropertyName("rounding")] string Rounding,
    [property: JsonPropertyName("rounded_tickets")] int RoundedTickets,
    [property: JsonPropertyName("cash_refunds")] string CashRefunds,
    [property: JsonPropertyName("paid_in")] string PaidIn,
    [property: JsonPropertyName("paid_out")] string PaidOut,
    [property: JsonPropertyName("tab_repayments")] string TabRepayments,
    [property: JsonPropertyName("drops")] string Drops,
    [property: JsonPropertyName("expected")] string Expected,
    [property: JsonPropertyName("counted")] string? Counted = null,
    [property: JsonPropertyName("variance")] string? Variance = null,
    [property: JsonPropertyName("past_threshold")] bool? PastThreshold = null,
    [property: JsonPropertyName("threshold")] string? Threshold = null,
    [property: JsonPropertyName("note")] string? Note = null);

/// <summary>Something the report flags for the owner. A cancelled ticket has no number (D-097): it is named by who and when.</summary>
/// <param name="Kind">One of <see cref="ShiftFlagKinds"/>.</param>
/// <param name="Value">A cancel's worth, or the variance, signed.</param>
/// <param name="Count">A person's cancels in the session.</param>
/// <param name="Threshold">What it was judged against: an amount as text, or a count as digits.</param>
public sealed record ShiftFlagWire(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("staff_name")] string? StaffName = null,
    [property: JsonPropertyName("at")] DateTimeOffset? At = null,
    [property: JsonPropertyName("value")] string? Value = null,
    [property: JsonPropertyName("count")] int? Count = null,
    [property: JsonPropertyName("threshold")] string? Threshold = null);

public static class ShiftFlagKinds
{
    public const string CancelAfterEncaisser = "cancel_after_encaisser";

    public const string VariancePastThreshold = "variance_past_threshold";

    public const string TooManyCancels = "too_many_cancels";

    public const string CancelAboveValue = "cancel_above_value";
}

/// <summary>A report, or why not.</summary>
/// <param name="Outcome">One of <see cref="ShiftReportOutcomes"/>.</param>
public sealed record ShiftReportAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("report")] ShiftReportWire? Report = null);

/// <summary>What the till lists beside a report: whether an X can be read, and the till's latest closes, the newest first.</summary>
public sealed record ShiftReportList(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("session_open")] bool SessionOpen = false,
    [property: JsonPropertyName("closes")] IReadOnlyList<ZSummaryWire>? Closes = null);

public sealed record ZSummaryWire(
    [property: JsonPropertyName("z_report_number")] long ZReportNumber,
    [property: JsonPropertyName("closed_at")] DateTimeOffset ClosedAt);

public static class ShiftReportOutcomes
{
    public const string Ok = "ok";

    public const string NotSignedIn = "not_signed_in";

    /// <summary>The person may not read reports alone: a manager's PIN, then ask again with the authorisation.</summary>
    public const string PinRequired = "pin_required";

    /// <summary>No session is open to read an X of, or the till gave no Z of that number.</summary>
    public const string NotFound = "not_found";
}
