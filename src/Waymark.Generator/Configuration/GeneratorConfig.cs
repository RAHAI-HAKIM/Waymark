namespace Waymark.Generator.Configuration;

// The shape of configs/*.json. Property names map to snake_case; unknown fields are refused,
// so a misspelt parameter fails loudly instead of silently taking a default.

internal sealed record GeneratorConfig
{
    public required int SchemaVersion { get; init; }

    /// <summary>The catalogue directory, relative to the config file.</summary>
    public required string Catalogue { get; init; }

    public required RunSettings Run { get; init; }

    public required CustomerSettings Customers { get; init; }

    public required OpeningStockSettings OpeningStock { get; init; }

    public required CalendarSettings Calendar { get; init; }

    public required SalesSettings Sales { get; init; }

    public required SupplySettings Supply { get; init; }

    public required MessSettings Mess { get; init; }

    public required ConnectivitySettings Connectivity { get; init; }

    public required IReadOnlyDictionary<string, Sourced<SeasonalityProfile>> SeasonalityProfiles { get; init; }
}

internal sealed record RunSettings
{
    public required long MasterSeed { get; init; }

    /// <summary>The first simulated trading day, store local date.</summary>
    public required DateOnly StartDate { get; init; }

    public required int HistoryMonths { get; init; }

    /// <summary>Reference data and the first customers are created this many days before <see cref="StartDate"/>.</summary>
    public required int CommissioningDaysBeforeStart { get; init; }
}

internal sealed record CustomerSettings
{
    public required Sourced<int> InitialCount { get; init; }

    public required Sourced<double> MarketingConsentShare { get; init; }

    public required Sourced<double> ObjectionShare { get; init; }

    public required Sourced<double> FrenchLanguageShare { get; init; }
}

internal sealed record OpeningStockSettings
{
    public required Sourced<NumberRange> CoverDays { get; init; }

    public required Sourced<NumberRange> RemainingShelfLife { get; init; }
}

internal sealed record NumberRange
{
    public required double Min { get; init; }

    public required double Max { get; init; }
}

internal sealed record CalendarSettings
{
    /// <summary>Keys: sunday … saturday.</summary>
    public required Sourced<IReadOnlyDictionary<string, double>> WeekdayFactors { get; init; }

    /// <summary>Day type to 24 hourly weights. Day types match the store's opening hours.</summary>
    public required IReadOnlyDictionary<string, Sourced<IReadOnlyList<double>>> HourlyTraffic { get; init; }

    public required Sourced<IReadOnlyList<RamadanPeriod>> Ramadan { get; init; }

    public required Sourced<int> StockUpDays { get; init; }

    public required Sourced<int> PostAidQuietDays { get; init; }

    public required Sourced<IReadOnlyList<CalendarEvent>> Events { get; init; }

    public required PaydaySettings Payday { get; init; }
}

internal sealed record RamadanPeriod
{
    public required string Label { get; init; }

    public required DateOnly Start { get; init; }

    public required DateOnly End { get; init; }

    public required DateOnly AidStart { get; init; }

    public required int AidDays { get; init; }
}

internal sealed record CalendarEvent
{
    public required string Name { get; init; }

    public required DateOnly Start { get; init; }

    public required int Days { get; init; }

    /// <summary>Overrides the day type (and so the opening hours and traffic) while the event lasts.</summary>
    public string? DayType { get; init; }
}

internal sealed record PaydaySettings
{
    public required Sourced<int> SpikeLastDaysOfMonth { get; init; }

    public required Sourced<int> SpikeFirstDaysOfMonth { get; init; }

    public required Sourced<int> PrePaydayDays { get; init; }

    public required Sourced<int> TroughFirstDay { get; init; }

    public required Sourced<int> TroughLastDay { get; init; }

    /// <summary>Keys: normal, spike, pre_payday, trough.</summary>
    public required Sourced<IReadOnlyDictionary<string, PaydayEffect>> Effects { get; init; }
}

internal sealed record PaydayEffect
{
    public required double Demand { get; init; }

    public required double BasketSize { get; init; }

    public required double OnAccount { get; init; }
}

