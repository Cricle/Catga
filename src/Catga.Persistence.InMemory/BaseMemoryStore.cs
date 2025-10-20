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
        ConcurrentDictionary<long, TValue> dictionary,
        Func<TValue, DateTime> timestampSelector,
        TimeSpan retentionPeriod)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;
        
        // Optimize: Directly remove without materializing to list
        // ConcurrentDictionary supports modification during enumeration
        foreach (var kvp in dictionary)
        {
            if (timestampSelector(kvp.Value) < cutoff)
                dictionary.TryRemove(kvp.Key, out _);
        }
    }
}

/// <summary>Base class for in-memory stores (lock-free)</summary>
public abstract class BaseMemoryStore<TMessage> where TMessage : class
{
    protected readonly ConcurrentDictionary<long, TMessage> Messages = new();

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

    /// <summary>
    /// Delete messages by predicate (AOT-safe, thread-safe)
    /// </summary>
    /// <remarks>
    /// Uses ConcurrentDictionary for thread safety.
    /// AOT compatible - no reflection or dynamic code generation.
    /// </remarks>
    protected ValueTask DeleteMessagesByPredicateAsync(
        Func<TMessage, bool> predicate,
        CancellationToken cancellationToken = default)
    {
        // Optimize: Directly remove without materializing to list
        // ConcurrentDictionary supports modification during enumeration
        foreach (var kvp in Messages)
        {
            if (predicate(kvp.Value))
                Messages.TryRemove(kvp.Key, out _);
        }

        return ValueTask.CompletedTask;
    }

    /// <summary>
    /// Delete expired messages based on timestamp and status filter (DRY helper)
    /// </summary>
    /// <param name="retentionPeriod">Retention period for messages</param>
    /// <param name="timestampSelector">Function to extract timestamp from message</param>
    /// <param name="statusFilter">Function to filter messages by status</param>
    /// <param name="cancellationToken">Cancellation token</param>
    /// <returns>ValueTask representing the async operation</returns>
    /// <remarks>
    /// <para>
    /// DRY pattern for cleaning expired messages in Outbox/Inbox stores.
    /// Eliminates ~8 lines of duplicate code per store.
    /// </para>
    /// <para>
    /// AOT-safe: Uses delegates instead of reflection.
    /// Thread-safe: Uses ConcurrentDictionary.
    /// </para>
    /// <para>
    /// Usage:
    /// <code>
    /// // OutboxStore
    /// await DeleteExpiredMessagesAsync(
    ///     retentionPeriod,
    ///     m => m.PublishedAt,
    ///     m => m.Status == OutboxStatus.Published,
    ///     cancellationToken);
    /// </code>
    /// </para>
    /// </remarks>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected ValueTask DeleteExpiredMessagesAsync(
        TimeSpan retentionPeriod,
        Func<TMessage, DateTime?> timestampSelector,
        Func<TMessage, bool> statusFilter,
        CancellationToken cancellationToken = default)
    {
        var cutoff = DateTime.UtcNow - retentionPeriod;

        return DeleteMessagesByPredicateAsync(
            message => statusFilter(message) &&
                       timestampSelector(message) is DateTime timestamp &&
                       timestamp < cutoff,
            cancellationToken);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryGetMessage(long messageId, out TMessage? message) => Messages.TryGetValue(messageId, out message);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void AddOrUpdateMessage(long messageId, TMessage message) => Messages[messageId] = message;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryRemoveMessage(long messageId, out TMessage? message) => Messages.TryRemove(messageId, out message);

    /// <summary>
    /// Execute action on message if exists (DRY helper for common pattern)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected Task ExecuteIfExistsAsync(long messageId, Action<TMessage> action)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
            action(message);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Get value from message if exists (DRY helper for common pattern)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected Task<TResult?> GetValueIfExistsAsync<TResult>(long messageId, Func<TMessage, TResult?> selector)
    {
        if (TryGetMessage(messageId, out var message) && message != null)
            return Task.FromResult(selector(message));
        return Task.FromResult<TResult?>(default);
    }

    public virtual void Clear() => Messages.Clear();
}

