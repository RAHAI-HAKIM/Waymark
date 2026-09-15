using Waymark.Generator.Calendar;
using Waymark.Generator.Catalogues;

namespace Waymark.Generator.Configuration;

/// <summary>
/// Everything that can be wrong with a configuration that JSON alone cannot catch: ranges,
/// key sets, calendar coverage. Every problem is reported, not the first.
/// </summary>
internal static class ConfigValidator
{
    /// <summary>
    /// A Ramadan starts about every 354 days. The run must not reach further than this past
    /// the configured periods, or it would contain a Ramadan the calendar does not know about.
    /// </summary>
    public const int LunarYearDays = 350;

    /// <summary>The largest basket the size table may describe.</summary>
    public const int MaxBasketUnits = 100;

    /// <summary>The payment methods S5 sells with. Store credit and on-account arrive with S7.</summary>
    public static readonly IReadOnlyList<string> PaymentKeys = ["cash", "card", "mobile_wallet"];

    /// <summary>The substitutability tiers a catalogue variant may carry.</summary>
    public static readonly IReadOnlyList<string> TierKeys = ["1", "2", "3"];

    private static readonly string[] Weekdays = ["sunday", "monday", "tuesday", "wednesday", "thursday", "friday", "saturday"];
    private static readonly string[] PaydayPhases = ["normal", "spike", "pre_payday", "trough"];
    private static readonly string[] RamadanPhases = ["stock_up", "ramadan", "aid", "post_aid"];

    public static List<string> Validate(GeneratorConfig config, StoreProfile store, RunWindow window)
    {
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(store);
        ArgumentNullException.ThrowIfNull(window);

        var problems = new List<string>();
        void Check(bool ok, string problem) => ConfigValidator.Check(problems, ok, problem);

        Check(config.SchemaVersion == 1, $"schema_version is {config.SchemaVersion}; this generator reads version 1.");

        var run = config.Run;
        Check(run.HistoryMonths is >= 1 and <= 120, $"run.history_months is {run.HistoryMonths}; expected 1 to 120.");
        Check(run.CommissioningDaysBeforeStart is >= 1 and <= 365, $"run.commissioning_days_before_start is {run.CommissioningDaysBeforeStart}; expected 1 to 365.");

        var customers = config.Customers;
        Check(customers.InitialCount.Value is >= 0 and <= 100_000, $"customers.initial_count is {customers.InitialCount.Value}; expected 0 to 100000.");
        CheckShare(problems, "customers.marketing_consent_share", customers.MarketingConsentShare.Value);
        CheckShare(problems, "customers.objection_share", customers.ObjectionShare.Value);
        CheckShare(problems, "customers.french_language_share", customers.FrenchLanguageShare.Value);

        var cover = config.OpeningStock.CoverDays.Value;
        Check(cover.Min >= 0 && cover.Min <= cover.Max, $"opening_stock.cover_days is {cover.Min}–{cover.Max}; expected 0 <= min <= max.");
        var shelf = config.OpeningStock.RemainingShelfLife.Value;
        Check(shelf.Min > 0 && shelf.Min <= shelf.Max && shelf.Max <= 1, $"opening_stock.remaining_shelf_life is {shelf.Min}–{shelf.Max}; expected 0 < min <= max <= 1.");

        ValidateCalendar(config.Calendar, store, window, problems);
        ValidateSales(config.Sales, problems);
        ValidateSupply(config.Supply, problems);
        ValidateMess(config.Mess, store, problems);

        var link = config.Connectivity;
        Check(link.DrainIntervalMinutes.Value is >= 1 and <= 60, $"connectivity.drain_interval_minutes is {link.DrainIntervalMinutes.Value}; expected 1 to 60.");
        Check(link.BatchSize.Value is >= 1 and <= 100_000, $"connectivity.batch_size is {link.BatchSize.Value}; expected 1 to 100000.");
        CheckShare(problems, "connectivity.flaky_link_up_share", link.FlakyLinkUpShare.Value);
        Check(link.OfflineStartDay.Value >= 0, $"connectivity.offline_start_day is {link.OfflineStartDay.Value}; expected at least 0.");
        Check(link.OfflineDays.Value >= 1, $"connectivity.offline_days is {link.OfflineDays.Value}; expected at least 1.");
        ValidateProfiles(config, problems);

        return problems;
    }

