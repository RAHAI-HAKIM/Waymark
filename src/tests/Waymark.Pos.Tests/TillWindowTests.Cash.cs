using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Input;
using Avalonia.VisualTree;
using Waymark.Contracts.Pos;
using Waymark.Pos.Screen;

namespace Waymark.Pos.Tests;

/// <summary>
/// B10 (D-102): "Petite caisse" and "Pointage" at the till. The silent failures: a reason for cash in
/// offered to take cash out; cash out sent with nobody's authorisation where the shop asks for one; a
/// clock that takes the seller for whoever typed their PIN.
/// </summary>
public sealed partial class TillWindowTests
{
    private static void OpenPettyCash(Till till, string direction)
    {
        till.Tap(Operation(till, Screen.Operation.PettyCash), new Point(20, 20));
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.PettyCashTitle);
        till.Tap(Keyed<FormKey>(till, key => key.Id == direction), new Point(10, 10));
    }

    [Fact]
    public Task Cash_out_lists_only_the_reasons_for_cash_out_or_either_way() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();

        OpenPettyCash(till, CustomerScreen.CashOutKey);
        till.WaitFor(() => FormOf(till)?.Rows.Count > 0);

        Assert.Equal(["caisse-depot", "caisse-divers"], FormOf(till)!.Rows.Select(row => row.Id));
    });

    [Fact]
    public Task A_paid_in_is_sent_with_its_reason_and_amount_and_shown_once_recorded() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();

        OpenPettyCash(till, CustomerScreen.CashInKey);
        till.WaitFor(() => FormOf(till)?.Rows.Count > 0);
        Keys(till, "2000");
        till.Tap(Keyed<FormRow>(till, row => row.Id == "caisse-reglement"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.CashInRecorded);

        var sent = Assert.Single(till.Server.CashMovements);
        Assert.Equal(("in", "2000.00", "caisse-reglement", (string?)null), (sent.Direction, sent.Amount, sent.ReasonCode, sent.Authorisation));
    });

    [Fact]
    public Task Cash_out_below_the_shops_rank_asks_a_pin_then_goes_with_the_authorisation() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();
        till.Server.PaidOutNeedsManager = true;

        OpenPettyCash(till, CustomerScreen.CashOutKey);
        till.WaitFor(() => FormOf(till)?.Rows.Count > 0);
        Keys(till, "500");
        till.Tap(Keyed<FormRow>(till, row => row.Id == "caisse-depot"), new Point(20, 10));
        till.Key(Key.Enter, PhysicalKey.Enter);
        SamiaAuthorises(till);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.CashOutRecorded);

        Assert.Equal("auth-samia", till.Server.CashMovements[^1].Authorisation);
    });

    [Fact]
    public Task Pointage_sends_who_and_their_pin_and_says_the_arrival() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();

        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.Clock), new Point(20, 20));
        till.WaitFor(() => FormOf(till)?.Rows.Count == 2);
        till.Tap(Keyed<FormRow>(till, row => row.Id == "samia"), new Point(20, 10));
        Keys(till, "1357");
        till.WaitFor(() => FormOf(till)!.Fields[0].Value.Length == 4); // each key is released by the scanner's timer
        Assert.Equal("●●●●", FormOf(till)!.Fields[0].Value); // dots, never the digits
        till.Key(Key.Enter, PhysicalKey.Enter);
        till.WaitFor(() => FormOf(till)?.Title == TillText.French.ClockedInTitle);

        Assert.Equal(new ClockRequest("till-1", "samia", "1357"), Assert.Single(till.Server.Clocks));
    });

    [Fact]
    public Task A_wrong_pin_clocks_nobody_and_says_so() => Headless.Run(() =>
    {
        using var till = Till.SignedIn();

        till.Tap(Operation(till, Screen.Operation.More), new Point(20, 20));
        till.Tap(Operation(till, Screen.Operation.Clock), new Point(20, 20));
        till.WaitFor(() => FormOf(till)?.Rows.Count == 2);
        till.Tap(Keyed<FormRow>(till, row => row.Id == "samia"), new Point(20, 10));
        Keys(till, "0000");
        till.Key(Key.Enter, PhysicalKey.Enter);

        till.WaitFor(() => FormOf(till)?.Message is not null);
        Assert.Equal(TillText.French.ClockTitle, FormOf(till)!.Title);
        Assert.Equal(TillText.French.WrongManagerPin(4), FormOf(till)!.Message!.Body);
    });

    private sealed partial class FakeStoreServer
    {
        public List<CashMovementRequest> CashMovements { get; } = [];

        public List<ClockRequest> Clocks { get; } = [];

        /// <summary>The shop's paid_out_min_rank is above the cashier's.</summary>
        public bool PaidOutNeedsManager { get; set; }

        public Task<CashMovementAnswer?> CashMovementAsync(CashMovementRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            CashMovements.Add(request);
            return Task.FromResult<CashMovementAnswer?>(request is { Direction: "out", Authorisation: null } && PaidOutNeedsManager
                ? new CashMovementAnswer(CashMovementOutcomes.PinRequired)
                : new CashMovementAnswer(CashMovementOutcomes.Recorded, request.Amount));
        }

        /// <summary>Samia, PIN 1357, clocks in; any other PIN is wrong.</summary>
        public Task<ClockAnswer?> ClockAsync(ClockRequest request, string sessionToken, CancellationToken cancellationToken = default)
        {
            Clocks.Add(request);
            return Task.FromResult<ClockAnswer?>(request.Pin == "1357"
                ? new ClockAnswer(ClockOutcomes.ClockedIn, DateTimeOffset.UtcNow)
                : new ClockAnswer(SignInOutcomes.WrongPin, AttemptsLeft: 4));
        }
    }
}
