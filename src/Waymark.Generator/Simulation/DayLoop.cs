using System.Globalization;
using Waymark.Domain.Enums;
using Waymark.Domain.Organisation;
using Waymark.Domain.Values;
using Waymark.Generator.Calendar;
using Waymark.Generator.Randomness;
using Waymark.Generator.Reporting;

namespace Waymark.Generator.Simulation;

/// <summary>What the trading days added up to, for the log.</summary>
internal sealed record TradingTotals(int OpenDays, long Transactions, long UnitsWanted, long UnitsSold, long UnitsLost, long Deliveries, long Returns);

/// <summary>
/// The simulated history, one store-local day at a time (W10 S5–S7).
///
/// <para>
/// Each day: latent demand per variant, baskets planned from it, then the day's events in
/// time order, with the clock advanced to each so every id and timestamp carries the moment it
/// happened. Before opening: a cycle count on count days, then expired stock written off.
/// During the day: drawers opening, shifts starting, float top-ups, deliveries, supplier
/// visits, customers, returns, tabs being settled, paid-outs, the midday drop, and drawers
/// closing. Within one
/// second the order is that list's order.
/// </para>
/// <para>
/// The shelf caps what sells: a line asks for what is sellable, an out-of-stock line may be
/// bought as a sibling variant, and whatever is left is lost demand, recorded only in
/// <c>latent-demand.csv</c>. <b>One <c>SaveChanges</c> per day</b>, with foreign keys, CHECKs
/// and append-only triggers all live.
/// </para>
/// </summary>
internal sealed class DayLoop
{
    private enum EventKind
    {
        Count,
        Expiry,
        SessionOpen,
        ShiftStart,
        PaidIn,
        Delivery,
        Visit,
        Basket,
        Return,
        Repayment,
        PaidOut,
        Drop,
        Drain,
        SessionClose,
    }

    private readonly record struct DayEvent(int Second, EventKind Kind, int Index, object? Payload);

    private readonly SimulationContext _context;
    private readonly DemandModel _demand;
    private readonly BasketAssembler _baskets;
    private readonly SaleWriter _sales;
    private readonly SupplyChain _supply;
    private readonly StockCounter _counter;
    private readonly Outbox _outbox;
    private readonly Receivables _receivables;
    private readonly RandomStream _substitution;
    private readonly RandomStream _cash;
    private readonly RandomStream _returnVisit;
    private readonly IReadOnlyList<int>[] _siblings;
    private readonly long[] _zReports;

    public DayLoop(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        var store = context.Store;
        _demand = new DemandModel(context.Config.SeasonalityProfiles, context.Random);
        _baskets = new BasketAssembler(context.Config.Sales, context.Calendar, store, context.Random);
        _outbox = new Outbox(context);
        _receivables = new Receivables(context);
        _sales = new SaleWriter(context, _outbox, _receivables);
        _supply = new SupplyChain(context);
        _counter = new StockCounter(context);
        _substitution = context.Random.Stream("substitution");
        _cash = context.Random.Stream("cash");
        _returnVisit = context.Random.Stream("return_visit");
        _zReports = new long[store.TerminalIds.Count];

        var byProduct = store.Variants.ToLookup(variant => variant.ProductId, StringComparer.Ordinal);
        _siblings = [.. store.Variants.Select(variant => (IReadOnlyList<int>)[.. byProduct[variant.ProductId]
            .Where(sibling => sibling.Index != variant.Index)
            .Select(sibling => sibling.Index)])];
    }

    /// <summary>What the till knows at the end: store credit and last order date per customer.</summary>
    public SaleWriter Sales => _sales;

    /// <summary>What each customer owes on account at the end.</summary>
    public Receivables Receivables => _receivables;

    /// <summary>The outbox: what was emitted, acknowledged and left queued.</summary>
    public Outbox Outbox => _outbox;

