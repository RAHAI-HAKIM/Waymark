using Waymark.Contracts;
using Waymark.Contracts.Pos;
using Waymark.Pos.Screen;
using Waymark.Pos.Server;

namespace Waymark.Pos.Checkout;

/// <summary>
/// What the till does with a code, scanned or typed (hop 1, D-068): ask
/// StoreServer, then either add a line or tell the cashier why not. And, since
/// hop 2 (D-070), paying: the cart goes to StoreServer as codes and counts.
///
/// <para>
/// Plain C# so the behaviour is tested without a window; the window only draws
/// <see cref="Cart"/> and <see cref="Notice"/> when <see cref="Changed"/> fires.
/// </para>
/// <para>
/// <b>Codes are handled in the order they arrived.</b> Two quick scans make two
/// requests, and the second can come back first; without the queue the cart's
/// lines would not be in the order the cashier scanned them, and a notice about
/// the first could overwrite the line of the second.
/// </para>
/// <para>
/// <b>The till holds more than one ticket</b> (B2, D-087): the one on screen, the ones put on hold
/// (<see cref="Parked"/>), and the ones cancelled today (<see cref="Drafts"/>). All three live in
/// this process's memory and belong to the till, not to whoever is signed in: closing the till
/// loses them, and any cashier may take one up.
/// </para>
/// </summary>
/// <param name="zone">The till's time zone, which says when "today" ends for a draft. The machine's when not given.</param>
public sealed class TillSession(IProductSource products, IStoreSales sales, TillIdentity till, TimeProvider? clock = null, TimeZoneInfo? zone = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private readonly TimeZoneInfo _zone = zone ?? TimeZoneInfo.Local;
    private readonly List<HeldTicket> _parked = [];
    private readonly List<HeldTicket> _drafts = [];
    private int _lastHeldId;
    private Task _tail = Task.CompletedTask;
    private Domain.Values.Rounding _policy = Domain.Values.Rounding.HalfUp;

    /// <summary>The ticket on screen. Parking or resuming puts another cart here.</summary>
    public Cart Cart { get; private set; } = new();

    /// <summary>
    /// How many units the next scan or touch adds: 1, or the n of a "n*" typed in the field (B1,
    /// D-088). Used once, then back to 1.
    /// </summary>
    public int NextCount { get; private set; } = 1;

    /// <summary>
    /// A product sold by weight waiting for its weight (B3, D-090), or null: its code was scanned,
    /// typed or touched, or "Poids" was pressed under a line weighed by hand. The weight is typed in
    /// the field; Entrée weighs it, Échap or the next scan gives it up.
    /// </summary>
    public PendingWeight? Weighing { get; private set; }

    /// <summary>
    /// A past ticket open read-only in the ticket view (B1, D-088), or null. The ticket on screen is
    /// untouched underneath; closing the view, or a scan, gives it back.
    /// </summary>
    public PastTicketDetail? Viewing { get; private set; }

    /// <summary>Tickets on hold, oldest first: the tabs in the top bar (G1, "Attente 14:05").</summary>
    public IReadOnlyList<HeldTicket> Parked => _parked;

    /// <summary>
    /// Tickets cancelled today, newest first ("Brouillons"). One cancelled on an earlier day is gone:
    /// a draft lasts until the till's date changes, or the till closes (D-087). What a cancellation
    /// leaves behind beyond that is B8's.
    /// </summary>
    public IReadOnlyList<HeldTicket> Drafts
    {
        get
        {
            var today = Day(_clock.GetUtcNow());
            _drafts.RemoveAll(draft => Day(draft.At) != today);
            return _drafts;
        }
    }

    /// <summary>What the cashier must be told about the last code, or null after one that went in.</summary>
    public TillNotice? Notice { get; private set; }

    /// <summary>The last completed sale, for the cashier to collect; null once a new cart starts.</summary>
    public SaleOutcome? LastSale { get; private set; }

    /// <summary>When <see cref="LastSale"/> completed, by this till's clock.</summary>
    public DateTimeOffset? LastSaleAt { get; private set; }

    /// <summary>
    /// The ticket just paid, lines and all, shown until the cashier starts a new sale or scans
    /// (G1, "monnaie à rendre"). Null otherwise.
    /// </summary>
    public PaidTicket? Paid { get; private set; }

    /// <summary>Whether StoreServer answered the last time it was asked, and since when it has not.</summary>
    public ServerState Server { get; private set; } = ServerState.Reachable;

    /// <summary>
    /// A sale sent with no usable answer: it may or may not have been recorded (D-085). While this is
    /// set, <see cref="PayAsync"/> sends nothing, whatever the screen offers; only
    /// <see cref="AcknowledgeUnconfirmed"/> clears it. Not a <see cref="Notice"/>: a scan replaces
    /// the notice and Échap clears it, and neither may re-open Encaisser.
    /// </summary>
    public UnconfirmedSale? Unconfirmed { get; private set; }

    /// <summary>
    /// Who is signed in, and the token their sales carry (A5, D-083). Null until somebody types
    /// their PIN, and again after a sign-out or when the server no longer holds the session.
    /// </summary>
    public SignedInStaff? SignedIn { get; private set; }

    /// <summary>
    /// Whether the cashier may change now. A ticket with lines no longer stops it: it is put on hold
    /// for whoever comes next (D-087). An unconfirmed sale does, because the next person must not
    /// inherit a ticket that may already be paid (D-085).
    /// </summary>
    public bool MaySwitchCashier => Unconfirmed is null;

    /// <summary>
    /// Whether the ticket on screen can be put on hold or cancelled: it has lines in the sale, it is
    /// not the ticket just paid, and it is not waiting on an unconfirmed sale (D-085), which the
    /// cashier checks first.
    /// </summary>
    public bool MayPutAside => Cart.ActiveLines.Count > 0 && Paid is null && Unconfirmed is null && Viewing is null && Weighing is null;

    /// <summary>
    /// Whether a discount can be given or taken off now (B4): the ticket on screen has a line in the
    /// sale, and nothing else is under way (a sale unconfirmed, a past ticket open, a weight awaited).
    /// </summary>
    public bool MayDiscount => MayPutAside;

    /// <summary>
    /// The store's rounding policy (D-053), from the till's context: every cart works its discount
    /// preview out with it, as the sale will (B4). Carts already held keep theirs.
    /// </summary>
    public void SetRoundingPolicy(Domain.Values.Rounding policy)
    {
        _policy = policy;
        Cart.Policy = policy;
        Raise();
    }

    /// <summary>
    /// A discount given at the counter (B4, D-091), on a line, or on the ticket when
    /// <paramref name="lineId"/> is null; null takes it off. The window has the authorisation already:
    /// the server gave it. False, changing nothing, when no discount can be given now.
    /// </summary>
    public bool Discount(string? lineId, CounterDiscount? discount)
    {
        if (!MayDiscount)
        {
            return false;
        }

        var done = lineId is null ? Cart.SetTicketDiscount(discount) : Cart.SetDiscount(lineId, discount);
        if (done)
        {
            Notice = null;
            Raise();
        }

        return done;
    }

    /// <summary>Raised after every change to <see cref="Cart"/> or <see cref="Notice"/>.</summary>
    public event EventHandler? Changed;

    /// <summary>Queues a code behind any still being looked up. The task completes when this one is handled.</summary>
    public Task SubmitAsync(string code)
    {
        var trimmed = code?.Trim() ?? string.Empty;
        if (trimmed.Length == 0)
        {
            return _tail;
        }

        _tail = HandleAfter(_tail, trimmed);
        return _tail;
    }

    /// <summary>
    /// Sends the cart as a cash sale, behind any code still being looked up, so every scanned
    /// line is in it. Completed: the cart empties and <see cref="LastSale"/> says what to
    /// collect. Refused or unknown: the cart stays, and <see cref="Notice"/> says why.
    /// </summary>
    public Task PayAsync()
    {
        _tail = PayAfter(_tail);
        return _tail;
    }

    /// <summary>
    /// Takes a line out of the sale. It stays on the ticket, struck through with the time (G1);
    /// the reason and its log entry arrive with B8.
    /// </summary>
    public void Remove(string lineId)
    {
        if (Cart.Remove(lineId, _clock.GetUtcNow()))
        {
            Raise();
        }
    }

    /// <summary>The − / + under a selected line (B2). Never below one; see <see cref="Checkout.Cart.SetCount"/>.</summary>
    public void SetCount(string lineId, int count)
    {
        if (Paid is null && Cart.SetCount(lineId, count))
        {
            Raise();
        }
    }

    /// <summary>
    /// "Attente" (F3): the ticket on screen goes on hold as a tab, and an empty one takes its place.
    /// </summary>
    /// <returns>False, changing nothing, when there is nothing to put aside (<see cref="MayPutAside"/>).</returns>
    public bool Park()
    {
        if (!MayPutAside)
        {
            return false;
        }

        _parked.Add(Hold(Cart));
        Cart = NewCart();
        Notice = null;
        Raise();
        return true;
    }

    /// <summary>
    /// "Annuler ticket": the ticket on screen goes to <see cref="Drafts"/>, with who cancelled it
    /// and when, and an empty one takes its place. Nothing reaches the server (D-087).
    /// </summary>
    public bool CancelTicket()
    {
        if (!MayPutAside)
        {
            return false;
        }

        _drafts.Insert(0, Hold(Cart));
        Cart = NewCart();
        Notice = null;
        Raise();
        return true;
    }

    /// <summary>Takes a ticket on hold back onto the screen. See <see cref="Resume"/>.</summary>
    public bool ResumeParked(string heldId) => Resume(_parked, heldId);

    /// <summary>Takes a cancelled ticket back onto the screen ("Reprendre"). See <see cref="Resume"/>.</summary>
    public bool ResumeDraft(string heldId)
    {
        _ = Drafts; // forget yesterday's first: a stale draft is not resumed
        return Resume(_drafts, heldId);
    }

    /// <summary>Somebody's PIN was right: their sales are theirs from now on.</summary>
    public void SignIn(SignedInStaff person)
    {
        ArgumentNullException.ThrowIfNull(person);
        SignedIn = person;
        Notice = null;
        Raise();
    }

    /// <summary>
    /// Ends the signed-in person's turn at the till ("Changer de caissier"). Refused, with a notice,
    /// while the ticket has lines in the sale (<see cref="MaySwitchCashier"/>).
    /// </summary>
    /// <returns>The token to end on the server; null when refused or nobody was signed in.</returns>
    public string? SignOut()
    {
        if (SignedIn is not { } person)
        {
            return null;
        }

        if (!MaySwitchCashier)
        {
            Notice = new TillNotice(TillNoticeKind.SwitchRefused, "-", string.Empty);
            Raise();
            return null;
        }

        // A ticket with lines is put on hold, for whoever takes the till next (D-087); one with only
        // struck lines is an abandoned ticket, not a sale, and the next person starts clean.
        if (MayPutAside)
        {
            _parked.Add(Hold(Cart));
        }

        SignedIn = null;
        Cart = NewCart();
        Paid = null;
        Notice = null;
        Raise();
        return person.Token;
    }

    /// <summary>The cashier has seen the change and starts the next sale ("Nouvelle vente").</summary>
    public void StartNewSale()
    {
        if (Paid is not null)
        {
            Paid = null;
            Raise();
        }
    }

    /// <summary>
    /// What a health check found. Kept apart from the answers to scans and sales, so a till
    /// sitting idle still learns that the server has gone, and that it is back.
    /// </summary>
    public void ReportHealth(bool reachable)
    {
        var before = Server;
        Observe(reachable);
        if (Server != before)
        {
            Raise();
        }
    }

    /// <summary>
    /// The cashier has checked whether the unconfirmed sale was recorded (D-085): Encaisser is
    /// available again. The ticket stays as it is; what to do with it is the cashier's call.
    /// </summary>
    public void AcknowledgeUnconfirmed()
    {
        if (Unconfirmed is not null)
        {
            Unconfirmed = null;
            Raise();
        }
    }

    /// <summary>"3*" typed in the field: the next scan or touch adds three (B1). Refused outside 1 to <see cref="Cart.MaxCount"/>.</summary>
    public void SetNextCount(int count)
    {
        if (count is >= 1 and <= Cart.MaxCount && count != NextCount)
        {
            NextCount = count;
            Raise();
        }
    }

    /// <summary>What the window learnt and the cashier must be told: a ticket not found, not allowed, the server gone.</summary>
    public void Tell(TillNoticeKind kind, string code, string detail)
    {
        Notice = new TillNotice(kind, code, detail);
        Raise();
    }

    /// <summary>Échap: the next scan adds one again.</summary>
    public void ResetNextCount() => SetNextCount(1);

    /// <summary>
    /// A search result touched (B1): it goes in as a scan of its code would, behind any code still
    /// being looked up, with the same count. The server prices it again when the sale is sent (D-070).
    /// </summary>
    public Task AddFoundAsync(ProductForSale product, string code)
    {
        ArgumentNullException.ThrowIfNull(product);
        ArgumentException.ThrowIfNullOrWhiteSpace(code);
        _tail = AddAfter(_tail, product, code);
        return _tail;
    }

    /// <summary>
    /// The weight typed so far, priced by the server as the cashier types (B3): the card under the
    /// field shows its total before Entrée. An answer to an older text is dropped.
    /// </summary>
    public async Task PreviewWeightAsync(string typed)
    {
        if (Weighing is not { } pending)
        {
            return;
        }

        if (!WeightEntry.TryParse(typed, pending.Decimals, out var thousandths))
        {
            Weighing = pending with { Typed = typed, Preview = null, Refusal = null, Invalid = typed.Trim().Length > 0 };
            Raise();
            return;
        }

        Weighing = pending with { Typed = typed, Invalid = false };
        var answer = await products.WeighAsync(pending.Code, Figures.Quantity(thousandths));
        if (Weighing is not { } now || now.Typed != typed || now.Code != pending.Code)
        {
            return;
        }

        Weighing = answer switch
        {
            LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.Found, Weighed: { } weighed, Product: { } product } } =>
                now with { Preview = WireFigures.Weight(weighed, product), Refusal = null },
            LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.NotSellable } refused } =>
                now with { Preview = null, Refusal = refused.Reason },
            _ => now with { Preview = null, Refusal = null },
        };
        Raise();
    }

    /// <summary>
    /// Entrée on a weight (B3): the weight typed, weighed by the server and put on the ticket, behind
    /// any code still being looked up. False, weighing nothing, when the text is not a weight the unit
    /// can be sold in (<see cref="WeightEntry"/>): the card says so and the text stays to be corrected.
    /// </summary>
    public bool ConfirmWeight(string typed)
    {
        if (Weighing is not { } pending)
        {
            return false;
        }

        if (!WeightEntry.TryParse(typed, pending.Decimals, out var thousandths))
        {
            Weighing = pending with { Typed = typed, Preview = null, Invalid = true };
            Raise();
            return false;
        }

        _tail = WeighAfter(_tail, pending, thousandths);
        return true;
    }

    /// <summary>Échap on a weight: nothing is weighed, and the ticket is as it was.</summary>
    public void CancelWeighing()
    {
        if (Weighing is not null)
        {
            Weighing = null;
            Raise();
        }
    }

    /// <summary>
    /// "Poids" under a line weighed by hand (B3): the weight typed again. A label's line has no such
    /// key; its weight or price is the label's (<see cref="Checkout.Cart.SetWeight"/>).
    /// </summary>
    public void Reweigh(string lineId)
    {
        if (Paid is null && Cart.ActiveLines.FirstOrDefault(line => line.LineId == lineId) is { Weight: { IsTyped: true } weight } line)
        {
            Weighing = new PendingWeight(line.Barcode, line.ProductName, line.VariantName, line.UnitPrice, line.UnitCode, weight.UnitDecimals, lineId);
            Raise();
        }
    }

    /// <summary>Opens a past ticket read-only (B1). Nothing of the ticket on screen changes.</summary>
    public void View(PastTicketDetail ticket)
    {
        ArgumentNullException.ThrowIfNull(ticket);
        Viewing = ticket;
        Notice = null;
        Raise();
    }

    /// <summary>Closes the past ticket and gives the ticket on screen back.</summary>
    public void CloseView()
    {
        if (Viewing is not null)
        {
            Viewing = null;
            Raise();
        }
    }

    /// <summary>The cashier has read the notice. An unconfirmed sale is not a notice and stays.</summary>
    public void Dismiss()
    {
        if (Notice is not null)
        {
            Notice = null;
            Raise();
        }
    }

    /// <summary>
    /// Moves a held ticket onto the screen. The ticket there, if it has lines in the sale, goes on
    /// hold in its place, so nothing is lost by resuming; the ticket just paid is closed, as a new
    /// sale would close it. Refused while a sale is unconfirmed (D-085).
    /// </summary>
    private bool Resume(List<HeldTicket> from, string heldId)
    {
        var index = from.FindIndex(held => held.Id == heldId);
        if (index < 0 || Unconfirmed is not null)
        {
            return false;
        }

        var held = from[index];
        from.RemoveAt(index);
        if (Paid is null && Cart.ActiveLines.Count > 0)
        {
            _parked.Add(Hold(Cart));
        }

        Cart = held.Cart;
        Paid = null;
        Viewing = null;
        Notice = null;
        Raise();
        return true;
    }

    private Cart NewCart() => new() { Policy = _policy };

    private HeldTicket Hold(Cart cart) =>
        new($"H{++_lastHeldId}", cart, _clock.GetUtcNow(), SignedIn?.StaffId, SignedIn?.Name);

    private DateOnly Day(DateTimeOffset moment) => DateOnly.FromDateTime(TimeZoneInfo.ConvertTime(moment, _zone).DateTime);

    private async Task HandleAfter(Task previous, string code)
    {
        // A failure of an earlier code is already on screen; it must not stop this one.
        try
        {
            await previous;
        }
        catch (Exception) when (previous.IsFaulted || previous.IsCanceled)
        {
        }

        Weighing = null; // a scan gives up a weight not yet typed: the scanned product is what the cashier holds
        Notice = await Handle(code);
        Paid = null;
        Viewing = null;
        if (Notice is null)
        {
            LastSale = null;
            LastSaleAt = null;
        }

        Raise();
    }

    private async Task AddAfter(Task previous, ProductForSale product, string code)
    {
        try
        {
            await previous;
        }
        catch (Exception) when (previous.IsFaulted || previous.IsCanceled)
        {
        }

        Weighing = null;
        Notice = AddToCart(product, code, weighed: null);
        Paid = null;
        Viewing = null;
        if (Notice is null)
        {
            LastSale = null;
            LastSaleAt = null;
        }

        Raise();
    }

    /// <summary>
    /// A found product into the cart, with the "QTÉ × n" typed before it, which is then spent. The
    /// same for a scan and a touch, so the two cannot disagree about a count.
    ///
    /// <para>
    /// A product sold by weight takes no count (B3): with a label's weight it goes in as that weight;
    /// without one it waits for a weight (<see cref="Weighing"/>). A count typed before it is spent
    /// and the cashier told, so "× 3" never silently becomes one weighing.
    /// </para>
    /// </summary>
    private TillNotice? AddToCart(ProductForSale product, string code, WeighedAnswer? weighed)
    {
        try
        {
            if (!product.IsWeighted)
            {
                Cart.Add(product, code, NextCount);
                NextCount = 1;
                return null;
            }

            var countIgnored = NextCount > 1;
            NextCount = 1;
            if (weighed is not null)
            {
                Cart.AddWeighed(product, code, WireFigures.Weight(weighed, product));
            }
            else
            {
                Weighing = new PendingWeight(
                    code, product.ProductName, product.VariantName, WireFigures.Money(product.PriceTtc, product.Currency),
                    product.SellingUnitCode, product.SellingUnitDecimalPlaces, LineId: null);
            }

            return countIgnored ? new TillNotice(TillNoticeKind.WeighedTakesNoCount, code, string.Empty) : null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return new TillNotice(TillNoticeKind.ServerUnavailable, code, $"The answer could not be used: {exception.Message}");
        }
    }

    private async Task WeighAfter(Task previous, PendingWeight pending, long thousandths)
    {
        try
        {
            await previous;
        }
        catch (Exception) when (previous.IsFaulted || previous.IsCanceled)
        {
        }

        if (!ReferenceEquals(Weighing, pending) && Weighing?.Code != pending.Code)
        {
            return; // given up, or replaced by a scan, while the weight was on its way
        }

        var answer = await products.WeighAsync(pending.Code, Figures.Quantity(thousandths));
        Observe(reachable: answer is not LookupAnswer.ServerUnavailable);
        Weighing = null;
        Notice = answer switch
        {
            LookupAnswer.ServerUnavailable unavailable => new TillNotice(TillNoticeKind.ServerUnavailable, pending.Code, unavailable.Why),
            LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.NotSellable } refused } =>
                new TillNotice(TillNoticeKind.NotSellable, pending.Code, refused.Reason ?? string.Empty),
            LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.Found, Product: { } product, Weighed: { } weighed } } =>
                PutWeight(pending, product, weighed),
            _ => new TillNotice(TillNoticeKind.ServerUnavailable, pending.Code, "StoreServer's answer could not be read."),
        };

        if (Notice is null)
        {
            Paid = null;
            LastSale = null;
            LastSaleAt = null;
        }

        Raise();
    }

    /// <summary>The weighed product onto the ticket, or its line's weight replaced ("Poids").</summary>
    private TillNotice? PutWeight(PendingWeight pending, ProductForSale product, WeighedAnswer weighed)
    {
        try
        {
            var weight = WireFigures.Weight(weighed, product);
            if (pending.LineId is { } lineId)
            {
                Cart.SetWeight(lineId, weight);
            }
            else
            {
                Cart.AddWeighed(product, pending.Code, weight);
            }

            return null;
        }
        catch (Exception exception) when (exception is FormatException or InvalidOperationException)
        {
            return new TillNotice(TillNoticeKind.ServerUnavailable, pending.Code, $"The answer could not be used: {exception.Message}");
        }
    }

    private async Task PayAfter(Task previous)
    {
        try
        {
            await previous;
        }
        catch (Exception) when (previous.IsFaulted || previous.IsCanceled)
        {
        }

        // D-085: a sale that may already be recorded is not sent again until the cashier has
        // checked. The screen shows Encaisser unavailable; this refuses whatever pressed it.
        // Nor while a past ticket is open (B1): the ticket that would be sold is hidden under it.
        if (Cart.ActiveLines.Count == 0 || Unconfirmed is not null || Viewing is not null || Weighing is not null)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(till.TerminalId))
        {
            Notice = new TillNotice(TillNoticeKind.SaleRefused, "-", "This till has no terminal configured (--terminal=).");
            Raise();
            return;
        }

        if (SignedIn is not { } seller)
        {
            Notice = new TillNotice(TillNoticeKind.NotSignedIn, "-", string.Empty);
            Raise();
            return;
        }

        var request = new SaleRequest(
            till.TerminalId,
            // Only what is still in the sale. A line taken out stays on screen, struck, and must
            // never be charged: sending it would put back what the cashier removed.
            // A weight typed by hand travels as text; a label's travels in its code, which the
            // server reads again (D-090).
            [.. Cart.ActiveLines.Select(line => new SaleRequestLine(
                line.Barcode,
                line.Count,
                line.Weight is { IsTyped: true } typed ? Figures.Quantity(typed.Quantity.Thousandths) : null,
                line.Discount?.ToWire()))],
            // What was given, never what it comes to: the server works the money out (D-091).
            Cart.TicketDiscount?.ToWire());

        switch (await sales.CompleteSaleAsync(request, seller.Token))
        {
            case SaleAnswer.Completed completed:
                Observe(reachable: true);
                var at = _clock.GetUtcNow();
                Paid = new PaidTicket(completed.Outcome, [.. Cart.Lines], at);
                Cart.Clear();
                LastSale = completed.Outcome;
                LastSaleAt = at;
                Notice = null;
                break;

            case SaleAnswer.Refused refused:
                Observe(reachable: true);
                Notice = new TillNotice(TillNoticeKind.SaleRefused, "-", refused.Reason);
                break;

            case SaleAnswer.NotSignedIn:
                // The server holds no session for this till (it restarted, or somebody signed in
                // elsewhere with this till's id). Nothing was written; the ticket is kept for
                // whoever signs in next.
                Observe(reachable: true);
                SignedIn = null;
                Notice = new TillNotice(TillNoticeKind.NotSignedIn, "-", string.Empty);
                break;

            case SaleAnswer.Unknown unknown:
                Unconfirmed = new UnconfirmedSale(
                    $"{unknown.Why} The sale may have been recorded: check before selling this cart again.",
                    _clock.GetUtcNow());
                Notice = null;
                break;
        }

        Raise();
    }

    private async Task<TillNotice?> Handle(string code)
    {
        var answer = await products.LookupAsync(code);
        Observe(reachable: answer is not LookupAnswer.ServerUnavailable);

        switch (answer)
        {
            case LookupAnswer.ServerUnavailable unavailable:
                return new TillNotice(TillNoticeKind.ServerUnavailable, code, unavailable.Why);

            case LookupAnswer.Answered { Lookup.Outcome: ProductLookupOutcome.UnknownBarcode }:
                return new TillNotice(TillNoticeKind.UnknownCode, code, "No product carries this code.");

            case LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.NotSellable } refused }:
                // The reason's code, not words: the screen says it in the till's language.
                return new TillNotice(TillNoticeKind.NotSellable, code, refused.Reason ?? string.Empty);

            case LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.Found, Product: { } product } found }:
                return AddToCart(product, code, found.Weighed);

            default:
                return new TillNotice(TillNoticeKind.ServerUnavailable, code, "StoreServer's answer could not be read.");
        }
    }

    /// <summary>
    /// Keeps the moment the server was first found unreachable, not the latest: "injoignable
    /// depuis 14:31" means since 14:31, however many scans have failed since.
    /// </summary>
    private void Observe(bool reachable) =>
        Server = reachable
            ? ServerState.Reachable
            : Server.UnreachableSince is null ? new ServerState(_clock.GetUtcNow()) : Server;

    private void Raise() => Changed?.Invoke(this, EventArgs.Empty);
}

