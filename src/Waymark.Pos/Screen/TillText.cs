using Waymark.Domain.Values;

namespace Waymark.Pos.Screen;

/// <summary>The till's two languages. French is the default (G1, 23/09).</summary>
public enum TillLanguage
{
    French,
    Arabic,
}

/// <summary>
/// Every word the till shows, in French and in Arabic, in one place (G1).
///
/// <para>
/// The screen model asks for a string by meaning and never builds a sentence itself, so a
/// language is added here and nowhere else. <b>Arabic wording comes from Hakim's Arabic board</b>
/// wherever the board has it; a string the board does not show carries <c>// ar: à relire</c>
/// and is the first thing to check.
/// </para>
/// <para>
/// Labels are capitals in French (IBM Plex Mono, tracked) and never in Arabic, where capitals do
/// not exist and tracking breaks the joins between letters (G1 kit §2). The view decides the
/// face; this class only gives the words, already in the right case.
/// </para>
/// </summary>
public abstract class TillText
{
    public static TillText French { get; } = new FrenchText();

    public static TillText Arabic { get; } = new ArabicText();

    public static TillText For(TillLanguage language) => language == TillLanguage.Arabic ? Arabic : French;

    public abstract TillLanguage Language { get; }

    public bool RightToLeft => Language == TillLanguage.Arabic;

    // ------------------------------------------------------------------ figures

    /// <summary>DZD is "DA" in French and "د.ج" in Arabic; anything else keeps its code.</summary>
    public abstract string CurrencySymbol(Currency currency);

    // ------------------------------------------------------------------ top bar

    public abstract string Online { get; }

    public abstract string Offline { get; }

    public abstract string CurrentTicket { get; }

    public abstract string NewTicket { get; }

    public abstract string Empty { get; }

    public abstract string Paid { get; }

    /// <summary>"Ticket n° 0142".</summary>
    public abstract string TicketNumber(string invoiceNumber);

    /// <summary>"14 lignes" · "14 سطرًا", with each language's own plural.</summary>
    public abstract string Lines(int count);

    // ------------------------------------------------------------------ search and notices

    /// <summary>Scan or type a code. Search by name and PLU is B1; the placeholder says only what works.</summary>
    public abstract string SearchPlaceholder { get; }

    public abstract string LastArticle { get; }

    public abstract string Ready { get; }

    /// <summary>"Dernière vente : ticket n° 0141 à 14:26 · 1 240,00".</summary>
    public abstract string LastSaleLine(string invoiceNumber, string clock, string total);

    public abstract string SaleRecorded { get; }

    /// <summary>"Ticket n° 0142 · le prochain scan ouvre un nouveau ticket".</summary>
    public abstract string SaleRecordedLine(string invoiceNumber);

    public abstract string UnknownCode { get; }

    public abstract string UnknownCodeDetail(string code);

    public abstract string NotSellable { get; }

    /// <summary>Why a known product may not be sold, in the cashier's words (D-066).</summary>
    public abstract string NotSellableReason(string? reason);

    /// <summary>"Serveur du magasin injoignable depuis 14:31".</summary>
    public abstract string OfflineSince(string clock);

    public abstract string SaleRefused { get; }

    public abstract string Problem { get; }

    // ------------------------------------------------------------------ cart

    public abstract string ColumnQuantity { get; }

    public abstract string ColumnArticle { get; }

    public abstract string ColumnUnitPrice { get; }

    public abstract string ColumnTotal { get; }

    public abstract string BeyondRecordedStock { get; }

    public abstract string PromotionalPrice { get; }

    /// <summary>
    /// "RETIRÉE", with no time (Hakim, 23/09). The cart still records when (<c>RemovedAt</c>) for
    /// B8's log; the screen does not show it.
    /// </summary>
    public abstract string Removed { get; }

    public abstract string RemoveLine { get; }

    public abstract string EmptyTitle { get; }

    public abstract string EmptyHint { get; }

    // ------------------------------------------------------------------ bottom bar

    public abstract string Subtotal { get; }

    public abstract string Discounts { get; }

    public abstract string TotalToPay { get; }

    public abstract string Collect { get; }

    /// <summary>"Espèces 3 320,00 DA": what the drawer takes, rounded to the cash step (D-034).</summary>
    public abstract string CashAmount(string amount);

    public abstract string CashDue { get; }

    public abstract string NewSale { get; }

    public abstract string EnterKey { get; }

    // ------------------------------------------------------------------ rail: after a sale

    public abstract string TicketTotal { get; }

    public abstract string CashRounding { get; }

    public abstract string NextScanOpensTicket { get; }

    // ------------------------------------------------------------------ rail: unconfirmed sale

    public abstract string SaleNotConfirmed { get; }

    public abstract string SaleNotConfirmedTitle { get; }

    public abstract string SaleNotConfirmedBody { get; }

    /// <summary>The key that says the cashier has checked, and re-opens Encaisser (D-085).</summary>
    public abstract string AcknowledgeUnconfirmed { get; }

    // ------------------------------------------------------------------ rail: Almanac

    /// <summary>The label qualifier after "ALMANAC ·", from the card's type.</summary>
    public abstract string AlmanacKind(string recommendationType);

    /// <summary>A near-expiry card's claim, written from its Because block, never its headline.</summary>
    public abstract string NearExpiryClaim(string productName, int daysToExpiry);

    /// <summary>The facts under the claim: stock, value at cost, the date.</summary>
    public abstract string NearExpiryDetail(string? unitsOnHand, string valueAtCost, string expiresOn);

    /// <summary>The primary button, from the option's intent.</summary>
    public abstract string IntentAction(string command, string serverLabel);

    public abstract string Adjust { get; }

    public abstract string Dismiss { get; }

    public abstract string NoCardsForYou { get; }

    /// <summary>"2 cartes attendent le responsable": cards above this person's rank, counted, never shown.</summary>
    public abstract string CardsAwaitingManager(int count);

    // ------------------------------------------------------------------ tickets on hold and drafts (B2)

    /// <summary>A parked tab's title: "Attente 14:05".</summary>
    public abstract string ParkedAt(string clock);

    public abstract string Park { get; }

    public abstract string CancelTicket { get; }

    /// <summary>"Brouillons", or "Brouillons (2)" when some were cancelled today.</summary>
    public abstract string DraftsKey(int count);

    public abstract string DraftsTitle { get; }

    public abstract string DraftsHint { get; }

    /// <summary>"Annulé à 14:32 · Nabil B.": when, and by whom when somebody was signed in.</summary>
    public abstract string CancelledAt(string clock, string? by);

    public abstract string ResumeTicket { get; }

    public abstract string Close { get; }

    public abstract string NoDrafts { get; }

    // ------------------------------------------------------------------ search and past tickets (B1)

    /// <summary>The field's chip: "QTÉ × 3".</summary>
    public abstract string NextCount(string count);

    // ------------------------------------------------------------------ discounts (B4)

    /// <summary>"Remise": the key under a selected line.</summary>
    public abstract string DiscountKey { get; }

    /// <summary>"Remise ticket": the rail's key.</summary>
    public abstract string TicketDiscountKey { get; }

    /// <summary>The panel's label on a line: "REMISE".</summary>
    public abstract string DiscountLabel { get; }

    /// <summary>The panel's label on the ticket, and the ticket's discount row: "REMISE TICKET".</summary>
    public abstract string TicketDiscountLabel { get; }

    /// <summary>The field's chip while a discount is typed: "REMISE · %".</summary>
    public abstract string DiscountChip(string unit);

    /// <summary>"MOTIF".</summary>
    public abstract string ReasonTitle { get; }

    public abstract string ContinueKey { get; }

    public abstract string BackToTicket { get; }

    public abstract string RemoveDiscount { get; }

    /// <summary>What to type, before anything is.</summary>
    public abstract string DiscountPrompt(bool percent);

    /// <summary>The text typed is not a discount.</summary>
    public abstract string DiscountInvalid(bool percent);

    public abstract string ChooseReason { get; }

    /// <summary>The shop has no discount reason: none can be recorded, so none can be given.</summary>
    public abstract string NoDiscountReasons { get; }

    public abstract string DiscountOffline { get; }

    // ------------------------------------------------------------------ price overrides (B5)

    /// <summary>"Prix": the key under a selected line.</summary>
    public abstract string PriceKey { get; }

    /// <summary>The panel's label and the line's chip: "PRIX MODIFIÉ".</summary>
    public abstract string PriceOverrideLabel { get; }

    /// <summary>"Prix en vigueur 143,00 DA".</summary>
    public abstract string PriceInForce(string price);

    /// <summary>"Nouveau total 240,00 DA".</summary>
    public abstract string NewTotal(string total);

    public abstract string PricePrompt { get; }

    public abstract string PriceInvalid { get; }

    /// <summary>Above the band: the most that may be charged, and what to do instead.</summary>
    public abstract string PriceAboveBand(string ceiling);

    public abstract string PriceNotAboveZero { get; }

    public abstract string PriceUnchanged { get; }

    /// <summary>A warning, not a refusal: below what the goods cost.</summary>
    public abstract string PriceBelowCost { get; }

    public abstract string BackToListPrice { get; }

    public abstract string NoOverrideReasons { get; }

    public abstract string ApproveOverride { get; }

    /// <summary>The field's chip while a note is typed: "NOTE".</summary>
    public abstract string NoteChip { get; }

    /// <summary>What the note is for: "Précisez « Autre »".</summary>
    public abstract string NotePrompt(string reason);

    // ------------------------------------------------------------ B6: the payment panel

    /// <summary>The panel's title, "Encaisser".</summary>
    public abstract string PaymentTitle { get; }

    /// <summary>"Ticket en cours · 14 lignes".</summary>
    public abstract string PaymentSubtitle(int lines);

    /// <summary>The big figure's label: the cash left, rounded.</summary>
    public abstract string CashToCollect { get; }

    public abstract string PartsTitle { get; }

    /// <summary>With no part: "Aucune part : tout le ticket en espèces."</summary>
    public abstract string NoParts { get; }

    public abstract string MethodCash { get; }

    public abstract string MethodCard { get; }

    public abstract string MethodWallet { get; }

    /// <summary>"MONTANT CARTE", "MONTANT BARIDIMOB".</summary>
    public abstract string PartAmountTitle(string method);

    public abstract string ReferenceTitle { get; }

    public abstract string ReferencePlaceholder { get; }

    /// <summary>"réf 4417", beside a part.</summary>
    public abstract string PartReference(string reference);

    public abstract string RestAfterPart { get; }

    public abstract string WholeRest { get; }

    public abstract string NoChangeOnCard { get; }

    public abstract string PartPrefilled { get; }

    /// <summary>In cash: how to leave without changing anything.</summary>
    public abstract string PaymentCloseHint { get; }

    public abstract string AddPart { get; }

    public abstract string PaymentValidate { get; }

    public abstract string AboveRest { get; }

    public abstract string AboveRestDetail(string rest);

    public abstract string PartInvalid { get; }

    public abstract string PartInvalidDetail { get; }

    public abstract string ReferenceRefused { get; }

    public abstract string ReferenceLooksLikeCard { get; }

    public abstract string ReferenceInvalidDetail { get; }

    /// <summary>A refused sale, in the panel: its parts are kept.</summary>
    public abstract string SaleRefusedInPanel { get; }

    /// <summary>The notice slot behind the payment panel: scans are ignored while it is open.</summary>
    public abstract string PayingNotice { get; }

    public abstract string PayingNoticeDetail { get; }

    /// <summary>The same behind the manager step.</summary>
    public abstract string ApprovingNotice { get; }

    public abstract string ApprovingNoticeDetail { get; }

    /// <summary>"Échap", in the ✕ key's corner (Entrée is <see cref="EnterKey"/>).</summary>
    public abstract string EscapeKey { get; }


    // ------------------------------------------------------------ B7: customers and the tab at the till

    // B10: petite caisse, pointage
    public abstract string PettyCashKey { get; }

    public abstract string MoreOperations { get; }

    public abstract string BackFromMore { get; }

    public abstract string ClockKey { get; }

    public abstract string PettyCashTitle { get; }

    public abstract string PettyCashSubtitle { get; }

    public abstract string ChooseCashDirection { get; }

    public abstract string CashInKey { get; }

    public abstract string CashOutKey { get; }

    public abstract string AmountTitle { get; }

    public abstract string NoteTitle { get; }

    public abstract string RecordKey { get; }

    public abstract string CashInRecorded { get; }

    public abstract string CashOutRecorded { get; }

    public abstract string ReasonLabel { get; }

    public abstract string ClockTitle { get; }

    public abstract string ClockSubtitle { get; }

    public abstract string WhoClocks { get; }

    public abstract string ClockPinTitle { get; }

    public abstract string ClockPrimary { get; }

    public abstract string ClockedInTitle { get; }

    public abstract string ClockedOutTitle { get; }

    public abstract string OnDutySince { get; }

    // B9b: store credit spent
    public abstract string CreditAvailableLabel { get; }

    public abstract string CreditAmountTitle { get; }

    public abstract string AboveCredit { get; }

    public abstract string AboveCreditDetail(string name, string available);

    public abstract string CreditPrefilled { get; }

    public abstract string CreditOf(string name);

    public abstract string RefundToCredit { get; }

    public abstract string NameOrPhoneTitle { get; }

    public abstract string NameOrPhoneRule { get; }

    public abstract string NameIncomplete { get; }

    public abstract string NameIncompleteDetail { get; }

    public abstract string TooManyNamed { get; }

    public abstract string ResultsAfterPause { get; }

    public abstract string ClientKey { get; }

    public abstract string ClientByPhone { get; }

    public abstract string ClientAttached { get; }

    public abstract string ClientAttachedDetail(string name);

    public abstract string ClientRefused { get; }

    public abstract string NotAPhone { get; }

    public abstract string NotAPhoneDetail(string phone);

    public abstract string AttachToRefund { get; }

    public abstract string AttachToTicket { get; }

    public abstract string CreateThisClient { get; }

    public abstract string SearchClient { get; }

    public abstract string ClientTitle { get; }

    public abstract string ClientForRefund { get; }

    public abstract string ClientForTicket(int lines);

    public abstract string PhoneTitle { get; }

    public abstract string PhoneRule { get; }

    public abstract string ResultTitle { get; }

    public abstract string ResultsTitle(int count);

    public abstract string NoSearchYet { get; }

    public abstract string NoClientWithNumber { get; }

