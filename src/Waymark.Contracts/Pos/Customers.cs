using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>The values of every customer answer's <c>outcome</c> (B7, D-096).</summary>
public static class CustomerOutcomes
{
    /// <summary>Done: the answer carries what was asked.</summary>
    public const string Ok = "ok";

    /// <summary>The tenant keeps no customers: the till shows no customer key at all.</summary>
    public const string ModuleOff = "module_off";

    /// <summary>No such customer.</summary>
    public const string NotFound = "not_found";

    /// <summary>Refused; <c>reason</c> says why, in words.</summary>
    public const string Refused = "refused";

    /// <summary>The till's session is not one the server holds: it asks for a PIN again.</summary>
    public const string NotSignedIn = "not_signed_in";

    /// <summary>The person may not; an authorisation for the capability is asked first.</summary>
    public const string NotAllowed = "not_allowed";
}

/// <summary>A customer as the till lists one: never more than it needs to pick the right person.</summary>
/// <param name="Phone">In its one form, "+213550123456".</param>
public sealed record CustomerSummaryWire(
    [property: JsonPropertyName("customer_id")] string CustomerId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("phone")] string? Phone);

/// <summary>Customers with a number, or why not.</summary>
public sealed record CustomerSearchAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("customers")] IReadOnlyList<CustomerSummaryWire>? Customers,
    [property: JsonPropertyName("reason")] string? Reason = null);

/// <summary>A customer created at the till (B7): a name and a number; the information notice in force is recorded by the server.</summary>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered for <see cref="Capabilities.CreateCustomer"/>, when the seller may not alone.</param>
public sealed record CreateCustomerRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("name")] string Name,
    [property: JsonPropertyName("phone")] string Phone,
    [property: JsonPropertyName("authorisation")] string? Authorisation = null);

/// <summary>The customer created, or why not.</summary>
public sealed record CustomerAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("customer")] CustomerSummaryWire? Customer,
    [property: JsonPropertyName("reason")] string? Reason = null);

/// <summary>A movement on the tab, as the statement shows it.</summary>
/// <param name="Kind">"charge", "payment", "adjustment", "write_off".</param>
/// <param name="Amount">Exact decimal text; positive means the customer owes more.</param>
/// <param name="At">When, as an instant.</param>
public sealed record TabMovementWire(
    [property: JsonPropertyName("kind")] string Kind,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("at")] DateTimeOffset At);

/// <summary>A customer's tab (B7, D-096): the figures are the server's, worked out by the tab's rule.</summary>
/// <param name="Limit">Null: no tab.</param>
/// <param name="Available">The limit less the balance; null with no tab.</param>
/// <param name="OldestUnpaid">When the oldest charge still unpaid was made; null when nothing is owed.</param>
/// <param name="OverdueDays">After how many days a charge is overdue; null when the shop has no such rule.</param>
public sealed record TabAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("customer")] CustomerSummaryWire? Customer,
    [property: JsonPropertyName("balance")] string? Balance,
    [property: JsonPropertyName("limit")] string? Limit,
    [property: JsonPropertyName("available")] string? Available,
    [property: JsonPropertyName("frozen")] bool Frozen,
    [property: JsonPropertyName("oldest_unpaid")] DateTimeOffset? OldestUnpaid,
    [property: JsonPropertyName("overdue_days")] int? OverdueDays,
    [property: JsonPropertyName("currency")] string? Currency,
    [property: JsonPropertyName("movements")] IReadOnlyList<TabMovementWire>? Movements,
    [property: JsonPropertyName("reason")] string? Reason = null);

/// <summary>A repayment in cash at this till (B7): what was handed over for the tab, and why the drawer takes it.</summary>
/// <param name="Amount">Exact decimal text.</param>
/// <param name="ReasonCode">An active <c>cash_movement</c> reason.</param>
public sealed record RepaymentRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("amount")] string Amount,
    [property: JsonPropertyName("reason_code")] string ReasonCode);

/// <summary>A change to a tab's limit (B7), always an owner's.</summary>
/// <param name="Action">One of <see cref="LimitActions"/>.</param>
/// <param name="Limit">For <see cref="LimitActions.Set"/>: exact decimal text, or null to close the tab.</param>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered for <see cref="Capabilities.ManageCredit"/>, when the seller may not alone.</param>
public sealed record LimitRequest(
    [property: JsonPropertyName("terminal_id")] string TerminalId,
    [property: JsonPropertyName("action")] string Action,
    [property: JsonPropertyName("limit")] string? Limit,
    [property: JsonPropertyName("authorisation")] string? Authorisation = null);

/// <summary>The values of <see cref="LimitRequest.Action"/>.</summary>
public static class LimitActions
{
    public const string Set = "set";

    public const string Freeze = "freeze";

    public const string Unfreeze = "unfreeze";
}
