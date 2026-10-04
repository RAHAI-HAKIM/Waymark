using System.Text.Json.Serialization;

namespace Waymark.Contracts.Pos;

/// <summary>
/// Why StoreServer refused, as a code and what it names, for the till to say in its own language
/// (D-107). The prose beside it (<c>reason</c>) stays, in English, for the logs and for a code the
/// till does not know: found in the block B review, a French or Arabic till showed the server's
/// English sentences as written.
/// </summary>
/// <param name="Code">One of <see cref="RefusalCodes"/>.</param>
/// <param name="Args">
/// What the sentence names, in the order the code's own comment gives: a product or a person as
/// written, an amount as exact decimal text ("2000.00"), a count as digits. Never a sentence.
/// </param>
public sealed record Refusal(
    [property: JsonPropertyName("code")] string Code,
    [property: JsonPropertyName("args")] IReadOnlyList<string>? Args = null);

/// <summary>
/// The refusals a cashier meets in ordinary work (D-107), each with what it names. A refusal with
/// no code is one the till's own screens prevent; it reaches the cashier as a plain sentence with
/// the server's words under it.
/// </summary>
public static class RefusalCodes
{
    // ------------------------------------------------------------------ a sale

    /// <summary>A line is no longer sellable (archived, no price in force) since it was scanned. Names: the code.</summary>
    public const string NotSellable = "not_sellable";

    /// <summary>No product carries a line's code any more. Names: the code.</summary>
    public const string UnknownCode = "unknown_code";

    /// <summary>A product was never received: there is no batch to sell it from. Names: the product.</summary>
    public const string NeverReceived = "never_received";

    /// <summary>A line holds too many units, or too great a weight. Names: the most a line holds.</summary>
    public const string LineTooLarge = "line_too_large";

    /// <summary>An overridden price is outside the band. Names: the product, the price, the most it may be.</summary>
    public const string PriceOutOfBand = "price_out_of_band";

    /// <summary>The parts come to more than the ticket. Names: the ticket's total.</summary>
    public const string PartsAboveTotal = "parts_above_total";

    /// <summary>A payment reference reads as a card number, or as nothing a terminal prints.</summary>
    public const string ReferenceRefused = "reference_refused";

    /// <summary>
    /// A discount or a price cites an approval the server no longer holds (it restarted): the manager
    /// gives it again (D-105).
    /// </summary>
    public const string ApprovalExpired = "approval_expired";

    /// <summary>A line was struck after "Encaisser" by someone who may not cancel alone: a manager's PIN (D-106).</summary>
    public const string StrikeNeedsPin = "strike_needs_pin";

    /// <summary>A discount takes nothing off, or more than the line.</summary>
    public const string DiscountInvalid = "discount_invalid";

    // ------------------------------------------------------------------ the tab and store credit, at a sale

    /// <summary>The tab part goes past the limit. Names: the customer, what is still available.</summary>
    public const string TabAboveLimit = "tab_above_limit";

    /// <summary>The customer has no tab. Names: the customer.</summary>
    public const string TabNone = "tab_none";

    /// <summary>The customer's tab is frozen. Names: the customer.</summary>
    public const string TabFrozen = "tab_frozen";

    /// <summary>The customer's oldest charge is overdue. Names: the customer.</summary>
    public const string TabOverdue = "tab_overdue";

    /// <summary>A store credit part is more than the customer has. Names: the customer, what they have.</summary>
    public const string CreditInsufficient = "credit_insufficient";

    /// <summary>The shop keeps no customers: the module is off.</summary>
    public const string ModuleOff = "module_off";

    /// <summary>No such customer.</summary>
    public const string CustomerUnknown = "customer_unknown";

    // ------------------------------------------------------------------ a cancel

    /// <summary>Not a reason this shop gives. Names: the reason's code.</summary>
    public const string ReasonUnknown = "reason_unknown";

    /// <summary>The reason asks for a note, and none was written.</summary>
    public const string NoteMissing = "note_missing";

    // ------------------------------------------------------------------ a refund

    /// <summary>The shop asks for a manager to authorise a refund.</summary>
    public const string RefundNeedsManager = "refund_needs_manager";

    /// <summary>The ticket was already refunded whole.</summary>
    public const string RefundAlreadyWhole = "refund_already_whole";

    /// <summary>More than is left of a line: some of it was already refunded.</summary>
    public const string RefundMoreThanLeft = "refund_more_than_left";

    /// <summary>The ticket was sold to another customer.</summary>
    public const string RefundOtherCustomer = "refund_other_customer";

    /// <summary>Store credit is a named customer's: one is attached first.</summary>
    public const string CreditNeedsCustomer = "credit_needs_customer";

    /// <summary>A refund is not refunded: the sale it refunds is.</summary>
    public const string RefundOfRefund = "refund_of_refund";

    // ------------------------------------------------------------------ customers

    /// <summary>Not a telephone number.</summary>
    public const string PhoneInvalid = "phone_invalid";

    /// <summary>Not a full name.</summary>
    public const string NameInvalid = "name_invalid";

    /// <summary>No information notice is published: nobody is created without being told what is kept.</summary>
    public const string NoNotice = "no_notice";

    /// <summary>A limit above the shop's ceiling. Names: the ceiling.</summary>
    public const string LimitAboveCeiling = "limit_above_ceiling";

    /// <summary>A repayment of more than is owed. Names: what is owed.</summary>
    public const string RepayAboveBalance = "repay_above_balance";

    /// <summary>A part repayment in cash that is not a multiple of the cash step (D-108). Names: the step.</summary>
    public const string RepayNotOnStep = "repay_not_on_step";

    // ------------------------------------------------------------------ cash in and out

    /// <summary>The shop asks for a manager to take cash out of the drawer.</summary>
    public const string PaidOutNeedsManager = "paid_out_needs_manager";
}