    public TradingTotals Run(RunWindow window, LatentDemandCsv csv)
    {
        ArgumentNullException.ThrowIfNull(window);
        ArgumentNullException.ThrowIfNull(csv);

        var totals = new TradingTotals(0, 0, 0, 0, 0, 0, 0);
        for (var date = window.FirstDay; date <= window.LastDay; date = date.AddDays(1))
        {
            totals = RunDay(date, date.DayNumber - window.FirstDay.DayNumber, csv, totals);
            _context.Database.Save();
        }

        _supply.WritePending();
        _outbox.WriteFinal();
        _context.Database.Save();
        return totals;
    }

    private TradingTotals RunDay(DateOnly date, int dayIndex, LatentDemandCsv csv, TradingTotals totals)
    {
        var day = _context.Calendar.Day(date);
        var variants = _context.Store.Variants;
        var latent = variants.Select(variant => _demand.Latent(variant, day)).ToArray();
        var openLevels = variants.Select(variant => _context.State.OnHand(variant.Index)).ToArray();
        var tally = new Tally[variants.Count];
        var counts = (Transactions: 0L, Deliveries: 0L, Returns: 0L);

        if (day.IsOpen)
        {
            counts = Trade(day, dayIndex, latent, tally);
        }

        _outbox.EndDay(date);
        _context.State.RecordDaySales([.. tally.Select(t => t.Sold + t.SoldAsSubstitute)]);

        foreach (var variant in variants)
        {
            var i = variant.Index;
            csv.Write(new LatentDemandRow(
                date,
                variant.Catalogue.Sku,
                day.IsOpen,
                openLevels[i] / Quantity.Scale,
                latent[i],
                tally[i].Sold,
                tally[i].Substituted,
                latent[i] - tally[i].Sold - tally[i].Substituted,
                tally[i].SoldAsSubstitute,
                _context.State.OnHand(i) / Quantity.Scale));
        }

        return totals with
        {
            OpenDays = totals.OpenDays + (day.IsOpen ? 1 : 0),
            Transactions = totals.Transactions + counts.Transactions,
            UnitsWanted = totals.UnitsWanted + latent.Sum(),
            UnitsSold = totals.UnitsSold + tally.Sum(t => (long)t.Sold + t.SoldAsSubstitute),
            UnitsLost = totals.UnitsLost + latent.Sum() - tally.Sum(t => (long)t.Sold + t.Substituted),
            Deliveries = totals.Deliveries + counts.Deliveries,
            Returns = totals.Returns + counts.Returns,
        };
    }

