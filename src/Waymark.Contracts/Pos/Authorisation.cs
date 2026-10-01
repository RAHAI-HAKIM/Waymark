using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// The till asking whether the person signed in may do something, and if not, a manager's PIN
/// (session B4, D-091). The session header says which till and who is selling.
/// </summary>
/// <param name="Capability">One of <see cref="Capabilities"/>.</param>
/// <param name="StaffId">
/// The manager whose PIN is typed; null to ask whether the seller may do it alone. The server
/// answers <see cref="AuthoriseOutcomes.PinRequired"/> when they may not.
/// </param>
/// <param name="Pin">The manager's PIN, checked in StoreServer only (§3.10). Never stored.</param>
public sealed record AuthoriseRequest(
    [property: JsonPropertyName("capability")] string Capability,
    [property: JsonPropertyName("staff_id")] string? StaffId,
    [property: JsonPropertyName("pin")] string? Pin);

/// <summary>StoreServer's answer: an authorisation to cite, or why not. Every outcome is a 200.</summary>
/// <param name="Authorisation">
/// When authorised: what the sale cites for the action. It names nobody on the wire; the server
/// knows who gave it, and that person is written as <c>authorised_by</c>.
/// </param>
/// <param name="StaffName">Who authorised it, for the till to say so.</param>
public sealed record AuthoriseAnswer(
    [property: JsonPropertyName("outcome")] string Outcome,
    [property: JsonPropertyName("authorisation")] string? Authorisation,
    [property: JsonPropertyName("staff_name")] string? StaffName,
    [property: JsonPropertyName("locked_until")] DateTimeOffset? LockedUntil,
    [property: JsonPropertyName("attempts_left")] int? AttemptsLeft);

/// <summary>The values of <see cref="AuthoriseAnswer.Outcome"/>.</summary>
public static class AuthoriseOutcomes
{
    /// <summary>Allowed: <see cref="AuthoriseAnswer.Authorisation"/> is set.</summary>
    public const string Authorised = "authorised";

    /// <summary>The seller may not do it alone: a manager's PIN is needed.</summary>
    public const string PinRequired = "pin_required";

    /// <summary>The PIN was right, and that person's rank does not reach it either.</summary>
    public const string NotAllowed = "not_allowed";

    public const string WrongPin = SignInOutcomes.WrongPin;

    public const string Locked = SignInOutcomes.Locked;

    public const string NoPin = SignInOutcomes.NoPin;

    public const string UnknownStaff = SignInOutcomes.UnknownStaff;

    /// <summary>The till's session is not one the server holds.</summary>
    public const string NotSignedIn = "not_signed_in";

    /// <summary>A capability the server does not know.</summary>
    public const string UnknownCapability = "unknown_capability";
}

/// <summary>What may be authorised, as the wire names it.</summary>
public static class Capabilities
{
    /// <summary>A discount given at the counter (B4).</summary>
    public const string ApplyDiscount = "apply_discount";

    /// <summary>A unit price typed in place of the price in force (B5).</summary>
    public const string OverridePrice = "override_price";

    /// <summary>A customer created at the till (B7).</summary>
    public const string CreateCustomer = "create_customer";

    /// <summary>A tab's limit changed, frozen or unfrozen, or one charge let past it (B7).</summary>
    public const string ManageCredit = "manage_credit";

    /// <summary>A ticket cancelled after "Encaisser" was opened on it, by a cashier (B8).</summary>
    public const string VoidTransaction = "void_transaction";
}

/// <summary>A discount given at the counter, as the till sends it (B4, D-091). The server works out the money.</summary>
/// <param name="Form">One of <see cref="DiscountForms"/>.</param>
/// <param name="Value">"10" or "12.5" for a percent; "50.00" for an amount. Invariant text, at most two decimals.</param>
/// <param name="ReasonCode">An active <c>discount</c> reason's code.</param>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered.</param>
/// <param name="Note">What the cashier wrote, for a reason that asks for a note (F-28); null otherwise.</param>
public sealed record DiscountRequest(
    [property: JsonPropertyName("form")] string Form,
    [property: JsonPropertyName("value")] string Value,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("authorisation")] string Authorisation,
    [property: JsonPropertyName("note")] string? Note = null);

/// <summary>A unit price typed at the counter in place of the price in force (B5, D-092). The server checks the band again.</summary>
/// <param name="Price">The new unit price, invariant text: "120.00".</param>
/// <param name="ReasonCode">An active <c>price_override</c> reason's code.</param>
/// <param name="Authorisation">What <c>/api/till/authorise</c> answered for <see cref="Capabilities.OverridePrice"/>.</param>
public sealed record PriceOverrideRequest(
    [property: JsonPropertyName("price")] string Price,
    [property: JsonPropertyName("reason_code")] string ReasonCode,
    [property: JsonPropertyName("authorisation")] string Authorisation);

/// <summary>The values of <see cref="DiscountRequest.Form"/>.</summary>
public static class DiscountForms
{
    public const string Percent = "percent";

    public const string Amount = "amount";
}