    public abstract string ConsultationLogged { get; }

    public abstract string CreateNeedsManager { get; }

    public abstract string TicketFrozenWhileSearching { get; }

    public abstract string FieldsToCorrect(int count);

    public abstract string FieldsToCorrectDetail { get; }

    public abstract string NewClientTitle { get; }

    public abstract string NewClientSubtitle { get; }

    public abstract string NameTitle { get; }

    public abstract string NameAndPhoneOnly { get; }

    public abstract string NoticeToHand { get; }

    public abstract string CreateAndAttach { get; }

    public abstract string CarnetLabel { get; }

    public abstract string OpenTabTitle(string name);

    public abstract string ChangeTabTitle(string name);

    public abstract string ChangeTabSubtitle { get; }

    public abstract string CurrentLimit { get; }

    public abstract string BalanceDue { get; }

    public abstract string NewLimit { get; }

    public abstract string CloseTabNote { get; }

    public abstract string ChangeRefused { get; }

    public abstract string SetLimit { get; }

    public abstract string OwnerPinAsked { get; }

    public abstract string UnfreezeKey { get; }

    public abstract string FreezeKey { get; }

    public abstract string CloseTabKey { get; }

    public abstract string RepayRefused { get; }

    public abstract string AboveDue { get; }

    public abstract string AboveDueDetail(string name, string balance);

    public abstract string NothingRepaid { get; }

    public abstract string NothingRepaidDetail { get; }

    public abstract string NoCashReasons { get; }

    public abstract string RepayTitle(string name);

    public abstract string RepaySubtitle { get; }

    public abstract string AmountRepaid { get; }

    public abstract string CashReasonTitle { get; }

    /// <summary>"MOTIF DE SORTIE DE CAISSE": over the reasons for cash out (B10); <see cref="CashReasonTitle"/> is cash in's.</summary>
    public abstract string CashOutReasonTitle { get; }

    // ------------------------------------------------------------------ what the server refused (D-107)

    /// <summary>
    /// A server's refusal in the till's language (D-107): the code's sentence, with what it names
    /// already written the till's way; null for a code this till does not know.
    /// </summary>
    public abstract string? Refusal(string code, IReadOnlyList<string> args);

    /// <summary>"Refusé par le serveur": said before the server's own words, for a refusal with no sentence here.</summary>
    public abstract string RefusedByServer { get; }

    /// <summary>The <paramref name="index"/>th thing a refusal names, or "?" when the server named fewer.</summary>
    protected static string Named(IReadOnlyList<string> args, int index) => index < args.Count ? args[index] : "?";

    // ------------------------------------------------------------------ a line struck after "Encaisser" (D-106)

    /// <summary>The manager step's title when a cashier strikes a line once payment was opened.</summary>
    public abstract string StrikeApprovalTitle { get; }

    public abstract string StrikeApprovalSummary(string article);

    /// <summary>"LIGNES RETIRÉES": the ticket holds only struck lines, and the cashier asked to leave it (D-106).</summary>
    public abstract string StruckTicketOpen { get; }

    public abstract string CancelBeforeSwitching { get; }

    // ------------------------------------------------------------------ an older ticket opened with a PIN (D-109)

    public abstract string OpenTicketApprovalTitle { get; }

    public abstract string OpenTicketApprovalSummary(string number);

    // ------------------------------------------------------------------ a tab repaid in cash steps (D-108)

    /// <summary>"Espèces à encaisser": what the drawer takes for a repayment, the due rounded to the cash step.</summary>
    public abstract string CashTaken { get; }

    public abstract string RepayOnStep { get; }

    public abstract string RepayOnStepDetail(string cashStep);

    /// <summary>"RETOUR ENREGISTRÉ": the notice once a refund in cash is written.</summary>
    public abstract string RefundRecorded { get; }

    public abstract string CashToHandBack(string amount);

    /// <summary>"2 EN ATTENTE": the count of tickets on hold, beside their tabs in the top bar.</summary>
    public abstract string HeldCount(int tickets);

    /// <summary>"PIN PROPRIÉTAIRE": over the PIN of a step only the owner may approve.</summary>
    public abstract string OwnerPin { get; }

    /// <summary>"Total HT": a past ticket's total before TVA, which is what its <c>subtotal</c> holds.</summary>
    public abstract string TotalBeforeTax { get; }

    public abstract string WholeDue(string amount);

    public abstract string CashIn { get; }

    public abstract string RepaidTitle { get; }

    public abstract string NewBalanceDue { get; }

    public abstract string BalanceBefore { get; }

    public abstract string RepaidInCash { get; }

    public abstract string Finish { get; }

    public abstract string ReturnLabel { get; }

    public abstract string CreditIssuedTitle { get; }

    public abstract string ReturnNumber(string number);

    public abstract string CreditAfterReturn(string name);

    public abstract string CreditBefore { get; }

    public abstract string IssuedForReturn(string number);

    public abstract string OpeningLogged(string clock, string staff);

    public abstract string CarnetOffline { get; }

    public abstract string StatementTitle { get; }

    public abstract string ChangeTabKey { get; }

    public abstract string OpenTabKey { get; }

    public abstract string RepayKey { get; }

    public abstract string TabFrozen { get; }

    public abstract string TabFrozenDetail { get; }

    public abstract string TabOverdue { get; }

    public abstract string TabOverdueDetail(int days, int rule);

    public abstract string NoTab { get; }

    public abstract string NoTabDetail { get; }

    public abstract string MovementLabel(string kind, bool negative);

    public abstract string CarnetTitle(string name);

    public abstract string CarnetSubtitle { get; }

    public abstract string Limit { get; }

    public abstract string Available { get; }

    public abstract string OldestUnpaid { get; }

    public abstract string DaysOf(int days, int rule);

    public abstract string Days(int days);

    public abstract string ColumnDate { get; }

    public abstract string ColumnMovement { get; }

    public abstract string ColumnAmount { get; }

    public abstract string ColumnBalance { get; }

    public abstract string NoMovement { get; }

    public abstract string MethodTab { get; }

    public abstract string TabOf(string name);

    public abstract string TabAvailable { get; }

    public abstract string AfterThisSale { get; }

    public abstract string TabAmountTitle { get; }

    public abstract string AboveLimit { get; }

    public abstract string AboveLimitDetail(string available, string amount);

    public abstract string TabFrozenRefused { get; }

    public abstract string TabFrozenRefusedDetail { get; }

    public abstract string TabOverdueRefused { get; }

    public abstract string TabOverdueRefusedDetail { get; }

    public abstract string WholeTicketOnly { get; }

    /// <summary>Under a tab part being typed: the tab takes the exact amount, never more than is left. Not the card's sentence.</summary>
    public abstract string TabPartExact { get; }

    public abstract string TabNotWithParts { get; }

    public abstract string TabNotWithPartsDetail { get; }

    public abstract string ValidateOnTab { get; }

    public abstract string OverrideWithOwnerPin { get; }

    public abstract string CreditIsNamed { get; }

    public abstract string CreditIsNamedDetail { get; }

    public abstract string AttachClient { get; }

    public abstract string ApproveCreateTitle(string name);

    public abstract string ApproveChangeTitle(string name);

    public abstract string ApproveOverrideTitle(string name);

    public abstract string OwnerApproval { get; }

    public abstract string ApproveValidate { get; }

    // ------------------------------------------------------------ B9: a refund linked to its sale

    /// <summary>The key on a past ticket: "Rembourser".</summary>
    public abstract string RefundKey { get; }

    /// <summary>The refund panel's label, and the manager step's title: "REMBOURSEMENT".</summary>
    public abstract string RefundLabel { get; }

    /// <summary>What the panel says while no line is touched.</summary>
    public abstract string RefundPrompt { get; }

    /// <summary>The line touched: how much of it comes back, of what is left of it.</summary>
    public abstract string RefundLineDetail(string article, string back, string left);

    /// <summary>"− 1", "+ 1" and "En rayon": the line's keys in the panel.</summary>
    public abstract string LessKey { get; }

    public abstract string MoreKey { get; }

    public abstract string RestockKey { get; }

    /// <summary>The line's chips: what comes back, whether on the shelf, and what came back before.</summary>
    public abstract string ReturnChip(string quantity);

    public abstract string RestockChip { get; }

    public abstract string NoRestockChip { get; }

    public abstract string ReturnedChip(string quantity);

    /// <summary>How many lines come back: the panel's preview.</summary>
    public abstract string RefundChosen(int lines);

    public abstract string NoReturnReasons { get; }

    public abstract string ChooseLines { get; }

    public abstract string RefundOffline { get; }

    /// <summary>A refused refund's title; the server's reason is its body.</summary>
    public abstract string RefundRefused { get; }

    public abstract string ApproveRefund { get; }

    /// <summary>The floating panel: "Rembourser", the figures, then what goes out.</summary>
    public abstract string RefundTitle { get; }

    public abstract string RefundTotal { get; }

    public abstract string RefundToTab { get; }

    public abstract string CashOut { get; }

    public abstract string CreditOut { get; }

    public abstract string MethodStoreCredit { get; }

    public abstract string RefundLinesTitle { get; }

    public abstract string RefundLinesSummary(int lines, int restocked);

    public abstract string RefundCloseHint { get; }

    /// <summary>A refund ticket's notice: whose sale it refunds.</summary>
    public abstract string RefundOfTicket(string number);

    /// <summary>The notice while a refund is prepared.</summary>
    public abstract string RefundingNotice { get; }

    public abstract string RefundingNoticeDetail { get; }

    // ------------------------------------------------------------ B8: cancelling a ticket

    /// <summary>The cancel panel's label, and the manager step's title: "ANNULER LE TICKET".</summary>
    public abstract string CancelLabel { get; }

    /// <summary>What a cancel does, with the ticket's total.</summary>
    public abstract string CancelDetail(string total);

    /// <summary>The key that goes on: "Annuler le ticket".</summary>
    public abstract string CancelConfirm { get; }

    /// <summary>The field's chip while the cancel panel is open.</summary>
    public abstract string CancelChip { get; }

    /// <summary>A warning, not a refusal: the payment panel had been opened on this ticket.</summary>
    public abstract string PaymentStarted { get; }

    public abstract string NoVoidReasons { get; }

    public abstract string CancelRefused { get; }

    public abstract string CancelOffline { get; }

    public abstract string ApproveCancel { get; }

    /// <summary>"AUTORISATION RESPONSABLE".</summary>
    public abstract string ManagerApproval { get; }

    /// <summary>"Qui autorise ?".</summary>
    public abstract string WhoApproves { get; }

    /// <summary>"PIN RESPONSABLE".</summary>
    public abstract string ManagerPin { get; }

    public abstract string ApproveDiscount { get; }

    public abstract string WrongManagerPin(int attemptsLeft);

    public abstract string ManagerLocked(string until);

    public abstract string NotAManager { get; }

    public abstract string ManagerHasNoPin { get; }

    // ------------------------------------------------------------------ weighed goods (B3)

    /// <summary>The field's chip while a weight is awaited: "POIDS · kg".</summary>
    public abstract string WeightChip(string unit);

    /// <summary>The card's label: "POIDS".</summary>
    public abstract string WeighTitle { get; }

    /// <summary>"180,00 DA / kg".</summary>
    public abstract string PerUnit(string price, string unit);

    /// <summary>What the card asks for before anything is typed.</summary>
    public abstract string WeighPrompt(string unit);

    /// <summary>"Entrée pèse · Échap annule".</summary>
    public abstract string WeighKeys { get; }

    /// <summary>The text typed is not a weight the unit is sold in.</summary>
    public abstract string WeighInvalid(int decimals);

    /// <summary>A line's chip: its weight was typed at the till.</summary>
    public abstract string TypedWeight { get; }

    /// <summary>A line's chip: its weight was read from a scale label.</summary>
    public abstract string LabelWeight { get; }

    /// <summary>A line's chip: its price was read from a scale label.</summary>
    public abstract string LabelPrice { get; }

    /// <summary>"Poids": the key under a line weighed by hand.</summary>
    public abstract string Reweigh { get; }

    public abstract string CountIgnored { get; }

    public abstract string CountIgnoredDetail { get; }

    /// <summary>"stock 12".</summary>
    public abstract string Stock(string quantity);

    public abstract string Searching { get; }

    public abstract string SearchOffline { get; }

    /// <summary>"Aucun article ne répond à « lait x »".</summary>
    public abstract string NoResults(string query);

    public abstract string TicketsKey { get; }

    public abstract string TicketsTitle { get; }

    /// <summary>"Aujourd'hui · 25/09".</summary>
    public abstract string TodayLabel(string dayAndMonth);

    public abstract string ThisTill { get; }

    public abstract string AllTills { get; }

    /// <summary>The key that switches scope: to every till, or back to this one.</summary>
    public abstract string OtherScope(bool allTills);

    public abstract string NoTickets { get; }

    public abstract string TicketsNotAllowed { get; }

    public abstract string TicketUnknown { get; }

    /// <summary>"S-2026-000142 — aucune vente de ce magasin ne porte ce numéro".</summary>
    public abstract string TicketUnknownDetail(string number);

    public abstract string ManagerOnly { get; }

    public abstract string StatusVoided { get; }

    public abstract string StatusRefunded { get; }

    public abstract string StatusPartlyRefunded { get; }

    public abstract string PastTicket { get; }

    /// <summary>"Vendu le 25/09 à 14:05 par Nabil B. · lecture seule".</summary>
    public abstract string PastTicketLine(string dayAndMonth, string clock, string? staff);

    public abstract string PastTicketFooter { get; }

    public abstract string TaxIncluded { get; }

    /// <summary>
    /// A payment row's method by the name the payment panel gave it: one list of names, so a past
    /// ticket cannot call a part something else than the panel that took it (block B review: "Mobile"
    /// for "BaridiMob", and cash spelled two ways in Arabic). Anything else as the wire says it.
    /// </summary>
    public string PaymentMethod(string method) => method switch
    {
        "cash" => MethodCash,
        "card" => MethodCard,
        "mobile_wallet" => MethodWallet,
        "store_credit" => MethodStoreCredit,
        "on_account" => MethodTab,
        _ => method,
    };

    // ------------------------------------------------------------------ sign-in (A5)

    public abstract string WhoOpensTheTill { get; }

    public abstract string ChooseYourName { get; }

    /// <summary>"Code PIN de Nabil B.".</summary>
    public abstract string PinOf(string name);

    public abstract string ClearKey { get; }

