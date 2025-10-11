using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Performance;

/// <summary>
/// High-performance handler cache to avoid repeated DI lookups
/// P3 Optimization: 3-tier cache (ThreadLocal -> Shared -> Global)
/// - L1 (ThreadLocal): Per-thread cache for hot paths (zero contention)
/// - L2 (Shared): ConcurrentDictionary for cross-thread sharing
/// - L3 (Global): IServiceProvider for first-time resolution
/// </summary>
public sealed class HandlerCache
{
    private readonly IServiceProvider _serviceProvider;

    // L2: Shared cache (ConcurrentDictionary)
    private readonly ConcurrentDictionary<Type, Delegate> _handlerFactories = new();
    private readonly ConcurrentDictionary<Type, Delegate> _eventHandlerFactories = new();

    // P2-3: Cache statistics
    private long _threadLocalHits;
    private long _sharedCacheHits;
    private long _serviceProviderCalls;

    // L1: Thread-local cache (zero contention)
    [ThreadStatic]
    private static Dictionary<Type, Delegate>? _threadLocalHandlerCache;

    [ThreadStatic]
    private static Dictionary<Type, Delegate>? _threadLocalEventHandlerCache;

    public HandlerCache(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    // P1-3 Optimization: Increase initial capacity to reduce rehashing
    private const int InitialCacheCapacity = 32;

    /// <summary>
    /// Get or create thread-local handler cache
    /// </summary>
    private static Dictionary<Type, Delegate> GetThreadLocalHandlerCache()
    {
        return _threadLocalHandlerCache ??= new Dictionary<Type, Delegate>(capacity: InitialCacheCapacity);
    }

    /// <summary>
    /// Get or create thread-local event handler cache
    /// </summary>
    private static Dictionary<Type, Delegate> GetThreadLocalEventHandlerCache()
    {
        return _threadLocalEventHandlerCache ??= new Dictionary<Type, Delegate>(capacity: InitialCacheCapacity);
    }

    /// <summary>
    /// Get request handler (cached factory, respects scoped lifetime)
    /// P3: 3-tier cache lookup (ThreadLocal -> Shared -> Global)
    /// </summary>
    public THandler GetRequestHandler<THandler>(IServiceProvider scopedProvider)
        where THandler : class
    {
        var handlerType = typeof(THandler);

        // P3 L1: Try thread-local cache first (fastest, zero contention)
        var threadCache = GetThreadLocalHandlerCache();
        if (threadCache.TryGetValue(handlerType, out var cachedFactory))
        {
            // P2-3: Track L1 hit
            Interlocked.Increment(ref _threadLocalHits);
            return ((Func<IServiceProvider, THandler>)cachedFactory)(scopedProvider);
        }

        // P3 L2: Try shared cache (ConcurrentDictionary)
        var wasAdded = false;
        var factory = _handlerFactories.GetOrAdd(handlerType, _ =>
        {
            wasAdded = true;
            // P2-3: Track service provider call (cache miss)
            Interlocked.Increment(ref _serviceProviderCalls);
            return CreateHandlerFactory<THandler>();
        });

        if (!wasAdded)
        {
            // P2-3: Track L2 hit
            Interlocked.Increment(ref _sharedCacheHits);
        }

        // P3: Cache in thread-local for future hits
        threadCache[handlerType] = factory;

        return ((Func<IServiceProvider, THandler>)factory)(scopedProvider);
    }

    /// <summary>
    /// Get event handlers (cached factory, supports multiple handlers)
    /// P3: 3-tier cache lookup (ThreadLocal -> Shared -> Global)
    /// </summary>
    public IReadOnlyList<THandler> GetEventHandlers<THandler>(IServiceProvider scopedProvider)
        where THandler : class
    {
        var handlerType = typeof(THandler);

        // P3 L1: Try thread-local cache first (fastest, zero contention)
        var threadCache = GetThreadLocalEventHandlerCache();
        if (threadCache.TryGetValue(handlerType, out var cachedFactory))
        {
            // P2-3: Track L1 hit
            Interlocked.Increment(ref _threadLocalHits);
            return ((Func<IServiceProvider, IReadOnlyList<THandler>>)cachedFactory)(scopedProvider);
        }

        // P3 L2: Try shared cache (ConcurrentDictionary)
        var wasAdded = false;
        var factory = _eventHandlerFactories.GetOrAdd(handlerType, _ =>
        {
            wasAdded = true;
            // P2-3: Track service provider call (cache miss)
            Interlocked.Increment(ref _serviceProviderCalls);
            return CreateEventHandlerFactory<THandler>();
        });

        if (!wasAdded)
        {
            // P2-3: Track L2 hit
            Interlocked.Increment(ref _sharedCacheHits);
        }

        // P3: Cache in thread-local for future hits
        threadCache[handlerType] = factory;

        return ((Func<IServiceProvider, IReadOnlyList<THandler>>)factory)(scopedProvider);
    }

    /// <summary>
    /// Create handler factory (caches the lookup logic)
    /// P3: Aggressive inlining for hot path
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<IServiceProvider, THandler> CreateHandlerFactory<THandler>()
        where THandler : class
    {
        return provider => provider.GetService<THandler>()!;
    }

    /// <summary>
    /// Create event handler factory (caches enumeration logic)
    /// P3: Aggressive inlining for hot path
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<IServiceProvider, IReadOnlyList<THandler>> CreateEventHandlerFactory<THandler>()
        where THandler : class
    {
        return provider =>
        {
            var handlers = provider.GetServices<THandler>();
            // Convert to array for better performance (avoids LINQ)
            if (handlers is IReadOnlyList<THandler> list)
                return list;

            return handlers.ToArray();
        };
    }

    /// <summary>
    /// Clear cache (for testing or dynamic registration scenarios)
    /// </summary>
    public void Clear()
    {
        _handlerFactories.Clear();
        _eventHandlerFactories.Clear();
    }

    // P2-3: Statistics API
    /// <summary>
    /// Get cache statistics
    /// </summary>
    public HandlerCacheStatistics GetStatistics()
    {
        var threadLocalHits = Interlocked.Read(ref _threadLocalHits);
        var sharedCacheHits = Interlocked.Read(ref _sharedCacheHits);
        var serviceProviderCalls = Interlocked.Read(ref _serviceProviderCalls);
        var totalRequests = threadLocalHits + sharedCacheHits + serviceProviderCalls;

        return new HandlerCacheStatistics
        {
            ThreadLocalHits = threadLocalHits,
            SharedCacheHits = sharedCacheHits,
            ServiceProviderCalls = serviceProviderCalls,
            TotalRequests = totalRequests,
            HitRate = totalRequests > 0 ? (threadLocalHits + sharedCacheHits) / (double)totalRequests : 0.0
        };
    }
}

/// <summary>
/// Handler cache statistics for monitoring
/// </summary>
public sealed class HandlerCacheStatistics
{
    /// <summary>
    /// Number of cache hits from thread-local cache (L1)
    /// </summary>
    public long ThreadLocalHits { get; init; }

    /// <summary>
    /// Number of cache hits from shared cache (L2)
    /// </summary>
    public long SharedCacheHits { get; init; }

    /// <summary>
    /// Number of cache misses requiring service provider calls (L3)
    /// </summary>
    public long ServiceProviderCalls { get; init; }

    /// <summary>
    /// Total number of handler requests
    /// </summary>
    public long TotalRequests { get; init; }

    /// <summary>
    /// Overall cache hit rate (0.0 to 1.0)
    /// </summary>
    public double HitRate { get; init; }
}

