using Catga.Abstractions;
using System.Text.RegularExpressions;

namespace Catga.EventSourcing;

/// <summary>
/// Event handler interface for subscription processing.
/// </summary>
public interface IEventHandler
{
    ValueTask HandleAsync(IEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Persistent subscription that survives restarts.
/// </summary>
public sealed class PersistentSubscription
{
    public PersistentSubscription(string name, string streamPattern)
    {
        Name = name;
        StreamPattern = streamPattern;
    }

    public string Name { get; }
    public string StreamPattern { get; }
    public long Position { get; set; } = -1; // -1 means no events processed yet
    public List<string> EventTypeFilter { get; set; } = [];
    public long ProcessedCount { get; set; }
    public DateTime? LastProcessedAt { get; set; }
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    public void UpdatePosition(long position)
    {
        Position = position;
        LastProcessedAt = DateTime.UtcNow;
    }

    public bool MatchesStream(string streamId)
    {
        if (StreamPattern == "*") return true;
        if (StreamPattern.EndsWith("*"))
        {
            var prefix = StreamPattern[..^1];
            return streamId.StartsWith(prefix);
        }
        return streamId == StreamPattern;
    }

    public bool MatchesEventType(string eventType)
    {
        if (EventTypeFilter.Count == 0) return true;
        return EventTypeFilter.Contains(eventType);
    }
}

/// <summary>
/// Subscription store interface for persisting subscriptions.
/// </summary>
public interface ISubscriptionStore
{
    ValueTask SaveAsync(PersistentSubscription subscription, CancellationToken ct = default);
    ValueTask<PersistentSubscription?> LoadAsync(string name, CancellationToken ct = default);
    ValueTask DeleteAsync(string name, CancellationToken ct = default);
    ValueTask<IReadOnlyList<PersistentSubscription>> ListAsync(CancellationToken ct = default);

    /// <summary>Try to acquire a lock for competing consumer processing.</summary>
    ValueTask<bool> TryAcquireLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default);

    /// <summary>Release the lock after processing.</summary>
    ValueTask ReleaseLockAsync(string subscriptionName, string consumerId, CancellationToken ct = default);
}

/// <summary>
/// Runs a subscription, processing events from the event store.
/// </summary>
public sealed class SubscriptionRunner
{
    private readonly IEventStore _eventStore;
    private readonly ISubscriptionStore _subscriptionStore;
    private readonly IEventHandler _handler;

    public SubscriptionRunner(
        IEventStore eventStore,
        ISubscriptionStore subscriptionStore,
        IEventHandler handler)
    {
        _eventStore = eventStore;
        _subscriptionStore = subscriptionStore;
        _handler = handler;
    }

    /// <summary>Process events once from the current position.</summary>
    public async ValueTask RunOnceAsync(string subscriptionName, CancellationToken ct = default)
    {
        var subscription = await _subscriptionStore.LoadAsync(subscriptionName, ct);
        if (subscription == null) return;

        var streams = await _eventStore.GetAllStreamIdsAsync(ct);
        var matchingStreams = streams.Where(s => subscription.MatchesStream(s)).ToList();

        long processedCount = 0;
        long maxPosition = subscription.Position;

        foreach (var streamId in matchingStreams)
        {
            var eventStream = await _eventStore.ReadAsync(streamId, 0, cancellationToken: ct);
            foreach (var stored in eventStream.Events)
            {
                if (stored.Version <= subscription.Position) continue;
                if (!subscription.MatchesEventType(stored.EventType)) continue;

                await _handler.HandleAsync(stored.Event, ct);
                processedCount++;
                maxPosition = Math.Max(maxPosition, stored.Version);
            }
        }

        if (processedCount > 0)
        {
            subscription.Position = maxPosition;
            subscription.ProcessedCount += processedCount;
            subscription.LastProcessedAt = DateTime.UtcNow;
            await _subscriptionStore.SaveAsync(subscription, ct);
        }
    }

    /// <summary>Continuously process events until cancelled.</summary>
    public async ValueTask RunContinuouslyAsync(string subscriptionName, CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            await RunOnceAsync(subscriptionName, ct);
            await Task.Delay(100, ct); // Polling interval
        }
    }
}

/// <summary>
/// Competing consumer for load-balanced event processing.
/// </summary>
public sealed class CompetingConsumer
{
    private readonly IEventStore _eventStore;
    private readonly ISubscriptionStore _subscriptionStore;
    private readonly IEventHandler _handler;
    private readonly string _subscriptionName;
    private readonly string _consumerId;

    public CompetingConsumer(
        IEventStore eventStore,
        ISubscriptionStore subscriptionStore,
        IEventHandler handler,
        string subscriptionName,
        string consumerId)
    {
        _eventStore = eventStore;
        _subscriptionStore = subscriptionStore;
        _handler = handler;
        _subscriptionName = subscriptionName;
        _consumerId = consumerId;
    }

    /// <summary>Try to process the next event. Returns true if an event was processed.</summary>
    public async ValueTask<bool> TryProcessNextAsync(CancellationToken ct = default)
    {
        // Try to acquire lock
        if (!await _subscriptionStore.TryAcquireLockAsync(_subscriptionName, _consumerId, ct))
            return false;

        try
        {
            var subscription = await _subscriptionStore.LoadAsync(_subscriptionName, ct);
            if (subscription == null) return false;

            var streams = await _eventStore.GetAllStreamIdsAsync(ct);
            var matchingStreams = streams.Where(s => subscription.MatchesStream(s)).ToList();

            foreach (var streamId in matchingStreams)
            {
                var eventStream = await _eventStore.ReadAsync(streamId, 0, cancellationToken: ct);
                foreach (var stored in eventStream.Events)
                {
                    if (stored.Version <= subscription.Position) continue;
                    if (!subscription.MatchesEventType(stored.EventType)) continue;

                    // Process single event
                    await _handler.HandleAsync(stored.Event, ct);

                    subscription.Position = stored.Version;
                    subscription.ProcessedCount++;
                    subscription.LastProcessedAt = DateTime.UtcNow;
                    await _subscriptionStore.SaveAsync(subscription, ct);

                    return true;
                }
            }

            return false;
        }
        finally
        {
            await _subscriptionStore.ReleaseLockAsync(_subscriptionName, _consumerId, ct);
        }
    }
}
