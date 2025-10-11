using System.Diagnostics.CodeAnalysis;
using Catga.Common;
using Catga.Configuration;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Performance;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga;

/// <summary>High-performance Catga Mediator (AOT-compatible, lock-free)</summary>
public class CatgaMediator : ICatgaMediator {
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly CatgaOptions _options;
    private readonly HandlerCache _handlerCache;

    public CatgaMediator(IServiceProvider serviceProvider, ILogger<CatgaMediator> logger, CatgaOptions options) {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
        _handlerCache = new HandlerCache(serviceProvider);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(
        TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse> {
        var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(_serviceProvider);
        if (handler == null)
            return CatgaResult<TResponse>.Failure($"No handler for {typeof(TRequest).Name}", new HandlerNotFoundException(typeof(TRequest).Name));

        var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
        if (behaviors is IList<IPipelineBehavior<TRequest, TResponse>> behaviorsList) {
            if (FastPath.CanUseFastPath(behaviorsList.Count))
                return await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken);
            return await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);
        }

        var materializedBehaviors = behaviors.ToList();
        if (FastPath.CanUseFastPath(materializedBehaviors.Count))
            return await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken);
        return await PipelineExecutor.ExecuteAsync(request, handler, materializedBehaviors, cancellationToken);
    }

    public async Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest>(
        TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest {
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest>>();
        if (handler == null)
            return CatgaResult.Failure($"No handler for {typeof(TRequest).Name}", new HandlerNotFoundException(typeof(TRequest).Name));
        return await handler.HandleAsync(request, cancellationToken);
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent {
        var handlerList = _handlerCache.GetEventHandlers<IEventHandler<TEvent>>(_serviceProvider);
        if (handlerList.Count == 0) {
            await FastPath.PublishEventNoOpAsync();
            return;
        }
        if (handlerList.Count == 1) {
            await HandleEventSafelyAsync(handlerList[0], @event, cancellationToken);
            return;
        }
        using var rentedTasks = Common.ArrayPoolHelper.RentOrAllocate<Task>(handlerList.Count);
        var tasks = rentedTasks.Array;
        for (int i = 0; i < handlerList.Count; i++)
            tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);
        await Task.WhenAll(rentedTasks.AsSpan().ToArray()).ConfigureAwait(false);
    }

    private async Task HandleEventSafelyAsync<TEvent>(IEventHandler<TEvent> handler, TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent {
        try {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        } catch (Exception ex) {
            _logger.LogError(ex, "Event handler failed: {HandlerType}", handler.GetType().Name);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(
        IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => await requests.ExecuteBatchWithResultsAsync(request => SendAsync<TRequest, TResponse>(request, cancellationToken));

    public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TRequest, TResponse>(
        IAsyncEnumerable<TRequest> requests, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse> {
        if (requests == null) yield break;
        await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return await SendAsync<TRequest, TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors)] TEvent>(
        IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent
        => await events.ExecuteBatchAsync(@event => PublishAsync(@event, cancellationToken));
}
