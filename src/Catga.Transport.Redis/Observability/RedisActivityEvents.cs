namespace Catga.Transport.Redis.Observability;

/// <summary>
/// Redis-specific activity event names for distributed tracing.
/// </summary>
internal static class RedisActivityEvents
{
    public const string RedisPublishEnqueued = "Redis.Publish.Enqueued";
    public const string RedisPublishSent = "Redis.Publish.Sent";
    public const string RedisPublishFailed = "Redis.Publish.Failed";
    public const string RedisStreamEnqueued = "Redis.Stream.Enqueued";
    public const string RedisStreamAdded = "Redis.Stream.Added";
    public const string RedisStreamFailed = "Redis.Stream.Failed";
    public const string RedisBatchPubSubSent = "Redis.Batch.PubSub.Sent";
    public const string RedisBatchStreamAdded = "Redis.Batch.Stream.Added";
    public const string RedisBatchItemFailed = "Redis.Batch.ItemFailed";
    public const string RedisReceiveDeserialized = "Redis.Receive.Deserialized";
    public const string RedisReceiveHandler = "Redis.Receive.Handler";
    public const string RedisReceiveProcessed = "Redis.Receive.Processed";
}
