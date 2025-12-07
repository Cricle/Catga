using System.Collections.Concurrent;
using Catga.EventSourcing;

namespace Catga.Persistence.InMemory.Stores;

/// <summary>
/// In-memory audit log store for development/testing.
/// </summary>
public sealed class InMemoryAuditLogStore : IAuditLogStore
{
    private readonly ConcurrentBag<AuditLogEntry> _entries = new();

    public ValueTask LogAsync(AuditLogEntry entry, CancellationToken ct = default)
    {
        _entries.Add(entry);
        return ValueTask.CompletedTask;
    }

    public ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsAsync(string streamId, CancellationToken ct = default)
    {
        var result = _entries.Where(e => e.StreamId == streamId).OrderBy(e => e.Timestamp).ToList();
        return ValueTask.FromResult<IReadOnlyList<AuditLogEntry>>(result);
    }

    public ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByTimeRangeAsync(DateTime from, DateTime to, CancellationToken ct = default)
    {
        var result = _entries.Where(e => e.Timestamp >= from && e.Timestamp <= to).OrderBy(e => e.Timestamp).ToList();
        return ValueTask.FromResult<IReadOnlyList<AuditLogEntry>>(result);
    }

    public ValueTask<IReadOnlyList<AuditLogEntry>> GetLogsByUserAsync(string userId, CancellationToken ct = default)
    {
        var result = _entries.Where(e => e.UserId == userId).OrderBy(e => e.Timestamp).ToList();
        return ValueTask.FromResult<IReadOnlyList<AuditLogEntry>>(result);
    }

    public void Clear() => _entries.Clear();
}

/// <summary>
/// In-memory GDPR store for development/testing.
/// </summary>
public sealed class InMemoryGdprStore : IGdprStore
{
    private readonly ConcurrentDictionary<string, ErasureRequest> _requests = new();

    public ValueTask SaveRequestAsync(ErasureRequest request, CancellationToken ct = default)
    {
        _requests[request.SubjectId] = request;
        return ValueTask.CompletedTask;
    }

    public ValueTask<ErasureRequest?> GetErasureRequestAsync(string subjectId, CancellationToken ct = default)
    {
        return ValueTask.FromResult(_requests.TryGetValue(subjectId, out var request) ? request : null);
    }

    public ValueTask<IReadOnlyList<ErasureRequest>> GetPendingRequestsAsync(CancellationToken ct = default)
    {
        var result = _requests.Values.Where(r => r.Status == ErasureStatus.Pending).ToList();
        return ValueTask.FromResult<IReadOnlyList<ErasureRequest>>(result);
    }

    public void Clear() => _requests.Clear();
}

/// <summary>
/// In-memory encryption key store for development/testing.
/// </summary>
public sealed class InMemoryEncryptionKeyStore : IEncryptionKeyStore
{
    private readonly ConcurrentDictionary<string, byte[]> _keys = new();

    public ValueTask SaveKeyAsync(string subjectId, byte[] key, CancellationToken ct = default)
    {
        _keys[subjectId] = key;
        return ValueTask.CompletedTask;
    }

    public ValueTask<byte[]?> GetKeyAsync(string subjectId, CancellationToken ct = default)
    {
        return ValueTask.FromResult(_keys.TryGetValue(subjectId, out var key) ? key : null);
    }

    public ValueTask DeleteKeyAsync(string subjectId, CancellationToken ct = default)
    {
        _keys.TryRemove(subjectId, out _);
        return ValueTask.CompletedTask;
    }

    public void Clear() => _keys.Clear();
}
