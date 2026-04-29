using System.Diagnostics.CodeAnalysis;
using System.Security.Cryptography;
using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.Messaging;
using Microsoft.Extensions.Logging;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace Catga.Transport.RabbitMQ;

/// <summary>
/// Competing consumer backed by a shared RabbitMQ queue.
/// Multiple instances attached to the same queue compete for messages.
/// </summary>
public sealed class RabbitMqCompetingConsumer<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>
    : ICompetingConsumer<TMessage>, IAsyncDisposable
    where TMessage : class
{
    private readonly IMessageSerializer _serializer;
    private readonly RabbitMqTransportOptions _transportOptions;
    private readonly CompetingConsumerOptions _options;
    private readonly IDeadLetterQueue? _deadLetterQueue;
    private readonly ILogger? _logger;
    private readonly string _queueName;
    private readonly string _routingKey;
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, int> _attempts = new();
    private CancellationTokenSource? _cts;
    private IConnection? _connection;
    private IChannel? _channel;

    public string GroupName { get; }
    public string ConsumerName { get; }

    public RabbitMqCompetingConsumer(
        IMessageSerializer serializer,
        string queueName,
        string routingKey,
        RabbitMqTransportOptions? transportOptions = null,
        CompetingConsumerOptions? options = null,
        IDeadLetterQueue? deadLetterQueue = null,
        ILogger? logger = null)
    {
        _serializer = serializer ?? throw new ArgumentNullException(nameof(serializer));
        _transportOptions = transportOptions ?? new RabbitMqTransportOptions();
        _options = options ?? new CompetingConsumerOptions();
        _deadLetterQueue = deadLetterQueue;
        _logger = logger;
        _queueName = queueName;
        _routingKey = routingKey;
        GroupName = _options.GroupName;
        ConsumerName = _options.ConsumerName ?? $"{Environment.MachineName}-{Guid.NewGuid().ToString("N")[..6]}";
    }

    public async Task StartAsync(Func<TMessage, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(handler);

        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var factory = new ConnectionFactory { Uri = new Uri(_transportOptions.Uri) };
        _connection = await factory.CreateConnectionAsync(_cts.Token);
        _channel = await _connection.CreateChannelAsync(cancellationToken: _cts.Token);

        if (_transportOptions.DeclareExchange)
        {
            await _channel.ExchangeDeclareAsync(
                _transportOptions.Exchange,
                RabbitMqTransportDelay.ResolveExchangeType(_transportOptions),
                durable: _transportOptions.DurableExchange,
                autoDelete: false,
                arguments: RabbitMqTransportDelay.BuildExchangeArguments(_transportOptions),
                cancellationToken: _cts.Token);
        }

        var queueArguments = RabbitMqTransportPriority.BuildQueueArguments(_transportOptions);
        await _channel.QueueDeclareAsync(
            _queueName,
            durable: _transportOptions.DurableQueues,
            exclusive: false,
            autoDelete: _transportOptions.AutoDeleteQueues,
            arguments: queueArguments,
            cancellationToken: _cts.Token);

        await _channel.QueueBindAsync(
            _queueName,
            _transportOptions.Exchange,
            _routingKey,
            cancellationToken: _cts.Token);

        var prefetch = (ushort)Math.Max(1, _options.Concurrency);
        await _channel.BasicQosAsync(0, prefetch, false, _cts.Token);

        _logger?.LogInformation(
            "[RabbitMQ CC] Consumer {Name} joined group {Group} on queue {Queue} (routing: {RoutingKey})",
            ConsumerName,
            GroupName,
            _queueName,
            _routingKey);

        using var semaphore = new SemaphoreSlim(_options.Concurrency, _options.Concurrency);
        var consumer = new AsyncEventingBasicConsumer(_channel);

        consumer.ReceivedAsync += (_, ea) =>
        {
            _ = ProcessDeliveryAsync(handler, semaphore, ea, _cts.Token);
            return Task.CompletedTask;
        };

        await _channel.BasicConsumeAsync(_queueName, autoAck: false, consumer, _cts.Token);

        try
        {
            await Task.Delay(Timeout.InfiniteTimeSpan, _cts.Token);
        }
        catch (OperationCanceledException)
        {
            // Expected on stop.
        }
    }

    public async Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();

        if (_channel != null)
        {
            await _channel.DisposeAsync();
            _channel = null;
        }

        if (_connection != null)
        {
            await _connection.DisposeAsync();
            _connection = null;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync();
        _cts?.Dispose();
    }

    private async Task ProcessDeliveryAsync(
        Func<TMessage, CancellationToken, Task> handler,
        SemaphoreSlim semaphore,
        BasicDeliverEventArgs ea,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var attemptKey = GetAttemptKey(ea);
            var deliveryAttempt = GetDeliveryAttempt(ea, attemptKey);
            var message = _serializer.Deserialize<TMessage>(ea.Body.ToArray());
            await handler(message, ct);
            if (attemptKey != null)
                _attempts.TryRemove(attemptKey, out _);
            await _channel!.BasicAckAsync(ea.DeliveryTag, false, ct);
        }
        catch (Exception ex)
        {
            var attemptKey = GetAttemptKey(ea);
            var deliveryAttempt = GetDeliveryAttempt(ea, attemptKey);
            var message = TryDeserialize(ea);

            if (deliveryAttempt >= Math.Max(1, _options.MaxDeliveryAttempts))
            {
                if (await TryFinalizePoisonMessageAsync(ea, message, ex, deliveryAttempt, ct))
                {
                    if (attemptKey != null)
                        _attempts.TryRemove(attemptKey, out _);
                    await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: false, ct);
                    return;
                }
            }

            _logger?.LogWarning(
                ex,
                "[RabbitMQ CC] Message on queue {Queue} failed on attempt {Attempt}, will be requeued",
                _queueName,
                deliveryAttempt);
            await _channel!.BasicNackAsync(ea.DeliveryTag, false, requeue: true, ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private TMessage? TryDeserialize(BasicDeliverEventArgs ea)
    {
        try
        {
            return _serializer.Deserialize<TMessage>(ea.Body.ToArray());
        }
        catch
        {
            return null;
        }
    }

    private string GetAttemptKey(BasicDeliverEventArgs ea)
        => !string.IsNullOrWhiteSpace(ea.BasicProperties.MessageId)
            ? ea.BasicProperties.MessageId!
            : Convert.ToHexString(SHA256.HashData(ea.Body.Span));

    private int GetDeliveryAttempt(BasicDeliverEventArgs ea, string attemptKey)
        => _attempts.AddOrUpdate(
            attemptKey,
            ea.Redelivered ? 2 : 1,
            (_, current) => ea.Redelivered ? current + 1 : Math.Max(current, 1));

    private async Task<bool> TryFinalizePoisonMessageAsync(
        BasicDeliverEventArgs ea,
        TMessage? message,
        Exception exception,
        int deliveryAttempt,
        CancellationToken ct)
    {
        if (_deadLetterQueue == null)
        {
            _logger?.LogError(
                exception,
                "[RabbitMQ CC] Message {MessageId} exceeded max attempts ({Attempts}) without a DLQ; rejecting without requeue",
                ea.BasicProperties.MessageId ?? "<none>",
                deliveryAttempt);
            return true;
        }

        if (message is not IMessage catgaMessage)
        {
            _logger?.LogError(
                exception,
                "[RabbitMQ CC] Message {MessageId} exceeded max attempts ({Attempts}) but does not implement IMessage; rejecting without requeue",
                ea.BasicProperties.MessageId ?? "<none>",
                deliveryAttempt);
            return true;
        }

        try
        {
            await _deadLetterQueue.SendAsync(catgaMessage, exception, Math.Max(0, deliveryAttempt - 1), ct);
            return true;
        }
        catch (Exception dlqEx)
        {
            _logger?.LogError(
                dlqEx,
                "[RabbitMQ CC] Failed to move message {MessageId} to DLQ after {Attempts} attempts; message will be retried",
                ea.BasicProperties.MessageId ?? "<none>",
                deliveryAttempt);
            return false;
        }
    }
}