    public abstract string OpenTheTill { get; }

    /// <summary>A person with no PIN set, on their row and as a message.</summary>
    public abstract string NoPinLabel { get; }

    public abstract string NoPinDetail { get; }

    public abstract string NobodyMaySignIn { get; }

    public abstract string NobodyMaySignInHint { get; }

    public abstract string WrongPin { get; }

    /// <summary>"Encore 3 essais avant le blocage".</summary>
    public abstract string AttemptsLeft(int count);

    public abstract string Locked { get; }

    /// <summary>"Trop d'essais. Réessayez à 08:06.".</summary>
    public abstract string LockedUntil(string clock);

    public abstract string UnknownStaff { get; }

    public abstract string UnknownStaffDetail { get; }

    public abstract string UnknownTerminal { get; }

    public abstract string UnknownTerminalDetail { get; }

    public abstract string NoTerminalDetail { get; }

    public abstract string SessionEnded { get; }

    public abstract string SessionEndedDetail { get; }

    /// <summary>"Changer de caissier" refused: the ticket still has lines in the sale.</summary>
    public abstract string TicketInProgress { get; }

    public abstract string FinishBeforeSwitching { get; }

    // ================================================================== French

    private sealed class FrenchText : TillText
    {
        public override TillLanguage Language => TillLanguage.French;

        public override string CurrencySymbol(Currency currency) => currency == Currency.Dzd ? "DA" : currency.Code;

        public override string Online => "EN LIGNE";
        public override string Offline => "HORS LIGNE";
        public override string CurrentTicket => "Ticket en cours";
        public override string NewTicket => "Nouveau ticket";
        public override string Empty => "vide";
        public override string Paid => "payé";
        public override string TicketNumber(string invoiceNumber) => $"Ticket n° {invoiceNumber}";
        public override string Lines(int count) => count == 1 ? "1 ligne" : $"{DisplayFigures.Count(count)} lignes";

        public override string SearchPlaceholder => "Recherchez un produit ou saisissez le code-barres";
        public override string LastArticle => "DERNIER ARTICLE";
        public override string Ready => "PRÊT";
        public override string LastSaleLine(string invoiceNumber, string clock, string total) =>
            $"Dernière vente : ticket n° {invoiceNumber} à {clock} · {total}";
        public override string SaleRecorded => "VENTE ENREGISTRÉE";
        public override string SaleRecordedLine(string invoiceNumber) =>
            $"Ticket n° {invoiceNumber} · le prochain scan ouvre un nouveau ticket";
        public override string UnknownCode => "CODE INCONNU";
        public override string UnknownCodeDetail(string code) => $"{code} — aucun article ne porte ce code";
        public override string NotSellable => "NON VENDABLE";
        public override string NotSellableReason(string? reason) => reason switch
        {
            Contracts.Pos.NotSellableReason.NoCurrentPrice => "aucun prix en vigueur aujourd'hui",
            Contracts.Pos.NotSellableReason.PriceNotTaxInclusive => "le prix est enregistré hors taxe ; la caisse vend en TTC",
            Contracts.Pos.NotSellableReason.Archived => "article archivé",
            Contracts.Pos.NotSellableReason.NotSoldByWeight => "vendu à la pièce, pas au poids",
            Contracts.Pos.NotSellableReason.WeightInvalid => "poids refusé : au-dessus de zéro, et pas plus fin que l'unité",
            Contracts.Pos.NotSellableReason.LabelNotSetUp => "étiquette de balance pour un article non vendu au poids : à corriger au catalogue",
            Contracts.Pos.NotSellableReason.LabelValueInvalid => "étiquette illisible : poids ou prix impossible à vendre",
            Contracts.Pos.NotSellableReason.NoCode => "ni code-barres ni PLU : l'article ne peut pas être vendu",
            _ => $"refusé ({reason})",
        };
        public override string OfflineSince(string clock) => $"Serveur du magasin injoignable depuis {clock}";
        public override string SaleRefused => "VENTE REFUSÉE";
        public override string Problem => "PROBLÈME";

        public override string ColumnQuantity => "QTÉ";
        public override string ColumnArticle => "ARTICLE";
        public override string ColumnUnitPrice => "P.U.";
        public override string ColumnTotal => "TOTAL";
        public override string BeyondRecordedStock => "AU-DELÀ DU STOCK ENREGISTRÉ";
        public override string PromotionalPrice => "PRIX PROMOTIONNEL";
        public override string Removed => "RETIRÉE";
        public override string RemoveLine => "Retirer la ligne";
        public override string EmptyTitle => "Le ticket est vide";
        public override string EmptyHint => "Scannez un article.";

        public override string Subtotal => "Sous-total";
        public override string Discounts => "Remises";
        public override string TotalToPay => "TOTAL À PAYER";
        public override string Collect => "Encaisser";
        public override string CashAmount(string amount) => $"Espèces {amount}";
        public override string CashDue => "ESPÈCES DUES";
        public override string NewSale => "Nouvelle vente";
        public override string EnterKey => "Entrée";

        public override string TicketTotal => "Total du ticket";
        public override string CashRounding => "Arrondi espèces";
        public override string NextScanOpensTicket => "Le prochain scan ouvre un nouveau ticket.";

        public override string SaleNotConfirmed => "VENTE NON CONFIRMÉE";
        // "Confirmé", not "enregistré": whether it was recorded is exactly what the till does not know.
        public override string SaleNotConfirmedTitle => "Le serveur du magasin n'a pas confirmé le ticket.";
        public override string SaleNotConfirmedBody =>
            "Ne rendez pas la monnaie et gardez la marchandise tant que la vente n'est pas confirmée. "
            + "Vérifiez si elle a été enregistrée : Encaisser reste indisponible jusque-là. Le ticket reste ouvert.";
        public override string AcknowledgeUnconfirmed => "J'ai vérifié";

        public override string AlmanacKind(string recommendationType) => recommendationType switch
        {
            "near_expiry" => "PÉREMPTION",
            _ => "PRÉVISION",
        };
        public override string NearExpiryClaim(string productName, int daysToExpiry) => daysToExpiry switch
        {
            < -1 => $"{productName} : périmé depuis {-daysToExpiry} jours",
            -1 => $"{productName} : périmé depuis hier",
            0 => $"{productName} : péremption aujourd'hui",
            1 => $"{productName} : péremption demain",
            _ => $"{productName} : péremption dans {daysToExpiry} jours",
        };
        public override string NearExpiryDetail(string? unitsOnHand, string valueAtCost, string expiresOn) =>
            unitsOnHand is null
                ? $"{valueAtCost} au prix d'achat. Le lot expire le {expiresOn}."
                : $"{unitsOnHand} en stock, {valueAtCost} au prix d'achat. Le lot expire le {expiresOn}.";
        public override string IntentAction(string command, string serverLabel) => command switch
        {
            "apply_markdown" => "Démarquer",
            "write_off_batch" => "Sortir du stock",
            _ => serverLabel,
        };
        public override string Adjust => "Ajuster";
        public override string Dismiss => "Ignorer";
        public override string NoCardsForYou => "Aucune carte pour vous pour l'instant.";
        public override string CardsAwaitingManager(int count) => count == 1
            ? "1 carte attend le responsable"
            : $"{DisplayFigures.Count(count)} cartes attendent le responsable";

        public override string ParkedAt(string clock) => $"Attente {clock}";
        public override string Park => "Attente";
        public override string CancelTicket => "Annuler ticket";
        public override string DraftsKey(int count) => count == 0 ? "Brouillons" : $"Brouillons ({DisplayFigures.Count(count)})";
        public override string DraftsTitle => "BROUILLONS";
        public override string DraftsHint => "Les tickets annulés aujourd'hui. Ils disparaissent à la fin de la journée.";
        public override string CancelledAt(string clock, string? by) => by is null ? $"Annulé à {clock}" : $"Annulé à {clock} · {by}";
        public override string ResumeTicket => "Reprendre";
        public override string Close => "Fermer";
        public override string NoDrafts => "Aucun ticket annulé aujourd'hui.";

        public override string NextCount(string count) => $"QTÉ × {count}";

        public override string DiscountKey => "Remise";
        public override string TicketDiscountKey => "Remise ticket";
        public override string DiscountLabel => "REMISE";
        public override string TicketDiscountLabel => "REMISE TICKET";
        public override string DiscountChip(string unit) => $"REMISE · {unit}";
        public override string ReasonTitle => "MOTIF";
        public override string ContinueKey => "Continuer";
        public override string BackToTicket => "Revenir au ticket";
        public override string RemoveDiscount => "Retirer la remise";
        public override string DiscountPrompt(bool percent) => percent
            ? "Tapez le pourcentage dans le champ, puis choisissez le motif"
            : "Tapez le montant dans le champ, puis choisissez le motif";
        public override string DiscountInvalid(bool percent) => percent
            ? "Un pourcentage de 0,01 à 100, 2 décimales au plus"
            : "Un montant au-dessus de zéro, 2 décimales au plus";
        public override string ChooseReason => "Choisissez un motif";
        public override string NoDiscountReasons => "Aucun motif de remise n'est défini : une remise ne peut pas être enregistrée";
        public override string DiscountOffline => "Serveur du magasin injoignable : la remise attend";
        public override string PriceKey => "Prix";
        public override string PriceOverrideLabel => "PRIX MODIFIÉ";
        public override string PriceInForce(string price) => $"Prix en vigueur {price}";
        public override string NewTotal(string total) => $"Nouveau total {total}";
        public override string PricePrompt => "Tapez le nouveau prix unitaire dans le champ, puis choisissez le motif";
        public override string PriceInvalid => "Un prix au-dessus de zéro, 2 décimales au plus";
        public override string PriceAboveBand(string ceiling) => $"Au plus {ceiling} (20 % au-dessus) : au-delà, corrigez le prix au catalogue";
        public override string PriceNotAboveZero => "Un prix au-dessus de zéro : pour offrir l'article, une remise de 100 %";
        public override string PriceUnchanged => "C'est déjà le prix en vigueur";
        public override string PriceBelowCost => "SOUS LE PRIX D'ACHAT · le propriétaire valide en connaissance de cause";
        public override string BackToListPrice => "Revenir au prix en vigueur";
        public override string NoOverrideReasons => "Aucun motif de prix modifié n'est défini : le prix ne peut pas être modifié";
        public override string ApproveOverride => "Valider le prix";
        public override string NoteChip => "NOTE";
        public override string NotePrompt(string reason) => $"Précisez « {reason} » dans le champ, puis Entrée";
        public override string PaymentTitle => "Encaisser";
        public override string PaymentSubtitle(int lines) => $"Ticket en cours · {lines} ligne{(lines > 1 ? "s" : string.Empty)}";
        public override string CashToCollect => "Espèces à encaisser";
        public override string PartsTitle => "PARTS";
        public override string NoParts => "Aucune part : tout le ticket en espèces.";
        public override string MethodCash => "Espèces";
        public override string MethodCard => "Carte";
        public override string MethodWallet => "BaridiMob";
        public override string PartAmountTitle(string method) => $"MONTANT {method.ToUpperInvariant()}";
        public override string ReferenceTitle => "RÉFÉRENCE · FACULTATIF";
        public override string ReferencePlaceholder => "4 derniers chiffres ou n° d'autorisation";
        public override string PartReference(string reference) => $"réf {reference}";
        public override string RestAfterPart => "RESTE APRÈS CETTE PART";
        public override string WholeRest => "Tout le reste";
        public override string NoChangeOnCard => "Une carte ne rend pas de monnaie : jamais plus que le reste.";
        public override string PartPrefilled => "Préremplie avec le reste — tapez pour remplacer. Entrée ajoute la part.";
        public override string PaymentCloseHint => "Échap ou ✕ ferme sans rien changer au ticket.";
        public override string AddPart => "Ajouter la part";
        public override string PaymentValidate => "Valider";
        public override string AboveRest => "MONTANT TROP ÉLEVÉ";
        public override string AboveRestDetail(string rest) => $"La part ne peut pas dépasser le reste à payer, {rest} : elle ne rend pas de monnaie.";
        public override string PartInvalid => "MONTANT INVALIDE";
        public override string PartInvalidDetail => "Un montant au-dessus de zéro, deux décimales au plus.";
        public override string ReferenceRefused => "RÉFÉRENCE REFUSÉE";
        public override string ReferenceLooksLikeCard => "Elle ressemblait à un numéro de carte : elle a été effacée et n'est gardée nulle part. Saisissez les 4 derniers chiffres ou le n° d'autorisation.";
        public override string ReferenceInvalidDetail => "Lettres, chiffres, espaces et tirets seulement, 32 au plus.";
        public override string SaleRefusedInPanel => "VENTE REFUSÉE · les parts sont gardées";
        public override string PayingNotice => "ENCAISSEMENT EN COURS";
        public override string PayingNoticeDetail => "Les scans sont ignorés tant que le paiement est ouvert.";
        public override string ApprovingNotice => "AUTORISATION EN COURS";
        public override string ApprovingNoticeDetail => "Les scans sont ignorés tant que l'autorisation est ouverte.";
        public override string EscapeKey => "Échap";
        public override string CancelLabel => "ANNULER LE TICKET";
        public override string CancelDetail(string total) => $"{total} · rien n'est vendu ; l'annulation est enregistrée";
        public override string CancelConfirm => "Annuler le ticket";
        public override string CancelChip => "ANNULATION";
        public override string PaymentStarted => "PAIEMENT COMMENCÉ · un responsable valide l'annulation de ce ticket";
        public override string NoVoidReasons => "Aucun motif d'annulation n'est défini : le ticket ne peut pas être annulé";
        public override string CancelRefused => "Le serveur a refusé l'annulation : le ticket reste à l'écran";
        public override string CancelOffline => "Serveur injoignable : rien n'est enregistré, le ticket reste. Mettez-le en attente";
        public override string ApproveCancel => "Valider l'annulation";
        public override string ManagerApproval => "AUTORISATION RESPONSABLE";
        public override string WhoApproves => "QUI AUTORISE";
        public override string ManagerPin => "PIN RESPONSABLE";
        public override string ApproveDiscount => "Valider la remise";
        public override string WrongManagerPin(int attemptsLeft) => $"PIN incorrect · {attemptsLeft} essai{(attemptsLeft > 1 ? "s" : string.Empty)} avant blocage";
        public override string ManagerLocked(string until) => $"Trop d'essais : bloqué jusqu'à {until}";
        public override string NotAManager => "Cette personne ne peut pas donner cette autorisation";
        public override string ManagerHasNoPin => "Aucun PIN n'est défini pour cette personne";