/// <summary>Why the last code did not become a line.</summary>
public enum TillNoticeKind
{
    UnknownCode,
    NotSellable,
    ServerUnavailable,

    /// <summary>StoreServer refused the sale; nothing was written.</summary>
    SaleRefused,

    /// <summary>Nobody is signed in, or the server no longer holds the session: nothing was sent, or nothing written.</summary>
    NotSignedIn,

    /// <summary>"Changer de caissier" while the ticket has lines in the sale.</summary>
    SwitchRefused,

    /// <summary>A ticket number this store has no finished sale for (B1).</summary>
    TicketUnknown,

    /// <summary>A past ticket of another day or till, and the person's rank does not reach 2 (B1, D-088).</summary>
    TicketNotAllowed,

    /// <summary>A "× n" typed before a product sold by weight: spent, and not applied (B3).</summary>
    WeighedTakesNoCount,
}

/// <summary>A product sold by weight waiting for its weight (B3, D-090).</summary>
/// <param name="Code">What the sale will send for it: its barcode or PLU.</param>
/// <param name="UnitPrice">Per unit of weight, for the card: "180,00 DA/kg".</param>
/// <param name="Decimals">How finely the unit is sold: the most decimals a weight may have.</param>
/// <param name="LineId">The line whose typed weight is being corrected, or null for a new weighing.</param>
/// <param name="Typed">The text in the field so far.</param>
/// <param name="Preview">The server's answer to it: the weight and the total, before Entrée.</param>
/// <param name="Refusal">The server's reason, when it refused that weight.</param>
/// <param name="Invalid">The text is not a weight the unit can be sold in.</param>
public sealed record PendingWeight(
    string Code,
    string ProductName,
    string VariantName,
    Domain.Values.Money UnitPrice,
    string UnitCode,
    int Decimals,
    string? LineId,
    string Typed = "",
    LineWeight? Preview = null,
    string? Refusal = null,
    bool Invalid = false);

