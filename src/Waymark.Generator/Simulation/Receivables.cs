using Waymark.Domain.Enums;
using Waymark.Domain.Ledgers;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Randomness;

namespace Waymark.Generator.Simulation;

/// <summary>
/// The tab (le carnet, F-16): what each customer owes, and the <c>receivable_movements</c> rows
/// that record it.
///
/// <para>
/// <b>Charges.</b> Every <c>on_account</c> payment row gets one charge of the same amount: a
/// sale adds to the tab, and a refund of an on-account sale takes it off again instead of paying
/// cash out of the drawer. A basket goes on account only for a customer with a credit limit, and
/// only if it keeps the tab within it.
/// </para>
/// <para>
/// <b>Repayments</b> cluster in the payday spike. A customer comes in and pays the whole tab, or
/// half of it, in whole cash steps (D-034): a <c>paid_in</c> on the drawer and a <c>payment</c>
/// row pointing at it, so the drawer still reconciles. Settling the whole tab leaves change under
/// one step, which the shopkeeper lets go as a <c>write_off</c>. A few customers never settle,
/// and the limit is what stops their tab growing.
/// </para>
/// </summary>
internal sealed class Receivables
{
    private readonly SimulationContext _context;
    private readonly Dictionary<string, Money> _owed = new(StringComparer.Ordinal);
    private readonly RandomStream _repayment;

    public Receivables(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _repayment = context.Random.Stream("repayment");
    }

    /// <summary>What <paramref name="customer"/> owes now.</summary>
    public Money Owed(GeneratedCustomer customer)
    {
        ArgumentNullException.ThrowIfNull(customer);
        return _owed.GetValueOrDefault(customer.CustomerId, Money.Zero(_context.Store.Currency));
    }

    /// <summary>Whether <paramref name="amount"/> more on the tab keeps it within the customer's limit. No limit, no tab.</summary>
    public bool CanCharge(GeneratedCustomer customer, Money amount) =>
        customer.CreditLimit is { } limit && Owed(customer) + amount <= limit;

    /// <summary>The charge mirroring one <c>on_account</c> payment row: positive for a sale, negative for its refund.</summary>
    public void Charge(GeneratedCustomer customer, string paymentId, Money amount, GeneratedStaff staff)
    {
        ArgumentNullException.ThrowIfNull(staff);
        Write(customer, ReceivableMovementType.Charge, amount, staff, paymentId: paymentId);
    }

    /// <summary>The customers who come in to settle today, in customer order, each at its second of the day and till.</summary>
    public IReadOnlyList<(GeneratedCustomer Customer, int Second, int Terminal)> RepaymentsDue(
        DayContext day,
        IReadOnlyList<OpeningInterval> intervals,
        WeightedTable hours,
        int terminals)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(hours);

        var settings = _context.Config.Receivables;
        var chance = day.PaydayPhase == PaydayPhase.Spike
            ? settings.PaydayRepaymentDailyChance.Value
            : settings.OtherRepaymentDailyChance.Value;
        var dayNumber = day.Date.DayNumber;
        var due = new List<(GeneratedCustomer, int, int)>();

        foreach (var customer in _context.Store.Customers)
        {
            if (customer.NeverSettles || !Owed(customer).IsPositive
                || !Distributions.Bernoulli(_repayment.Uniform(dayNumber, customer.Number, 0), chance))
            {
                continue;
            }

            var second = BasketAssembler.ArrivalSecond(
                intervals,
                hours.Pick(_repayment.Uniform(dayNumber, customer.Number, 1)),
                _repayment.Uniform(dayNumber, customer.Number, 2));
            var terminal = Distributions.UniformInt(_repayment.Uniform(dayNumber, customer.Number, 3), 0, terminals - 1);
            due.Add((customer, second, terminal));
        }

        return due;
    }

    /// <summary>
    /// A customer settles at the till: all of the tab or half of it, in whole cash steps. A full
    /// settlement writes off the change left under one step.
    /// </summary>
    public void Repay(DateOnly date, GeneratedCustomer customer, CashDrawer drawer, GeneratedStaff staff)
    {
        ArgumentNullException.ThrowIfNull(customer);
        ArgumentNullException.ThrowIfNull(drawer);
        ArgumentNullException.ThrowIfNull(staff);

        var owed = Owed(customer);
        if (!owed.IsPositive)
        {
            return;
        }

        var settings = _context.Config.Receivables;
        var coordinates = (date.DayNumber, customer.Number);
        var partial = Distributions.Bernoulli(_repayment.Uniform(coordinates.DayNumber, coordinates.Number, 4), settings.PartialRepaymentShare.Value);

        // Allocate, not a division: the larger half stays on the tab, and nothing is rounded.
        var target = partial ? owed.Allocate(2)[1] : owed;

        // Notes and coins, never centimes: what is handed over is the target cut down to a whole
        // cash step. Nothing is rounded away: the rest stays owed or is written off below.
        var step = owed.Currency.CashRoundingStep;
        var paid = new Money(target.MinorUnits / step * step, owed.Currency);

        if (paid.IsPositive)
        {
            var reason = _context.Reason(settings.ReasonCodes.Repayment, _repayment.Uniform(coordinates.DayNumber, coordinates.Number, 5));
            var cashMovementId = drawer.Move(CashMovementType.PaidIn, paid, reason, staff)
                ?? throw new InvalidOperationException("The drawer refused a repayment; a paid-in is never refused.");
            Write(customer, ReceivableMovementType.Payment, -paid, staff, cashMovementId: cashMovementId);
        }

        var rest = owed - paid;
        if (!partial && rest.IsPositive)
        {
            var reason = _context.Reason(settings.ReasonCodes.WriteOff, _repayment.Uniform(coordinates.DayNumber, coordinates.Number, 6));
            Write(customer, ReceivableMovementType.WriteOff, -rest, staff, reasonCode: reason.Code);
        }
    }

    private void Write(
        GeneratedCustomer customer,
        ReceivableMovementType type,
        Money amount,
        GeneratedStaff staff,
        string? paymentId = null,
        string? cashMovementId = null,
        string? reasonCode = null)
    {
        _owed[customer.CustomerId] = Owed(customer) + amount;
        _context.Database.Context.ReceivableMovements.Add(new ReceivableMovement
        {
            MovementId = _context.Ids.NewId(),
            StoreId = _context.Store.StoreId,
            CustomerId = customer.CustomerId,
            MovementType = type,
            Amount = amount,
            OccurredAt = _context.Clock.GetUtcNow(),
            PaymentId = paymentId,
            CashMovementId = cashMovementId,
            ReasonCode = reasonCode,
            StaffId = staff.StaffId,
        });
    }
}
