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
    Task<bool> HasBeenProcessedAsync(string messageId, CancellationToken cancellationToken = default);

    /// <summary>
    /// Mark message as processed with result (generic)
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
    Task MarkAsProcessedAsync<TResult>(string messageId, TResult? result = default, CancellationToken cancellationToken = default);

    /// <summary>
    /// Get cached result for previously processed message (generic)
    /// </summary>
    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
    Task<TResult?> GetCachedResultAsync<TResult>(string messageId, CancellationToken cancellationToken = default);
}

/// <summary>
/// In-memory idempotency store implementation
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

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
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

    [RequiresUnreferencedCode("JSON serialization may require types that cannot be statically analyzed.")]
    [RequiresDynamicCode("JSON serialization may require dynamic code generation.")]
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
