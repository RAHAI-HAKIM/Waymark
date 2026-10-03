using Microsoft.EntityFrameworkCore;
using Waymark.Application.Sales;
using Waymark.Domain.Enums;
using Waymark.Domain.Reference;
using Waymark.Persistence.Organisation;
using Waymark.Persistence.Reference;
using Waymark.Persistence.Sales;

namespace Waymark.Integration.Tests;

/// <summary>
/// B10 (D-102): cash in and out with no sale, and the clock, against a real database. The silent
/// failures: cash out of the drawer with nobody's authorisation where the shop asks for one; a reason for
/// cash in used to take cash out; a shift opened twice for one person, or never closed.
/// </summary>
public sealed partial class CompleteSaleTests
{
    private const string BankDeposit = "caisse-depot";

    private const string Float = "caisse-apport";

    private const string Sundry = "caisse-divers";

    /// <summary>A reason for cash out, one for cash in, and one either way that asks for a note.</summary>
    private void CashReasons()
    {
        using var context = database.NewContext();
        foreach (var (code, direction, note) in new[] { (BankDeposit, (CashDirection?)CashDirection.Out, false), (Float, CashDirection.In, false), (Sundry, null, true) })
        {
            if (!context.ReasonCodes.Any(reason => reason.ReasonCodeValue == code))
            {
                context.ReasonCodes.Add(new ReasonCode
                {
                    ReasonCodeValue = code, AppliesTo = ReasonCodeAppliesTo.CashMovement, LabelAr = code, LabelFr = code,
                    Direction = direction, RequiresNote = note, CreatedAt = Now,
                });
            }
        }

        context.SaveChanges();
    }

    private Task<RecordedCashMovement> Cash(
        Shop shop, CashDirection direction, long amount, string reason, string? note = null, bool sellerMay = true, string? authorisedBy = null) =>
        Run(shop, (context, work) => new RecordCashMovementHandler(new SalesLedger(context), work, new ReasonCodes(context), new FixedClock()),
            new RecordCashMovement(shop.TerminalId, shop.StaffId, direction, amount, reason, note, sellerMay, authorisedBy));

    private Task<Clocked> Clock(Shop shop) =>
        Run(shop, (context, work) => new ToggleClockHandler(new SalesLedger(context), new ShiftLedger(context), work, new FixedClock()),
            new ToggleClock(shop.StaffId, shop.TerminalId));

    [Fact]
    public async Task A_paid_in_and_a_paid_out_are_rows_on_the_tills_session_never_negative()
    {
        CashReasons();
        var shop = new Shop(database);

        await Cash(shop, CashDirection.In, 200_000, Float);
        await Cash(shop, CashDirection.Out, 50_000, BankDeposit);

        using var read = Read(shop);
        var session = await read.CashSessions.SingleAsync(s => s.TerminalId == shop.TerminalId);
        var movements = await read.CashMovements.Where(m => m.SessionId == session.SessionId).OrderBy(m => m.MovementType).ToListAsync();
        Assert.Equal([(CashMovementType.PaidIn, Dzd(200_000)), (CashMovementType.PaidOut, Dzd(50_000))], movements.Select(m => (m.MovementType, m.Amount)));
        Assert.All(movements, m => Assert.Equal(shop.StaffId, m.StaffId));
    }

    [Fact]
    public async Task Cash_out_below_the_shops_rank_needs_an_authorisation_and_names_it()
    {
        CashReasons();
        var shop = new Shop(database);

        await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.Out, 50_000, BankDeposit, sellerMay: false));
        await Cash(shop, CashDirection.Out, 50_000, BankDeposit, sellerMay: false, authorisedBy: shop.StaffId);

        using var read = Read(shop);
        Assert.Equal(shop.StaffId, (await read.CashMovements.SingleAsync()).AuthorisedBy);
    }

    [Fact]
    public async Task A_reason_for_the_other_way_a_missing_note_or_nothing_is_refused_and_nothing_is_written()
    {
        CashReasons();
        var shop = new Shop(database);

        await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.Out, 50_000, Float));       // a reason for cash in
        await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.In, 50_000, BankDeposit));   // a reason for cash out
        await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.Out, 50_000, Sundry));       // no note
        await Assert.ThrowsAsync<CashMovementRefusedException>(() => Cash(shop, CashDirection.In, 0, Float));
        await Cash(shop, CashDirection.Out, 1_500, Sundry, note: "Sacs plastique");                                        // either way, with its note

        using var read = Read(shop);
        Assert.Equal("Sacs plastique", (await read.CashMovements.SingleAsync()).Note);
    }

    [Fact]
    public async Task The_clock_opens_a_shift_then_closes_it_and_never_opens_two()
    {
        var shop = new Shop(database);

        var arrived = await Clock(shop);
        var left = await Clock(shop);
        var back = await Clock(shop);

        Assert.Equal((true, false, true), (arrived.In, left.In, back.In));
        Assert.Equal(Now, left.Since);
        using var read = Read(shop);
        var shifts = await read.Shifts.Where(s => s.StaffId == shop.StaffId).ToListAsync();
        Assert.Equal(2, shifts.Count);
        Assert.Single(shifts, s => s.Status == ShiftStatus.Open);
        Assert.Single(shifts, s => s.Status == ShiftStatus.Closed && s.EndTime is not null);
    }
}