    private static void ValidateSupply(SupplySettings supply, List<string> problems)
    {
        void Check(bool ok, string problem) => ConfigValidator.Check(problems, ok, problem);

        Check(supply.MemoryDays.Value is >= 1 and <= 90, $"supply.memory_days is {supply.MemoryDays.Value}; expected 1 to 90.");
        Check(supply.ReorderCoverDays.Value is >= 0 and <= 90, $"supply.reorder_cover_days is {supply.ReorderCoverDays.Value}; expected 0 to 90.");
        Check(supply.OrderUpToCoverDays.Value > supply.ReorderCoverDays.Value && supply.OrderUpToCoverDays.Value <= 180,
            $"supply.order_up_to_cover_days is {supply.OrderUpToCoverDays.Value}; it must exceed reorder_cover_days and be at most 180.");
        Check(supply.SlowMoverRate.Value >= 0, $"supply.slow_mover_rate is {supply.SlowMoverRate.Value}; expected at least 0.");
        CheckPositive(problems, "supply.ramadan_over_order", supply.RamadanOverOrder.Value);
        Check(supply.RamadanLookaheadDays.Value is >= 0 and <= 60, $"supply.ramadan_lookahead_days is {supply.RamadanLookaheadDays.Value}; expected 0 to 60.");
        Check(supply.LateDeliveryMaxDays.Value is >= 0 and <= 30, $"supply.late_delivery_max_days is {supply.LateDeliveryMaxDays.Value}; expected 0 to 30.");
        CheckShare(problems, "supply.fill_rate", supply.FillRate.Value);
        CheckFraction(problems, "supply.short_delivery_fraction", supply.ShortDeliveryFraction.Value, allowZero: true);
        CheckFraction(problems, "supply.delivered_shelf_life", supply.DeliveredShelfLife.Value, allowZero: false);
        Check(supply.VisitMinutesAfterOpening.Value is >= 0 and <= 600, $"supply.visit_minutes_after_opening is {supply.VisitMinutesAfterOpening.Value}; expected 0 to 600.");
        Check(supply.DeliveryWindowMinutes.Value is >= 1 and <= 600, $"supply.delivery_window_minutes is {supply.DeliveryWindowMinutes.Value}; expected 1 to 600.");
    }

    private static void ValidateMess(MessSettings mess, StoreProfile store, List<string> problems)
    {
        void Check(bool ok, string problem) => ConfigValidator.Check(problems, ok, problem);

        foreach (var (name, share) in new[]
        {
            ("discount_line_share", mess.DiscountLineShare.Value), ("void_share", mess.VoidShare.Value),
            ("return_line_share", mess.ReturnLineShare.Value), ("restock_share", mess.RestockShare.Value),
            ("store_credit_refund_share", mess.StoreCreditRefundShare.Value), ("store_credit_use_share", mess.StoreCreditUseShare.Value),
            ("on_account_share", mess.OnAccountShare.Value), ("paid_out_daily_chance", mess.PaidOutDailyChance.Value),
            ("paid_in_daily_chance", mess.PaidInDailyChance.Value), ("cash_count_discrepancy_chance", mess.CashCountDiscrepancyChance.Value),
            ("count_shrinkage_chance", mess.CountShrinkageChance.Value), ("count_found_chance", mess.CountFoundChance.Value),
        })
        {
            CheckShare(problems, $"mess.{name}", share);
        }

        CheckRange(problems, "mess.discount_percent", mess.DiscountPercent.Value, 1, 100);
        CheckRange(problems, "mess.return_delay_days", mess.ReturnDelayDays.Value, 1, 60);
        CheckRange(problems, "mess.paid_out_amount", mess.PaidOutAmount.Value, 1, 1_000_000);
        CheckRange(problems, "mess.paid_in_amount", mess.PaidInAmount.Value, 1, 1_000_000);
        Check(mess.DropThreshold.Value is >= 1000 and <= 10_000_000, $"mess.drop_threshold is {mess.DropThreshold.Value}; expected 1000 to 10000000.");
        Check(mess.CashCountDiscrepancyMax.Value is >= 5 and <= 100_000, $"mess.cash_count_discrepancy_max is {mess.CashCountDiscrepancyMax.Value}; expected 5 to 100000.");
        Check(mess.CountIntervalDays.Value is >= 1 and <= 90, $"mess.count_interval_days is {mess.CountIntervalDays.Value}; expected 1 to 90.");

        var codes = store.ReasonCodes.ToDictionary(code => code.Code, StringComparer.Ordinal);
        foreach (var (kind, list, appliesTo) in new[]
        {
            ("discount", mess.ReasonCodes.Discount, Domain.Enums.ReasonCodeAppliesTo.Discount),
            ("void", mess.ReasonCodes.Void, Domain.Enums.ReasonCodeAppliesTo.Void),
            ("return", mess.ReasonCodes.Return, Domain.Enums.ReasonCodeAppliesTo.Return),
            ("paid_out", mess.ReasonCodes.PaidOut, Domain.Enums.ReasonCodeAppliesTo.CashMovement),
            ("paid_in", mess.ReasonCodes.PaidIn, Domain.Enums.ReasonCodeAppliesTo.CashMovement),
            ("drop", mess.ReasonCodes.Drop, Domain.Enums.ReasonCodeAppliesTo.CashMovement),
        })
        {
            Check(list.Count > 0, $"mess.reason_codes.{kind} lists no reason code.");
            foreach (var code in list)
            {
                if (!codes.TryGetValue(code, out var definition))
                {
                    problems.Add($"mess.reason_codes.{kind} names '{code}', which store.json's reason_codes does not define.");
                }
                else if (definition.AppliesTo != appliesTo)
                {
                    problems.Add($"mess.reason_codes.{kind} names '{code}', which applies to {definition.AppliesTo}, not {appliesTo}.");
                }
            }
        }
    }

