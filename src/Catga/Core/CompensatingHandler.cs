using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;

namespace Catga.Core;

/// <summary>
/// Base class for handlers that need automatic compensation on failure.
/// Wraps the handler logic and auto-publishes compensation events.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
/// <typeparam name="TCompensationEvent">Compensation event type</typeparam>
public abstract class CompensatingHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCompensationEvent> : IRequestHandler<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
    where TCompensationEvent : IEvent
{
    private readonly ICatgaMediator _mediator;

    protected CompensatingHandler(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CatgaResult<TResponse>> HandleAsync(TRequest request, CancellationToken ct = default)
    {
        try
        {
            return await HandleCoreAsync(request, ct);
        }
        catch (Exception ex)
        {
            // Auto-publish compensation event
            var compensationEvent = CreateCompensationEvent(request, ex);
            if (compensationEvent != null)
            {
                await _mediator.PublishAsync(compensationEvent, ct);
            }

            return CatgaResult<TResponse>.Failure(ex.Message);
        }
    }

    /// <summary>Implement your business logic here.</summary>
    protected abstract Task<CatgaResult<TResponse>> HandleCoreAsync(TRequest request, CancellationToken ct);

    /// <summary>Create the compensation event. Override to customize.</summary>
    protected abstract TCompensationEvent? CreateCompensationEvent(TRequest request, Exception ex);
}

/// <summary>
/// Base class for handlers without response that need automatic compensation.
/// </summary>
public abstract class CompensatingHandler<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCompensationEvent> : IRequestHandler<TRequest>
    where TRequest : IRequest
    where TCompensationEvent : IEvent
{
    private readonly ICatgaMediator _mediator;

    protected CompensatingHandler(ICatgaMediator mediator)
    {
        _mediator = mediator;
    }

    public async Task<CatgaResult> HandleAsync(TRequest request, CancellationToken ct = default)
    {
        try
        {
            return await HandleCoreAsync(request, ct);
        }
        catch (Exception ex)
        {
            var compensationEvent = CreateCompensationEvent(request, ex);
            if (compensationEvent != null)
            {
                await _mediator.PublishAsync(compensationEvent, ct);
            }

            return CatgaResult.Failure(ex.Message);
        }
    }

    protected abstract Task<CatgaResult> HandleCoreAsync(TRequest request, CancellationToken ct);
    protected abstract TCompensationEvent? CreateCompensationEvent(TRequest request, Exception ex);
}

/// <summary>
/// Simplified compensating handler using Func delegates.
/// </summary>
public static class Compensate
{
    /// <summary>
    /// Wrap a handler with automatic compensation.
    /// </summary>
    public static async Task<CatgaResult<TResponse>> WithCompensationAsync<TRequest, TResponse, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCompensationEvent>(
        ICatgaMediator mediator,
        TRequest request,
        Func<TRequest, CancellationToken, Task<CatgaResult<TResponse>>> handler,
        Func<TRequest, Exception, TCompensationEvent> createCompensation,
        CancellationToken ct = default)
        where TCompensationEvent : IEvent
    {
        try
        {
            return await handler(request, ct);
        }
        catch (Exception ex)
        {
            var compensation = createCompensation(request, ex);
            await mediator.PublishAsync(compensation, ct);
            return CatgaResult<TResponse>.Failure(ex.Message);
        }
    }

    /// <summary>
    /// Wrap a handler with automatic compensation (no response).
    /// </summary>
    public static async Task<CatgaResult> WithCompensationAsync<TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TCompensationEvent>(
        ICatgaMediator mediator,
        TRequest request,
        Func<TRequest, CancellationToken, Task<CatgaResult>> handler,
        Func<TRequest, Exception, TCompensationEvent> createCompensation,
        CancellationToken ct = default)
        where TCompensationEvent : IEvent
    {
        try
        {
            return await handler(request, ct);
        }
        catch (Exception ex)
        {
            var compensation = createCompensation(request, ex);
            await mediator.PublishAsync(compensation, ct);
            return CatgaResult.Failure(ex.Message);
        }
    }
}
