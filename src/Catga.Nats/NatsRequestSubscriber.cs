using Catga.Exceptions;
using Catga.Handlers;
using Catga.Messages;
using Catga.Nats.Serialization;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Nats;

/// <summary>
/// NATS request subscriber with full Pipeline Behaviors support (AOT-compatible)
/// </summary>
public class NatsRequestSubscriber<TRequest, TResponse> : IDisposable
    where TRequest : IRequest<TResponse>
{
    private readonly INatsConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _subscriptionTask;

    public NatsRequestSubscriber(
        INatsConnection connection,
        IServiceProvider serviceProvider,
        ILogger<NatsRequestSubscriber<TRequest, TResponse>> logger)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Start subscribing to requests
    /// </summary>
    public void Start()
    {
        var subject = $"transit.request.{typeof(TRequest).Name}";

        _subscriptionTask = Task.Run(async () =>
        {
            _logger.LogInformation("Started NATS subscription for {RequestType} on {Subject}",
                typeof(TRequest).Name, subject);

            await foreach (var msg in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: _cts.Token))
            {
                _ = Task.Run(async () => await HandleRequestAsync(msg), _cts.Token);
            }
        }, _cts.Token);
    }

    private async Task HandleRequestAsync(NatsMsg<byte[]> msg)
    {
        try
        {
            // Deserialize request
            var request = NatsJsonSerializer.Deserialize<TRequest>(msg.Data);
            if (request == null)
            {
                _logger.LogWarning("Failed to deserialize request for {RequestType}", typeof(TRequest).Name);
                return;
            }

            // Create scope for handler and behaviors
            using var scope = _serviceProvider.CreateScope();
            var handler = scope.ServiceProvider.GetService<IRequestHandler<TRequest, TResponse>>();

            if (handler == null)
            {
                _logger.LogError("No handler found for {RequestType}", typeof(TRequest).Name);

                var errorResult = CatgaResult<TResponse>.Failure($"No handler for {typeof(TRequest).Name}");
                await ReplyAsync(msg, errorResult);
                return;
            }

            // Build pipeline with behaviors (same as Memory transport)
            var behaviors = scope.ServiceProvider
                .GetServices<IPipelineBehavior<TRequest, TResponse>>()
                .ToList();

            // 🔥 优化: 使用 PipelineExecutor 减少闭包分配
            var result = await Pipeline.PipelineExecutor.ExecuteAsync(
                request,
                handler,
                behaviors,
                _cts.Token);

            // Reply
            await ReplyAsync(msg, result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling NATS request for {RequestType}", typeof(TRequest).Name);

            var errorResult = CatgaResult<TResponse>.Failure(
                "Internal server error",
                new CatgaException("Request processing failed", ex));

            await ReplyAsync(msg, errorResult);
        }
    }

    private async Task ReplyAsync(NatsMsg<byte[]> msg, CatgaResult<TResponse> result)
    {
        try
        {
            var responseBytes = NatsJsonSerializer.SerializeToUtf8Bytes(result);
            await msg.ReplyAsync(responseBytes, cancellationToken: _cts.Token);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send reply for {RequestType}", typeof(TRequest).Name);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _subscriptionTask?.Wait(TimeSpan.FromSeconds(5));
    }
}

