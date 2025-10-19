using System.Diagnostics;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.EventSourcing;

/// <summary>
/// Repository interface for loading and saving aggregates from event store
/// </summary>
public interface IEventStoreRepository<TId, TState>
    where TId : notnull
    where TState : class, new()
{
    ValueTask<TAggregate?> LoadAsync<TAggregate>(
        TId id,
        CancellationToken ct = default)
        where TAggregate : AggregateRoot<TId, TState>, new();

    ValueTask SaveAsync<TAggregate>(
        TAggregate aggregate,
        CancellationToken ct = default)
        where TAggregate : AggregateRoot<TId, TState>;
}

/// <summary>
/// High-performance repository for event-sourced aggregates.
/// Zero reflection, optimized GC, thread-safe.
/// </summary>
public sealed class EventStoreRepository<TId, TState> : IEventStoreRepository<TId, TState>
    where TId : notnull
    where TState : class, new()
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<EventStoreRepository<TId, TState>> _logger;

    public EventStoreRepository(
        IEventStore eventStore,
        ILogger<EventStoreRepository<TId, TState>> logger)
    {
        _eventStore = eventStore ?? throw new ArgumentNullException(nameof(eventStore));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<TAggregate?> LoadAsync<TAggregate>(
        TId id,
        CancellationToken ct = default)
        where TAggregate : AggregateRoot<TId, TState>, new()
    {
        if (id == null) throw new ArgumentNullException(nameof(id));

        var streamId = GetStreamId<TAggregate>(id);

        using var activity = Activity.Current?.Source.StartActivity("EventStore.Load");
        activity?.SetTag("aggregate.type", typeof(TAggregate).Name);
        activity?.SetTag("aggregate.id", id.ToString());

        var stopwatch = Stopwatch.StartNew();

        try
        {
            var eventStream = await _eventStore.ReadAsync(streamId, 0, int.MaxValue, ct);

            if (eventStream.Events.Count == 0)
            {
                CatgaLog.AggregateNotFound(_logger, typeof(TAggregate).Name, id.ToString()!);
                return null;
            }

            // Create aggregate without reflection (uses new() constraint)
            var aggregate = new TAggregate();

            // Load events
            aggregate.LoadFromHistory(eventStream.Events);

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("aggregate.version", aggregate.Version);
            activity?.SetTag("aggregate.event_count", eventStream.Events.Count);

            CatgaLog.AggregateLoaded(
                _logger,
                typeof(TAggregate).Name,
                id.ToString()!,
                aggregate.Version,
                eventStream.Events.Count,
                stopwatch.ElapsedMilliseconds);

            return aggregate;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);

            CatgaLog.AggregateLoadFailed(_logger, typeof(TAggregate).Name, id.ToString()!, ex.Message, ex);
            throw;
        }
    }

    public async ValueTask SaveAsync<TAggregate>(
        TAggregate aggregate,
        CancellationToken ct = default)
        where TAggregate : AggregateRoot<TId, TState>
    {
        ArgumentNullException.ThrowIfNull(aggregate, nameof(aggregate));
        if (aggregate.Id == null) throw new InvalidOperationException("Aggregate ID cannot be null");

        var uncommittedEvents = aggregate.UncommittedEvents;
        if (uncommittedEvents.Count == 0)
        {
            CatgaLog.AggregateNoChanges(_logger, typeof(TAggregate).Name, aggregate.Id.ToString()!);
            return; // No changes to save
        }

        var streamId = GetStreamId<TAggregate>(aggregate.Id);

        using var activity = Activity.Current?.Source.StartActivity("EventStore.Save");
        activity?.SetTag("aggregate.type", typeof(TAggregate).Name);
        activity?.SetTag("aggregate.id", aggregate.Id.ToString());
        activity?.SetTag("aggregate.event_count", uncommittedEvents.Count);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // ✅ 优化：直接传递 IReadOnlyList，避免 ToArray() 分配
            await _eventStore.AppendAsync(streamId, uncommittedEvents, aggregate.Version, ct);

            // Mark events as committed
            aggregate.MarkEventsAsCommitted();

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("aggregate.new_version", aggregate.Version);

            CatgaLog.AggregateSaved(
                _logger,
                typeof(TAggregate).Name,
                aggregate.Id.ToString()!,
                aggregate.Version,
                uncommittedEvents.Count,
                stopwatch.ElapsedMilliseconds);
        }
        catch (ConcurrencyException ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, "Concurrency conflict");
            activity?.SetTag("expected_version", ex.ExpectedVersion);
            activity?.SetTag("actual_version", ex.ActualVersion);

            CatgaLog.AggregateConcurrencyConflict(
                _logger,
                typeof(TAggregate).Name,
                aggregate.Id.ToString()!,
                ex.ExpectedVersion,
                ex.ActualVersion);

            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);

            CatgaLog.AggregateSaveFailed(_logger, typeof(TAggregate).Name, aggregate.Id.ToString()!, ex.Message, ex);
            throw;
        }
    }

    private static string GetStreamId<TAggregate>(TId id)
        where TAggregate : AggregateRoot<TId, TState> =>
        // Use type name + ID for stream naming (avoid reflection)
        $"{typeof(TAggregate).Name}-{id}";
}