    private static void CheckFraction(List<string> problems, string name, NumberRange range, bool allowZero)
    {
        if (!((allowZero ? range.Min >= 0 : range.Min > 0) && range.Min <= range.Max && range.Max <= 1))
        {
            problems.Add($"{name} is {range.Min}–{range.Max}; expected {(allowZero ? "0 <=" : "0 <")} min <= max <= 1.");
        }
    }

    private static void CheckRange(List<string> problems, string name, NumberRange range, double lowest, double highest)
    {
        if (!(range.Min >= lowest && range.Min <= range.Max && range.Max <= highest) || range.Min != Math.Floor(range.Min) || range.Max != Math.Floor(range.Max))
        {
            problems.Add($"{name} is {range.Min}–{range.Max}; expected whole numbers with {lowest} <= min <= max <= {highest}.");
        }
    }

    private static void ValidateSales(SalesSettings sales, List<string> problems)
    {
        void Check(bool ok, string problem) => ConfigValidator.Check(problems, ok, problem);

        var sizes = sales.BasketUnits.Value;
        Check(sizes.Count is >= 1 and <= MaxBasketUnits, $"sales.basket_units has {sizes.Count} weights; expected 1 to {MaxBasketUnits}, one per basket size from 1 unit.");
        Check(sizes.All(w => w >= 0 && double.IsFinite(w)) && sizes.Sum() > 0, "sales.basket_units needs finite weights of at least 0, and at least one above 0.");

        CheckKeys(problems, "sales.substitution_by_tier", sales.SubstitutionByTier.Value.Keys, TierKeys);
        foreach (var (tier, chance) in sales.SubstitutionByTier.Value)
        {
            CheckShare(problems, $"sales.substitution_by_tier.{tier}", chance);
        }

        CheckKeys(problems, "sales.payment_shares", sales.PaymentShares.Value.Keys, PaymentKeys);
        foreach (var (method, share) in sales.PaymentShares.Value)
        {
            CheckShare(problems, $"sales.payment_shares.{method}", share);
        }

        var total = sales.PaymentShares.Value.Values.Sum();
        Check(Math.Abs(total - 1) <= 0.001, $"sales.payment_shares sum to {total}; shares of every basket sum to 1.");

        CheckShare(problems, "sales.customer_attach_share", sales.CustomerAttachShare.Value);
        Check(sales.OpeningFloat.Value is >= 0 and <= 10_000_000, $"sales.opening_float is {sales.OpeningFloat.Value}; expected 0 to 10000000 whole units.");
    }

