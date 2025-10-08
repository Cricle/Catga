using System.Runtime.CompilerServices;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

namespace Catga.Performance;

/// <summary>
/// Zero-allocation fast paths for common scenarios
/// Used when no pipeline behaviors are needed
/// </summary>
internal static class FastPath
{
    /// <summary>
    /// Fast path for request without pipeline (zero allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteRequestDirectAsync<TRequest, TResponse>(
        IRequestHandler<TRequest, TResponse> handler,
        TRequest request,
        CancellationToken cancellationToken)
        where TRequest : IRequest<TResponse>
    {
        try
        {
            return await handler.HandleAsync(request, cancellationToken).ConfigureAwait(false);
        }
        catch (Catga.Exceptions.CatgaException ex)
        {
            return CatgaResult<TResponse>.Failure($"Handler failed: {ex.Message}", ex);
        }
        catch (Exception ex)
        {
            return CatgaResult<TResponse>.Failure($"Handler failed: {ex.Message}");
        }
    }

    /// <summary>
    /// Fast path for synchronously completed requests (zero allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<CatgaResult<TResponse>> ExecuteRequestSync<TResponse>(
        CatgaResult<TResponse> result) =>
        ValueTask.FromResult(result);

    /// <summary>
    /// Fast path for events without handlers (no-op, zero allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask PublishEventNoOpAsync() =>
        ValueTask.CompletedTask;

    /// <summary>
    /// Fast path for single event handler (reduced allocation)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask PublishEventSingleAsync<TEvent>(
        IEventHandler<TEvent> handler,
        TEvent @event,
        CancellationToken cancellationToken)
        where TEvent : IEvent
    {
        try
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            // Event handlers don't propagate exceptions
            // Log would happen in caller
        }
    }

    /// <summary>
    /// Check if request can use fast path (no behaviors)
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanUseFastPath(int behaviorCount) => behaviorCount == 0;
}

/// <summary>
/// Fast path configuration options
/// </summary>
public sealed class FastPathOptions
{
    /// <summary>
    /// Enable fast path optimization (default: true)
    /// </summary>
    public bool EnableFastPath { get; set; } = true;

    /// <summary>
    /// Enable handler caching (default: true)
    /// </summary>
    public bool EnableHandlerCaching { get; set; } = true;

    /// <summary>
    /// Enable context pooling (default: true)
    /// </summary>
    public bool EnableContextPooling { get; set; } = true;

    /// <summary>
    /// Maximum pool size for contexts (default: 1024)
    /// </summary>
    public int MaxPoolSize { get; set; } = 1024;
}