/// <summary>
/// Logging extensions for event store repository
/// </summary>
public static partial class CatgaLog
{
    [LoggerMessage(
        EventId = 5001,
        Level = LogLevel.Debug,
        Message = "Aggregate {AggregateType} not found, ID: {AggregateId}")]
    public static partial void AggregateNotFound(
        ILogger logger, string aggregateType, string aggregateId);

    [LoggerMessage(
        EventId = 5002,
        Level = LogLevel.Information,
        Message = "Loaded aggregate {AggregateType}, ID: {AggregateId}, Version: {Version}, Events: {EventCount}, Duration: {DurationMs}ms")]
    public static partial void AggregateLoaded(
        ILogger logger, string aggregateType, string aggregateId, long version, int eventCount, long durationMs);

    [LoggerMessage(
        EventId = 5003,
        Level = LogLevel.Error,
        Message = "Failed to load aggregate {AggregateType}, ID: {AggregateId}, Error: {ErrorMessage}")]
    public static partial void AggregateLoadFailed(
        ILogger logger, string aggregateType, string aggregateId, string errorMessage, Exception ex);

    [LoggerMessage(
        EventId = 5004,
        Level = LogLevel.Debug,
        Message = "Aggregate {AggregateType} has no uncommitted changes, ID: {AggregateId}")]
    public static partial void AggregateNoChanges(
        ILogger logger, string aggregateType, string aggregateId);

    [LoggerMessage(
        EventId = 5005,
        Level = LogLevel.Information,
        Message = "Saved aggregate {AggregateType}, ID: {AggregateId}, Version: {Version}, Events: {EventCount}, Duration: {DurationMs}ms")]
    public static partial void AggregateSaved(
        ILogger logger, string aggregateType, string aggregateId, long version, int eventCount, long durationMs);

    [LoggerMessage(
        EventId = 5006,
        Level = LogLevel.Warning,
        Message = "Concurrency conflict on aggregate {AggregateType}, ID: {AggregateId}, Expected: {ExpectedVersion}, Actual: {ActualVersion}")]
    public static partial void AggregateConcurrencyConflict(
        ILogger logger, string aggregateType, string aggregateId, long expectedVersion, long actualVersion);

    [LoggerMessage(
        EventId = 5007,
        Level = LogLevel.Error,
        Message = "Failed to save aggregate {AggregateType}, ID: {AggregateId}, Error: {ErrorMessage}")]
    public static partial void AggregateSaveFailed(
        ILogger logger, string aggregateType, string aggregateId, string errorMessage, Exception ex);
}