    private static void ValidateCalendar(CalendarSettings calendar, StoreProfile store, RunWindow window, List<string> problems)
    {
        void Check(bool ok, string problem) => ConfigValidator.Check(problems, ok, problem);

        CheckKeys(problems, "calendar.weekday_factors", calendar.WeekdayFactors.Value.Keys, Weekdays);
        foreach (var (day, factor) in calendar.WeekdayFactors.Value)
        {
            CheckPositive(problems, $"calendar.weekday_factors.{day}", factor);
        }

        CheckKeys(problems, "calendar.hourly_traffic", calendar.HourlyTraffic.Keys, CalendarModel.RequiredDayTypes);
        CheckKeys(problems, "store.json opening_hours", store.OpeningHours.Keys, CalendarModel.RequiredDayTypes);

        foreach (var (dayType, intervals) in store.OpeningHours)
        {
            foreach (var text in intervals)
            {
                if (!OpeningInterval.TryParse(text, out _))
                {
                    problems.Add($"store.json opening_hours.{dayType}: '{text}' is not HH:mm-HH:mm with the end after the start.");
                }
            }
        }

        foreach (var (dayType, weights) in calendar.HourlyTraffic)
        {
            if (weights.Value.Count != 24)
            {
                problems.Add($"calendar.hourly_traffic.{dayType} has {weights.Value.Count} values; it needs one per hour, 24.");
                continue;
            }

            if (weights.Value.Any(w => w < 0 || !double.IsFinite(w)))
            {
                problems.Add($"calendar.hourly_traffic.{dayType} has a negative or non-finite weight.");
                continue;
            }

            if (store.OpeningHours.TryGetValue(dayType, out var hours)
                && hours.All(text => OpeningInterval.TryParse(text, out _)))
            {
                var intervals = OpeningInterval.ParseAll(hours);
                var open = Enumerable.Range(0, 24).Sum(hour => weights.Value[hour] * OpeningInterval.OpenFraction(intervals, hour));

                // Another day type may legitimately be closed all day — many shops close on
                // Fridays. An ordinary day that nobody can arrive on is a configuration mistake.
                if (open <= 0 && string.Equals(dayType, CalendarModel.Normal, StringComparison.Ordinal))
                {
                    problems.Add(
                        $"calendar.hourly_traffic.{dayType} puts no weight on any hour the store is open "
                        + "(store.json opening_hours): nobody could ever arrive on an ordinary day.");
                }
            }
        }

        var periods = calendar.Ramadan.Value.OrderBy(p => p.Start).ToList();
        if (periods.Count == 0)
        {
            problems.Add("calendar.ramadan lists no period.");
        }

        foreach (var period in periods)
        {
            var length = period.End.DayNumber - period.Start.DayNumber + 1;
            if (length is < 29 or > 30)
            {
                problems.Add($"calendar.ramadan {period.Label}: {period.Start:yyyy-MM-dd} to {period.End:yyyy-MM-dd} is {length} days; Ramadan lasts 29 or 30.");
            }

            if (period.AidStart != period.End.AddDays(1))
            {
                problems.Add($"calendar.ramadan {period.Label}: aid_start {period.AidStart:yyyy-MM-dd} must be the day after Ramadan ends.");
            }

            if (period.AidDays is < 1 or > 3)
            {
                problems.Add($"calendar.ramadan {period.Label}: aid_days is {period.AidDays}; expected 1 to 3.");
            }
        }

        for (var i = 1; i < periods.Count; i++)
        {
            var gap = periods[i].Start.DayNumber - periods[i - 1].Start.DayNumber;
            if (gap is < 350 or > 358)
            {
                problems.Add(
                    $"calendar.ramadan {periods[i - 1].Label} and {periods[i].Label} start {gap} days apart; consecutive "
                    + "Ramadans are 354 or 355 days apart. Is a year missing?");
            }
        }

        if (periods.Count > 0)
        {
            if (window.FirstDay < periods[0].Start.AddDays(-LunarYearDays))
            {
                problems.Add(
                    $"The run starts {window.FirstDay:yyyy-MM-dd}, more than a lunar year before the first configured Ramadan "
                    + $"({periods[0].Label}, {periods[0].Start:yyyy-MM-dd}). Add the Ramadan before it to calendar.ramadan.");
            }

            if (window.LastDay > periods[^1].Start.AddDays(LunarYearDays))
            {
                problems.Add(
                    $"The run ends {window.LastDay:yyyy-MM-dd}, more than a lunar year after the last configured Ramadan "
                    + $"({periods[^1].Label}, {periods[^1].Start:yyyy-MM-dd}). Add the next Ramadan to calendar.ramadan.");
            }
        }

        Check(calendar.StockUpDays.Value is >= 0 and <= 30, $"calendar.stock_up_days is {calendar.StockUpDays.Value}; expected 0 to 30.");
        Check(calendar.PostAidQuietDays.Value is >= 0 and <= 30, $"calendar.post_aid_quiet_days is {calendar.PostAidQuietDays.Value}; expected 0 to 30.");

        foreach (var e in calendar.Events.Value)
        {
            Check(!string.IsNullOrWhiteSpace(e.Name), "calendar.events has an event with no name.");
            Check(e.Days >= 1, $"calendar.events {e.Name} lasts {e.Days} days; expected at least 1.");
            Check(e.DayType is null || CalendarModel.RequiredDayTypes.Contains(e.DayType, StringComparer.Ordinal),
                $"calendar.events {e.Name} has day_type '{e.DayType}'; expected one of {string.Join(", ", CalendarModel.RequiredDayTypes)}.");
        }

        var payday = calendar.Payday;
        Check(payday.SpikeLastDaysOfMonth.Value is >= 0 and <= 10, $"calendar.payday.spike_last_days_of_month is {payday.SpikeLastDaysOfMonth.Value}; expected 0 to 10.");
        Check(payday.SpikeFirstDaysOfMonth.Value is >= 0 and <= 10, $"calendar.payday.spike_first_days_of_month is {payday.SpikeFirstDaysOfMonth.Value}; expected 0 to 10.");
        Check(payday.PrePaydayDays.Value is >= 0 and <= 14, $"calendar.payday.pre_payday_days is {payday.PrePaydayDays.Value}; expected 0 to 14.");
        Check(payday.TroughFirstDay.Value >= 1 && payday.TroughFirstDay.Value <= payday.TroughLastDay.Value && payday.TroughLastDay.Value <= 28,
            $"calendar.payday trough is day {payday.TroughFirstDay.Value} to {payday.TroughLastDay.Value}; expected 1 <= first <= last <= 28.");
        CheckKeys(problems, "calendar.payday.effects", payday.Effects.Value.Keys, PaydayPhases);
        foreach (var (phase, effect) in payday.Effects.Value)
        {
            CheckPositive(problems, $"calendar.payday.effects.{phase}.demand", effect.Demand);
            CheckPositive(problems, $"calendar.payday.effects.{phase}.basket_size", effect.BasketSize);
            CheckPositive(problems, $"calendar.payday.effects.{phase}.on_account", effect.OnAccount);
        }
    }

