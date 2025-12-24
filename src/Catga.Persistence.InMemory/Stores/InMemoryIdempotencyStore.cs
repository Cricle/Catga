using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Idempotency;
using Catga.Observability;
using Microsoft.Extensions.Options;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>In-memory idempotency store for development/testing.</summary>
public sealed class InMemoryIdempotencyStore(IMessageSerializer serializer, IOptions<InMemoryPersistenceOptions>? options = null) : IIdempotencyStore
{
    private readonly ConcurrentDictionary<long, (DateTime At, byte[]? Data)> _store = new();
    private readonly TimeSpan _retention = options?.Value.IdempotencyRetention ?? TimeSpan.FromHours(24);

    public Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken ct = default)
    {
        var exists = _store.TryGetValue(messageId, out var e) && e.At > DateTime.UtcNow - _retention;
        (exists ? CatgaDiagnostics.IdempotencyHits : CatgaDiagnostics.IdempotencyMisses).Add(1);
        return Task.FromResult(exists);
    }

    public Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId, TResult? result = default, CancellationToken ct = default)
    {
        _store[messageId] = (DateTime.UtcNow, result is null ? null : serializer.Serialize(result));
        return Task.CompletedTask;
    }

    public Task<TResult?> GetCachedResultAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(
        long messageId, CancellationToken ct = default)
    {
        if (_store.TryGetValue(messageId, out var e) && e.Data is { } data)
        {
            CatgaDiagnostics.IdempotencyHits.Add(1);
            return Task.FromResult((TResult?)serializer.Deserialize(data, typeof(TResult)));
        }
        CatgaDiagnostics.IdempotencyMisses.Add(1);
        return Task.FromResult(default(TResult?));
    }
}
