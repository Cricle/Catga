using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Common;
using Catga.Configuration;
using Catga.Core;
using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Observability;
using Catga.Performance;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Catga;

/// <summary>High-performance Catga Mediator (AOT-compatible, lock-free)</summary>
public class CatgaMediator : ICatgaMediator
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<CatgaMediator> _logger;
    private readonly CatgaOptions _options;
    private readonly HandlerCache _handlerCache;

    public CatgaMediator(IServiceProvider serviceProvider, ILogger<CatgaMediator> logger, CatgaOptions options)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
        _handlerCache = new HandlerCache(serviceProvider);
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Command.Execute", ActivityKind.Internal);
        var sw = Stopwatch.StartNew();
        var reqType = TypeNameCache<TRequest>.Name;
        var msgId = (request as IMessage)?.MessageId;

        activity?.SetTag("catga.request.type", reqType);
        activity?.SetTag("catga.message.id", msgId);
        CatgaLog.CommandExecuting(_logger, reqType, msgId, (request as IMessage)?.CorrelationId);

        try
        {
            var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(_serviceProvider);
            if (handler == null)
            {
                CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", "false"));
                return CatgaResult<TResponse>.Failure($"No handler for {reqType}", new HandlerNotFoundException(reqType));
            }

            var behaviors = _serviceProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
            var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();
            var result = FastPath.CanUseFastPath(behaviorsList.Count)
                ? await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken)
                : await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);

            sw.Stop();
            CatgaDiagnostics.CommandsExecuted.Add(1, new KeyValuePair<string, object?>("request_type", reqType), new KeyValuePair<string, object?>("success", result.IsSuccess.ToString()));
            CatgaDiagnostics.CommandDuration.Record(sw.Elapsed.TotalMilliseconds, new KeyValuePair<string, object?>("request_type", reqType));
            CatgaLog.CommandExecuted(_logger, reqType, msgId, sw.Elapsed.TotalMilliseconds, result.IsSuccess);

            activity?.SetTag("catga.success", result.IsSuccess);
            activity?.SetTag("catga.duration_ms", sw.Elapsed.TotalMilliseconds);
            if (!result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error);
                CatgaLog.CommandFailed(_logger, result.Exception, reqType, msgId, result.Error ?? "Unknown");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            CatgaDiagnostics.CommandsExecuted.Add(1, new KeyValuePair<string, object?>("request_type", reqType), new KeyValuePair<string, object?>("success", "false"));
            RecordException(activity, ex);
            CatgaLog.CommandFailed(_logger, ex, reqType, msgId, ex.Message);
            throw;
        }
    }

    public async Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        var handler = _serviceProvider.GetService<IRequestHandler<TRequest>>();
        if (handler == null)
            return CatgaResult.Failure($"No handler for {TypeNameCache<TRequest>.Name}", new HandlerNotFoundException(TypeNameCache<TRequest>.Name));
        return await handler.HandleAsync(request, cancellationToken);
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Event.Publish", ActivityKind.Producer);
        var eventType = TypeNameCache<TEvent>.Name;
        var msgId = (@event as IMessage)?.MessageId;

        activity?.SetTag("catga.event.type", eventType);
        activity?.SetTag("catga.message.id", msgId);
        CatgaLog.EventPublishing(_logger, eventType, msgId);

        var handlerList = _handlerCache.GetEventHandlers<IEventHandler<TEvent>>(_serviceProvider);
        if (handlerList.Count == 0)
        {
            await FastPath.PublishEventNoOpAsync();
            CatgaLog.EventPublished(_logger, eventType, msgId, 0);
            return;
        }

        CatgaDiagnostics.EventsPublished.Add(1, new KeyValuePair<string, object?>("event_type", eventType), new KeyValuePair<string, object?>("handler_count", handlerList.Count.ToString()));

        if (handlerList.Count == 1)
        {
            await HandleEventSafelyAsync(handlerList[0], @event, cancellationToken);
            CatgaLog.EventPublished(_logger, eventType, msgId, 1);
            return;
        }

        using var rentedTasks = Common.ArrayPoolHelper.RentOrAllocate<Task>(handlerList.Count);
        var tasks = rentedTasks.Array;
        for (int i = 0; i < handlerList.Count; i++)
            tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);
        await Task.WhenAll(rentedTasks.AsSpan().ToArray()).ConfigureAwait(false);
        CatgaLog.EventPublished(_logger, eventType, msgId, handlerList.Count);
    }

    private async Task HandleEventSafelyAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IEventHandler<TEvent> handler, TEvent @event, CancellationToken cancellationToken) where TEvent : IEvent
    {
        try
        {
            await handler.HandleAsync(@event, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Event handler failed: {HandlerType}", handler.GetType().Name);
        }
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<IReadOnlyList<CatgaResult<TResponse>>> SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IReadOnlyList<TRequest> requests, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
        => await requests.ExecuteBatchWithResultsAsync(request => SendAsync<TRequest, TResponse>(request, cancellationToken));

    public async IAsyncEnumerable<CatgaResult<TResponse>> SendStreamAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(IAsyncEnumerable<TRequest> requests, [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        if (requests == null) yield break;
        await foreach (var request in requests.WithCancellation(cancellationToken).ConfigureAwait(false))
            yield return await SendAsync<TRequest, TResponse>(request, cancellationToken).ConfigureAwait(false);
    }

    public async Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(IReadOnlyList<TEvent> events, CancellationToken cancellationToken = default) where TEvent : IEvent
        => await events.ExecuteBatchAsync(@event => PublishAsync(@event, cancellationToken));

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    private static void RecordException(Activity? activity, Exception ex)
    {
        activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
        activity?.AddTag("exception.type", ExceptionTypeCache.GetFullTypeName(ex));
        activity?.AddTag("exception.message", ex.Message);
    }
}
