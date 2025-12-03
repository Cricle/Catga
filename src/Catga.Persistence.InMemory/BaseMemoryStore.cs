using System.Collections.Concurrent;
using System.Runtime.CompilerServices;

namespace Catga.Common;

/// <summary>Base class for in-memory stores.</summary>
public abstract class BaseMemoryStore<TMessage> where TMessage : class
{
    protected readonly ConcurrentDictionary<long, TMessage> Messages = new();

    public int GetMessageCount() => Messages.Count;

    protected int GetCountByPredicate(Func<TMessage, bool> predicate)
    {
        var count = 0;
        foreach (var (_, v) in Messages) if (predicate(v)) count++;
        return count;
    }

    protected List<TMessage> GetMessagesByPredicate(Func<TMessage, bool> predicate, int maxCount, IComparer<TMessage>? comparer = null)
    {
        var result = new List<TMessage>(maxCount);
        foreach (var (_, v) in Messages)
        {
            if (predicate(v)) { result.Add(v); if (result.Count >= maxCount) break; }
        }
        if (comparer != null && result.Count > 1) result.Sort(comparer);
        return result;
    }

    protected ValueTask DeleteExpiredMessagesAsync(TimeSpan retention, Func<TMessage, DateTime?> ts, Func<TMessage, bool> filter, CancellationToken ct = default)
    {
        var cutoff = DateTime.UtcNow - retention;
        foreach (var (k, v) in Messages)
            if (filter(v) && ts(v) is { } t && t < cutoff) Messages.TryRemove(k, out _);
        return ValueTask.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected bool TryGetMessage(long id, out TMessage? msg) => Messages.TryGetValue(id, out msg);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    protected void AddOrUpdateMessage(long id, TMessage msg) => Messages[id] = msg;

    protected void ExecuteIfExistsAsync(long id, Action<TMessage> action) { if (Messages.TryGetValue(id, out var m)) action(m); }

    public virtual void Clear() => Messages.Clear();
}

