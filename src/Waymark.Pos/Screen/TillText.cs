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

    /// <summary>"Espèces", "Carte", "Mobile"; anything else as the wire says it.</summary>
    public abstract string PaymentMethod(string method);

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
        public override string ManagerApproval => "AUTORISATION RESPONSABLE";
        public override string WhoApproves => "QUI AUTORISE";
        public override string ManagerPin => "PIN RESPONSABLE";
        public override string ApproveDiscount => "Valider la remise";
        public override string WrongManagerPin(int attemptsLeft) => $"PIN incorrect · {attemptsLeft} essai{(attemptsLeft > 1 ? "s" : string.Empty)} avant blocage";
        public override string ManagerLocked(string until) => $"Trop d'essais : bloqué jusqu'à {until}";
        public override string NotAManager => "Cette personne n'a pas le rang pour autoriser une remise";
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
        public override string ManagerOnly => "RÉSERVÉ AU RESPONSABLE";
        public override string StatusVoided => "ANNULÉE";
        public override string StatusRefunded => "REMBOURSÉE";
        public override string StatusPartlyRefunded => "REMBOURSÉE EN PARTIE";
        public override string PastTicket => "TICKET PASSÉ";
        public override string PastTicketLine(string dayAndMonth, string clock, string? staff) =>
            staff is null ? $"Vendu le {dayAndMonth} à {clock} · lecture seule" : $"Vendu le {dayAndMonth} à {clock} par {staff} · lecture seule";
        public override string PastTicketFooter => "Lecture seule. Fermer rend le ticket en cours.";
        public override string TaxIncluded => "Dont TVA";
        public override string PaymentMethod(string method) => method switch
        {
            "cash" => "Espèces",
            "card" => "Carte",
            "mobile_wallet" => "Mobile",
            _ => method,
        };

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
        public override string ManagerOnly => "مخصّص للمسؤول"; // ar: à relire
        public override string StatusVoided => "ملغاة"; // ar: à relire
        public override string StatusRefunded => "مستردّة"; // ar: à relire
        public override string StatusPartlyRefunded => "مستردّة جزئيًا"; // ar: à relire
        public override string PastTicket => "تذكرة سابقة"; // ar: à relire
        public override string PastTicketLine(string dayAndMonth, string clock, string? staff) => // ar: à relire
            staff is null ? $"بيعت يوم {dayAndMonth} على {clock} · للقراءة فقط" : $"بيعت يوم {dayAndMonth} على {clock} من طرف {staff} · للقراءة فقط";
        public override string PastTicketFooter => "للقراءة فقط. «إغلاق» يعيد التذكرة الحالية."; // ar: à relire
        public override string TaxIncluded => "منها الرسم"; // ar: à relire
        public override string PaymentMethod(string method) => method switch // ar: à relire
        {
            "cash" => "نقدًا",
            "card" => "بطاقة",
            "mobile_wallet" => "الهاتف",
            _ => method,
        };

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
