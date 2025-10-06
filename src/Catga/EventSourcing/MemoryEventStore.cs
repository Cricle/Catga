using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using System.Text.Json;
using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// 内存事件存储实现（用于测试）
/// </summary>
public class MemoryEventStore : IEventStore
{
    private readonly ConcurrentDictionary<string, List<StoredEvent>> _streams = new();
    private readonly ConcurrentDictionary<string, (object Snapshot, long Version)> _snapshots = new();
    private readonly List<StoredEvent> _allEvents = new();
    private long _globalPosition = 0;
    private readonly object _lock = new();

    public Task AppendToStreamAsync(
        string streamId,
        IEnumerable<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            var stream = _streams.GetOrAdd(streamId, _ => new List<StoredEvent>());

            // 检查版本冲突
            var currentVersion = stream.Count > 0 ? stream[^1].Version : -1;
            if (expectedVersion != -1 && currentVersion != expectedVersion)
            {
                throw new InvalidOperationException(
                    $"Version conflict: expected {expectedVersion}, but current is {currentVersion}");
            }

            foreach (var @event in events)
            {
                currentVersion++;
                var storedEvent = new StoredEvent(
                    streamId,
                    currentVersion,
                    @event.GetType().FullName ?? @event.GetType().Name,
                    JsonSerializer.Serialize(@event, @event.GetType()),
                    null,
                    DateTime.UtcNow)
                {
                    Position = Interlocked.Increment(ref _globalPosition)
                };

                stream.Add(storedEvent);
                _allEvents.Add(storedEvent);
            }
        }

        return Task.CompletedTask;
    }

    public Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default)
    {
        if (!_streams.TryGetValue(streamId, out var stream))
        {
            return Task.FromResult<IReadOnlyList<StoredEvent>>(Array.Empty<StoredEvent>());
        }

        lock (_lock)
        {
            var events = stream
                .Where(e => e.Version >= fromVersion)
                .Take(maxCount)
                .ToList();

            return Task.FromResult<IReadOnlyList<StoredEvent>>(events);
        }
    }

    public async IAsyncEnumerable<StoredEvent> ReadAllAsync(
        long fromPosition = 0,
        [EnumeratorCancellation] CancellationToken cancellationToken = default)
    {
        List<StoredEvent> events;
        lock (_lock)
        {
            events = _allEvents.Where(e => e.Position >= fromPosition).ToList();
        }

        foreach (var @event in events)
        {
            yield return @event;
            await Task.Yield(); // 允许取消
        }
    }

    public Task SaveSnapshotAsync<TSnapshot>(
        string streamId,
        long version,
        TSnapshot snapshot,
        CancellationToken cancellationToken = default) where TSnapshot : class
    {
        _snapshots[streamId] = (snapshot, version);
        return Task.CompletedTask;
    }

    public Task<(TSnapshot? Snapshot, long Version)> LoadSnapshotAsync<TSnapshot>(
        string streamId,
        CancellationToken cancellationToken = default) where TSnapshot : class
    {
        if (_snapshots.TryGetValue(streamId, out var snapshot))
        {
            return Task.FromResult(((TSnapshot)snapshot.Snapshot, snapshot.Version));
        }

        return Task.FromResult<(TSnapshot?, long)>((null, -1));
    }

    public Task DeleteStreamAsync(string streamId, CancellationToken cancellationToken = default)
    {
        lock (_lock)
        {
            if (_streams.TryRemove(streamId, out var stream))
            {
                // 从全局事件列表中移除
                foreach (var @event in stream)
                {
                    _allEvents.Remove(@event);
                }
            }

            _snapshots.TryRemove(streamId, out _);
        }

        return Task.CompletedTask;
    }
}

