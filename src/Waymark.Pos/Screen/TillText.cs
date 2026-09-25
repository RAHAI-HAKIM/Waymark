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

        public override string SearchPlaceholder => "Scanner ou saisir un code-barres";
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
            Contracts.Pos.NotSellableReason.Weighted => "vendu au poids, pas encore pris en charge",
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

        public override string SearchPlaceholder => "امسح الرمز الشريطي أو أدخله"; // ar: à relire
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
            Contracts.Pos.NotSellableReason.Weighted => "يُباع بالوزن، غير مدعوم بعد",
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
