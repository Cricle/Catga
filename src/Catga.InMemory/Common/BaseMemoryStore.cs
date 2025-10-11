using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>
/// Base class for all in-memory stores (Inbox, Outbox, Event, Idempotency)
/// Provides common operations and reduces code duplication (DRY principle)
/// 完全无锁实现，依赖 ConcurrentDictionary 的原子操作
/// </summary>
/// <typeparam name="TMessage">The message type stored</typeparam>
public abstract class BaseMemoryStore<TMessage> where TMessage : class
{
    protected readonly ConcurrentDictionary<string, TMessage> Messages = new();

    /// <summary>
    /// Get total message count (无锁，原子读取)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetMessageCount() => Messages.Count;

    /// <summary>
    /// Get message count by predicate (无锁遍历)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int GetCountByPredicate(Func<TMessage, bool> predicate)
    {
        int count = 0;
        foreach (var kvp in Messages)
        {
            if (predicate(kvp.Value))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Get messages by predicate with limit (无锁遍历)
    /// </summary>
    protected List<TMessage> GetMessagesByPredicate(
        Func<TMessage, bool> predicate,
        int maxCount,
        IComparer<TMessage>? comparer = null)
    {
        var result = new List<TMessage>(maxCount);

        // 无锁遍历
        foreach (var kvp in Messages)
        {
            if (predicate(kvp.Value))
            {
                result.Add(kvp.Value);

                if (result.Count >= maxCount)
                    break;
            }
        }

        // Sort if comparer provided
        if (comparer != null && result.Count > 1)
        {
            result.Sort(comparer);
        }

        return result;
    }

    /// <summary>
    /// Delete expired messages from store (无锁删除)
    /// </summary>
    protected Task DeleteExpiredMessagesAsync(
        TimeSpan retentionPeriod,
        Func<TMessage, bool> shouldDelete,
        CancellationToken cancellationToken = default)
    {
        // 无锁实现：LINQ 查询 + TryRemove
        var keysToRemove = Messages
            .Where(kvp => shouldDelete(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        // ConcurrentDictionary.TryRemove 是原子操作，无需锁
        foreach (var key in keysToRemove)
        {
            Messages.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    /// <summary>
    /// Try get message by ID (无锁，原子读取)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryGetMessage(string messageId, out TMessage? message)
    {
        return Messages.TryGetValue(messageId, out message);
    }

    /// <summary>
    /// Add or update message (无锁，原子操作)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void AddOrUpdateMessage(string messageId, TMessage message)
    {
        Messages[messageId] = message;
    }

    /// <summary>
    /// Remove message by ID (无锁，原子操作)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryRemoveMessage(string messageId, out TMessage? message)
    {
        return Messages.TryRemove(messageId, out message);
    }

    /// <summary>
    /// Clear all messages (for testing)
    /// </summary>
    public virtual void Clear()
    {
        Messages.Clear();
    }
}
