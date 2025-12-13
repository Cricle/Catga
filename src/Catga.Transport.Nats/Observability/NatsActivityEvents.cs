namespace Catga.Transport.Nats.Observability;

/// <summary>
/// NATS-specific activity event names for distributed tracing.
/// </summary>
internal static class NatsActivityEvents
{
    public const string NatsPublishEnqueued = "NATS.Publish.Enqueued";
    public const string NatsPublishSent = "NATS.Publish.Sent";
    public const string NatsPublishFailed = "NATS.Publish.Failed";
    public const string NatsReceiveEmpty = "NATS.Receive.Empty";
    public const string NatsReceiveDeserialized = "NATS.Receive.Deserialized";
    public const string NatsReceiveDroppedDuplicate = "NATS.Receive.DroppedDuplicate";
    public const string NatsReceiveHandler = "NATS.Receive.Handler";
    public const string NatsReceiveProcessed = "NATS.Receive.Processed";
    public const string NatsBatchItemSent = "NATS.Batch.ItemSent";
    public const string NatsBatchItemFailed = "NATS.Batch.ItemFailed";
}