    /// <summary>One open day. Returns the sales, deliveries and returns it held.</summary>
    private (long Transactions, long Deliveries, long Returns) Trade(DayContext day, int dayIndex, int[] latent, Tally[] tally)
    {
        var date = day.Date;
        var dayNumber = date.DayNumber;
        var intervals = _context.Calendar.OpeningIntervals(day.DayType);
        var terminals = _context.Store.TerminalIds.Count;
        var opening = intervals[0].StartMinute * 60;
        var hours = new WeightedTable(day.HourlyTraffic);
        var events = new List<DayEvent>();

        if (_counter.IsCountDay(dayIndex))
        {
            events.Add(new DayEvent(Math.Max(0, opening - (StockCounter.MinutesBeforeOpening * 60)), EventKind.Count, 0, null));
        }

        events.Add(new DayEvent(opening, EventKind.Expiry, 0, null));

        for (var t = 0; t < terminals; t++)
        {
            events.Add(new DayEvent(opening, EventKind.SessionOpen, t, null));
            events.Add(new DayEvent(opening, EventKind.PaidIn, t, null));
            events.Add(new DayEvent(intervals[^1].EndMinute * 60, EventKind.SessionClose, t, null));
            for (var i = 0; i < intervals.Count; i++)
            {
                events.Add(new DayEvent(intervals[i].StartMinute * 60, EventKind.ShiftStart, (t * intervals.Count) + i, null));
            }

            events.Add(new DayEvent(BasketAssembler.ArrivalSecond(intervals, hours.Pick(_cash.Uniform(dayNumber, t, 2)), _cash.Uniform(dayNumber, t, 3)), EventKind.PaidOut, t, null));
            if (intervals.Count > 1)
            {
                events.Add(new DayEvent(intervals[0].EndMinute * 60, EventKind.Drop, t, null));
            }
        }

        var drainEvery = _context.Config.Connectivity.DrainIntervalMinutes.Value * 60;
        var closing = intervals[^1].EndMinute * 60;
        for (var second = opening + drainEvery; second < closing; second += drainEvery)
        {
            events.Add(new DayEvent(second, EventKind.Drain, 0, null));
        }

        events.Add(new DayEvent(closing, EventKind.Drain, 0, null));

        events.AddRange(_supply.Deliveries(day, intervals).Select(d => new DayEvent(d.Second, EventKind.Delivery, d.Order.Number, d.Order)));
        events.AddRange(_supply.Visits(day, intervals).Select(v => new DayEvent(v.Second, EventKind.Visit, v.Supplier.Index, v.Supplier)));
        events.AddRange(_baskets.Plan(day, latent).Select(basket => new DayEvent(basket.SecondOfDay, EventKind.Basket, basket.Number, basket)));
        events.AddRange(_sales.ReturnsDue(date).Select((r, index) => new DayEvent(
            BasketAssembler.ArrivalSecond(intervals, hours.Pick(_returnVisit.Uniform(r.DayNumber, r.Basket, r.Item, 0)), _returnVisit.Uniform(r.DayNumber, r.Basket, r.Item, 1)),
            EventKind.Return,
            index,
            r)));
        events.AddRange(_receivables.RepaymentsDue(day, intervals, hours, terminals).Select(r => new DayEvent(
            r.Second,
            EventKind.Repayment,
            r.Terminal,
            r.Customer)));

        events.Sort((a, b) => a.Second != b.Second ? a.Second.CompareTo(b.Second)
            : a.Kind != b.Kind ? a.Kind.CompareTo(b.Kind)
            : a.Index.CompareTo(b.Index));

        var drawers = new CashDrawer[terminals];
        var result = (Transactions: 0L, Deliveries: 0L, Returns: 0L);

        foreach (var (second, kind, index, payload) in events)
        {
            _context.Clock.AdvanceTo(_context.At(date, second));
            var interval = IntervalAtOrBefore(intervals, second);

            switch (kind)
            {
                case EventKind.Count:
                    _counter.Count(date);
                    break;

                case EventKind.Expiry:
                    _supply.WriteOffExpired(date, _context.OnDuty(date, 0, 0));
                    break;

                case EventKind.SessionOpen:
                    drawers[index] = new CashDrawer(_context, index, _context.OnDuty(date, 0, index));
                    break;

                case EventKind.ShiftStart:
                    StartShift(date, intervals, index / intervals.Count, index % intervals.Count);
                    break;

                case EventKind.PaidIn:
                    CashEvent(drawers[index], CashMovementType.PaidIn, dayNumber, _context.OnDuty(date, 0, index));
                    break;

                case EventKind.Delivery:
                    _supply.Receive((PendingOrder)payload!, date, _context.OnDuty(date, interval, 0));
                    result.Deliveries++;
                    break;

                case EventKind.Visit:
                    _supply.Visit(day, (GeneratedSupplier)payload!, _context.OnDuty(date, interval, 0));
                    break;

                case EventKind.Basket:
                    var basket = (PlannedBasket)payload!;
                    if (Serve(day, basket, drawers[basket.TerminalIndex], _context.OnDuty(date, interval, basket.TerminalIndex), tally))
                    {
                        result.Transactions++;
                    }

                    break;

                case EventKind.Return:
                    var returned = (ScheduledReturn)payload!;
                    var till = Distributions.UniformInt(_returnVisit.Uniform(returned.DayNumber, returned.Basket, returned.Item, 2), 0, terminals - 1);
                    if (_sales.WriteRefund(date, returned, drawers[till], _context.OnDuty(date, interval, till)))
                    {
                        result.Returns++;
                    }

                    break;

                case EventKind.Repayment:
                    _receivables.Repay(date, (GeneratedCustomer)payload!, drawers[index], _context.OnDuty(date, interval, index));
                    break;

                case EventKind.PaidOut:
                    CashEvent(drawers[index], CashMovementType.PaidOut, dayNumber, _context.OnDuty(date, interval, index));
                    break;

                case EventKind.Drop:
                    Drop(drawers[index], dayNumber, _context.OnDuty(date, 0, index));
                    break;

                case EventKind.Drain:
                    _outbox.Drain(date, dayIndex, second);
                    break;

                case EventKind.SessionClose:
                    Close(drawers[index], dayNumber, _context.OnDuty(date, intervals.Count - 1, index));
                    break;
            }
        }

        return result;
    }