    private static void ValidateProfiles(GeneratorConfig config, List<string> problems)
    {
        var eventNames = config.Calendar.Events.Value.Select(e => e.Name).ToHashSet(StringComparer.Ordinal);

        if (config.SeasonalityProfiles.Count == 0)
        {
            problems.Add("seasonality_profiles is empty.");
        }

        foreach (var (name, profile) in config.SeasonalityProfiles)
        {
            var value = profile.Value;
            if (value.Months.Count != 12)
            {
                problems.Add($"seasonality_profiles.{name}.months has {value.Months.Count} values; it needs 12.");
            }

            for (var i = 0; i < value.Months.Count; i++)
            {
                CheckPositive(problems, $"seasonality_profiles.{name}.months[{i}]", value.Months[i]);
            }

            CheckKeys(problems, $"seasonality_profiles.{name}.ramadan", value.Ramadan.Keys, RamadanPhases);
            foreach (var (phase, factor) in value.Ramadan)
            {
                CheckPositive(problems, $"seasonality_profiles.{name}.ramadan.{phase}", factor);
            }

            foreach (var (eventName, factor) in value.Events)
            {
                if (!eventNames.Contains(eventName))
                {
                    problems.Add($"seasonality_profiles.{name}.events names '{eventName}', which calendar.events does not define.");
                }

                CheckPositive(problems, $"seasonality_profiles.{name}.events.{eventName}", factor);
            }
        }
    }

    private static void Check(List<string> problems, bool ok, string problem)
    {
        if (!ok)
        {
            problems.Add(problem);
        }
    }

    private static void CheckShare(List<string> problems, string name, double value)
    {
        if (value is < 0 or > 1 || double.IsNaN(value))
        {
            problems.Add($"{name} is {value}; a share is between 0 and 1.");
        }
    }

    private static void CheckPositive(List<string> problems, string name, double value)
    {
        if (!(value > 0) || !double.IsFinite(value))
        {
            problems.Add($"{name} is {value}; a multiplier is a positive number.");
        }
    }

    private static void CheckKeys(List<string> problems, string name, IEnumerable<string> actual, IReadOnlyList<string> expected)
    {
        var keys = actual.ToHashSet(StringComparer.Ordinal);
        var missing = expected.Where(key => !keys.Contains(key)).ToList();
        var extra = keys.Where(key => !expected.Contains(key, StringComparer.Ordinal)).Order(StringComparer.Ordinal).ToList();

        if (missing.Count > 0)
        {
            problems.Add($"{name} is missing {string.Join(", ", missing)}.");
        }

        if (extra.Count > 0)
        {
            problems.Add($"{name} has unexpected {string.Join(", ", extra)}; expected exactly {string.Join(", ", expected)}.");
        }
    }
}
