using System.Globalization;
using System.Text.Json;
using Waymark.Contracts.Sync;
using Waymark.Domain.Enums;
using Waymark.Domain.Sync;
using Waymark.Domain.Values;
using Waymark.Generator.Configuration;
using Waymark.Generator.Randomness;

namespace Waymark.Generator.Simulation;

/// <summary>The outbox at the end of one day: the largest it grew that day and what was left at close.</summary>
internal sealed record BacklogDay(DateOnly Date, long Max, long End);

/// <summary>
/// The statistics outbox and its drain (W10 S8, D-043, sync-design §2).
///
/// <para>
/// <b>Emission never depends on connectivity.</b> Every completed sale writes one
/// <c>anonymous_basket</c> message on <c>A_statistics</c>, with a gapless sequence number, at
/// the moment of the sale. Refunds and voids emit nothing: D-043 admits no field without a
/// named consumer, and the basket stream's consumers are baskets. Customer period records are
/// not emitted, because there is no spend-banding scheme yet (D-049) and the generator may not
/// compute pseudonyms (D-054).
/// </para>
/// <para>
/// <b>The drain is all that connectivity changes.</b> While the store is open, the drain runs
/// every <c>drain_interval_minutes</c> and at close. If the link is up it sends batch after
/// batch until the outbox is empty, and acked rows are deleted (sync-design §2.3). If the link
/// is down, every queued row counts a failed attempt. The drain mints no id and draws from its
/// own stream, so the same seed under any profile produces identical sales (D-046 §31).
/// </para>
/// <para>
/// The outbox and <c>sync_state</c> describe a state, so they are written once at the end of
/// the run, like purchase orders still on their way: what is still queued, and the checkpoint.
/// </para>
/// </summary>
internal sealed class Outbox
{
    public const string MessageType = "anonymous_basket";

    private readonly SimulationContext _context;
    private readonly ConnectivitySettings _settings;
    private readonly LinkedList<Queued> _queue = new();
    private readonly List<BacklogDay> _backlog = [];
    private readonly RandomStream _link;
    private long _lastSequence;
    private long _lastAcked;
    private long _batches;
    private long _todayMax;
    private DateTimeOffset? _lastDrainAt;
    private DateTimeOffset? _lastAttemptAt;

    public Outbox(SimulationContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        _context = context;
        _settings = context.Config.Connectivity;
        _link = context.Random.Stream("link");
    }

    /// <summary>Messages emitted so far.</summary>
    public long Emitted => _lastSequence;

    /// <summary>Messages the cloud has acknowledged.</summary>
    public long Acknowledged => _lastAcked;

    /// <summary>Round trips that carried a batch.</summary>
    public long Batches => _batches;

    /// <summary>The outbox's size at the end of each trading day.</summary>
    public IReadOnlyList<BacklogDay> Backlog => _backlog;

    /// <summary>
    /// Emits a completed sale as an anonymous basket: date, hour, weekday, product lines, a
    /// payment class and a discount flag. No customer, no transaction id, no time finer than the
    /// hour, and an entity id of null, so the record cannot be joined back to the till.
    /// </summary>
    public void EmitBasket(DateOnly date, IReadOnlyList<(GeneratedVariant Variant, Quantity Quantity, Money LineTotal, bool Discounted)> lines, IReadOnlyCollection<PaymentMethod> methods)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(methods);

        var now = _context.Clock.GetUtcNow();
        var hour = (int)Math.Floor((now - _context.Calendar.ToUtc(date, 0)).TotalHours);
        var currency = _context.Store.Currency;

        var products = lines
            .GroupBy(line => line.Variant.ProductId, StringComparer.Ordinal)
            .Select(group => new BasketLine(
                group.Key,
                Units(group.Sum(line => line.Quantity.Thousandths)),
                Amount(group.Aggregate(Money.Zero(currency), (sum, line) => sum + line.LineTotal))))
            .ToList();

        var paymentClass = methods.Distinct().Count() > 1 ? "mixed" : Code(methods.First());
        var record = new AnonymousBasketRecord(
            _context.Ids.NewId(),
            _context.Store.StoreId,
            date,
            hour,
            (int)date.DayOfWeek,
            products,
            paymentClass,
            lines.Any(line => line.Discounted));

