namespace Catga.Idempotency;

/// <summary>
/// Store for tracking processed messages (idempotency) - 100% AOT compatible
/// </summary>
public interface IIdempotencyStore
{
    /// <summary>
    /// Check if message has already been processed
    /// </summary>
    Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as processed with result (generic)
    /// </summary>
    Task MarkAsProcessedAsync<TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cached result for previously processed message (generic)
    /// </summary>
    Task<TResult?> GetCachedResultAsync<TResult>(string messageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory idempotency store implementation (100% AOT compatible)
/// </summary>
public class MemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<string, (DateTime ProcessedAt, Type? ResultType, string? ResultJson)> _processedMessages = new();
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

    public async Task MarkAsProcessedAsync<TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            CleanupOldEntries();

            string? resultJson = null;
            Type? resultType = null;

            if (result != null)
            {
                resultType = typeof(TResult);
                resultJson = System.Text.Json.JsonSerializer.Serialize(result);
            }

            _processedMessages[messageId] = (DateTime.UtcNow, resultType, resultJson);
        }
        finally
        {
            _lock.Release();
        }
    }

    public async Task<TResult?> GetCachedResultAsync<TResult>(string messageId, CancellationToken cancellationToken = default)
    {
        await _lock.WaitAsync(cancellationToken);
        try
        {
            if (_processedMessages.TryGetValue(messageId, out var entry))
            {
                if (entry.ResultJson != null && entry.ResultType == typeof(TResult))
                {
                    return System.Text.Json.JsonSerializer.Deserialize<TResult>(entry.ResultJson);
                }
            }
            return default;
        }
        finally
        {
            _lock.Release();
        }
    }

    private void CleanupOldEntries()
    {
        var cutoff = DateTime.UtcNow - _retentionPeriod;
        var expiredKeys = _processedMessages
            .Where(x => x.Value.ProcessedAt < cutoff)
            .Select(x => x.Key)
            .ToList();

        foreach (var key in expiredKeys)
        {
            _processedMessages.Remove(key);
        }
    }
}
