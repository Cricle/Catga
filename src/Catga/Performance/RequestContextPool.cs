using System.Collections.Concurrent;
using Catga.Messages;

namespace Catga.Performance;

/// <summary>
/// Object pool for request context to reduce allocations
/// Thread-safe pool using ConcurrentBag for lock-free operations
/// </summary>
/// <typeparam name="TContext">The context type to pool</typeparam>
internal sealed class RequestContextPool<TContext> where TContext : class, new()
{
    private readonly ConcurrentBag<TContext> _pool = new();
    private readonly int _maxSize;

    public RequestContextPool(int maxSize = 1024)
    {
        _maxSize = maxSize;
    }

    /// <summary>
    /// Rent a context from pool (or create new if empty)
    /// </summary>
    public TContext Rent()
    {
        if (_pool.TryTake(out var context))
            return context;

        return new TContext();
    }

    /// <summary>
    /// Return context to pool (if under capacity)
    /// </summary>
    public void Return(TContext context)
    {
        // Simple size limit check (not exact due to concurrency, but good enough)
        if (_pool.Count < _maxSize)
        {
            // Reset context if it has a reset method
            if (context is IResettable resettable)
                resettable.Reset();

            _pool.Add(context);
        }
    }
}

/// <summary>
/// Interface for resettable pooled objects
/// </summary>
internal interface IResettable
{
    void Reset();
}

/// <summary>
/// Pooled pipeline context to reduce allocations
/// </summary>
internal sealed class PipelineContext : IResettable
{
    public IMessage? Request { get; set; }
    public CancellationToken CancellationToken { get; set; }
    public Dictionary<string, object>? Metadata { get; set; }

    public void Reset()
    {
        Request = null;
        CancellationToken = default;
        Metadata?.Clear();
    }
}

/// <summary>
/// Global static pools for common contexts
/// </summary>
internal static class ContextPools
{
    private static readonly ConcurrentDictionary<Type, object> _pools = new();

    /// <summary>
    /// Get or create pool for a specific type
    /// </summary>
    public static RequestContextPool<TContext> GetPool<TContext>() where TContext : class, new()
    {
        var poolType = typeof(TContext);

        if (_pools.TryGetValue(poolType, out var existingPool))
            return (RequestContextPool<TContext>)existingPool;

        var newPool = new RequestContextPool<TContext>();
        _pools[poolType] = newPool;
        return newPool;
    }
}