        _queue.AddLast(new Queued(_context.Ids.NewId(), ++_lastSequence, JsonSerializer.Serialize(record), now));
        _todayMax = Math.Max(_todayMax, _queue.Count);
    }

    /// <summary>One drain attempt at the current moment, <paramref name="dayIndex"/> days into the run.</summary>
    public void Drain(DateOnly date, int dayIndex, int secondOfDay)
    {
        if (_queue.Count == 0)
        {
            return;
        }

        var now = _context.Clock.GetUtcNow();
        _lastAttemptAt = now;

        if (!LinkUp(date, dayIndex, secondOfDay / 3600))
        {
            foreach (var message in _queue)
            {
                message.Attempts++;
                message.LastAttemptAt = now;
            }

            return;
        }

        // No catch-up mode (sync-design §10.3): the same loop, one batch after another.
        _batches += (_queue.Count + _settings.BatchSize.Value - 1) / _settings.BatchSize.Value;
        _lastAcked = _queue.Last!.Value.Sequence;
        _lastDrainAt = now;
        _queue.Clear();
    }

    /// <summary>Records the day's backlog: its peak and what is left once the store has closed.</summary>
    public void EndDay(DateOnly date)
    {
        _backlog.Add(new BacklogDay(date, _todayMax, _queue.Count));
        _todayMax = _queue.Count;
    }

    /// <summary>At the end of the run: the rows still queued, and the sync checkpoint.</summary>
    public void WriteFinal()
    {
        var db = _context.Database.Context;
        foreach (var message in _queue)
        {
            db.Outbox.Add(new OutboxMessage
            {
                OutboxId = message.OutboxId,
                SequenceNumber = message.Sequence,
                Channel = OutboxMessageChannel.AStatistics,
                MessageType = MessageType,
                PayloadJson = message.PayloadJson,
                Attempts = message.Attempts,
                LastAttemptAt = message.LastAttemptAt,
                LastError = message.Attempts > 0 ? "No connection to the cloud (synthetic)." : null,
                CreatedAt = message.CreatedAt,
            });
        }

        var at = _context.Clock.GetUtcNow();
        var state = new (string Key, string? Value)[]
        {
            ("last_sequence", _lastSequence.ToString(CultureInfo.InvariantCulture)),
            ("last_acked_sequence", _lastAcked.ToString(CultureInfo.InvariantCulture)),
            ("last_drain_at", _lastDrainAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
            ("last_attempt_at", _lastAttemptAt?.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)),
        };

        foreach (var (key, value) in state)
        {
            if (value is not null)
            {
                db.SyncState.Add(new SyncState { StateKey = key, StateValue = value, UpdatedAt = at });
            }
        }
    }

    /// <summary>Whether the store can reach the cloud in this hour, by the profile.</summary>
    private bool LinkUp(DateOnly date, int dayIndex, int hour) => _settings.Profile switch
    {
        ConnectivityProfile.AlwaysOn => true,
        ConnectivityProfile.Flaky => Distributions.Bernoulli(_link.Uniform(date.DayNumber, hour), _settings.FlakyLinkUpShare.Value),
        ConnectivityProfile.OfflineStretch => dayIndex < _settings.OfflineStartDay.Value || dayIndex >= _settings.OfflineStartDay.Value + _settings.OfflineDays.Value,
        _ => throw new InvalidOperationException($"Unknown connectivity profile {_settings.Profile}."),
    };

    /// <summary>A quantity as exact decimal text in the selling unit: "3", "0.25" (Contracts: figures cross as strings, D-049).</summary>
    private static string Units(long thousandths)
    {
        var text = (thousandths / (decimal)Quantity.Scale).ToString("0.###", CultureInfo.InvariantCulture);
        return text;
    }

    /// <summary>An amount as exact decimal text in currency units: "1250.00".</summary>
    private static string Amount(Money money) =>
        (money.MinorUnits / (decimal)Currency.StorageScale).ToString("0.00", CultureInfo.InvariantCulture);

    private static string Code(PaymentMethod method) => method switch
    {
        PaymentMethod.Cash => "cash",
        PaymentMethod.Card => "card",
        PaymentMethod.MobileWallet => "mobile_wallet",
        PaymentMethod.StoreCredit => "store_credit",
        _ => "on_account",
    };

    private sealed class Queued(string outboxId, long sequence, string payloadJson, DateTimeOffset createdAt)
    {
        public string OutboxId { get; } = outboxId;

        public long Sequence { get; } = sequence;

        public string PayloadJson { get; } = payloadJson;

        public DateTimeOffset CreatedAt { get; } = createdAt;

        public long Attempts { get; set; }

        public DateTimeOffset? LastAttemptAt { get; set; }
    }
}
