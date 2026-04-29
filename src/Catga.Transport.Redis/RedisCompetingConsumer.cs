using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Catga.DeadLetter;
using Catga.Messaging;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace Catga.Transport.Redis;

/// <summary>
/// Competing consumer backed by Redis Streams Consumer Groups.
/// Multiple instances in the same group each receive different messages.
/// On failure, messages are re-delivered after VisibilityTimeout.
/// </summary>
public sealed class RedisCompetingConsumer<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>
    : ICompetingConsumer<TMessage>
    where TMessage : class
{
    private readonly IConnectionMultiplexer _redis;
    private readonly IMessageSerializer _serializer;
    private readonly CompetingConsumerOptions _options;
    private readonly IDeadLetterQueue? _deadLetterQueue;
    private readonly ILogger? _logger;
    private readonly string _streamKey;
    private CancellationTokenSource? _cts;

    public string GroupName => _options.GroupName;
    public string ConsumerName { get; }

    public RedisCompetingConsumer(
        IConnectionMultiplexer redis,
        IMessageSerializer serializer,
        string streamKey,
        CompetingConsumerOptions? options = null,
        IDeadLetterQueue? deadLetterQueue = null,
        ILogger? logger = null)
    {
        _redis = redis;
        _serializer = serializer;
        _options = options ?? new CompetingConsumerOptions();
        _deadLetterQueue = deadLetterQueue;
        _logger = logger;
        _streamKey = streamKey;
        ConsumerName = _options.ResolvedConsumerName;
    }

    public async Task StartAsync(Func<TMessage, CancellationToken, Task> handler, CancellationToken ct = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        var db = _redis.GetDatabase();

        // Ensure consumer group exists
        try
        {
            await db.StreamCreateConsumerGroupAsync(_streamKey, GroupName, StreamPosition.NewMessages, createStream: true);
        }
        catch (RedisException ex) when (ex.Message.Contains("BUSYGROUP"))
        {
            // Group already exists — fine
        }

        _logger?.LogInformation("[Redis CC] Consumer {Name} joined group {Group} on stream {Stream}",
            ConsumerName, GroupName, _streamKey);

        using var semaphore = new SemaphoreSlim(_options.Concurrency, _options.Concurrency);

        while (!_cts.Token.IsCancellationRequested)
        {
            try
            {
                // Reclaim pending messages that timed out first
                await ReclaimPendingAsync(db, handler, _cts.Token);

                // Read new messages
                var entries = await db.StreamReadGroupAsync(
                    _streamKey, GroupName, ConsumerName,
                    count: _options.BatchSize,
                    noAck: false);

                if (entries.Length == 0)
                {
                    await Task.Delay(_options.PollInterval, _cts.Token);
                    continue;
                }

                var tasks = entries.Select(entry => ProcessEntryAsync(db, entry, deliveryAttempt: 1, handler, semaphore, _cts.Token));
                await Task.WhenAll(tasks);
            }
            catch (OperationCanceledException) { break; }
            catch (Exception ex)
            {
                _logger?.LogError(ex, "[Redis CC] Consumer {Name} error", ConsumerName);
                await Task.Delay(TimeSpan.FromSeconds(1), _cts.Token).ConfigureAwait(false);
            }
        }
    }

    public Task StopAsync(CancellationToken ct = default)
    {
        _cts?.Cancel();
        return Task.CompletedTask;
    }

    private async Task ProcessEntryAsync(
        IDatabase db,
        StreamEntry entry,
        int deliveryAttempt,
        Func<TMessage, CancellationToken, Task> handler,
        SemaphoreSlim semaphore,
        CancellationToken ct)
    {
        await semaphore.WaitAsync(ct);
        try
        {
            var payload = (byte[]?)entry["payload"];
            if (payload == null) { await db.StreamAcknowledgeAsync(_streamKey, GroupName, entry.Id); return; }

            var message = _serializer.Deserialize<TMessage>(payload);
            await handler(message, ct);
            await db.StreamAcknowledgeAsync(_streamKey, GroupName, entry.Id);
        }
        catch (Exception ex)
        {
            if (deliveryAttempt >= Math.Max(1, _options.MaxDeliveryAttempts))
            {
                if (await TryFinalizePoisonMessageAsync(entry.Id.ToString(), TryDeserialize(entry), ex, deliveryAttempt, ct))
                {
                    await db.StreamAcknowledgeAsync(_streamKey, GroupName, entry.Id);
                    return;
                }
            }

            _logger?.LogWarning(ex, "[Redis CC] Message {Id} failed on attempt {Attempt}, will be redelivered", entry.Id, deliveryAttempt);
        }
        finally
        {
            semaphore.Release();
        }
    }

    private async Task ReclaimPendingAsync(
        IDatabase db,
        Func<TMessage, CancellationToken, Task> handler,
        CancellationToken ct)
    {
        var minIdleMs = (long)_options.VisibilityTimeout.TotalMilliseconds;

        // Get pending messages that have been idle too long
        var pending = await db.StreamPendingMessagesAsync(
            _streamKey, GroupName, count: _options.BatchSize, consumerName: RedisValue.Null);

        var timedOut = pending
            .Where(p => p.IdleTimeInMilliseconds >= minIdleMs)
            .ToArray();

        var timedOutIds = timedOut
            .Select(p => p.MessageId)
            .ToArray();

        if (timedOutIds.Length == 0) return;

        // Claim them for this consumer
        var claimed = await db.StreamClaimAsync(
            _streamKey, GroupName, ConsumerName,
            minIdleTimeInMs: minIdleMs,
            messageIds: timedOutIds);

        var attemptsById = timedOut.ToDictionary(
            p => p.MessageId.ToString(),
            p => Math.Max(1, p.DeliveryCount));

        using var semaphore = new SemaphoreSlim(_options.Concurrency, _options.Concurrency);
        foreach (var entry in claimed)
        {
            var attempt = attemptsById.TryGetValue(entry.Id.ToString(), out var deliveryCount)
                ? deliveryCount
                : 2;
            await ProcessEntryAsync(db, entry, attempt, handler, semaphore, ct);
        }
    }

    private TMessage? TryDeserialize(StreamEntry entry)
    {
        try
        {
            var payload = (byte[]?)entry["payload"];
            return payload == null ? null : _serializer.Deserialize<TMessage>(payload);
        }
        catch
        {
            return null;
        }
    }

    private async Task<bool> TryFinalizePoisonMessageAsync(
        string messageId,
        TMessage? message,
        Exception exception,
        int deliveryAttempt,
        CancellationToken ct)
    {
        if (_deadLetterQueue == null)
        {
            _logger?.LogError(
                exception,
                "[Redis CC] Message {Id} exceeded max attempts ({Attempts}) without a DLQ; acknowledging to stop retries",
                messageId,
                deliveryAttempt);
            return true;
        }

        if (message is not IMessage catgaMessage)
        {
            _logger?.LogError(
                exception,
                "[Redis CC] Message {Id} exceeded max attempts ({Attempts}) but does not implement IMessage; acknowledging to stop retries",
                messageId,
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
                "[Redis CC] Failed to move message {Id} to DLQ after {Attempts} attempts; will retry delivery",
                messageId,
                deliveryAttempt);
            return false;
        }
    }
}