        public override string WeightChip(string unit) => $"POIDS · {unit}";
        public override string WeighTitle => "POIDS";
        public override string PerUnit(string price, string unit) => $"{price} / {unit}";
        public override string WeighPrompt(string unit) => $"Tapez le poids en {unit}, puis Entrée";
        public override string WeighKeys => "Entrée pèse · Échap annule";
        public override string WeighInvalid(int decimals) => decimals == 0
            ? "Un poids entier, au-dessus de zéro"
            : $"Un poids au-dessus de zéro, {decimals} décimale{(decimals > 1 ? "s" : string.Empty)} au plus : 0,556";
        public override string TypedWeight => "POIDS SAISI";
        public override string LabelWeight => "ÉTIQUETTE";
        public override string LabelPrice => "ÉTIQUETTE PRIX";
        public override string Reweigh => "Poids";
        public override string CountIgnored => "QUANTITÉ IGNORÉE";
        public override string CountIgnoredDetail => "un article pesé se vend à son poids, pas à la quantité";
        public override string Stock(string quantity) => $"stock {quantity}";
        public override string Searching => "Recherche…";
        public override string SearchOffline => "Serveur du magasin injoignable : rien à montrer pour l'instant.";
        public override string NoResults(string query) => $"Aucun article ne répond à « {query} ».";
        public override string TicketsKey => "Tickets";
        public override string TicketsTitle => "TICKETS";
        public override string TodayLabel(string dayAndMonth) => $"Aujourd'hui · {dayAndMonth}";
        public override string ThisTill => "Cette caisse";
        public override string AllTills => "Toutes les caisses";
        public override string OtherScope(bool allTills) => allTills ? "Cette caisse" : "Toutes les caisses";
        public override string NoTickets => "Aucune vente ce jour-là.";
        public override string TicketsNotAllowed => "Un autre jour ou une autre caisse : réservé au responsable.";
        public override string TicketUnknown => "TICKET INTROUVABLE";
        public override string TicketUnknownDetail(string number) => $"{number} — aucune vente de ce magasin ne porte ce numéro";
        public override string ClientKey => "Client";
        public override string PettyCashKey => "Petite caisse";
        public override string MoreOperations => "Plus…";
        public override string BackFromMore => "Retour";
        public override string ClockKey => "Pointage";
        public override string PettyCashTitle => "Petite caisse";
        public override string PettyCashSubtitle => "Hors vente · enregistrée avec son motif";
        public override string ChooseCashDirection => "Entrée ou sortie de caisse : choisissez d'abord.";
        public override string CashInKey => "Entrée de caisse";
        public override string CashOutKey => "Sortie de caisse";
        public override string AmountTitle => "MONTANT";
        public override string NoteTitle => "NOTE";
        public override string RecordKey => "Enregistrer";
        public override string CashInRecorded => "Entrée de caisse enregistrée";
        public override string CashOutRecorded => "Sortie de caisse enregistrée";
        public override string ReasonLabel => "Motif";
        public override string ClockTitle => "Pointage";
        public override string ClockSubtitle => "Arrivée ou départ, avec votre code PIN";
        public override string WhoClocks => "QUI POINTE ?";
        public override string ClockPinTitle => "CODE PIN";
        public override string ClockPrimary => "Pointer";
        public override string ClockedInTitle => "Arrivée enregistrée";
        public override string ClockedOutTitle => "Départ enregistré";
        public override string OnDutySince => "En poste depuis";
        public override string CreditAvailableLabel => "Avoir disponible";
        public override string CreditAmountTitle => "MONTANT EN AVOIR";
        public override string AboveCredit => "PLUS QUE L'AVOIR";
        public override string AboveCreditDetail(string name, string available) => $"{name} a {available} d'avoir : la part ne peut pas dépasser ce solde, ni le reste du ticket.";
        public override string CreditPrefilled => "Prérempli avec le plus petit des deux : l'avoir ou le reste. Entrée ajoute la part.";
        public override string CreditOf(string name) => $"AVOIR DE {name}";
        public override string RefundToCredit => "Rendu en avoir";
        public override string NameOrPhoneTitle => "NOM ET PRÉNOM, OU TÉLÉPHONE";
        public override string NameOrPhoneRule => "Le prénom et le nom en entier, ou les 10 chiffres du numéro.";
        public override string NameIncomplete => "NOM INCOMPLET";
        public override string NameIncompleteDetail => "Le prénom et le nom, en entier : deux lettres au moins chacun.";
        public override string TooManyNamed => "Plus de 3 clients portent ce nom : cherchez par le numéro.";
        public override string ResultsAfterPause => "Les résultats viennent 3 secondes après la dernière touche, ou à Entrée.";
        public override string ClientByPhone => "nom ou téléphone";
        public override string ClientAttached => "CLIENT RATTACHÉ";
        public override string ClientAttachedDetail(string name) => $"{name} · touchez le nom pour ouvrir le carnet";
        public override string ClientRefused => "REFUSÉ";
        public override string NotAPhone => "PAS UN NUMÉRO";
        public override string NotAPhoneDetail(string phone) => $"« {phone} » : un numéro, c'est 10 chiffres, commençant par 05, 06, 07, 02, 03 ou 04.";
        public override string AttachToRefund => "Rattacher au retour";
        public override string AttachToTicket => "Rattacher au ticket";
        public override string CreateThisClient => "Créer ce client";
        public override string SearchClient => "Chercher";
        public override string ClientTitle => "Client";
        public override string ClientForRefund => "Rattacher au retour : un avoir est nominatif";
        public override string ClientForTicket(int lines) => $"Rattacher au ticket en cours · {Lines(lines)}";
        public override string PhoneTitle => "NUMÉRO DE TÉLÉPHONE";
        public override string PhoneRule => "10 chiffres, commençant par 05, 06, 07, 02, 03 ou 04.";
        public override string ResultTitle => "RÉSULTAT";
        public override string ResultsTitle(int count) => count == 1 ? "1 RÉSULTAT" : $"{DisplayFigures.Count(count)} RÉSULTATS";
        public override string NoSearchYet => "Aucune recherche tant que le numéro n'est pas complet.";
        public override string NoClientWithNumber => "Aucun client avec ce numéro.";
        public override string ConsultationLogged => "Chaque fiche affichée est une consultation, et elle est journalisée.";
        public override string CreateNeedsManager => "Créer demande le PIN d'un responsable.";
        public override string TicketFrozenWhileSearching => "Le ticket est gelé pendant la recherche.";
        public override string FieldsToCorrect(int count) => count == 1 ? "1 CHAMP À CORRIGER" : $"{DisplayFigures.Count(count)} CHAMPS À CORRIGER";
        public override string FieldsToCorrectDetail => "Nom : 1 à 100 caractères. Téléphone : 10 chiffres, commençant par 05, 06, 07, 02, 03 ou 04.";
        public override string NewClientTitle => "Nouveau client";
        public override string NewClientSubtitle => "Création minimale · rattaché au ticket en cours";
        public override string NameTitle => "NOM";
        public override string NameAndPhoneOnly => "Nom et téléphone seulement : pas d'adresse, rien d'autre.";
        public override string NoticeToHand => "NOTICE D'INFORMATION · ART. 32 — remettez-la au client avant de créer : la version en vigueur est enregistrée sur sa fiche.";
        public override string CreateAndAttach => "Créer et rattacher";
        public override string CarnetLabel => "CARNET";
        public override string OpenTabTitle(string name) => $"Ouvrir un carnet — {name}";
        public override string ChangeTabTitle(string name) => $"Modifier le carnet — {name}";
        public override string ChangeTabSubtitle => "Plafond, gel, fermeture";
        public override string CurrentLimit => "PLAFOND ACTUEL";
        public override string BalanceDue => "SOLDE DÛ";
        public override string NewLimit => "NOUVEAU PLAFOND";
        public override string CloseTabNote => "Fermer retire le plafond : plus d'achat au carnet, le solde reste dû et se rembourse.";
        public override string ChangeRefused => "REFUSÉ";
        public override string SetLimit => "Fixer le plafond";
        public override string OwnerPinAsked => "Le PIN du propriétaire sera demandé.";
        public override string UnfreezeKey => "Dégeler";
        public override string FreezeKey => "Geler";
        public override string CloseTabKey => "Fermer le carnet";
        public override string RepayRefused => "REFUSÉ";
        public override string AboveDue => "PLUS QUE LE DÛ";
        public override string AboveDueDetail(string name, string balance) => $"{name} doit {balance} : un règlement ne peut pas dépasser le solde.";
        public override string NothingRepaid => "MONTANT NUL";
        public override string NothingRepaidDetail => "Saisissez un montant supérieur à 0,00 DA.";
        public override string NoCashReasons => "Aucun motif d'entrée de caisse n'est défini : le responsable les ajoute dans l'administration.";
        public override string RepayTitle(string name) => $"Règlement du carnet — {name}";
        public override string RepaySubtitle => "Entre dans le tiroir comme entrée de caisse";
        public override string AmountRepaid => "MONTANT RÉGLÉ";
        public override string CashReasonTitle => "MOTIF D'ENTRÉE DE CAISSE";
        public override string CashOutReasonTitle => "MOTIF DE SORTIE DE CAISSE";
        public override string RefusedByServer => "Refusé par le serveur";
        public override string? Refusal(string code, IReadOnlyList<string> args) => code switch
        {
            Contracts.Pos.RefusalCodes.NotSellable => $"{Named(args, 0)} n'est plus vendable : retirez la ligne.",
            Contracts.Pos.RefusalCodes.UnknownCode => $"{Named(args, 0)} : plus aucun article ne porte ce code. Retirez la ligne.",
            Contracts.Pos.RefusalCodes.NeverReceived => $"{Named(args, 0)} n'a jamais été réceptionné : rien à vendre.",
            Contracts.Pos.RefusalCodes.LineTooLarge => $"Une ligne contient au plus {Named(args, 0)}.",
            Contracts.Pos.RefusalCodes.PriceOutOfBand => $"{Named(args, 0)} : le prix {Named(args, 1)} dépasse la limite ({Named(args, 2)}).",
            Contracts.Pos.RefusalCodes.PartsAboveTotal => $"Les parts dépassent le ticket ({Named(args, 0)}).",
            Contracts.Pos.RefusalCodes.ReferenceRefused => "Référence refusée : jamais un numéro de carte.",
            Contracts.Pos.RefusalCodes.ApprovalExpired => "Une autorisation n'est plus valable : redonnez la remise ou le prix, ou retirez-les.",
            Contracts.Pos.RefusalCodes.StrikeNeedsPin => "Ligne retirée après Encaisser : le PIN d'un responsable est requis.",
            Contracts.Pos.RefusalCodes.DiscountInvalid => "Remise impossible : plus que rien, et au plus la ligne.",
            Contracts.Pos.RefusalCodes.TabAboveLimit => $"Carnet de {Named(args, 0)} : plafond dépassé, disponible {Named(args, 1)}.",
            Contracts.Pos.RefusalCodes.TabNone => $"{Named(args, 0)} n'a pas de carnet.",
            Contracts.Pos.RefusalCodes.TabFrozen => $"Le carnet de {Named(args, 0)} est gelé.",
            Contracts.Pos.RefusalCodes.TabOverdue => $"Carnet de {Named(args, 0)} en retard : un règlement d'abord.",
            Contracts.Pos.RefusalCodes.CreditInsufficient => $"Avoir de {Named(args, 0)} : {Named(args, 1)} disponible, pas plus.",
            Contracts.Pos.RefusalCodes.ModuleOff => "Ce magasin ne tient pas de clients.",
            Contracts.Pos.RefusalCodes.CustomerUnknown => "Client introuvable.",
            Contracts.Pos.RefusalCodes.ReasonUnknown => "Ce motif n'est plus proposé : choisissez-en un autre.",
            Contracts.Pos.RefusalCodes.NoteMissing => "Ce motif demande une note.",
            Contracts.Pos.RefusalCodes.RefundNeedsManager => "Un responsable autorise le retour.",
            Contracts.Pos.RefusalCodes.RefundAlreadyWhole => "Tout ce ticket a déjà été remboursé.",
            Contracts.Pos.RefusalCodes.RefundMoreThanLeft => "Plus que le reste de la ligne : une partie a déjà été remboursée.",
            Contracts.Pos.RefusalCodes.RefundOtherCustomer => "Ce ticket est celui d'un autre client.",
            Contracts.Pos.RefusalCodes.CreditNeedsCustomer => "Un avoir est nominatif : rattachez un client.",
            Contracts.Pos.RefusalCodes.RefundOfRefund => "Un retour ne se rembourse pas : c'est la vente qui se rembourse.",
            Contracts.Pos.RefusalCodes.PhoneInvalid => $"Numéro invalide : {PhoneRule}",
            Contracts.Pos.RefusalCodes.NameInvalid => "Nom complet : le prénom et le nom, deux lettres au moins chacun.",
            Contracts.Pos.RefusalCodes.NoNotice => "Aucune notice d'information publiée : pas de fiche client sans elle.",
            Contracts.Pos.RefusalCodes.LimitAboveCeiling => $"Plafond du magasin : {Named(args, 0)} au plus.",
            Contracts.Pos.RefusalCodes.RepayAboveBalance => $"Plus que le dû : {Named(args, 0)}.",
            Contracts.Pos.RefusalCodes.RepayNotOnStep => $"En espèces, par pas de {Named(args, 0)} : ou tout le dû.",
            Contracts.Pos.RefusalCodes.PaidOutNeedsManager => "Un responsable autorise la sortie de caisse.",
            _ => null,
        };

