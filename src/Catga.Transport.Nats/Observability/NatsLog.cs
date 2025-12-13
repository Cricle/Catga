using Microsoft.Extensions.Logging;

namespace Catga.Transport.Nats.Observability;

/// <summary>
/// NATS-specific high-performance logging using source-generated LoggerMessage.
/// </summary>
internal static partial class NatsLog
{
    [LoggerMessage(EventId = 7100, Level = LogLevel.Debug, Message = "Published to NATS Core (QoS 0 - fire-and-forget): {MessageId}")]
    public static partial void NatsPublishedCore(ILogger logger, long? messageId);

    [LoggerMessage(EventId = 7101, Level = LogLevel.Debug, Message = "Published to JetStream (QoS 1 - at-least-once): {MessageId}, Seq: {Seq}, Duplicate: {Dup}")]
    public static partial void NatsPublishedQoS1(ILogger logger, long? messageId, ulong Seq, bool Dup);

    [LoggerMessage(EventId = 7102, Level = LogLevel.Debug, Message = "Published to JetStream (QoS 2 - exactly-once): {MessageId}, Seq: {Seq}, Duplicate: {Dup}")]
    public static partial void NatsPublishedQoS2(ILogger logger, long? messageId, ulong Seq, bool Dup);

    [LoggerMessage(EventId = 7103, Level = LogLevel.Error, Message = "NATS publish failed for subject {Subject}, MessageId: {MessageId}")]
    public static partial void NatsPublishFailed(ILogger logger, Exception? exception, string Subject, long? MessageId);

    [LoggerMessage(EventId = 7104, Level = LogLevel.Debug, Message = "Published batch item to JetStream: Seq={Seq}, Dup={Dup}")]
    public static partial void NatsBatchPublishedJetStream(ILogger logger, ulong Seq, bool Dup);

    [LoggerMessage(EventId = 7105, Level = LogLevel.Error, Message = "NATS batch publish failed for subject {Subject}")]
    public static partial void NatsBatchPublishFailed(ILogger logger, Exception? exception, string Subject);

    [LoggerMessage(EventId = 7106, Level = LogLevel.Warning, Message = "Received empty message from subject {Subject}")]
    public static partial void NatsEmptyMessage(ILogger logger, string Subject);

    [LoggerMessage(EventId = 7107, Level = LogLevel.Warning, Message = "Failed to deserialize message from subject {Subject}")]
    public static partial void NatsDeserializeFailed(ILogger logger, string Subject);

    [LoggerMessage(EventId = 7108, Level = LogLevel.Debug, Message = "Dropped duplicate message {MessageId} (QoS={QoS}) on subject {Subject}")]
    public static partial void NatsDroppedDuplicate(ILogger logger, long? MessageId, int QoS, string Subject);

    [LoggerMessage(EventId = 7109, Level = LogLevel.Error, Message = "Error processing message from subject {Subject}")]
    public static partial void NatsProcessingError(ILogger logger, Exception? exception, string Subject);
}
