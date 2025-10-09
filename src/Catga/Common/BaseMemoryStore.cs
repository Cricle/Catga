using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>
/// Base class for all in-memory stores (Inbox, Outbox, Event, Idempotency)
/// Provides common operations and reduces code duplication (DRY principle)
/// Thread-safe, zero-allocation design
/// </summary>
/// <typeparam name="TMessage">The message type stored</typeparam>
public abstract class BaseMemoryStore<TMessage> where TMessage : class
{
    protected readonly ConcurrentDictionary<string, TMessage> Messages = new();
    protected readonly SemaphoreSlim Lock = new(1, 1);

    /// <summary>
    /// Get total message count
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public int GetMessageCount() => Messages.Count;

    /// <summary>
    /// Get message count by predicate (zero-allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected int GetCountByPredicate(Func<TMessage, bool> predicate)
    {
        return MessageStoreHelper.GetMessageCountByPredicate(Messages, predicate);
    }

    /// <summary>
    /// Get messages by predicate with limit (zero-allocation iteration)
    /// </summary>
    protected List<TMessage> GetMessagesByPredicate(
        Func<TMessage, bool> predicate,
        int maxCount,
        IComparer<TMessage>? comparer = null)
    {
        return MessageStoreHelper.GetMessagesByPredicate(Messages, predicate, maxCount, comparer);
    }

    /// <summary>
    /// Delete expired messages from store
    /// </summary>
    protected Task DeleteExpiredMessagesAsync(
        TimeSpan retentionPeriod,
        Func<TMessage, bool> shouldDelete,
        CancellationToken cancellationToken = default)
    {
        return MessageStoreHelper.DeleteExpiredMessagesAsync(
            Messages,
            Lock,
            retentionPeriod,
            shouldDelete,
            cancellationToken);
    }

    /// <summary>
    /// Try get message by ID (thread-safe)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryGetMessage(string messageId, out TMessage? message)
    {
        return Messages.TryGetValue(messageId, out message);
    }

    /// <summary>
    /// Add or update message (thread-safe)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void AddOrUpdateMessage(string messageId, TMessage message)
    {
        Messages[messageId] = message;
    }

    /// <summary>
    /// Remove message by ID (thread-safe)
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

    /// <summary>
    /// Execute operation with lock (for complex operations)
    /// </summary>
    protected async Task<TResult> ExecuteWithLockAsync<TResult>(
        Func<Task<TResult>> operation,
        CancellationToken cancellationToken = default)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            return await operation();
        }
        finally
        {
            Lock.Release();
        }
    }

    /// <summary>
    /// Execute operation with lock (for simple operations)
    /// </summary>
    protected async Task ExecuteWithLockAsync(
        Func<Task> operation,
        CancellationToken cancellationToken = default)
    {
        await Lock.WaitAsync(cancellationToken);
        try
        {
            await operation();
        }
        finally
        {
            Lock.Release();
        }
    }
}

