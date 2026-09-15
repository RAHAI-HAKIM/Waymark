using Waymark.Domain.Enums;
using Waymark.Generator.Calendar;
using Waymark.Generator.Configuration;
using Waymark.Generator.Randomness;

namespace Waymark.Generator.Simulation;

/// <summary>A customer's visit as planned from demand, before the shelf has a say.</summary>
/// <param name="Number">The basket's index within its day: its coordinate in every basket stream.</param>
/// <param name="SecondOfDay">Store-local arrival time, in seconds from midnight.</param>
/// <param name="TerminalIndex">Which till rings it up.</param>
/// <param name="Customer">The enrolled customer it is rung up against, if any.</param>
/// <param name="Method">How it is paid.</param>
/// <param name="Lines">What the customer wants, one line per variant, in the order picked up.</param>
internal sealed record PlannedBasket(
    int Number,
    int SecondOfDay,
    int TerminalIndex,
    GeneratedCustomer? Customer,
    PaymentMethod Method,
    IReadOnlyList<WantedLine> Lines);

/// <summary>Whole units of one variant wanted in one basket.</summary>
internal readonly record struct WantedLine(int VariantIndex, int Units);

/// <summary>
/// Turns a day's latent demand into baskets (D-046 §19): demand first, baskets second.
///
/// <para>
/// Every unit wanted that day goes into one pool, which is shuffled by hashing each unit's
/// address — <c>(day, variant, unit)</c> — and cut into baskets of drawn sizes. Footfall is
/// therefore the day's demand divided by basket size, not a separate number that could
/// disagree with it. The pay cycle scales basket size, so a payday brings bigger baskets as
/// well as more demand.
/// </para>
/// <para>
/// Each basket's arrival time is drawn from the day type's hourly traffic, then uniformly
/// among the open seconds of that hour, so nobody arrives while the shop is shut for lunch,
/// prayer or iftar. Baskets are returned in arrival order, the order the till serves them —
/// which is also the order the simulated clock must move in.
/// </para>
/// </summary>
internal sealed class BasketAssembler
{
    private static readonly PaymentMethod[] Methods = [PaymentMethod.Cash, PaymentMethod.Card, PaymentMethod.MobileWallet];

    private readonly CalendarModel _calendar;
    private readonly GeneratedStore _store;
    private readonly double _attachShare;
    private readonly WeightedTable _sizes;
    private readonly WeightedTable _payments;
    private readonly WeightedTable? _customers;
    private readonly RandomStream _mix;
    private readonly RandomStream _size;
    private readonly RandomStream _arrival;
    private readonly RandomStream _attach;
    private readonly RandomStream _payment;
    private readonly RandomStream _terminal;

    public BasketAssembler(SalesSettings settings, CalendarModel calendar, GeneratedStore store, RandomSource random)
    {
        ArgumentNullException.ThrowIfNull(settings);
        ArgumentNullException.ThrowIfNull(calendar);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(random);

        _calendar = calendar;
        _store = store;
        _attachShare = settings.CustomerAttachShare.Value;
        _sizes = new WeightedTable(settings.BasketUnits.Value);
        _payments = new WeightedTable([.. ConfigValidator.PaymentKeys.Select(key => settings.PaymentShares.Value[key])]);

        // Regulars differ in how often they come: each gets an exponentially distributed visit
        // weight, drawn once, so a few customers account for many of the attached baskets.
        var frequency = random.Stream("customer_frequency");
        _customers = store.Customers.Count == 0
            ? null
            : new WeightedTable([.. store.Customers.Select(customer => -Math.Log(frequency.Uniform(customer.Number)))]);

        _mix = random.Stream("basket_mix");
        _size = random.Stream("basket_size");
        _arrival = random.Stream("arrival");
        _attach = random.Stream("customer_attach");
        _payment = random.Stream("payment");
        _terminal = random.Stream("terminal");
    }