        public override string StrikeApprovalTitle => "Retirer une ligne après Encaisser";
        public override string StrikeApprovalSummary(string article) => $"{article} · le paiement était ouvert sur ce ticket";
        public override string StruckTicketOpen => "LIGNES RETIRÉES";
        public override string CancelBeforeSwitching => "Ce ticket n'a plus que des lignes retirées : annulez-le d'abord (Annuler ticket).";
        public override string OpenTicketApprovalTitle => "Ouvrir un ticket d'un autre jour ou d'une autre caisse";
        public override string OpenTicketApprovalSummary(string number) => $"Ticket {number}";
        public override string CashTaken => "Espèces à encaisser";
        public override string RepayOnStep => "PAS DE MONNAIE POUR CE MONTANT";
        public override string RepayOnStepDetail(string cashStep) => $"En espèces, un acompte se règle par pas de {cashStep}. « Tout le dû » solde le carnet.";
        public override string RefundRecorded => "RETOUR ENREGISTRÉ";
        public override string CashToHandBack(string amount) => $"Espèces à rendre au client : {amount}";
        public override string HeldCount(int tickets) => $"{DisplayFigures.Count(tickets)} EN ATTENTE";
        public override string OwnerPin => "PIN PROPRIÉTAIRE";
        public override string TotalBeforeTax => "Total HT";
        public override string WholeDue(string amount) => $"Tout le dû · {amount}";
        public override string CashIn => "Encaisser";
        public override string RepaidTitle => "Règlement encaissé";
        public override string NewBalanceDue => "NOUVEAU SOLDE DÛ";
        public override string BalanceBefore => "Solde avant";
        public override string RepaidInCash => "Réglé en espèces";
        public override string Finish => "Terminer";
        public override string ReturnLabel => "RETOUR";
        public override string CreditIssuedTitle => "Avoir émis";
        public override string ReturnNumber(string number) => $"Retour n° {number}";
        public override string CreditAfterReturn(string name) => $"AVOIR DE {name.ToUpperInvariant()} APRÈS CE RETOUR";
        public override string CreditBefore => "Avoir avant";
        public override string IssuedForReturn(string number) => $"Émis pour le retour n° {number}";
        public override string OpeningLogged(string clock, string staff) => $"Ouverture journalisée à {clock} par {staff}";
        public override string CarnetOffline => "Serveur injoignable : le carnet ne peut pas être lu.";
        public override string StatementTitle => "RELEVÉ · DU PLUS ANCIEN AU PLUS RÉCENT";
        public override string ChangeTabKey => "Modifier le carnet";
        public override string OpenTabKey => "Ouvrir un carnet";
        public override string RepayKey => "Encaisser un règlement";
        public override string TabFrozen => "GELÉ";
        public override string TabFrozenDetail => "Par le propriétaire : aucun nouvel achat au carnet. Les remboursements restent possibles.";
        public override string TabOverdue => "EN RETARD";
        public override string TabOverdueDetail(int days, int rule) => $"Le plus ancien impayé a {DisplayFigures.Count(days)} jours, pour un délai de {DisplayFigures.Count(rule)}.";
        public override string NoTab => "SANS CARNET";
        public override string NoTabDetail => "Aucun plafond : ce client ne peut pas acheter au carnet.";
        public override string MovementLabel(string kind, bool negative) => kind switch { "charge" when negative => "Retour au carnet", "charge" => "Achat au carnet", "payment" => "Remboursement · espèces", "adjustment" => "Ajustement", _ => "Passé en perte" };
        public override string CarnetTitle(string name) => $"Carnet — {name}";
        public override string CarnetSubtitle => "Consultation du compte client";
        public override string Limit => "PLAFOND";
        public override string Available => "DISPONIBLE";
        public override string OldestUnpaid => "PLUS ANCIEN IMPAYÉ";
        public override string DaysOf(int days, int rule) => $"{DisplayFigures.Count(days)} j / {DisplayFigures.Count(rule)}";
        public override string Days(int days) => $"{DisplayFigures.Count(days)} j";
        public override string ColumnDate => "DATE";
        public override string ColumnMovement => "MOUVEMENT";
        public override string ColumnAmount => "MONTANT";
        public override string ColumnBalance => "SOLDE";
        public override string NoMovement => "Aucun mouvement.";
        public override string MethodTab => "Carnet";
        public override string TabOf(string name) => $"CARNET DE {name}";
        public override string TabAvailable => "Disponible";
        public override string AfterThisSale => "Après cette vente";
        public override string TabAmountTitle => "MONTANT AU CARNET";
        public override string AboveLimit => "AU-DESSUS DU PLAFOND";
        public override string AboveLimitDetail(string available, string amount) => $"Il reste {available} sur ce carnet pour {amount}. Le propriétaire peut laisser passer cette vente.";
        public override string TabFrozenRefused => "CARNET GELÉ";
        public override string TabFrozenRefusedDetail => "Gelé par le propriétaire : aucun achat au carnet. Payez autrement.";
        public override string TabOverdueRefused => "CARNET EN RETARD";
        public override string TabOverdueRefusedDetail => "L'impayé le plus ancien a dépassé le délai : payez autrement, ou réglez d'abord le carnet.";
        public override string WholeTicketOnly => "Tout le ticket ou rien : le carnet ne se combine pas avec d'autres moyens dans ce magasin.";
        public override string TabPartExact => "Le carnet prend le montant exact : jamais plus que le reste.";
        public override string TabNotWithParts => "TOUT LE TICKET OU RIEN";
        public override string TabNotWithPartsDetail => "Retirez les parts carte ou BaridiMob pour mettre le ticket au carnet.";
        public override string ValidateOnTab => "Valider au carnet";
        public override string OverrideWithOwnerPin => "Dépasser avec le PIN propriétaire";
        public override string CreditIsNamed => "UN AVOIR EST NOMINATIF";
        public override string CreditIsNamedDetail => "Le ticket d'origine n'avait pas de client : rattachez-en un (F5).";
        public override string AttachClient => "Rattacher un client · F5";
        public override string ApproveCreateTitle(string name) => $"Créer le client {name}";
        public override string ApproveChangeTitle(string name) => $"Changer le carnet de {name}";
        public override string ApproveOverrideTitle(string name) => $"Dépasser le plafond de {name}";
        public override string OwnerApproval => "AUTORISATION PROPRIÉTAIRE";
        public override string ApproveValidate => "Valider";
        public override string RefundKey => "Rembourser";
        public override string RefundLabel => "REMBOURSEMENT";
        public override string RefundPrompt => "Touchez un article du ticket pour le rendre.";
        public override string RefundLineDetail(string article, string back, string left) => $"{article} · rendu {back} sur {left}";
        public override string LessKey => "− 1";
        public override string MoreKey => "+ 1";
        public override string RestockKey => "En rayon";
        public override string ReturnChip(string quantity) => $"RENDU {quantity}";
        public override string RestockChip => "EN RAYON";
        public override string NoRestockChip => "HORS RAYON";
        public override string ReturnedChip(string quantity) => $"DÉJÀ RENDU {quantity}";
        public override string RefundChosen(int lines) => lines == 1 ? "1 article rendu" : $"{DisplayFigures.Count(lines)} articles rendus";
        public override string NoReturnReasons => "Aucun motif de retour n'est défini : le responsable les ajoute dans l'administration.";
        public override string ChooseLines => "Touchez au moins un article à rendre.";
        public override string RefundOffline => "Serveur injoignable : rien n'est remboursé.";
        public override string RefundRefused => "REMBOURSEMENT REFUSÉ";
        public override string ApproveRefund => "Valider le remboursement";
        public override string RefundTitle => "Rembourser";
        public override string RefundTotal => "À rendre";
        public override string RefundToTab => "Retiré du carnet";
        public override string CashOut => "ESPÈCES À RENDRE";
        public override string CreditOut => "AVOIR AU CLIENT";
        public override string MethodStoreCredit => "Avoir";
        public override string RefundLinesTitle => "ARTICLES RENDUS";
        public override string RefundLinesSummary(int lines, int restocked) =>
            $"{RefundChosen(lines)} · {DisplayFigures.Count(restocked)} remis en rayon";
        public override string RefundCloseHint => "Échap ou ✕ revient au ticket sans rien rembourser.";
        public override string RefundOfTicket(string number) => $"Remboursement du ticket {number}";
        public override string RefundingNotice => "REMBOURSEMENT";
        public override string RefundingNoticeDetail => "Touchez les articles rendus, choisissez un motif, puis Continuer.";
        public override string ManagerOnly => "RÉSERVÉ AU RESPONSABLE";
        public override string StatusVoided => "ANNULÉE";
        public override string StatusRefunded => "REMBOURSÉE";
        public override string StatusPartlyRefunded => "REMBOURSÉE EN PARTIE";
        public override string PastTicket => "TICKET PASSÉ";
        public override string PastTicketLine(string dayAndMonth, string clock, string? staff) =>
            staff is null ? $"Vendu le {dayAndMonth} à {clock} · lecture seule" : $"Vendu le {dayAndMonth} à {clock} par {staff} · lecture seule";
        public override string PastTicketFooter => "Lecture seule. Fermer rend le ticket en cours.";
        public override string TaxIncluded => "Dont TVA";
        public override string WhoOpensTheTill => "Qui ouvre la caisse ?";
        public override string ChooseYourName => "Choisissez votre nom";
        public override string PinOf(string name) => $"Code PIN de {name}";
        public override string ClearKey => "Effacer";
        public override string OpenTheTill => "Ouvrir la caisse";
        public override string NoPinLabel => "SANS CODE PIN";
        public override string NoPinDetail => "Aucun code PIN n'est défini pour cette personne. Le responsable le définit sur le serveur du magasin.";
        public override string NobodyMaySignIn => "Personne ne peut ouvrir cette caisse";
        public override string NobodyMaySignInHint => "Aucun membre actif du personnel n'est enregistré pour ce magasin.";
        public override string WrongPin => "CODE INCORRECT";
        public override string AttemptsLeft(int count) => count == 1
            ? "Encore 1 essai avant le blocage"
            : $"Encore {DisplayFigures.Count(count)} essais avant le blocage";
        public override string Locked => "BLOQUÉ";
        public override string LockedUntil(string clock) => $"Trop d'essais. Réessayez à {clock}.";
        public override string UnknownStaff => "PERSONNE INCONNUE";
        public override string UnknownStaffDetail => "Cette personne ne peut plus ouvrir la caisse. La liste a été mise à jour.";
        public override string UnknownTerminal => "CAISSE INCONNUE";
        public override string UnknownTerminalDetail => "Le serveur du magasin ne connaît pas cette caisse. Vérifiez son identifiant (--terminal=).";
        public override string NoTerminalDetail => "Cette caisse n'a pas d'identifiant (--terminal=).";
        public override string SessionEnded => "SESSION TERMINÉE";
        public override string SessionEndedDetail => "Le serveur ne reconnaît plus cette session. Reconnectez-vous : le ticket en cours est conservé.";
        public override string TicketInProgress => "TICKET EN COURS";
        public override string FinishBeforeSwitching => "Encaissez ou retirez les lignes avant de changer de caissier.";
    }

    // ================================================================== Arabic

    private sealed class ArabicText : TillText
    {
        public override TillLanguage Language => TillLanguage.Arabic;

        public override string CurrencySymbol(Currency currency) => currency == Currency.Dzd ? "د.ج" : currency.Code;

        public override string Online => "متصل";
        public override string Offline => "غير متصل"; // ar: à relire
        public override string CurrentTicket => "التذكرة الحالية";
        public override string NewTicket => "تذكرة جديدة"; // ar: à relire
        public override string Empty => "فارغة"; // ar: à relire
        public override string Paid => "مدفوعة"; // ar: à relire
        public override string TicketNumber(string invoiceNumber) => $"التذكرة رقم {invoiceNumber}"; // ar: à relire

        // Arabic counts its nouns in four forms: one, two (the dual), three to ten, eleven and up.
        public override string Lines(int count) => ArabicCount(count, "سطر واحد", "سطران", "أسطر", "سطرًا", "سطر");

        public override string SearchPlaceholder => "ابحث عن منتج او ادخل الرمز الشريطي"; // ar: à relire
        public override string LastArticle => "آخر منتج";
        public override string Ready => "جاهز"; // ar: à relire
        public override string LastSaleLine(string invoiceNumber, string clock, string total) =>
            $"آخر عملية بيع: التذكرة رقم {invoiceNumber} على {clock} · {total}"; // ar: à relire
        public override string SaleRecorded => "تم تسجيل البيع"; // ar: à relire
        public override string SaleRecordedLine(string invoiceNumber) =>
            $"التذكرة رقم {invoiceNumber} · المسح التالي يفتح تذكرة جديدة"; // ar: à relire
        public override string UnknownCode => "رمز غير معروف"; // ar: à relire
        public override string UnknownCodeDetail(string code) => $"{code} — لا يوجد منتج بهذا الرمز"; // ar: à relire
        public override string NotSellable => "غير قابل للبيع"; // ar: à relire
        public override string NotSellableReason(string? reason) => reason switch // ar: à relire
        {
            Contracts.Pos.NotSellableReason.NoCurrentPrice => "لا يوجد سعر ساري اليوم",
            Contracts.Pos.NotSellableReason.PriceNotTaxInclusive => "السعر مسجّل دون احتساب الرسم؛ الصندوق يبيع بالأسعار شاملة الرسوم",
            Contracts.Pos.NotSellableReason.Archived => "منتج مؤرشف",
            Contracts.Pos.NotSellableReason.NotSoldByWeight => "يُباع بالقطعة لا بالوزن",
            Contracts.Pos.NotSellableReason.WeightInvalid => "وزن مرفوض: أكبر من الصفر، وليس أدق من الوحدة",
            Contracts.Pos.NotSellableReason.LabelNotSetUp => "ملصق ميزان لمنتج لا يُباع بالوزن: يُصحَّح في الكتالوج",
            Contracts.Pos.NotSellableReason.LabelValueInvalid => "ملصق غير مقروء: وزن أو سعر لا يمكن بيعه",
            Contracts.Pos.NotSellableReason.NoCode => "بلا رمز شريطي ولا PLU: لا يمكن بيعه",
            _ => $"مرفوض ({reason})",
        };
        public override string OfflineSince(string clock) => $"خادم المتجر غير متاح منذ {clock}"; // ar: à relire
        public override string SaleRefused => "البيع مرفوض"; // ar: à relire
        public override string Problem => "مشكلة"; // ar: à relire

        public override string ColumnQuantity => "الكمية";
        public override string ColumnArticle => "المنتج";
        public override string ColumnUnitPrice => "سعر الوحدة";
        public override string ColumnTotal => "المجموع";
        public override string BeyondRecordedStock => "يتجاوز المخزون المسجّل";
        public override string PromotionalPrice => "سعر ترويجي"; // ar: à relire
        public override string Removed => "أُزيل";
        public override string RemoveLine => "إزالة السطر";
        public override string EmptyTitle => "التذكرة فارغة"; // ar: à relire
        public override string EmptyHint => "امسح منتجًا."; // ar: à relire

