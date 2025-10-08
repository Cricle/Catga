using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>
/// Common helper methods for message stores (Inbox/Outbox)
/// Reduces code duplication in MemoryInboxStore and MemoryOutboxStore
/// </summary>
public static class MessageStoreHelper
{
    /// <summary>
    /// Delete expired messages from concurrent dictionary
    /// Zero-allocation implementation
    /// </summary>
    public static async Task DeleteExpiredMessagesAsync<TMessage>(
        ConcurrentDictionary<string, TMessage> messages,
        SemaphoreSlim lockObj,
        TimeSpan retentionPeriod,
        Func<TMessage, bool> shouldDelete,
        CancellationToken cancellationToken = default)
    {
        await lockObj.WaitAsync(cancellationToken);
        try
        {
            var cutoff = DateTime.UtcNow - retentionPeriod;
            List<string>? keysToRemove = null;

            // Zero-allocation traversal
            foreach (var kvp in messages)
            {
                if (shouldDelete(kvp.Value))
                {
                    keysToRemove ??= new List<string>();
                    keysToRemove.Add(kvp.Key);
                }
            }

            // Delete expired messages
            if (keysToRemove != null)
            {
                foreach (var key in keysToRemove)
                {
                    messages.TryRemove(key, out _);
                }
            }
        }
        finally
        {
            lockObj.Release();
        }
    }

    /// <summary>
    /// Get message count by predicate (zero-allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int GetMessageCountByPredicate<TMessage>(
        ConcurrentDictionary<string, TMessage> messages,
        Func<TMessage, bool> predicate)
    {
        int count = 0;
        foreach (var kvp in messages)
        {
            if (predicate(kvp.Value))
                count++;
        }
        return count;
    }

    /// <summary>
    /// Get messages by predicate with limit (zero-allocation iteration)
    /// </summary>
    public static List<TMessage> GetMessagesByPredicate<TMessage>(
        ConcurrentDictionary<string, TMessage> messages,
        Func<TMessage, bool> predicate,
        int maxCount,
        IComparer<TMessage>? comparer = null)
    {
        var result = new List<TMessage>(maxCount);

        // Zero-allocation iteration
        foreach (var kvp in messages)
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
}
