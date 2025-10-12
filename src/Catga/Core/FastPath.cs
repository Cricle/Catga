using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Handlers;
using Catga.Messages;
using Catga.Results;

namespace Catga.Performance;

/// <summary>Zero-allocation fast paths (no pipeline behaviors)</summary>
public static class FastPath
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask<CatgaResult<TResponse>> ExecuteRequestDirectAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IRequestHandler<TRequest, TResponse> handler, TRequest request, CancellationToken cancellationToken) where TRequest : IRequest<TResponse>
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask<CatgaResult<TResponse>> ExecuteRequestSync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(CatgaResult<TResponse> result)
        => ValueTask.FromResult(result);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static ValueTask PublishEventNoOpAsync() => ValueTask.CompletedTask;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static async ValueTask PublishEventSingleAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IEventHandler<TEvent> handler, TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent
    {
        try
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool CanUseFastPath(int behaviorCount) => behaviorCount == 0;
}

/// <summary>Fast path configuration options</summary>
public sealed class FastPathOptions
{
    public bool EnableFastPath { get; set; } = true;
    public bool EnableHandlerCaching { get; set; } = true;
    public bool EnableContextPooling { get; set; } = true;
    public int MaxPoolSize { get; set; } = 1024;
}

