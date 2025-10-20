using System.Runtime.CompilerServices;
using Microsoft.Extensions.DependencyInjection;

namespace Catga.Handlers;

/// <summary>
/// Minimal handler resolver - delegates to DI container directly.
/// No caching to avoid over-optimization and respect DI lifecycle.
/// </summary>
public sealed class HandlerCache
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public THandler GetRequestHandler<THandler>(IServiceProvider scopedProvider) where THandler : class
        => scopedProvider.GetRequiredService<THandler>();

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public IReadOnlyList<THandler> GetEventHandlers<THandler>(IServiceProvider scopedProvider) where THandler : class
    {
        var handlers = scopedProvider.GetServices<THandler>();
        // Avoid ToArray() if already IReadOnlyList
        return handlers as IReadOnlyList<THandler> ?? handlers.ToArray();
    }
}
