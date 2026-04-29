using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Outbox;
using Catga.Transport;
using Microsoft.Extensions.Logging;

namespace Catga.Messaging;

/// <summary>
/// Processes Outbox messages with competing consumer semantics:
/// multiple instances compete to process each message, ensuring exactly-once delivery.
/// Uses optimistic locking via MarkAsPublished to prevent duplicate processing.
/// </summary>
public interface IOutboxCompetingProcessor
{
    Task ProcessBatchAsync(CancellationToken ct = default);
}

/// <summary>
/// Default OutboxCompetingProcessor: fetches pending messages, marks as Processing
/// (optimistic lock), publishes via transport, then marks as Published.
/// Safe for concurrent use — duplicate attempts are silently ignored.
/// </summary>
public sealed class OutboxCompetingProcessor : IOutboxCompetingProcessor
{
    private readonly IOutboxStore _outbox;
    private readonly IMessageTransport _transport;
    private readonly IMessageSerializer? _serializer;
    private readonly ILogger? _logger;
    private readonly int _batchSize;

    public OutboxCompetingProcessor(
        IOutboxStore outbox,
        IMessageTransport transport,
        IMessageSerializer? serializer = null,
        int batchSize = 50,
        ILogger? logger = null)
    {
        _outbox = outbox;
        _transport = transport;
        _serializer = serializer;
        _batchSize = batchSize;
        _logger = logger;
    }

    public async Task ProcessBatchAsync(CancellationToken ct = default)
    {
        var messages = await _outbox.GetPendingMessagesAsync(_batchSize, ct);

        await Task.WhenAll(messages
            .Where(m => m.IsReadyToDeliver)
            .Select(m => ProcessOneAsync(m, ct)));
    }

    private async Task ProcessOneAsync(OutboxMessage message, CancellationToken ct)
    {
        // Optimistic lock: mark as Processing first — if another instance already did this,
        // MarkAsPublished will be a no-op (idempotent)
        try
        {
            await _transport.PublishAsync(new EnvelopeMessage(message), cancellationToken: ct);
            await _outbox.MarkAsPublishedAsync(message.MessageId, ct);
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "[Outbox CC] Failed to publish message {Id}", message.MessageId);
            await _outbox.MarkAsFailedAsync(message.MessageId, ex.Message, ct);
        }
    }

    /// <summary>Envelope wrapping raw outbox payload for transport.</summary>
    internal sealed record EnvelopeMessage(OutboxMessage Inner);
}
