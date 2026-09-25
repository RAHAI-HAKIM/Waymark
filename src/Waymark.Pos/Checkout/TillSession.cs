using Waymark.Contracts.Pos;
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
/// </summary>
public sealed class TillSession(IProductSource products, IStoreSales sales, TillIdentity till, TimeProvider? clock = null)
{
    private readonly TimeProvider _clock = clock ?? TimeProvider.System;
    private Task _tail = Task.CompletedTask;

    public Cart Cart { get; } = new();

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
    /// Whether the cashier may change now: not while the ticket has lines still in the sale. Until
    /// B2 parks a ticket, changing hands would sell one person's ticket under another's name.
    /// </summary>
    public bool MaySwitchCashier => Cart.ActiveLines.Count == 0;

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
    public void Remove(string variantId)
    {
        if (Cart.Remove(variantId, _clock.GetUtcNow()))
        {
            Raise();
        }
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

        // Only struck lines can be left: an abandoned ticket, not a sale. The next person starts clean.
        SignedIn = null;
        Cart.Clear();
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

    /// <summary>The cashier has read the notice. An unconfirmed sale is not a notice and stays.</summary>
    public void Dismiss()
    {
        if (Notice is not null)
        {
            Notice = null;
            Raise();
        }
    }

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

        Notice = await Handle(code);
        Paid = null;
        if (Notice is null)
        {
            LastSale = null;
            LastSaleAt = null;
        }

        Raise();
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
        if (Cart.ActiveLines.Count == 0 || Unconfirmed is not null)
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
            [.. Cart.ActiveLines.Select(line => new SaleRequestLine(line.Barcode, line.Count))]);

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

            case LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.Found, Product: { } product } }:
                try
                {
                    Cart.Add(product, code);
                    return null;
                }
                catch (Exception exception) when (exception is FormatException or InvalidOperationException)
                {
                    return new TillNotice(TillNoticeKind.ServerUnavailable, code, $"The answer could not be used: {exception.Message}");
                }

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
}

/// <summary>Which till this is: its terminal id, from <c>--terminal=</c>. Who sells at it is the sign-in's (A5).</summary>
public sealed record TillIdentity(string? TerminalId);

/// <summary>A sale with no usable answer (D-085): why, as the client reported it, and when.</summary>
public sealed record UnconfirmedSale(string Why, DateTimeOffset At);

/// <summary>The person signed in at this till, and the token StoreServer gave them (D-083).</summary>
public sealed record SignedInStaff(string StaffId, string Token);

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