    /// <summary>
    /// Serves one basket against the shelf. Returns false if nothing could be sold, in which case
    /// the customer leaves and no transaction exists.
    /// </summary>
    private bool Serve(DayContext day, PlannedBasket basket, CashDrawer drawer, GeneratedStaff staff, Tally[] tally)
    {
        var date = day.Date;
        var served = new List<ServedLine>();

        foreach (var line in basket.Lines)
        {
            var variant = _context.Store.Variants[line.VariantIndex];
            var sold = (int)Math.Min(line.Units, _context.State.Sellable(line.VariantIndex, date) / Quantity.Scale);

            if (sold > 0)
            {
                Take(variant, date, sold, served);
                tally[line.VariantIndex].Sold += sold;
            }

            var unmet = line.Units - sold;
            if (unmet > 0)
            {
                tally[line.VariantIndex].Substituted += Substitute(day, basket, variant, unmet, served, tally);
            }
        }

        if (served.Count == 0)
        {
            return false;
        }

        if (_sales.IsMisrung(date, basket))
        {
            _sales.WriteVoid(date, basket, drawer, staff, served);
        }

        _sales.WriteSale(day, basket, drawer, staff, served);
        return true;
    }

    /// <summary>
    /// An out-of-stock line may be bought as another variant of the same product, with the
    /// chance its substitutability tier gives. Returns the units substituted.
    /// </summary>
    private int Substitute(DayContext day, PlannedBasket basket, GeneratedVariant variant, int unmet, List<ServedLine> served, Tally[] tally)
    {
        var dayNumber = day.Date.DayNumber;
        var tier = variant.Catalogue.SubstitutabilityTier.ToString(CultureInfo.InvariantCulture);

        if (!Distributions.Bernoulli(_substitution.Uniform(dayNumber, basket.Number, variant.Index, 0), _context.Config.Sales.SubstitutionByTier.Value[tier]))
        {
            return 0;
        }

        var candidates = _siblings[variant.Index].Where(index => _context.State.Sellable(index, day.Date) > 0).ToList();
        if (candidates.Count == 0)
        {
            return 0;
        }

        var chosen = candidates[Distributions.UniformInt(_substitution.Uniform(dayNumber, basket.Number, variant.Index, 1), 0, candidates.Count - 1)];
        var units = (int)Math.Min(unmet, _context.State.Sellable(chosen, day.Date) / Quantity.Scale);
        if (units == 0)
        {
            return 0;
        }

        Take(_context.Store.Variants[chosen], day.Date, units, served);
        tally[chosen].SoldAsSubstitute += units;
        return units;
    }

    private void Take(GeneratedVariant variant, DateOnly date, int units, List<ServedLine> served)
    {
        foreach (var (lot, quantity) in _context.State.Take(variant.Index, date, variant.SellingUnit.Whole(units)))
        {
            served.Add(new ServedLine(variant, lot, quantity));
        }
    }

