using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Idempotency;
using Catga.Observability;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory idempotency store for development and testing.
/// Thread-safe, zero-allocation design.
/// </summary>
public sealed class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly ConcurrentDictionary<long, (DateTime ProcessedAt, byte[]? ResultData)> _store = new();
    private readonly ConcurrentDictionary<long, ConcurrentDictionary<string, byte[]>> _typedResults = new();
    private readonly IMessageSerializer _serializer;
    private readonly TimeSpan _retentionPeriod;

    public InMemoryIdempotencyStore(IMessageSerializer serializer, TimeSpan? retentionPeriod = null)
    {
        _serializer = serializer;
        _retentionPeriod = retentionPeriod ?? TimeSpan.FromHours(24);
    }

    public Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken ct = default)
    {
        CleanupExpired();
        var exists = _store.ContainsKey(messageId);
        if (exists) CatgaDiagnostics.IdempotencyHits.Add(1);
        else CatgaDiagnostics.IdempotencyMisses.Add(1);
        return Task.FromResult(exists);
    }

    public Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        TResult? result = default,
        CancellationToken ct = default)
    {
        CleanupExpired();
        _store[messageId] = (DateTime.UtcNow, null);
        CatgaDiagnostics.IdempotencyMarked.Add(1);

        if (result != null)
        {
            var resultData = _serializer.Serialize(result, typeof(TResult));
            var dict = _typedResults.GetOrAdd(messageId, _ => new ConcurrentDictionary<string, byte[]>());
            dict[typeof(TResult).FullName ?? typeof(TResult).Name] = resultData;
        }

        return Task.CompletedTask;
    }

    public Task<TResult?> GetCachedResultAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId,
        CancellationToken ct = default)
    {
        var typeName = typeof(TResult).FullName ?? typeof(TResult).Name;
        if (_typedResults.TryGetValue(messageId, out var dict) && dict.TryGetValue(typeName, out var resultData))
        {
            CatgaDiagnostics.IdempotencyCacheHits.Add(1);
            return Task.FromResult((TResult?)_serializer.Deserialize(resultData, typeof(TResult)));
        }
        CatgaDiagnostics.IdempotencyCacheMisses.Add(1);
        return Task.FromResult(default(TResult?));
    }

    private void CleanupExpired()
    {
        var cutoff = DateTime.UtcNow - _retentionPeriod;
        foreach (var kvp in _store)
        {
            if (kvp.Value.ProcessedAt < cutoff)
            {
                _store.TryRemove(kvp.Key, out _);
                _typedResults.TryRemove(kvp.Key, out _);
            }
        }
    }
}
