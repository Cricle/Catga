using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>Helper for expired entry cleanup (DRY for idempotency stores)</summary>
internal static class ExpirationHelper
{
    /// <summary>Check if entry is expired based on retention period</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool IsExpired(DateTime timestamp, TimeSpan retentionPeriod)
        => DateTime.UtcNow - timestamp > retentionPeriod;

    /// <summary>Remove expired entries from dictionary</summary>
    public static void CleanupExpired<TValue>(
        ConcurrentDictionary<string, TValue> dictionary,
        Func<TValue, DateTime> timestampSelector,
        TimeSpan retentionPeriod)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        var expiredKeys = dictionary
            .Where(kvp => timestampSelector(kvp.Value) < cutoff)
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in expiredKeys)
            dictionary.TryRemove(key, out _);
    }
}

/// <summary>Base class for in-memory stores (lock-free)</summary>
public abstract class BaseMemoryStore<TMessage> where TMessage : class
{
    protected readonly ConcurrentDictionary<string, TMessage> Messages = new();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetMessageCount() => Messages.Count;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int GetCountByPredicate(Func<TMessage, bool> predicate)
    {
        int count = 0;
        foreach (var kvp in Messages)
        {
            if (predicate(kvp.Value)) count++;
        }
        return count;
    }

    protected List<TMessage> GetMessagesByPredicate(Func<TMessage, bool> predicate, int maxCount, IComparer<TMessage>? comparer = null)
    {
        var result = new List<TMessage>(maxCount);
        foreach (var kvp in Messages)
        {
            if (predicate(kvp.Value))
            {
                result.Add(kvp.Value);
                if (result.Count >= maxCount) break;
            }
        }
        if (comparer != null && result.Count > 1) result.Sort(comparer);
        return result;
    }

    protected Task DeleteExpiredMessagesAsync(TimeSpan retentionPeriod, Func<TMessage, bool> shouldDelete, CancellationToken cancellationToken = default)
    {
        var keysToRemove = Messages.Where(kvp => shouldDelete(kvp.Value)).Select(kvp => kvp.Key).ToList();
        foreach (var key in keysToRemove)
            Messages.TryRemove(key, out _);
        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryGetMessage(string messageId, out TMessage? message) => Messages.TryGetValue(messageId, out message);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void AddOrUpdateMessage(string messageId, TMessage message) => Messages[messageId] = message;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryRemoveMessage(string messageId, out TMessage? message) => Messages.TryRemove(messageId, out message);

    /// <summary>
    /// Execute action on message if exists (DRY helper for common pattern)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected Task ExecuteIfExistsAsync(string messageId, Action<TMessage> action)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
            action(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get value from message if exists (DRY helper for common pattern)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected Task<TResult?> GetValueIfExistsAsync<TResult>(string messageId, Func<TMessage, TResult?> selector)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
            return Task.FromResult(selector(message));
        return Task.FromResult<TResult?>(default);
    }

    public virtual void Clear() => Messages.Clear();
}