        public override string Subtotal => "المجموع الفرعي";
        public override string Discounts => "التخفيضات";
        public override string TotalToPay => "المجموع المستحق";
        // "الدفع", the word Arabic tills use on this key; the board had "تحصيل", which reads as
        // collecting a debt (Hakim, 23/09).
        public override string Collect => "الدفع";
        public override string CashAmount(string amount) => $"نقدًا {amount}";
        public override string CashDue => "النقد المستحق"; // ar: à relire
        public override string NewSale => "بيع جديد"; // ar: à relire
        public override string EnterKey => "Enter"; // the key as it is printed (Hakim, 23/09)

        public override string TicketTotal => "مجموع التذكرة"; // ar: à relire
        public override string CashRounding => "تقريب النقد"; // ar: à relire
        public override string NextScanOpensTicket => "المسح التالي يفتح تذكرة جديدة."; // ar: à relire

        public override string SaleNotConfirmed => "البيع غير مؤكَّد"; // ar: à relire
        public override string SaleNotConfirmedTitle => "لم يؤكّد خادم المتجر التذكرة."; // ar: à relire
        public override string SaleNotConfirmedBody => // ar: à relire
            "لا تُرجع الباقي واحتفظ بالبضاعة ما دام البيع غير مؤكَّد. تحقّق مما إذا سُجّل: يبقى الدفع غير متاح إلى ذلك الحين. التذكرة تبقى مفتوحة.";
        public override string AcknowledgeUnconfirmed => "قمت بالتحقق"; // ar: à relire

        public override string AlmanacKind(string recommendationType) => recommendationType switch
        {
            "near_expiry" => "انتهاء الصلاحية", // ar: à relire
            _ => "توقّع",
        };
        public override string NearExpiryClaim(string productName, int daysToExpiry) => daysToExpiry switch // ar: à relire
        {
            < 0 => $"{productName}: انتهت صلاحيته منذ {ArabicCount(-daysToExpiry, "يوم واحد", "يومين", "أيام", "يومًا", "يوم")}",
            0 => $"{productName}: تنتهي صلاحيته اليوم",
            _ => $"{productName}: تنتهي صلاحيته خلال {ArabicCount(daysToExpiry, "يوم واحد", "يومين", "أيام", "يومًا", "يوم")}",
        };
        public override string NearExpiryDetail(string? unitsOnHand, string valueAtCost, string expiresOn) => // ar: à relire
            unitsOnHand is null
                ? $"{valueAtCost} بسعر الشراء. تنتهي صلاحية الدفعة في {expiresOn}."
                : $"{unitsOnHand} في المخزون، {valueAtCost} بسعر الشراء. تنتهي صلاحية الدفعة في {expiresOn}.";
        public override string IntentAction(string command, string serverLabel) => command switch // ar: à relire
        {
            "apply_markdown" => "تخفيض السعر",
            "write_off_batch" => "إخراج من المخزون",
            _ => serverLabel,
        };
        public override string Adjust => "تعديل";
        public override string Dismiss => "تجاهل";
        public override string NoCardsForYou => "لا توجد بطاقات لك حاليًا."; // ar: à relire
        public override string CardsAwaitingManager(int count) =>
            ArabicCount(count, "بطاقة واحدة", "بطاقتان", "بطاقات", "بطاقة", "بطاقة") + " بانتظار المسؤول";

        // Tickets on hold and drafts (B2). The board has none of these in Arabic.
        public override string ParkedAt(string clock) => $"انتظار {clock}"; // ar: à relire
        public override string Park => "انتظار"; // ar: à relire
        public override string CancelTicket => "إلغاء التذكرة"; // ar: à relire
        public override string DraftsKey(int count) => count == 0 ? "المسودات" : $"المسودات ({DisplayFigures.Count(count)})"; // ar: à relire
        public override string DraftsTitle => "المسودات"; // ar: à relire
        public override string DraftsHint => "التذاكر الملغاة اليوم. تختفي في نهاية اليوم."; // ar: à relire
        public override string CancelledAt(string clock, string? by) => by is null ? $"أُلغيت على {clock}" : $"أُلغيت على {clock} · {by}"; // ar: à relire
        public override string ResumeTicket => "استئناف"; // ar: à relire
        public override string Close => "إغلاق"; // ar: à relire
        public override string NoDrafts => "لا توجد تذاكر ملغاة اليوم."; // ar: à relire

        // Search and past tickets (B1). No board shows these in Arabic.
        public override string NextCount(string count) => $"الكمية × {count}"; // ar: à relire

        public override string DiscountKey => "تخفيض"; // ar: à relire
        public override string TicketDiscountKey => "تخفيض التذكرة"; // ar: à relire
        public override string DiscountLabel => "تخفيض"; // ar: à relire
        public override string TicketDiscountLabel => "تخفيض التذكرة"; // ar: à relire
        public override string DiscountChip(string unit) => $"تخفيض · {unit}"; // ar: à relire
        public override string ReasonTitle => "السبب"; // ar: à relire
        public override string ContinueKey => "متابعة"; // ar: à relire
        public override string BackToTicket => "العودة إلى التذكرة"; // ar: à relire
        public override string RemoveDiscount => "إزالة التخفيض"; // ar: à relire
        public override string DiscountPrompt(bool percent) => percent // ar: à relire
            ? "اكتب النسبة في الحقل ثم اختر السبب"
            : "اكتب المبلغ في الحقل ثم اختر السبب";
        public override string DiscountInvalid(bool percent) => percent // ar: à relire
            ? "نسبة من 0,01 إلى 100، برقمين عشريين على الأكثر"
            : "مبلغ أكبر من الصفر، برقمين عشريين على الأكثر";
        public override string ChooseReason => "اختر سببًا"; // ar: à relire
        public override string NoDiscountReasons => "لا يوجد سبب تخفيض معرَّف: لا يمكن تسجيل التخفيض"; // ar: à relire
        public override string DiscountOffline => "خادم المتجر غير متاح: التخفيض في الانتظار"; // ar: à relire
        public override string PriceKey => "السعر"; // ar: à relire
        public override string PriceOverrideLabel => "سعر معدَّل"; // ar: à relire
        public override string PriceInForce(string price) => $"السعر الساري {price}"; // ar: à relire
        public override string NewTotal(string total) => $"المجموع الجديد {total}"; // ar: à relire
        public override string PricePrompt => "اكتب سعر الوحدة الجديد في الحقل ثم اختر السبب"; // ar: à relire
        public override string PriceInvalid => "سعر أكبر من الصفر، برقمين عشريين على الأكثر"; // ar: à relire
        public override string PriceAboveBand(string ceiling) => $"على الأكثر {ceiling} (20 % فوق السعر): بعده يُصحَّح السعر في الكتالوج"; // ar: à relire
        public override string PriceNotAboveZero => "سعر أكبر من الصفر: لإهداء المنتج، تخفيض 100 %"; // ar: à relire
        public override string PriceUnchanged => "هذا هو السعر الساري"; // ar: à relire
        public override string PriceBelowCost => "أقل من سعر الشراء · المالك يؤكد عن علم"; // ar: à relire
        public override string BackToListPrice => "العودة إلى السعر الساري"; // ar: à relire
        public override string NoOverrideReasons => "لا يوجد سبب لتعديل السعر: لا يمكن تعديله"; // ar: à relire
        public override string ApproveOverride => "تأكيد السعر"; // ar: à relire
        public override string NoteChip => "ملاحظة"; // ar: à relire
        public override string NotePrompt(string reason) => $"وضّح «{reason}» في الحقل ثم إدخال"; // ar: à relire
        public override string PaymentTitle => "تحصيل";
        public override string PaymentSubtitle(int lines) => $"التذكرة الجارية · {lines} سطرا";
        public override string CashToCollect => "النقد المطلوب";
        public override string PartsTitle => "الأجزاء";
        public override string NoParts => "لا أجزاء: التذكرة كلها نقدا."; // ar: à relire
        public override string MethodCash => "نقدا";
        public override string MethodCard => "بطاقة";
        public override string MethodWallet => "BaridiMob";
        public override string PartAmountTitle(string method) => $"مبلغ {method}"; // ar: à relire
        public override string ReferenceTitle => "المرجع · اختياري"; // ar: à relire
        public override string ReferencePlaceholder => "آخر 4 أرقام أو رقم الترخيص"; // ar: à relire
        public override string PartReference(string reference) => $"مرجع {reference}";
        public override string RestAfterPart => "الباقي بعد هذا الجزء"; // ar: à relire
        public override string WholeRest => "كل الباقي"; // ar: à relire
        public override string NoChangeOnCard => "البطاقة لا تُرجع الباقي: ليس أكثر من الرصيد المتبقي أبدًا."; // ar: à relire
        public override string PartPrefilled => "مملوء بالباقي — اضغط للاستبدال. إدخال يضيف الجزء."; // ar: à relire
        public override string PaymentCloseHint => "Esc أو × يغلق دون أي تغيير على التذكرة";
        public override string AddPart => "إضافة الجزء"; // ar: à relire
        public override string PaymentValidate => "تأكيد";
        public override string AboveRest => "مبلغ مرتفع جدا"; // ar: à relire
        public override string AboveRestDetail(string rest) => $"لا يتجاوز الجزء الباقي للدفع، {rest}: لا يُرجع الباقي."; // ar: à relire
        public override string PartInvalid => "مبلغ غير صالح"; // ar: à relire
        public override string PartInvalidDetail => "مبلغ أكبر من الصفر، برقمين عشريين على الأكثر."; // ar: à relire
        public override string ReferenceRefused => "مرجع مرفوض"; // ar: à relire
        public override string ReferenceLooksLikeCard => "كان يشبه رقم بطاقة: حُذف ولم يُحفظ. اكتب آخر 4 أرقام أو رقم الترخيص."; // ar: à relire
        public override string ReferenceInvalidDetail => "حروف وأرقام ومسافات وشرطات فقط، 32 على الأكثر."; // ar: à relire
        public override string SaleRefusedInPanel => "البيع مرفوض · الأجزاء محفوظة"; // ar: à relire
        public override string PayingNotice => "تحصيل جارٍ"; // ar: à relire
        public override string PayingNoticeDetail => "المسح مُتجاهَل ما دام الدفع مفتوحا."; // ar: à relire
        public override string ApprovingNotice => "إذن جارٍ"; // ar: à relire
        public override string ApprovingNoticeDetail => "المسح مُتجاهَل ما دام الإذن مفتوحا."; // ar: à relire
        public override string EscapeKey => "Esc";
        public override string CancelLabel => "إلغاء التذكرة"; // ar: à relire
        public override string CancelDetail(string total) => $"{total} · لا شيء يُباع؛ الإلغاء يُسجَّل"; // ar: à relire
        public override string CancelConfirm => "إلغاء التذكرة"; // ar: à relire
        public override string CancelChip => "إلغاء"; // ar: à relire
        public override string PaymentStarted =>"بدات عملية الدفع . يجب تاكيد الغاء التذكرة من طرف مسؤول"; // ar: à relire
        public override string NoVoidReasons => "لا يوجد سبب للإلغاء: لا يمكن إلغاء التذكرة"; // ar: à relire
        public override string CancelRefused => "رفض الخادم الإلغاء: التذكرة باقية"; // ar: à relire
        public override string CancelOffline => "الخادم غير متاح: لم يُسجَّل شيء، التذكرة باقية. ضعها في الانتظار"; // ar: à relire
        public override string ApproveCancel => "تأكيد الإلغاء"; // ar: à relire
        public override string ManagerApproval => "إذن المسؤول"; // ar: à relire
        public override string WhoApproves => "من يأذن"; // ar: à relire
        public override string ManagerPin => "رمز المسؤول"; // ar: à relire
        public override string ApproveDiscount => "تأكيد التخفيض"; // ar: à relire
        public override string WrongManagerPin(int attemptsLeft) => $"رمز خاطئ · {attemptsLeft} محاولات قبل القفل"; // ar: à relire
        public override string ManagerLocked(string until) => $"محاولات كثيرة: مقفل حتى {until}"; // ar: à relire
        public override string NotAManager => "ليس لهذا الشخص صلاحية الإذن بالتخفيض"; // ar: à relire
        public override string ManagerHasNoPin => "لا يوجد رمز لهذا الشخص"; // ar: à relire

