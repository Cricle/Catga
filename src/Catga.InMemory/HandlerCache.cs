using System.Collections.Concurrent;
using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Performance;

/// <summary>3-tier handler cache (ThreadLocal → Shared → Global)</summary>
public sealed class HandlerCache {
    private const int InitialCacheCapacity = 32;
    private readonly IServiceProvider _serviceProvider;
    private readonly ConcurrentDictionary<Type, Delegate> _handlerFactories = new();
    private readonly ConcurrentDictionary<Type, Delegate> _eventHandlerFactories = new();
    private long _threadLocalHits;
    private long _sharedCacheHits;
    private long _serviceProviderCalls;

    [ThreadStatic] private static Dictionary<Type, Delegate>? _threadLocalHandlerCache;
    [ThreadStatic] private static Dictionary<Type, Delegate>? _threadLocalEventHandlerCache;

    public HandlerCache(IServiceProvider serviceProvider) => _serviceProvider = serviceProvider;

    private static Dictionary<Type, Delegate> GetThreadLocalHandlerCache()
        => _threadLocalHandlerCache ??= new Dictionary<Type, Delegate>(capacity: InitialCacheCapacity);

    private static Dictionary<Type, Delegate> GetThreadLocalEventHandlerCache()
        => _threadLocalEventHandlerCache ??= new Dictionary<Type, Delegate>(capacity: InitialCacheCapacity);

    public THandler GetRequestHandler<THandler>(IServiceProvider scopedProvider) where THandler : class {
        var handlerType = typeof(THandler);
        var threadCache = GetThreadLocalHandlerCache();
        if (threadCache.TryGetValue(handlerType, out var cachedFactory)) {
            Interlocked.Increment(ref _threadLocalHits);
            return ((Func<IServiceProvider, THandler>)cachedFactory)(scopedProvider);
        }

        var wasAdded = false;
        var factory = _handlerFactories.GetOrAdd(handlerType, _ => {
            wasAdded = true;
            Interlocked.Increment(ref _serviceProviderCalls);
            return CreateHandlerFactory<THandler>();
        });

        if (!wasAdded) Interlocked.Increment(ref _sharedCacheHits);
        threadCache[handlerType] = factory;
        return ((Func<IServiceProvider, THandler>)factory)(scopedProvider);
    }

    public IReadOnlyList<THandler> GetEventHandlers<THandler>(IServiceProvider scopedProvider) where THandler : class {
        var handlerType = typeof(THandler);
        var threadCache = GetThreadLocalEventHandlerCache();
        if (threadCache.TryGetValue(handlerType, out var cachedFactory)) {
            Interlocked.Increment(ref _threadLocalHits);
            return ((Func<IServiceProvider, IReadOnlyList<THandler>>)cachedFactory)(scopedProvider);
        }

        var wasAdded = false;
        var factory = _eventHandlerFactories.GetOrAdd(handlerType, _ => {
            wasAdded = true;
            Interlocked.Increment(ref _serviceProviderCalls);
            return CreateEventHandlerFactory<THandler>();
        });

        if (!wasAdded) Interlocked.Increment(ref _sharedCacheHits);
        threadCache[handlerType] = factory;
        return ((Func<IServiceProvider, IReadOnlyList<THandler>>)factory)(scopedProvider);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<IServiceProvider, THandler> CreateHandlerFactory<THandler>() where THandler : class
        => provider => provider.GetService<THandler>()!;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Func<IServiceProvider, IReadOnlyList<THandler>> CreateEventHandlerFactory<THandler>() where THandler : class
        => provider => {
            var handlers = provider.GetServices<THandler>();
            if (handlers is IReadOnlyList<THandler> list) return list;
            return handlers.ToArray();
        };

    public void Clear() {
        _handlerFactories.Clear();
        _eventHandlerFactories.Clear();
    }

    public HandlerCacheStatistics GetStatistics() {
        var threadLocalHits = Interlocked.Read(ref _threadLocalHits);
        var sharedCacheHits = Interlocked.Read(ref _sharedCacheHits);
        var serviceProviderCalls = Interlocked.Read(ref _serviceProviderCalls);
        var totalRequests = threadLocalHits + sharedCacheHits + serviceProviderCalls;
        return new HandlerCacheStatistics {
            ThreadLocalHits = threadLocalHits,
            SharedCacheHits = sharedCacheHits,
            ServiceProviderCalls = serviceProviderCalls,
            TotalRequests = totalRequests,
            HitRate = totalRequests > 0 ? (threadLocalHits + sharedCacheHits) / (double)totalRequests : 0.0
        };
    }
}

/// <summary>Handler cache statistics</summary>
public sealed class HandlerCacheStatistics {
    public long ThreadLocalHits { get; init; }
    public long SharedCacheHits { get; init; }
    public long ServiceProviderCalls { get; init; }
    public long TotalRequests { get; init; }
    public double HitRate { get; init; }
}