/// <summary>How the day's demand turns into sales at the till (W10 S5).</summary>
internal sealed record SalesSettings
{
    /// <summary>Relative weights of a basket of 1, 2, 3 … units, before the pay-cycle multiplier.</summary>
    public required Sourced<IReadOnlyList<double>> BasketUnits { get; init; }

    /// <summary>Keys "1", "2", "3": the chance an out-of-stock line is bought as another variant of the same product.</summary>
    public required Sourced<IReadOnlyDictionary<string, double>> SubstitutionByTier { get; init; }

    /// <summary>Keys cash, card, mobile_wallet, summing to 1: how a basket is paid.</summary>
    public required Sourced<IReadOnlyDictionary<string, double>> PaymentShares { get; init; }

    /// <summary>The share of baskets rung up against an enrolled customer.</summary>
    public required Sourced<double> CustomerAttachShare { get; init; }

    /// <summary>The cash in the drawer when a session opens, in whole units of the store's currency.</summary>
    public required Sourced<int> OpeningFloat { get; init; }
}

/// <summary>How the shopkeeper restocks and how suppliers deliver (W10 S6, D-046 §20–23).</summary>
internal sealed record SupplySettings
{
    /// <summary>Days of past sales the shopkeeper's sense of a product's rate covers.</summary>
    public required Sourced<int> MemoryDays { get; init; }

    /// <summary>Reorder when sellable plus on order falls to this many days of perceived sales.</summary>
    public required Sourced<double> ReorderCoverDays { get; init; }

    /// <summary>Order up to this many days of perceived sales, rounded up to whole cartons.</summary>
    public required Sourced<double> OrderUpToCoverDays { get; init; }

    /// <summary>Below this perceived rate (units a day) a product is ignored until it runs out.</summary>
    public required Sourced<double> SlowMoverRate { get; init; }

    /// <summary>Multiplier on the order-up-to level when Ramadan is coming.</summary>
    public required Sourced<double> RamadanOverOrder { get; init; }

    /// <summary>How far ahead the shopkeeper looks for Ramadan's stock-up.</summary>
    public required Sourced<int> RamadanLookaheadDays { get; init; }

    /// <summary>A delivery comes after the stated lead time plus 0 to this many days.</summary>
    public required Sourced<int> LateDeliveryMaxDays { get; init; }

    /// <summary>The chance an ordered line arrives in full.</summary>
    public required Sourced<double> FillRate { get; init; }

    /// <summary>When a line arrives short, the fraction of its cartons that come.</summary>
    public required Sourced<NumberRange> ShortDeliveryFraction { get; init; }

    /// <summary>The fraction of shelf life a delivered batch still has.</summary>
    public required Sourced<NumberRange> DeliveredShelfLife { get; init; }

    /// <summary>A supplier's representative takes the order this many minutes after opening.</summary>
    public required Sourced<int> VisitMinutesAfterOpening { get; init; }

    /// <summary>A delivery arrives within this many minutes of opening.</summary>
    public required Sourced<int> DeliveryWindowMinutes { get; init; }
}

/// <summary>What makes a real shop's data untidy (W10 S7, D-046 §24–30).</summary>
internal sealed record MessSettings
{
    /// <summary>The share of sale lines given a discount.</summary>
    public required Sourced<double> DiscountLineShare { get; init; }

    /// <summary>A discount's percentage of the line, drawn in whole percent.</summary>
    public required Sourced<NumberRange> DiscountPercent { get; init; }

    /// <summary>The share of baskets first rung wrongly, voided and rung again.</summary>
    public required Sourced<double> VoidShare { get; init; }

    /// <summary>The share of sale lines brought back later.</summary>
    public required Sourced<double> ReturnLineShare { get; init; }

    /// <summary>Whole days between a sale and its return.</summary>
    public required Sourced<NumberRange> ReturnDelayDays { get; init; }

    /// <summary>The chance a returned unit goes back on the shelf, if it has not expired.</summary>
    public required Sourced<double> RestockShare { get; init; }

    /// <summary>The chance an enrolled customer is refunded in store credit.</summary>
    public required Sourced<double> StoreCreditRefundShare { get; init; }

    /// <summary>The chance a customer holding store credit spends it on a basket.</summary>
    public required Sourced<double> StoreCreditUseShare { get; init; }

    /// <summary>The chance an enrolled customer's basket goes on account, before the pay-cycle multiplier.</summary>
    public required Sourced<double> OnAccountShare { get; init; }