        public override string WeightChip(string unit) => $"الوزن · {unit}"; // ar: à relire
        public override string WeighTitle => "الوزن"; // ar: à relire
        public override string PerUnit(string price, string unit) => $"{price} / {unit}"; // ar: à relire
        public override string WeighPrompt(string unit) => $"اكتب الوزن بـ {unit} ثم اضغط إدخال"; // ar: à relire
        public override string WeighKeys => "إدخال للوزن · خروج للإلغاء"; // ar: à relire
        public override string WeighInvalid(int decimals) => decimals == 0 // ar: à relire
            ? "وزن صحيح أكبر من الصفر"
            : $"وزن أكبر من الصفر، بـ {decimals} أرقام عشرية على الأكثر: 0,556";
        public override string TypedWeight => "وزن مُدخَل"; // ar: à relire
        public override string LabelWeight => "ملصق"; // ar: à relire
        public override string LabelPrice => "ملصق السعر"; // ar: à relire
        public override string Reweigh => "الوزن"; // ar: à relire
        public override string CountIgnored => "الكمية لم تُطبَّق"; // ar: à relire
        public override string CountIgnoredDetail => "المنتج الموزون يُباع بوزنه لا بالكمية"; // ar: à relire
        public override string Stock(string quantity) => $"المخزون {quantity}"; // ar: à relire
        public override string Searching => "جارٍ البحث…"; // ar: à relire
        public override string SearchOffline => "خادم المتجر غير متاح: لا شيء للعرض حاليًا."; // ar: à relire
        public override string NoResults(string query) => $"لا يوجد منتج يطابق «{query}»."; // ar: à relire
        public override string TicketsKey => "التذاكر"; // ar: à relire
        public override string TicketsTitle => "التذاكر"; // ar: à relire
        public override string TodayLabel(string dayAndMonth) => $"اليوم · {dayAndMonth}"; // ar: à relire
        public override string ThisTill => "هذا الصندوق"; // ar: à relire
        public override string AllTills => "كل الصناديق"; // ar: à relire
        public override string OtherScope(bool allTills) => allTills ? "هذا الصندوق" : "كل الصناديق"; // ar: à relire
        public override string NoTickets => "لا مبيعات في هذا اليوم."; // ar: à relire
        public override string TicketsNotAllowed => "يوم آخر أو صندوق آخر: مخصّص للمسؤول."; // ar: à relire
        public override string TicketUnknown => "تذكرة غير موجودة"; // ar: à relire
        public override string TicketUnknownDetail(string number) => $"{number} — لا توجد عملية بيع بهذا الرقم في هذا المتجر"; // ar: à relire
        public override string ClientKey => "زبون"; // ar: à relire
        public override string PettyCashKey => "الصندوق الصغير"; // ar: à relire
        public override string MoreOperations => "المزيد…"; // ar: à relire
        public override string BackFromMore => "رجوع"; // ar: à relire
        public override string ClockKey => "تسجيل الحضور"; // ar: à relire
        public override string PettyCashTitle => "الصندوق الصغير"; // ar: à relire
        public override string PettyCashSubtitle => "خارج البيع · تُسجَّل مع سببها"; // ar: à relire
        public override string ChooseCashDirection => "إدخال أو إخراج نقد: اختر أولًا."; // ar: à relire
        public override string CashInKey => "إدخال نقد"; // ar: à relire
        public override string CashOutKey => "إخراج نقد"; // ar: à relire
        public override string AmountTitle => "المبلغ"; // ar: à relire
        public override string NoteTitle => "ملاحظة"; // ar: à relire
        public override string RecordKey => "تسجيل"; // ar: à relire
        public override string CashInRecorded => "تم تسجيل إدخال النقد"; // ar: à relire
        public override string CashOutRecorded => "تم تسجيل إخراج النقد"; // ar: à relire
        public override string ReasonLabel => "السبب"; // ar: à relire
        public override string ClockTitle => "تسجيل الحضور"; // ar: à relire
        public override string ClockSubtitle => "وصول أو مغادرة، برمزك السري"; // ar: à relire
        public override string WhoClocks => "من يسجل؟"; // ar: à relire
        public override string ClockPinTitle => "الرمز السري"; // ar: à relire
        public override string ClockPrimary => "تسجيل"; // ar: à relire
        public override string ClockedInTitle => "تم تسجيل الوصول"; // ar: à relire
        public override string ClockedOutTitle => "تم تسجيل المغادرة"; // ar: à relire
        public override string OnDutySince => "في العمل منذ"; // ar: à relire
        public override string CreditAvailableLabel => "الرصيد المتاح"; // ar: à relire
        public override string CreditAmountTitle => "المبلغ من الرصيد"; // ar: à relire
        public override string AboveCredit => "أكثر من الرصيد"; // ar: à relire
        public override string AboveCreditDetail(string name, string available) => $"لدى {name} رصيد {available}: لا يتجاوز الجزء هذا الرصيد ولا باقي التذكرة."; // ar: à relire
        public override string CreditPrefilled => "معبأ بالأصغر بين الرصيد والباقي. الإدخال يضيف الجزء."; // ar: à relire
        public override string CreditOf(string name) => $"رصيد {name}"; // ar: à relire
        public override string RefundToCredit => "مُعاد كرصيد"; // ar: à relire
        public override string NameOrPhoneTitle => "الاسم واللقب، أو الهاتف"; // ar: à relire
        public override string NameOrPhoneRule => "الاسم واللقب كاملين، أو أرقام الهاتف العشرة."; // ar: à relire
        public override string NameIncomplete => "اسم ناقص"; // ar: à relire
        public override string NameIncompleteDetail => "الاسم واللقب كاملين: حرفان على الأقل لكل منهما."; // ar: à relire
        public override string TooManyNamed => "أكثر من 3 زبائن بهذا الاسم: ابحث بالرقم."; // ar: à relire
        public override string ResultsAfterPause => "تظهر النتائج بعد 3 ثوانٍ من آخر حرف، أو عند الإدخال."; // ar: à relire
        public override string ClientByPhone => "بالاسم أو الهاتف"; // ar: à relire
        public override string ClientAttached => "زبون مرتبط"; // ar: à relire
        public override string ClientAttachedDetail(string name) => $"{name} · المس الاسم لفتح دفتر الديون"; // ar: à relire
        public override string ClientRefused => "مرفوض"; // ar: à relire
        public override string NotAPhone => "ليس رقمًا"; // ar: à relire
        public override string NotAPhoneDetail(string phone) => $"« {phone} »: الرقم 10 أرقام تبدأ بـ 05 أو 06 أو 07 أو 02 أو 03 أو 04."; // ar: à relire
        public override string AttachToRefund => "ربط بالإرجاع"; // ar: à relire
        public override string AttachToTicket => "ربط بالتذكرة"; // ar: à relire
        public override string CreateThisClient => "إنشاء هذا الزبون"; // ar: à relire
        public override string SearchClient => "بحث"; // ar: à relire
        public override string ClientTitle => "الزبون"; // ar: à relire
        public override string ClientForRefund => "ربط بالإرجاع: الرصيد بالاسم"; // ar: à relire
        public override string ClientForTicket(int lines) => $"ربط بالتذكرة الحالية · {Lines(lines)}"; // ar: à relire
        public override string PhoneTitle => "رقم الهاتف"; // ar: à relire
        public override string PhoneRule => "10 أرقام، تبدأ بـ 05 أو 06 أو 07 أو 02 أو 03 أو 04."; // ar: à relire
        public override string ResultTitle => "النتيجة"; // ar: à relire
        public override string ResultsTitle(int count) => $"{DisplayFigures.Count(count)} نتائج"; // ar: à relire
        public override string NoSearchYet => "لا بحث قبل اكتمال الرقم."; // ar: à relire
        public override string NoClientWithNumber => "لا زبون بهذا الرقم."; // ar: à relire
        public override string ConsultationLogged => "كل بطاقة معروضة تسجل كاطلاع، وهي مسجلة."; // ar: à relire
        public override string CreateNeedsManager => "الإنشاء يتطلب رمز مسؤول."; // ar: à relire
        public override string TicketFrozenWhileSearching => "التذكرة مجمدة أثناء البحث."; // ar: à relire
        public override string FieldsToCorrect(int count) => $"{DisplayFigures.Count(count)} حقول للتصحيح"; // ar: à relire
        public override string FieldsToCorrectDetail => "الاسم: من 1 إلى 100 حرف. الهاتف: 10 أرقام."; // ar: à relire
        public override string NewClientTitle => "زبون جديد"; // ar: à relire
        public override string NewClientSubtitle => "إنشاء مختصر · مرتبط بالتذكرة الحالية"; // ar: à relire
        public override string NameTitle => "الاسم"; // ar: à relire
        public override string NameAndPhoneOnly => "الاسم والهاتف فقط: لا عنوان ولا غير ذلك."; // ar: à relire
        public override string NoticeToHand => "إشعار المعلومات · المادة 32 — سلّمه للزبون قبل الإنشاء: تُسجَّل النسخة السارية في بطاقته."; // ar: à relire
        public override string CreateAndAttach => "إنشاء وربط"; // ar: à relire
        public override string CarnetLabel => "دفتر الديون"; // ar: à relire
        public override string OpenTabTitle(string name) => $"فتح دفتر ديون — {name}"; // ar: à relire
        public override string ChangeTabTitle(string name) => $"تعديل دفتر الديون — {name}"; // ar: à relire
        public override string ChangeTabSubtitle => "السقف، التجميد، الإغلاق"; // ar: à relire
        public override string CurrentLimit => "السقف الحالي"; // ar: à relire
        public override string BalanceDue => "الرصيد المستحق"; // ar: à relire
        public override string NewLimit => "السقف الجديد"; // ar: à relire
        public override string CloseTabNote => "الإغلاق يزيل السقف: لا شراء بالدَّين، ويبقى الرصيد مستحقًا."; // ar: à relire
        public override string ChangeRefused => "رُفض التعديل"; // ar: à relire
        public override string SetLimit => "تحديد السقف"; // ar: à relire
        public override string OwnerPinAsked => "سيُطلب رمز المالك."; // ar: à relire
        public override string UnfreezeKey => "إلغاء التجميد"; // ar: à relire
        public override string FreezeKey => "تجميد"; // ar: à relire
        public override string CloseTabKey => "إغلاق دفتر الديون"; // ar: à relire
        public override string RepayRefused => "رُفض التسديد"; // ar: à relire
        public override string AboveDue => "أكثر من المستحق"; // ar: à relire
        public override string AboveDueDetail(string name, string balance) => $"{name} مدين بـ {balance}: لا يتجاوز التسديد الرصيد."; // ar: à relire
        public override string NothingRepaid => "مبلغ منعدم"; // ar: à relire
        public override string NothingRepaidDetail => "أدخل مبلغًا أكبر من 0,00 دج."; // ar: à relire
        public override string NoCashReasons => "لا أسباب لإدخال النقد: يضيفها المسؤول من الإدارة."; // ar: à relire
        public override string RepayTitle(string name) => $"تسديد — {name}"; // ar: à relire
        public override string RepaySubtitle => "يدخل الدرج كإدخال نقدي"; // ar: à relire
        public override string AmountRepaid => "المبلغ المسدد"; // ar: à relire
        public override string CashReasonTitle => "سبب إدخال النقد"; // ar: à relire
        public override string CashOutReasonTitle => "سبب إخراج النقد"; // ar: à relire
        public override string RefusedByServer => "رفضه الخادم"; // ar: à relire
        public override string? Refusal(string code, IReadOnlyList<string> args) => code switch // ar: à relire
        {
            Contracts.Pos.RefusalCodes.NotSellable => $"{Named(args, 0)} لم يعد قابلًا للبيع: أزل السطر.",
            Contracts.Pos.RefusalCodes.UnknownCode => $"{Named(args, 0)}: لم يعد أي منتج يحمل هذا الرمز. أزل السطر.",
            Contracts.Pos.RefusalCodes.NeverReceived => $"{Named(args, 0)} لم يُستلم قط: لا شيء للبيع.",
            Contracts.Pos.RefusalCodes.LineTooLarge => $"السطر يحمل {Named(args, 0)} على الأكثر.",
            Contracts.Pos.RefusalCodes.PriceOutOfBand => $"{Named(args, 0)}: السعر {Named(args, 1)} يتجاوز الحد ({Named(args, 2)}).",
            Contracts.Pos.RefusalCodes.PartsAboveTotal => $"الأجزاء تتجاوز التذكرة ({Named(args, 0)}).",
            Contracts.Pos.RefusalCodes.ReferenceRefused => "المرجع مرفوض: لا يُكتب رقم بطاقة أبدًا.",
            Contracts.Pos.RefusalCodes.ApprovalExpired => "إذن لم يعد صالحًا: أعد منح التخفيض أو السعر، أو أزلهما.",
            Contracts.Pos.RefusalCodes.StrikeNeedsPin => "سطر أُزيل بعد فتح الدفع: مطلوب رمز مسؤول.",
            Contracts.Pos.RefusalCodes.DiscountInvalid => "تخفيض غير ممكن: أكثر من لا شيء، وعلى الأكثر قيمة السطر.",
            Contracts.Pos.RefusalCodes.TabAboveLimit => $"دفتر {Named(args, 0)}: تجاوز السقف، المتاح {Named(args, 1)}.",
            Contracts.Pos.RefusalCodes.TabNone => $"{Named(args, 0)} ليس له دفتر ديون.",
            Contracts.Pos.RefusalCodes.TabFrozen => $"دفتر {Named(args, 0)} مجمَّد.",
            Contracts.Pos.RefusalCodes.TabOverdue => $"دفتر {Named(args, 0)} متأخر: التسديد أولًا.",
            Contracts.Pos.RefusalCodes.CreditInsufficient => $"رصيد {Named(args, 0)}: المتاح {Named(args, 1)}، لا أكثر.",
            Contracts.Pos.RefusalCodes.ModuleOff => "هذا المتجر لا يسجل الزبائن.",
            Contracts.Pos.RefusalCodes.CustomerUnknown => "الزبون غير موجود.",
            Contracts.Pos.RefusalCodes.ReasonUnknown => "هذا السبب لم يعد متاحًا: اختر سببًا آخر.",
            Contracts.Pos.RefusalCodes.NoteMissing => "هذا السبب يتطلب ملاحظة.",
            Contracts.Pos.RefusalCodes.RefundNeedsManager => "الإرجاع يأذن به مسؤول.",
            Contracts.Pos.RefusalCodes.RefundAlreadyWhole => "كل هذه التذكرة أُرجعت من قبل.",
            Contracts.Pos.RefusalCodes.RefundMoreThanLeft => "أكثر مما بقي من السطر: جزء منه أُرجع من قبل.",
            Contracts.Pos.RefusalCodes.RefundOtherCustomer => "هذه التذكرة لزبون آخر.",
            Contracts.Pos.RefusalCodes.CreditNeedsCustomer => "الرصيد بالاسم: اربط زبونًا أولًا.",
            Contracts.Pos.RefusalCodes.RefundOfRefund => "الإرجاع لا يُرجَع: البيع هو الذي يُرجَع.",
            Contracts.Pos.RefusalCodes.PhoneInvalid => $"رقم غير صالح: {PhoneRule}",
            Contracts.Pos.RefusalCodes.NameInvalid => "الاسم الكامل: الاسم واللقب، حرفان على الأقل لكل منهما.",
            Contracts.Pos.RefusalCodes.NoNotice => "لا يوجد إشعار إعلام منشور: لا بطاقة زبون من دونه.",
            Contracts.Pos.RefusalCodes.LimitAboveCeiling => $"سقف المتجر: {Named(args, 0)} على الأكثر.",
            Contracts.Pos.RefusalCodes.RepayAboveBalance => $"أكثر من المستحق: {Named(args, 0)}.",
            Contracts.Pos.RefusalCodes.RepayNotOnStep => $"نقدًا، بمضاعفات {Named(args, 0)}: أو كل المستحق.",
            Contracts.Pos.RefusalCodes.PaidOutNeedsManager => "إخراج النقد يأذن به مسؤول.",
            _ => null,
        };

