using System.Collections.Concurrent;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Performance;

/// <summary>
/// High-performance handler cache to avoid repeated DI lookups
/// Thread-safe, lock-free cache with lazy initialization
/// </summary>
internal sealed class HandlerCache
{
    private readonly IServiceProvider _serviceProvider;

    // Cache handler factories instead of instances (respects DI lifetime)
    private readonly ConcurrentDictionary<Type, Delegate> _handlerFactories = new();

    // Cache for multiple event handlers
    private readonly ConcurrentDictionary<Type, Delegate> _eventHandlerFactories = new();

    public HandlerCache(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
    }

    /// <summary>
    /// Get request handler (cached factory, respects scoped lifetime)
    /// </summary>
    public THandler GetRequestHandler<THandler>(IServiceProvider scopedProvider)
        where THandler : class
    {
        var handlerType = typeof(THandler);

        // Fast path: check cache first
        if (_handlerFactories.TryGetValue(handlerType, out var factory))
        {
            return ((Func<IServiceProvider, THandler>)factory)(scopedProvider);
        }

        // Slow path: create and cache factory
        var newFactory = CreateHandlerFactory<THandler>();
        _handlerFactories[handlerType] = newFactory;
        return newFactory(scopedProvider);
    }

    /// <summary>
    /// Get event handlers (cached factory, supports multiple handlers)
    /// </summary>
    public IReadOnlyList<THandler> GetEventHandlers<THandler>(IServiceProvider scopedProvider)
        where THandler : class
    {
        var handlerType = typeof(THandler);

        // Fast path: check cache first
        if (_eventHandlerFactories.TryGetValue(handlerType, out var factory))
        {
            return ((Func<IServiceProvider, IReadOnlyList<THandler>>)factory)(scopedProvider);
        }

        // Slow path: create and cache factory
        var newFactory = CreateEventHandlerFactory<THandler>();
        _eventHandlerFactories[handlerType] = newFactory;
        return newFactory(scopedProvider);
    }

    /// <summary>
    /// Create handler factory (caches the lookup logic)
    /// </summary>
    private static Func<IServiceProvider, THandler> CreateHandlerFactory<THandler>()
        where THandler : class
    {
        return provider => provider.GetRequiredService<THandler>();
    }

    /// <summary>
    /// Create event handler factory (caches enumeration logic)
    /// </summary>
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
}

