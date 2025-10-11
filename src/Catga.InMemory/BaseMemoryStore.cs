using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>
/// Base class for all in-memory stores (Inbox, Outbox, Event, Idempotency)
/// Lock-free implementation using ConcurrentDictionary atomic operations
/// </summary>
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
            if (predicate(kvp.Value))
                count++;
        }
        return count;
    }

    protected List<TMessage> GetMessagesByPredicate(
        Func<TMessage, bool> predicate,
        int maxCount,
        IComparer<TMessage>? comparer = null)
    {
        var result = new List<TMessage>(maxCount);

        foreach (var kvp in Messages)
        {
            if (predicate(kvp.Value))
            {
                result.Add(kvp.Value);

                if (result.Count >= maxCount)
                    break;
            }
        }

        if (comparer != null && result.Count > 1)
        {
            result.Sort(comparer);
        }

        return result;
    }

    protected Task DeleteExpiredMessagesAsync(
        TimeSpan retentionPeriod,
        Func<TMessage, bool> shouldDelete,
        CancellationToken cancellationToken = default)
    {
        var keysToRemove = Messages
            .Where(kvp => shouldDelete(kvp.Value))
            .Select(kvp => kvp.Key)
            .ToList();

        foreach (var key in keysToRemove)
        {
            Messages.TryRemove(key, out _);
        }

        return Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryGetMessage(string messageId, out TMessage? message)
    {
        return Messages.TryGetValue(messageId, out message);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void AddOrUpdateMessage(string messageId, TMessage message)
    {
        Messages[messageId] = message;
    }

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
