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
public sealed class TillSession(IProductSource products, IStoreSales sales, TillIdentity till)
{
    private Task _tail = Task.CompletedTask;

    public Cart Cart { get; } = new();

    /// <summary>What the cashier must be told about the last code, or null after one that went in.</summary>
    public TillNotice? Notice { get; private set; }

    /// <summary>The last completed sale, for the cashier to collect; null once a new cart starts.</summary>
    public SaleOutcome? LastSale { get; private set; }

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

    /// <summary>Takes a line out of the sale.</summary>
    public void Remove(string variantId)
    {
        if (Cart.Remove(variantId))
        {
            Raise();
        }
    }

    /// <summary>The cashier has read the notice.</summary>
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
        if (Notice is null)
        {
            LastSale = null;
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

        if (Cart.Lines.Count == 0)
        {
            return;
        }

        if (string.IsNullOrWhiteSpace(till.TerminalId) || string.IsNullOrWhiteSpace(till.StaffId))
        {
            Notice = new TillNotice(TillNoticeKind.SaleRefused, "-", "This till has no terminal or staff configured (--terminal=, --staff=).");
            Raise();
            return;
        }

        var request = new SaleRequest(
            till.TerminalId,
            till.StaffId,
            [.. Cart.Lines.Select(line => new SaleRequestLine(line.Barcode, line.Count))]);

        switch (await sales.CompleteSaleAsync(request))
        {
            case SaleAnswer.Completed completed:
                Cart.Clear();
                LastSale = completed.Outcome;
                Notice = null;
                break;

            case SaleAnswer.Refused refused:
                Notice = new TillNotice(TillNoticeKind.SaleRefused, "-", refused.Reason);
                break;

            case SaleAnswer.Unknown unknown:
                Notice = new TillNotice(
                    TillNoticeKind.SaleOutcomeUnknown,
                    "-",
                    $"{unknown.Why} The sale may have been recorded: check before selling this cart again.");
                break;
        }

        Raise();
    }

    private async Task<TillNotice?> Handle(string code)
    {
        switch (await products.LookupAsync(code))
        {
            case LookupAnswer.ServerUnavailable unavailable:
                return new TillNotice(TillNoticeKind.ServerUnavailable, code, unavailable.Why);

            case LookupAnswer.Answered { Lookup.Outcome: ProductLookupOutcome.UnknownBarcode }:
                return new TillNotice(TillNoticeKind.UnknownCode, code, "No product carries this code.");

            case LookupAnswer.Answered { Lookup: { Outcome: ProductLookupOutcome.NotSellable } refused }:
                return new TillNotice(TillNoticeKind.NotSellable, code, Explain(refused.Reason));

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

    /// <summary>The refusal in the cashier's words. The skeleton's UI is English; Arabic and French are Phase 1.</summary>
    private static string Explain(string? reason) => reason switch
    {
        NotSellableReason.NoCurrentPrice => "No price is in force for this product today.",
        NotSellableReason.PriceNotTaxInclusive => "Its price is recorded without tax (HT); the till sells TTC prices only.",
        NotSellableReason.Archived => "This product is archived.",
        NotSellableReason.NoTaxRate => "Its category has no TVA rate.",
        NotSellableReason.ConflictingTaxRates => "Its categories disagree on the TVA rate.",
        NotSellableReason.Weighted => "It is sold by weight, which the till does not handle yet.",
        _ => $"Not sellable ({reason}).",
    };

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

    /// <summary>No answer to a sale: it may or may not have been written.</summary>
    SaleOutcomeUnknown,
}

/// <summary>Who is selling at which till. No login until Phase 1: the till is configured with both.</summary>
public sealed record TillIdentity(string? TerminalId, string? StaffId);

/// <summary>What the cashier is told: the kind, the code, and why, in words.</summary>
public sealed record TillNotice(TillNoticeKind Kind, string Code, string Detail);