        public override string StrikeApprovalTitle => "إزالة سطر بعد فتح الدفع"; // ar: à relire
        public override string StrikeApprovalSummary(string article) => $"{article} · كان الدفع مفتوحًا على هذه التذكرة"; // ar: à relire
        public override string StruckTicketOpen => "أسطر مُزالة"; // ar: à relire
        public override string CancelBeforeSwitching => "لم يبق في هذه التذكرة إلا أسطر مُزالة: ألغِها أولًا (إلغاء التذكرة)."; // ar: à relire
        public override string OpenTicketApprovalTitle => "فتح تذكرة ليوم آخر أو صندوق آخر"; // ar: à relire
        public override string OpenTicketApprovalSummary(string number) => $"التذكرة {number}"; // ar: à relire
        public override string CashTaken => "النقد المطلوب تحصيله"; // ar: à relire
        public override string RepayOnStep => "لا فكّة لهذا المبلغ"; // ar: à relire
        public override string RepayOnStepDetail(string cashStep) => $"نقدًا، يُسدَّد الجزء بمضاعفات {cashStep}. «كل المستحق» يصفّي الدفتر."; // ar: à relire
        public override string RefundRecorded => "تم تسجيل الإرجاع"; // ar: à relire
        public override string CashToHandBack(string amount) => $"النقد المطلوب إرجاعه للزبون: {amount}"; // ar: à relire
        public override string HeldCount(int tickets) => $"{DisplayFigures.Count(tickets)} في الانتظار"; // ar: à relire
        public override string OwnerPin => "رمز المالك"; // ar: à relire
        public override string TotalBeforeTax => "المجموع خارج الرسم"; // ar: à relire
        public override string WholeDue(string amount) => $"كل المستحق · {amount}"; // ar: à relire
        public override string CashIn => "تحصيل"; // ar: à relire
        public override string RepaidTitle => "تم تحصيل التسديد"; // ar: à relire
        public override string NewBalanceDue => "الرصيد المستحق الجديد"; // ar: à relire
        public override string BalanceBefore => "الرصيد قبل"; // ar: à relire
        public override string RepaidInCash => "مسدد نقدًا"; // ar: à relire
        public override string Finish => "إنهاء"; // ar: à relire
        public override string ReturnLabel => "إرجاع"; // ar: à relire
        public override string CreditIssuedTitle => "تم إصدار الرصيد"; // ar: à relire
        public override string ReturnNumber(string number) => $"إرجاع رقم {number}"; // ar: à relire
        public override string CreditAfterReturn(string name) => $"رصيد {name} بعد هذا الإرجاع"; // ar: à relire
        public override string CreditBefore => "الرصيد قبل"; // ar: à relire
        public override string IssuedForReturn(string number) => $"صادر عن الإرجاع رقم {number}"; // ar: à relire
        public override string OpeningLogged(string clock, string staff) => $"فتح مسجل على {clock} من طرف {staff}"; // ar: à relire
        public override string CarnetOffline => "الخادم غير متاح: لا يمكن قراءة دفتر الديون."; // ar: à relire
        public override string StatementTitle => "الكشف · من الأقدم إلى الأحدث"; // ar: à relire
        public override string ChangeTabKey => "تعديل دفتر الديون"; // ar: à relire
        public override string OpenTabKey => "فتح دفتر ديون"; // ar: à relire
        public override string RepayKey => "تحصيل تسديد"; // ar: à relire
        public override string TabFrozen => "مجمد"; // ar: à relire
        public override string TabFrozenDetail => "من طرف المالك: لا شراء جديد بالدَّين. التسديد ممكن."; // ar: à relire
        public override string TabOverdue => "متأخر"; // ar: à relire
        public override string TabOverdueDetail(int days, int rule) => $"أقدم دين عمره {DisplayFigures.Count(days)} يومًا، والمهلة {DisplayFigures.Count(rule)}."; // ar: à relire
        public override string NoTab => "بدون دفتر ديون"; // ar: à relire
        public override string NoTabDetail => "لا سقف: لا يمكن لهذا الزبون الشراء بالدَّين."; // ar: à relire
        public override string MovementLabel(string kind, bool negative) => kind switch { "charge" when negative => "إرجاع إلى دفتر الديون", "charge" => "شراء بالدَّين", "payment" => "تسديد · نقدًا", "adjustment" => "تعديل", _ => "شطب" }; // ar: à relire
        public override string CarnetTitle(string name) => $"دفتر الديون — {name}"; // ar: à relire
        public override string CarnetSubtitle => "اطلاع على حساب الزبون"; // ar: à relire
        public override string Limit => "السقف"; // ar: à relire
        public override string Available => "المتاح"; // ar: à relire
        public override string OldestUnpaid => "أقدم دين"; // ar: à relire
        // The figures first, the word after: a letter between two numbers reordered them ("ي / 30 0").
        public override string DaysOf(int days, int rule) => $"{DisplayFigures.Count(days)} / {DisplayFigures.Count(rule)} يوم"; // ar: à relire
        public override string Days(int days) => $"{DisplayFigures.Count(days)} ي"; // ar: à relire
        public override string ColumnDate => "التاريخ"; // ar: à relire
        public override string ColumnMovement => "الحركة"; // ar: à relire
        public override string ColumnAmount => "المبلغ"; // ar: à relire
        public override string ColumnBalance => "الرصيد"; // ar: à relire
        public override string NoMovement => "لا حركة."; // ar: à relire
        public override string MethodTab => "دفتر الديون"; // ar: à relire
        public override string TabOf(string name) => $"دفتر ديون {name}"; // ar: à relire
        public override string TabAvailable => "المتاح"; // ar: à relire
        public override string AfterThisSale => "بعد هذا البيع"; // ar: à relire
        public override string TabAmountTitle => "المبلغ على دفتر الديون"; // ar: à relire
        public override string AboveLimit => "فوق السقف"; // ar: à relire
        public override string AboveLimitDetail(string available, string amount) => $"يتبقى {available} في دفتر الديون هذا مقابل {amount}. يمكن للمالك السماح بهذا البيع."; // ar: à relire
        public override string TabFrozenRefused => "دفتر ديون مجمد"; // ar: à relire
        public override string TabFrozenRefusedDetail => "مجمد من طرف المالك: لا شراء بالدَّين. ادفع بطريقة أخرى."; // ar: à relire
        public override string TabOverdueRefused => "دفتر ديون متأخر"; // ar: à relire
        public override string TabOverdueRefusedDetail => "تجاوز أقدم دين المهلة: ادفع بطريقة أخرى أو سدد أولًا."; // ar: à relire
        public override string WholeTicketOnly => "التذكرة كلها أو لا شيء: لا يُجمع دفتر الديون مع وسائل أخرى في هذا المتجر."; // ar: à relire
        public override string TabPartExact => "دفتر الديون يأخذ المبلغ بالضبط: ليس أكثر من الباقي أبدًا."; // ar: à relire
        public override string TabNotWithParts => "التذكرة كلها أو لا شيء"; // ar: à relire
        public override string TabNotWithPartsDetail => "أزل أجزاء البطاقة أو بريدي موب لوضع التذكرة على دفتر الديون."; // ar: à relire
        public override string ValidateOnTab => "تأكيد على دفتر الديون"; // ar: à relire
        public override string OverrideWithOwnerPin => "تجاوز برمز المالك"; // ar: à relire
        public override string CreditIsNamed => "الرصيد بالاسم"; // ar: à relire
        public override string CreditIsNamedDetail => "التذكرة الأصلية بدون زبون: اربط زبونًا (F5)."; // ar: à relire
        public override string AttachClient => "ربط زبون · F5"; // ar: à relire
        public override string ApproveCreateTitle(string name) => $"إنشاء الزبون {name}"; // ar: à relire
        public override string ApproveChangeTitle(string name) => $"تغيير دفتر ديون {name}"; // ar: à relire
        public override string ApproveOverrideTitle(string name) => $"تجاوز سقف {name}"; // ar: à relire
        public override string OwnerApproval => "ترخيص المالك"; // ar: à relire
        public override string ApproveValidate => "تأكيد"; // ar: à relire
        public override string RefundKey => "استرداد"; // ar: à relire
        public override string RefundLabel => "استرداد"; // ar: à relire
        public override string RefundPrompt => "المس سلعة من التذكرة لإرجاعها."; // ar: à relire
        public override string RefundLineDetail(string article, string back, string left) => $"{article} · إرجاع {back} من {left}"; // ar: à relire
        public override string LessKey => "− 1";
        public override string MoreKey => "+ 1";
        public override string RestockKey => "إلى الرف"; // ar: à relire
        public override string ReturnChip(string quantity) => $"مُرجَع {quantity}"; // ar: à relire
        public override string RestockChip => "إلى الرف"; // ar: à relire
        public override string NoRestockChip => "خارج الرف"; // ar: à relire
        public override string ReturnedChip(string quantity) => $"أُرجع سابقًا {quantity}"; // ar: à relire
        public override string RefundChosen(int lines) => $"{DisplayFigures.Count(lines)} سلعة مُرجَعة"; // ar: à relire
        public override string NoReturnReasons => "لا توجد أسباب إرجاع: يضيفها المسؤول من الإدارة."; // ar: à relire
        public override string ChooseLines => "المس سلعة واحدة على الأقل لإرجاعها."; // ar: à relire
        public override string RefundOffline => "الخادم غير متاح: لم يُسترد شيء."; // ar: à relire
        public override string RefundRefused => "رُفض الاسترداد"; // ar: à relire
        public override string ApproveRefund => "تأكيد الاسترداد"; // ar: à relire
        public override string RefundTitle => "استرداد"; // ar: à relire
        public override string RefundTotal => "المبلغ المُرجَع"; // ar: à relire
        public override string RefundToTab => "مخصوم من دفتر الديون"; // ar: à relire
        public override string CashOut => "نقدًا للزبون"; // ar: à relire
        public override string CreditOut => "رصيد للزبون"; // ar: à relire
        public override string MethodStoreCredit => "رصيد"; // ar: à relire
        public override string RefundLinesTitle => "السلع المُرجَعة"; // ar: à relire
        public override string RefundLinesSummary(int lines, int restocked) => $"{RefundChosen(lines)} · {DisplayFigures.Count(restocked)} إلى الرف"; // ar: à relire
        public override string RefundCloseHint => "Esc أو × يعود إلى التذكرة دون استرداد"; // ar: à relire
        public override string RefundOfTicket(string number) => $"استرداد التذكرة {number}"; // ar: à relire
        public override string RefundingNotice => "استرداد"; // ar: à relire
        public override string RefundingNoticeDetail => "المس السلع المُرجَعة، اختر سببًا، ثم متابعة."; // ar: à relire
        public override string ManagerOnly => "مخصّص للمسؤول"; // ar: à relire
        public override string StatusVoided => "ملغاة"; // ar: à relire
        public override string StatusRefunded => "مستردّة"; // ar: à relire
        public override string StatusPartlyRefunded => "مستردّة جزئيًا"; // ar: à relire
        public override string PastTicket => "تذكرة سابقة"; // ar: à relire
        public override string PastTicketLine(string dayAndMonth, string clock, string? staff) => // ar: à relire
            staff is null ? $"بيعت يوم {dayAndMonth} على {clock} · للقراءة فقط" : $"بيعت يوم {dayAndMonth} على {clock} من طرف {staff} · للقراءة فقط";
        public override string PastTicketFooter => "للقراءة فقط. «إغلاق» يعيد التذكرة الحالية."; // ar: à relire
        public override string TaxIncluded => "منها الرسم"; // ar: à relire
        // Sign-in (A5). No Arabic board shows this screen: every string here is to be read.
        public override string WhoOpensTheTill => "من يفتح الصندوق؟"; // ar: à relire
        public override string ChooseYourName => "اختر اسمك"; // ar: à relire
        public override string PinOf(string name) => $"الرمز السري لـ {name}"; // ar: à relire
        public override string ClearKey => "مسح"; // ar: à relire
        public override string OpenTheTill => "فتح الصندوق"; // ar: à relire
        public override string NoPinLabel => "بدون رمز سري"; // ar: à relire
        public override string NoPinDetail => "لم يُحدَّد رمز سري لهذا الشخص. يحدّده المسؤول على خادم المتجر."; // ar: à relire
        public override string NobodyMaySignIn => "لا أحد يمكنه فتح هذا الصندوق"; // ar: à relire
        public override string NobodyMaySignInHint => "لا يوجد موظف نشط مسجّل لهذا المتجر."; // ar: à relire
        public override string WrongPin => "رمز خاطئ"; // ar: à relire
        public override string AttemptsLeft(int count) => // ar: à relire
            "يتبقى " + ArabicCount(count, "محاولة واحدة", "محاولتان", "محاولات", "محاولة", "محاولة") + " قبل القفل";
        public override string Locked => "مقفل"; // ar: à relire
        public override string LockedUntil(string clock) => $"محاولات كثيرة. أعد المحاولة على {clock}."; // ar: à relire
        public override string UnknownStaff => "شخص غير معروف"; // ar: à relire
        public override string UnknownStaffDetail => "لم يعد بإمكان هذا الشخص فتح الصندوق. تم تحديث القائمة."; // ar: à relire
        public override string UnknownTerminal => "صندوق غير معروف"; // ar: à relire
        public override string UnknownTerminalDetail => "خادم المتجر لا يعرف هذا الصندوق. تحقّق من معرّفه (--terminal=)."; // ar: à relire
        public override string NoTerminalDetail => "هذا الصندوق بلا معرّف (--terminal=)."; // ar: à relire
        public override string SessionEnded => "انتهت الجلسة"; // ar: à relire
        public override string SessionEndedDetail => "لم يعد الخادم يعرف هذه الجلسة. سجّل الدخول مجددًا: التذكرة الحالية محفوظة."; // ar: à relire
        public override string TicketInProgress => "تذكرة جارية"; // ar: à relire
        public override string FinishBeforeSwitching => "ادفع أو أزل الأسطر قبل تغيير البائع."; // ar: à relire

        /// <summary>
        /// A counted noun in Arabic (F-24). One and two are words; above that the noun agrees with
        /// the number's <b>last two digits</b>, not the whole: 3 to 10 take the plural ("103 أسطر"),
        /// 11 to 99 the singular with tanwin ("111 سطرًا"), and a round hundred, or a hundred and one
        /// or two, the bare singular ("100 سطر", "102 سطر").
        /// </summary>
        private static string ArabicCount(int count, string one, string two, string few, string many, string single)
        {
            if (count is 1 or 2)
            {
                return count == 1 ? one : two;
            }

            var figure = DisplayFigures.Count(count);
            return (count % 100) switch
            {
                >= 3 and <= 10 => $"{figure} {few}",
                <= 2 when count >= 100 => $"{figure} {single}",
                _ => $"{figure} {many}",
            };
        }
    }
}
