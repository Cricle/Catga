using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Catga.Debugger.Models;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Storage;

/// <summary>In-memory event store with ring buffer - zero-allocation design</summary>
public sealed partial class InMemoryEventStore : IEventStore, IDisposable
{
    private readonly ReplayOptions _options;
    private readonly ILogger<InMemoryEventStore> _logger;

    // Ring buffer for events (fixed size, circular)
    private readonly ReplayableEvent?[] _ringBuffer;
    private int _head;
    private int _tail;
    private int _count;
    private readonly object _bufferLock = new();

    // Indexes for fast lookups
    private readonly ConcurrentDictionary<string, List<int>> _correlationIndex = new();
    private readonly ConcurrentDictionary<string, int> _eventIdIndex = new();

    // Time-based index (simplified B+ tree using SortedList)
    private readonly SortedList<DateTime, List<int>> _timeIndex = new();
    private readonly object _timeIndexLock = new();

    // Cleanup timer
    private readonly System.Threading.Timer? _cleanupTimer;
    private bool _disposed;

    public InMemoryEventStore(ReplayOptions options, ILogger<InMemoryEventStore> logger)
    {
        _options = options;
        _logger = logger;

        var capacity = options.UseRingBuffer ? options.RingBufferCapacity : 10000;
        _ringBuffer = new ReplayableEvent[capacity];

        // Start cleanup timer
        if (options.EventRetention > TimeSpan.Zero)
        {
            _cleanupTimer = new System.Threading.Timer(
                callback: _ => _ = CleanupAsync(DateTime.UtcNow - options.EventRetention),
                state: null,
                dueTime: TimeSpan.FromMinutes(1),
                period: TimeSpan.FromMinutes(5)
            );
        }
    }