    /// <summary>The chance of a paid-out from the drawer on an open day.</summary>
    public required Sourced<double> PaidOutDailyChance { get; init; }

    /// <summary>A paid-out's amount, whole units of the store's currency.</summary>
    public required Sourced<NumberRange> PaidOutAmount { get; init; }

    /// <summary>The chance the float is topped up (a paid-in) at opening.</summary>
    public required Sourced<double> PaidInDailyChance { get; init; }

    /// <summary>A paid-in's amount, whole units of the store's currency.</summary>
    public required Sourced<NumberRange> PaidInAmount { get; init; }

    /// <summary>At the midday break, cash above the float beyond this is dropped to the safe, in whole thousands.</summary>
    public required Sourced<int> DropThreshold { get; init; }

    /// <summary>The chance a drawer count disagrees with what is expected.</summary>
    public required Sourced<double> CashCountDiscrepancyChance { get; init; }

    /// <summary>The largest discrepancy, whole units of the store's currency, either way.</summary>
    public required Sourced<int> CashCountDiscrepancyMax { get; init; }

    /// <summary>A cycle count of one subcategory every this many days.</summary>
    public required Sourced<int> CountIntervalDays { get; init; }

    /// <summary>The chance a counted lot is found short (theft, breakage).</summary>
    public required Sourced<double> CountShrinkageChance { get; init; }

    /// <summary>The chance a counted lot is found over by one unit.</summary>
    public required Sourced<double> CountFoundChance { get; init; }

    /// <summary>Which of store.json's reason codes each kind of event uses. Names, not numbers, so not sourced.</summary>
    public required MessReasonCodes ReasonCodes { get; init; }
}

/// <summary>How often the store reaches the cloud (D-046 §31). It changes the outbox and sync_state, never a sale.</summary>
internal enum ConnectivityProfile
{
    /// <summary>The link is always up: the outbox empties at every drain.</summary>
    AlwaysOn,

    /// <summary>The link is up or down hour by hour.</summary>
    Flaky,

    /// <summary>Always up except one stretch of days with no link at all.</summary>
    OfflineStretch,
}

/// <summary>The drain of the outbox (W10 S8, sync-design §2.1, §10).</summary>
internal sealed record ConnectivitySettings
{
    /// <summary>Which profile a run uses; <c>--connectivity</c> overrides it. A choice, not a behavioural number, so not sourced.</summary>
    public required ConnectivityProfile Profile { get; init; }

    /// <summary>Minutes between drain attempts while the store is open.</summary>
    public required Sourced<int> DrainIntervalMinutes { get; init; }

    /// <summary>Messages per round trip; a drain that reaches the cloud sends batch after batch until the outbox is empty.</summary>
    public required Sourced<int> BatchSize { get; init; }

    /// <summary>Flaky profile: the chance the link is up in a given hour.</summary>
    public required Sourced<double> FlakyLinkUpShare { get; init; }

    /// <summary>Offline-stretch profile: the first day without a link, counted from the first trading day (0-based).</summary>
    public required Sourced<int> OfflineStartDay { get; init; }

    /// <summary>Offline-stretch profile: how many days the link stays down.</summary>
    public required Sourced<int> OfflineDays { get; init; }
}

/// <summary>Reason codes per event kind; one is drawn uniformly from each list.</summary>
internal sealed record MessReasonCodes
{
    public required IReadOnlyList<string> Discount { get; init; }

    public required IReadOnlyList<string> Void { get; init; }

    public required IReadOnlyList<string> Return { get; init; }

    public required IReadOnlyList<string> PaidOut { get; init; }

    public required IReadOnlyList<string> PaidIn { get; init; }

    public required IReadOnlyList<string> Drop { get; init; }
}

internal sealed record SeasonalityProfile
{
    /// <summary>Twelve multipliers, January first.</summary>
    public required IReadOnlyList<double> Months { get; init; }

    /// <summary>Keys: stock_up, ramadan, aid, post_aid.</summary>
    public required IReadOnlyDictionary<string, double> Ramadan { get; init; }

    /// <summary>Event name to multiplier. An event not listed leaves this profile unchanged.</summary>
    public required IReadOnlyDictionary<string, double> Events { get; init; }
}
