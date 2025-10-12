using System.Diagnostics.CodeAnalysis;

namespace Catga.Idempotency;

/// <summary>
/// Store for tracking processed messages (idempotency)
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Check if message has already been processed
    /// </summary>
    public Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as processed with result (generic)
    /// </summary>
    public Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cached result for previously processed message (generic)
    /// </summary>
    public Task<TResult?> GetCachedResultAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory idempotency store implementation
/// </summary>
public class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<string, (DateTime ProcessedAt, Type? ResultType, string? ResultJson)> _processedMessages = new();
    private readonly Dictionary<string, Dictionary<Type, string>> _typedResults = new();
    private readonly TimeSpan _retentionPeriod = TimeSpan.FromHours(24);
    private readonly SemaphoreSlim _lock = new(1, 1);

    public async Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            CleanupOldEntries();
            return _processedMessages.ContainsKey(messageId);
        }
        finally
        {
            _lock.Release();
        }
    }
    public async Task MarkAsProcessedAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            CleanupOldEntries();
            _processedMessages[messageId] = (DateTime.UtcNow, null, null);
            
            if (result != null)
            {
                var resultJson = System.Text.Json.JsonSerializer.Serialize(result);
                if (!_typedResults.TryGetValue(messageId, out var dict))
                {
                    dict = new Dictionary<Type, string>();
                    _typedResults[messageId] = dict;
                }
                dict[typeof(TResult)] = resultJson;
            }
        }
        finally
        {
            _lock.Release();
        }
    }
    public async Task<TResult?> GetCachedResultAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResult>(string messageId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_typedResults.TryGetValue(messageId, out var dict) && dict.TryGetValue(typeof(TResult), out var resultJson))
                return System.Text.Json.JsonSerializer.Deserialize<TResult>(resultJson);
            return default;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void CleanupOldEntries()
    {
        // Zero-allocation cleanup: avoid LINQ, iterate directly
        var cutoff = DateTime.UtcNow - _retentionPeriod;

        // Use List to avoid "Collection was modified" exception
        List<string>? expiredKeys = null;

        foreach (var kvp in _processedMessages)
        {
            if (kvp.Value.ProcessedAt < cutoff)
            {
                expiredKeys ??= new List<string>();
                expiredKeys.Add(kvp.Key);
            }
        }

        if (expiredKeys != null)
        {
            foreach (var key in expiredKeys)
            {
                _processedMessages.Remove(key);
            }
        }
    }
}