    public ValueTask SaveAsync(IEnumerable<ReplayableEvent> events, CancellationToken cancellationToken = default)
    {
        foreach (var evt in events)
        {
            if (cancellationToken.IsCancellationRequested) break;

            SaveEventToRingBuffer(evt);
        }

        return default;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void SaveEventToRingBuffer(ReplayableEvent evt)
    {
        lock (_bufferLock)
        {
            var index = _tail;

            // Check for overflow
            if (_count >= _ringBuffer.Length)
            {
                // Handle overflow based on strategy
                if (_options.OverflowStrategy == OverflowStrategy.DropOldest)
                {
                    // Remove oldest event from indexes
                    var oldEvent = _ringBuffer[_head];
                    if (oldEvent != null)
                    {
                        RemoveFromIndexes(oldEvent, _head);
                    }

                    // Overwrite oldest
                    _ringBuffer[_tail] = evt;
                    _tail = (_tail + 1) % _ringBuffer.Length;
                    _head = (_head + 1) % _ringBuffer.Length;
                }
                else if (_options.OverflowStrategy == OverflowStrategy.DropNewest)
                {
                    // Drop new event
                    LogBufferFullDroppingEvent(evt.Id);
                    return;
                }
            }
            else
            {
                _ringBuffer[_tail] = evt;
                _tail = (_tail + 1) % _ringBuffer.Length;
                _count++;
            }

            // Update indexes
            AddToIndexes(evt, index);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void AddToIndexes(ReplayableEvent evt, int bufferIndex)
    {
        // Event ID index
        _eventIdIndex[evt.Id] = bufferIndex;

        // Correlation ID index
        _correlationIndex.AddOrUpdate(
            evt.CorrelationId,
            _ => new List<int> { bufferIndex },
            (_, list) => { list.Add(bufferIndex); return list; }
        );

        // Time index
        lock (_timeIndexLock)
        {
            if (!_timeIndex.TryGetValue(evt.Timestamp, out var timeList))
            {
                timeList = new List<int>();
                _timeIndex[evt.Timestamp] = timeList;
            }
            timeList.Add(bufferIndex);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void RemoveFromIndexes(ReplayableEvent evt, int bufferIndex)
    {
        // Event ID index
        _eventIdIndex.TryRemove(evt.Id, out _);

        // Correlation ID index
        if (_correlationIndex.TryGetValue(evt.CorrelationId, out var corrList))
        {
            corrList.Remove(bufferIndex);
            if (corrList.Count == 0)
                _correlationIndex.TryRemove(evt.CorrelationId, out _);
        }

        // Time index
        lock (_timeIndexLock)
        {
            if (_timeIndex.TryGetValue(evt.Timestamp, out var timeList))
            {
                timeList.Remove(bufferIndex);
                if (timeList.Count == 0)
                    _timeIndex.Remove(evt.Timestamp);
            }
        }
    }

    public ValueTask<IEnumerable<ReplayableEvent>> GetEventsAsync(
        DateTime startTime,
        DateTime endTime,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReplayableEvent>();

        lock (_timeIndexLock)
        {
            // Use time index for efficient range query
            foreach (var kvp in _timeIndex)
            {
                if (kvp.Key < startTime) continue;
                if (kvp.Key > endTime) break;

                foreach (var index in kvp.Value)
                {
                    lock (_bufferLock)
                    {
                        var evt = _ringBuffer[index];
                        if (evt != null)
                            results.Add(evt);
                    }
                }
            }
        }

        return new(results.OrderBy(e => e.Timestamp));
    }

    public ValueTask<IEnumerable<ReplayableEvent>> GetEventsByCorrelationAsync(
        string correlationId,
        CancellationToken cancellationToken = default)
    {
        var results = new List<ReplayableEvent>();

        if (_correlationIndex.TryGetValue(correlationId, out var indexes))
        {
            lock (_bufferLock)
            {
                foreach (var index in indexes)
                {
                    var evt = _ringBuffer[index];
                    if (evt != null)
                        results.Add(evt);
                }
            }
        }

        return new(results.OrderBy(e => e.Timestamp));
    }

    public ValueTask<ReplayableEvent?> GetEventByIdAsync(
        string eventId,
        CancellationToken cancellationToken = default)
    {
        if (_eventIdIndex.TryGetValue(eventId, out var index))
        {
            lock (_bufferLock)
            {
                return new(_ringBuffer[index]);
            }
        }

        return new((ReplayableEvent?)null);
    }

    public ValueTask CleanupAsync(DateTime olderThan, CancellationToken cancellationToken = default)
    {
        var removed = 0;

        lock (_bufferLock)
        {
            lock (_timeIndexLock)
            {
                var keysToRemove = _timeIndex.Keys.Where(k => k < olderThan).ToList();

                foreach (var key in keysToRemove)
                {
                    if (_timeIndex.TryGetValue(key, out var indexes))
                    {
                        foreach (var index in indexes)
                        {
                            var evt = _ringBuffer[index];
                            if (evt != null)
                            {
                                RemoveFromIndexes(evt, index);
                                _ringBuffer[index] = null;
                                removed++;
                                _count--;
                            }
                        }

                        _timeIndex.Remove(key);
                    }
                }
            }
        }

        if (removed > 0)
        {
            LogCleanupCompleted(removed, olderThan);
        }

        return default;
    }

    public ValueTask<EventStoreStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        lock (_bufferLock)
        {
            lock (_timeIndexLock)
            {
                var stats = new EventStoreStats
                {
                    TotalEvents = _count,
                    TotalFlows = _correlationIndex.Count,
                    StorageSizeBytes = EstimateStorageSize(),
                    OldestEvent = _timeIndex.Keys.FirstOrDefault(),
                    NewestEvent = _timeIndex.Keys.LastOrDefault()
                };

                return new(stats);
            }
        }
    }

    private long EstimateStorageSize()
    {
        // Rough estimation: each event ~1KB
        return _count * 1024L;
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;

        _cleanupTimer?.Dispose();

        lock (_bufferLock)
        {
            Array.Clear(_ringBuffer, 0, _ringBuffer.Length);
        }

        _correlationIndex.Clear();
        _eventIdIndex.Clear();
        _timeIndex.Clear();
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Ring buffer full, dropping new event {EventId}")]
    partial void LogBufferFullDroppingEvent(string eventId);

    [LoggerMessage(Level = LogLevel.Information, Message = "Cleaned up {Count} old events older than {Time}")]
    partial void LogCleanupCompleted(int count, DateTime time);
}