    /// <summary>The day's baskets in arrival order. <paramref name="latent"/> is units wanted per variant index.</summary>
    public IReadOnlyList<PlannedBasket> Plan(DayContext day, IReadOnlyList<int> latent)
    {
        ArgumentNullException.ThrowIfNull(day);
        ArgumentNullException.ThrowIfNull(latent);

        var dayNumber = day.Date.DayNumber;
        var pool = new List<(ulong Key, int Variant, int Unit)>();
        for (var variant = 0; variant < latent.Count; variant++)
        {
            for (var unit = 0; unit < latent[variant]; unit++)
            {
                pool.Add((_mix.Bits(dayNumber, variant, unit), variant, unit));
            }
        }

        if (pool.Count == 0)
        {
            return [];
        }

        pool.Sort((a, b) => a.Key != b.Key ? a.Key.CompareTo(b.Key)
            : a.Variant != b.Variant ? a.Variant.CompareTo(b.Variant)
            : a.Unit.CompareTo(b.Unit));

        var hours = new WeightedTable(day.HourlyTraffic);
        var intervals = _calendar.OpeningIntervals(day.DayType);
        var baskets = new List<PlannedBasket>();
        var position = 0;

        for (var number = 0; position < pool.Count; number++)
        {
            var size = Math.Min(BasketSize(day, number), pool.Count - position);
            var lines = Group(pool, position, size);
            position += size;

            var hour = hours.Pick(_arrival.Uniform(dayNumber, number, 0));
            var second = ArrivalSecond(intervals, hour, _arrival.Uniform(dayNumber, number, 1));

            GeneratedCustomer? customer = null;
            if (_customers is not null && Distributions.Bernoulli(_attach.Uniform(dayNumber, number, 0), _attachShare))
            {
                customer = _store.Customers[_customers.Pick(_attach.Uniform(dayNumber, number, 1))];
            }

            baskets.Add(new PlannedBasket(
                number,
                second,
                Distributions.UniformInt(_terminal.Uniform(dayNumber, number), 0, _store.TerminalIds.Count - 1),
                customer,
                Methods[_payments.Pick(_payment.Uniform(dayNumber, number))],
                lines));
        }

        return [.. baskets.OrderBy(b => b.SecondOfDay).ThenBy(b => b.Number)];
    }

    /// <summary>
    /// A basket's size in units: drawn from the size table, then scaled by the pay cycle and
    /// rounded stochastically, so the scaled mean is exact rather than rounded away.
    /// </summary>
    internal int BasketSize(DayContext day, int number)
    {
        var drawn = _sizes.Pick(_size.Uniform(day.Date.DayNumber, number, 0)) + 1;
        var scaled = drawn * day.Payday.BasketSize;
        var whole = (int)Math.Floor(scaled);
        if (Distributions.Bernoulli(_size.Uniform(day.Date.DayNumber, number, 1), scaled - whole))
        {
            whole++;
        }

        return Math.Max(1, whole);
    }

    /// <summary>
    /// The <paramref name="u"/>-th open second of <paramref name="hour"/>, uniformly. The hour
    /// was chosen by traffic weights that are zero wherever the store is closed, so it always
    /// has an open second.
    /// </summary>
    internal static int ArrivalSecond(IReadOnlyList<OpeningInterval> intervals, int hour, double u)
    {
        var from = hour * 3600;
        var to = from + 3600;
        var segments = intervals
            .Select(i => (Start: Math.Max(from, i.StartMinute * 60), End: Math.Min(to, i.EndMinute * 60)))
            .Where(s => s.End > s.Start)
            .ToList();

        var open = segments.Sum(s => s.End - s.Start);
        if (open == 0)
        {
            throw new InvalidOperationException($"Hour {hour} was drawn for an arrival but the store is closed throughout it.");
        }

        var offset = Distributions.UniformInt(u, 0, open - 1);
        foreach (var (start, end) in segments)
        {
            if (offset < end - start)
            {
                return start + offset;
            }

            offset -= end - start;
        }

        throw new InvalidOperationException("Unreachable: the offset is below the open seconds.");
    }

    private static List<WantedLine> Group(List<(ulong Key, int Variant, int Unit)> pool, int start, int count)
    {
        var lines = new List<WantedLine>();
        for (var i = start; i < start + count; i++)
        {
            var variant = pool[i].Variant;
            var existing = lines.FindIndex(line => line.VariantIndex == variant);
            if (existing >= 0)
            {
                lines[existing] = lines[existing] with { Units = lines[existing].Units + 1 };
            }
            else
            {
                lines.Add(new WantedLine(variant, 1));
            }
        }

        return lines;
    }
}
