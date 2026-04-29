using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.DeadLetter;
using Catga.Messaging;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;
using NATS.Client.JetStream;
using NATS.Client.JetStream.Models;

namespace Catga.Transport.Nats;

/// <summary>
/// Competing consumer backed by NATS JetStream Queue Groups.
/// Multiple instances in the same queue group each receive different messages.
/// NATS handles load balancing and re-delivery automatically.
/// </summary>
public sealed class NatsCompetingConsumer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>
    : ICompetingConsumer<TMessage>
    where TMessage : class
{
    private readonly INatsConnection _nats;
    private readonly IMessageSerializer _serializer;
    private readonly CompetingConsumerOptions _options;
    private readonly IDeadLetterQueue? _deadLetterQueue;
    private readonly ILogger? _logger;
    private readonly string _subject;
    private readonly string _streamName;
    private CancellationTokenSource? _cts;

    public string GroupName => _options.GroupName;
    public string ConsumerName { get; }

    public NatsCompetingConsumer(
        INatsConnection nats,
        IMessageSerializer serializer,
        string subject,
        string? streamName = null,
        CompetingConsumerOptions? options = null,
        IDeadLetterQueue? deadLetterQueue = null,
        ILogger? logger = null)
    {
        _nats = nats;
        _serializer = serializer;
        _options = options ?? new CompetingConsumerOptions();
        _deadLetterQueue = deadLetterQueue;
        _logger = logger;
        _subject = subject;
        _streamName = streamName ?? $"catga-{subject.Replace('.', '-')}";
        ConsumerName = _options.ResolvedConsumerName;
    }

    public async Task StartAsync(Func<TMessage, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var js = new NatsJSContext((NatsConnection)_nats);

        // Ensure stream exists
        try
        {
            await js.CreateStreamAsync(new StreamConfig(_streamName, [_subject]), _cts.Token);
        }
        catch (NatsJSApiException ex) when (ex.Error.Code == 400)
        {
            // Stream already exists
        }

        // Create durable consumer with queue group (competing consumers)
        var consumerConfig = new ConsumerConfig
        {
            Name = GroupName,
            DurableName = GroupName,
            FilterSubject = _subject,
            AckPolicy = ConsumerConfigAckPolicy.Explicit,
            MaxDeliver = _options.MaxDeliveryAttempts,
            AckWait = _options.VisibilityTimeout,
            MaxAckPending = _options.Concurrency * _options.BatchSize
        };

        var consumer = await js.CreateOrUpdateConsumerAsync(_streamName, consumerConfig, _cts.Token);

        _logger?.LogInformation("[NATS CC] Consumer {Name} joined group {Group} on subject {Subject}",
            ConsumerName, GroupName, _subject);

        using var semaphore = new SemaphoreSlim(_options.Concurrency, _options.Concurrency);

        await foreach (var msg in consumer.ConsumeAsync<byte[]>(cancellationToken: _cts.Token))
        {
            await semaphore.WaitAsync(_cts.Token);
            _ = ProcessMessageAsync(msg, handler, semaphore, _cts.Token);
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task ProcessMessageAsync(
        INatsJSMsg<byte[]> msg,
        Func<TMessage, CancellationToken, Task> handler,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        try
        {
            if (msg.Data == null) { await msg.AckAsync(cancellationToken: ct); return; }

            var message = _serializer.Deserialize<TMessage>(msg.Data);
            await handler(message, ct);
            await msg.AckAsync(cancellationToken: ct);
        }
        catch (Exception ex)
        {
            var deliveryAttempt = (int)Math.Max(1UL, msg.Metadata?.NumDelivered ?? 1UL);
            if (deliveryAttempt >= Math.Max(1, _options.MaxDeliveryAttempts))
            {
                if (await TryFinalizePoisonMessageAsync(msg, ex, deliveryAttempt, ct))
                {
                    await msg.AckTerminateAsync(cancellationToken: ct);
                    return;
                }
            }

            _logger?.LogWarning(ex, "[NATS CC] Message failed on attempt {Attempt}, will be redelivered", deliveryAttempt);
            await msg.NakAsync(cancellationToken: ct);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task<bool> TryFinalizePoisonMessageAsync(
        INatsJSMsg<byte[]> msg,
        Exception exception,
        int deliveryAttempt,
        CancellationToken ct)
    {
        if (_deadLetterQueue == null)
        {
            _logger?.LogError(
                exception,
                "[NATS CC] Message exceeded max attempts ({Attempts}) without a DLQ; terminating delivery",
                deliveryAttempt);
            return true;
        }

        TMessage? message;
        try
        {
            message = msg.Data == null ? null : _serializer.Deserialize<TMessage>(msg.Data);
        }
        catch
        {
            message = null;
        }

        if (message is not IMessage catgaMessage)
        {
            _logger?.LogError(
                exception,
                "[NATS CC] Message exceeded max attempts ({Attempts}) but does not implement IMessage; terminating delivery",
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
                "[NATS CC] Failed to move message to DLQ after {Attempts} attempts; delivery will be retried",
                deliveryAttempt);
            return false;
        }
    }
}
