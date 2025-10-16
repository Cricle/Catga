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
    private readonly GracefulShutdownManager? _shutdownManager;

    public CatgaMediator(
        IServiceProvider serviceProvider,
        ILogger<CatgaMediator> logger,
        CatgaOptions options,
        GracefulShutdownManager? shutdownManager = null)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _options = options;
        _handlerCache = new HandlerCache(serviceProvider);
        _shutdownManager = shutdownManager;
    }

    [System.Runtime.CompilerServices.MethodImpl(System.Runtime.CompilerServices.MethodImplOptions.AggressiveInlining)]
    public async ValueTask<CatgaResult<TResponse>> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest<TResponse>
    {
        // 优雅停机：自动跟踪操作
        using var operationScope = _shutdownManager?.BeginOperation();

        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Command.Execute", ActivityKind.Internal);
        var sw = Stopwatch.StartNew();
        var reqType = TypeNameCache<TRequest>.Name;
        var message = request as IMessage;

        activity?.SetTag("catga.request.type", reqType);
        activity?.SetTag("catga.message.id", message?.MessageId);
        CatgaLog.CommandExecuting(_logger, reqType, message?.MessageId, message?.CorrelationId);

        try
        {
            using var scope = _serviceProvider.CreateScope();
            var scopedProvider = scope.ServiceProvider;

            var handler = _handlerCache.GetRequestHandler<IRequestHandler<TRequest, TResponse>>(scopedProvider);
            if (handler == null)
            {
                CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", "false"));
                return CatgaResult<TResponse>.Failure($"No handler for {reqType}", new HandlerNotFoundException(reqType));
            }

            var behaviors = scopedProvider.GetServices<IPipelineBehavior<TRequest, TResponse>>();
            var behaviorsList = behaviors as IList<IPipelineBehavior<TRequest, TResponse>> ?? behaviors.ToList();
            var result = FastPath.CanUseFastPath(behaviorsList.Count)
                ? await FastPath.ExecuteRequestDirectAsync(handler, request, cancellationToken)
                : await PipelineExecutor.ExecuteAsync(request, handler, behaviorsList, cancellationToken);

            sw.Stop();
            var duration = sw.Elapsed.TotalMilliseconds;
            CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", result.IsSuccess.ToString()));
            CatgaDiagnostics.CommandDuration.Record(duration, new KeyValuePair<string, object?>("request_type", reqType));
            CatgaLog.CommandExecuted(_logger, reqType, message?.MessageId, duration, result.IsSuccess);

            activity?.SetTag("catga.success", result.IsSuccess);
            activity?.SetTag("catga.duration_ms", duration);
            if (!result.IsSuccess)
            {
                activity?.SetStatus(ActivityStatusCode.Error, result.Error);
                CatgaLog.CommandFailed(_logger, result.Exception, reqType, message?.MessageId, result.Error ?? "Unknown");
            }

            return result;
        }
        catch (Exception ex)
        {
            sw.Stop();
            CatgaDiagnostics.CommandsExecuted.Add(1, new("request_type", reqType), new("success", "false"));
            RecordException(activity, ex);
            CatgaLog.CommandFailed(_logger, ex, reqType, message?.MessageId, ex.Message);
            throw;
        }
    }

    public async Task<CatgaResult> SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest>(TRequest request, CancellationToken cancellationToken = default) where TRequest : IRequest
    {
        using var scope = _serviceProvider.CreateScope();
        var handler = scope.ServiceProvider.GetService<IRequestHandler<TRequest>>();
        if (handler == null)
            return CatgaResult.Failure($"No handler for {TypeNameCache<TRequest>.Name}", new HandlerNotFoundException(TypeNameCache<TRequest>.Name));
        return await handler.HandleAsync(request, cancellationToken);
    }

    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : IEvent
    {
        // 优雅停机：自动跟踪操作
        using var operationScope = _shutdownManager?.BeginOperation();

        using var activity = CatgaDiagnostics.ActivitySource.StartActivity("Event.Publish", ActivityKind.Producer);
        var eventType = TypeNameCache<TEvent>.Name;
        var message = @event as IMessage;

        activity?.SetTag("catga.event.type", eventType);
        activity?.SetTag("catga.message.id", message?.MessageId);
        CatgaLog.EventPublishing(_logger, eventType, message?.MessageId);

        using var scope = _serviceProvider.CreateScope();
        var scopedProvider = scope.ServiceProvider;

        // Fast-path: use generated router if available
        var generatedRouter = scopedProvider.GetService<Catga.Handlers.IGeneratedEventRouter>();
        if (generatedRouter != null && generatedRouter.TryRoute(scopedProvider, @event, cancellationToken, out var dispatchedTask))
        {
            if (dispatchedTask != null)
                await dispatchedTask.ConfigureAwait(false);
            CatgaLog.EventPublished(_logger, eventType, message?.MessageId, 0);
            return;
        }

        var handlerList = _handlerCache.GetEventHandlers<IEventHandler<TEvent>>(scopedProvider);
        if (handlerList.Count == 0)
        {
            await FastPath.PublishEventNoOpAsync();
            CatgaLog.EventPublished(_logger, eventType, message?.MessageId, 0);
            return;
        }

        CatgaDiagnostics.EventsPublished.Add(1, new("event_type", eventType), new("handler_count", handlerList.Count.ToString()));

        if (handlerList.Count == 1)
        {
            await HandleEventSafelyAsync(handlerList[0], @event, cancellationToken);
            CatgaLog.EventPublished(_logger, eventType, message?.MessageId, 1);
            return;
        }

        using var rentedTasks = Common.ArrayPoolHelper.RentOrAllocate<Task>(handlerList.Count);
        var tasks = rentedTasks.Array;
        for (int i = 0; i < handlerList.Count; i++)
            tasks[i] = HandleEventSafelyAsync(handlerList[i], @event, cancellationToken);

        // Zero-allocation: use exact-sized array or ArraySegment
        if (tasks.Length == handlerList.Count)
        {
            // Perfect size match - zero allocation
            await Task.WhenAll((IEnumerable<Task>)tasks).ConfigureAwait(false);
        }
        else
        {
            // Use ArraySegment to avoid copying entire array
            await Task.WhenAll((IEnumerable<Task>)new ArraySegment<Task>(tasks, 0, handlerList.Count)).ConfigureAwait(false);
        }
        CatgaLog.EventPublished(_logger, eventType, message?.MessageId, handlerList.Count);
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