/// <summary>Which till this is: its terminal id, from <c>--terminal=</c>. Who sells at it is the sign-in's (A5).</summary>
public sealed record TillIdentity(string? TerminalId);

/// <summary>A sale with no usable answer (D-085): why, as the client reported it, and when.</summary>
public sealed record UnconfirmedSale(string Why, DateTimeOffset At);

/// <summary>The person signed in at this till, and the token StoreServer gave them (D-083).</summary>
/// <param name="Name">Their name as the sign-in list showed it, for what the till records them doing.</param>
public sealed record SignedInStaff(string StaffId, string Token, string? Name = null);

/// <summary>A ticket off the screen: on hold, or cancelled (D-087).</summary>
/// <param name="Id">The till's own id for it, for the tab or the row that brings it back.</param>
/// <param name="At">When it was put aside.</param>
/// <param name="ByStaffId">Who put it aside, when somebody was signed in.</param>
/// <param name="ByName">Their name, for the drafts list.</param>
public sealed record HeldTicket(string Id, Cart Cart, DateTimeOffset At, string? ByStaffId, string? ByName);

/// <summary>
/// What the cashier is told: the kind, the code, and the detail the screen turns into words.
/// For <see cref="TillNoticeKind.NotSellable"/> the detail is the reason's code (D-066), which
/// <c>TillText</c> says in the till's language; for the others it is what the server or this
/// client reported.
/// </summary>
public sealed record TillNotice(TillNoticeKind Kind, string Code, string Detail);

/// <summary>A sale just completed: its outcome, the lines as they stood, and when.</summary>
/// <param name="Lines">Every line, the removed ones struck, as the cashier saw them when paying.</param>
public sealed record PaidTicket(SaleOutcome Outcome, IReadOnlyList<CartLine> Lines, DateTimeOffset At);

/// <summary>Whether StoreServer can be reached, and since when it could not.</summary>
/// <param name="UnreachableSince">Null while it answers.</param>
public sealed record ServerState(DateTimeOffset? UnreachableSince)
{
    public static ServerState Reachable { get; } = new((DateTimeOffset?)null);

    public bool IsReachable => UnreachableSince is null;
}