    /// <summary>A float top-up at opening, or a paid-out during the day, each with its daily chance.</summary>
    private void CashEvent(CashDrawer drawer, CashMovementType type, int dayNumber, GeneratedStaff staff)
    {
        var mess = _context.Config.Mess;
        var (chance, range, codes, k) = type == CashMovementType.PaidIn
            ? (mess.PaidInDailyChance.Value, mess.PaidInAmount.Value, mess.ReasonCodes.PaidIn, 4)
            : (mess.PaidOutDailyChance.Value, mess.PaidOutAmount.Value, mess.ReasonCodes.PaidOut, 0);

        if (!Distributions.Bernoulli(_cash.Uniform(dayNumber, drawer.Terminal, k), chance))
        {
            return;
        }

        var units = Distributions.UniformInt(_cash.Uniform(dayNumber, drawer.Terminal, k + 1), (int)range.Min, (int)range.Max);
        var reason = _context.Reason(codes, _cash.Uniform(dayNumber, drawer.Terminal, 10 + k));
        drawer.Move(type, Whole(units), reason, staff);
    }

    /// <summary>At the midday break, cash beyond the threshold above the float goes to the safe, in whole thousands.</summary>
    private void Drop(CashDrawer drawer, int dayNumber, GeneratedStaff staff)
    {
        var mess = _context.Config.Mess;
        var aboveFloat = drawer.Expected - Whole(_context.Config.Sales.OpeningFloat.Value);
        if (aboveFloat <= Whole(mess.DropThreshold.Value))
        {
            return;
        }

        var thousands = aboveFloat.MinorUnits / (1000L * Currency.StorageScale);
        var reason = _context.Reason(mess.ReasonCodes.Drop, _cash.Uniform(dayNumber, drawer.Terminal, 12));
        drawer.Move(CashMovementType.Drop, Whole(thousands * 1000), reason, staff);
    }

    /// <summary>Closes a drawer, with a miscount of up to the configured amount, in 5-unit steps, on some days.</summary>
    private void Close(CashDrawer drawer, int dayNumber, GeneratedStaff staff)
    {
        var mess = _context.Config.Mess;
        var miscount = Money.Zero(_context.Store.Currency);

        if (Distributions.Bernoulli(_cash.Uniform(dayNumber, drawer.Terminal, 6), mess.CashCountDiscrepancyChance.Value))
        {
            var steps = Distributions.UniformInt(_cash.Uniform(dayNumber, drawer.Terminal, 7), 1, mess.CashCountDiscrepancyMax.Value / 5);
            var sign = Distributions.Bernoulli(_cash.Uniform(dayNumber, drawer.Terminal, 8), 0.5) ? -1 : 1;
            miscount = Whole(sign * steps * 5L);
        }

        drawer.Close(staff, miscount, ++_zReports[drawer.Terminal]);
    }

    /// <summary>A staff work period for one opening interval on one till, written as finished.</summary>
    private void StartShift(DateOnly date, IReadOnlyList<OpeningInterval> intervals, int terminal, int interval)
    {
        var start = _context.Clock.GetUtcNow();
        var end = _context.At(date, intervals[interval].EndMinute * 60);

        _context.Database.Context.Shifts.Add(new Shift
        {
            ShiftId = _context.Ids.NewId(),
            StaffId = _context.OnDuty(date, interval, terminal).StaffId,
            TerminalId = _context.Store.TerminalIds[terminal],
            StoreId = _context.Store.StoreId,
            StartTime = Timestamp(start),
            EndTime = Timestamp(end),
            Status = ShiftStatus.Closed,
            CreatedAt = start,
            UpdatedAt = end,
        });
    }

    private Money Whole(long units) => new(checked(units * Currency.StorageScale), _context.Store.Currency);

    /// <summary>The opening interval a second falls in, or the last one that started before it (a drop at the break, a close).</summary>
    private static int IntervalAtOrBefore(IReadOnlyList<OpeningInterval> intervals, int second)
    {
        var found = 0;
        for (var i = 0; i < intervals.Count; i++)
        {
            if (second >= intervals[i].StartMinute * 60)
            {
                found = i;
            }
        }

        return found;
    }

    private static string Timestamp(DateTimeOffset instant) =>
        instant.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);

    /// <summary>What happened to one variant's demand on one day.</summary>
    private struct Tally
    {
        public int Sold;
        public int Substituted;
        public int SoldAsSubstitute;
    }
}
