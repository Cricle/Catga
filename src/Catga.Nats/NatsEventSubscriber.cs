using System.Text.Json;
using CatCat.Transit.Handlers;
using CatCat.Transit.Messages;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Nats;

/// <summary>
/// NATS event subscriber (AOT-compatible)
/// </summary>
public class NatsEventSubscriber<TEvent> : IDisposable
    where TEvent : IEvent
{
    private readonly INatsConnection _connection;
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger _logger;
    private readonly CancellationTokenSource _cts = new();
    private Task? _subscriptionTask;

    public NatsEventSubscriber(
        INatsConnection connection,
        IServiceProvider serviceProvider,
        ILogger<NatsEventSubscriber<TEvent>> logger)
    {
        _connection = connection;
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Start subscribing to events
    /// </summary>
    public void Start()
    {
        var subject = $"transit.event.{typeof(TEvent).Name}";

        _subscriptionTask = Task.Run(async () =>
        {
            _logger.LogInformation("Started NATS event subscription for {EventType} on {Subject}",
                typeof(TEvent).Name, subject);

            await foreach (var msg in _connection.SubscribeAsync<byte[]>(subject, cancellationToken: _cts.Token))
            {
                _ = Task.Run(async () => await HandleEventAsync(msg.Data), _cts.Token);
            }
        }, _cts.Token);
    }

    private async Task HandleEventAsync(byte[] data)
    {
        try
        {
            // Deserialize event
            var @event = JsonSerializer.Deserialize<TEvent>(data);
            if (@event == null)
            {
                _logger.LogWarning("Failed to deserialize event for {EventType}", typeof(TEvent).Name);
                return;
            }

            // Get all handlers
            using var scope = _serviceProvider.CreateScope();
            var handlers = scope.ServiceProvider.GetServices<IEventHandler<TEvent>>();

            // Execute all handlers in parallel
            var tasks = handlers.Select(async handler =>
            {
                try
                {
                    await handler.HandleAsync(@event, _cts.Token);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error in event handler {HandlerType} for {EventType}",
                        handler.GetType().Name, typeof(TEvent).Name);
                }
            });

            await Task.WhenAll(tasks);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling NATS event for {EventType}", typeof(TEvent).Name);
        }
    }

    public void Dispose()
    {
        _cts?.Cancel();
        _cts?.Dispose();
        _subscriptionTask?.Wait(TimeSpan.FromSeconds(5));
    }
}

