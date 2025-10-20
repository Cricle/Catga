using Catga.Abstractions;
using Catga.EventSourcing;
using Catga.Core;
using StackExchange.Redis;

namespace Catga.Persistence;

/// <summary>
/// Redis-based event store implementation (PLACEHOLDER - TODO: Full Implementation)
/// </summary>
/// <remarks>
/// This is a placeholder for future implementation.
/// See tests/Catga.Tests/Persistence/RedisEventStoreTests.cs for specification.
/// </remarks>
[Obsolete("This is a placeholder implementation. Full implementation is pending.", error: false)]
public sealed class RedisEventStore : IEventStore, IDisposable
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;

    public RedisEventStore(IConnectionMultiplexer redis, IMessageSerializer serializer)
    {
        _redis = redis ?? throw new ArgumentNullException(nameof(redis));
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
    }

    public ValueTask AppendAsync(string streamId, IReadOnlyList<IEvent> events, long expectedVersion = -1, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("RedisEventStore implementation is pending. See TEST-IMPLEMENTATION-MAP.md for details.");
    }

    public ValueTask<EventStream> ReadAsync(string streamId, long fromVersion = 0, int maxCount = int.MaxValue, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("RedisEventStore implementation is pending. See TEST-IMPLEMENTATION-MAP.md for details.");
    }

    public ValueTask<long> GetVersionAsync(string streamId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("RedisEventStore implementation is pending. See TEST-IMPLEMENTATION-MAP.md for details.");
    }

    public ValueTask<bool> ExistsAsync(string streamId, CancellationToken cancellationToken = default)
    {
        throw new NotImplementedException("RedisEventStore implementation is pending. See TEST-IMPLEMENTATION-MAP.md for details.");
    }

    public void Dispose()
    {
        // Nothing to dispose in placeholder
    }
}

