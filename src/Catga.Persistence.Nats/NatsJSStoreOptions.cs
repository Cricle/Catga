using NATS.Client.JetStream.Models;

namespace Catga.Persistence;

/// <summary>
/// NATS JetStream store options. Unified configuration for all NATS stores.
/// </summary>
public class NatsJSStoreOptions
{
    /// <summary>Stream name prefix (default: "CATGA").</summary>
    public string StreamName { get; set; } = "CATGA";

    /// <summary>Optional custom StreamConfig. If null, uses defaults.</summary>
    public StreamConfig? CustomStreamConfig { get; set; }

    /// <summary>Stream name for idempotency store. Default: CATGA_IDEMPOTENCY.</summary>
    public string IdempotencyStreamName { get; set; } = "CATGA_IDEMPOTENCY";

    /// <summary>TTL for idempotency records. Default: 24 hours.</summary>
    public TimeSpan IdempotencyTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Stream name for dead letter queue. Default: CATGA_DLQ.</summary>
    public string DlqStreamName { get; set; } = "CATGA_DLQ";

    /// <summary>Stream name for inbox store. Default: CATGA_INBOX.</summary>
    public string InboxStreamName { get; set; } = "CATGA_INBOX";

    /// <summary>Stream name for outbox store. Default: CATGA_OUTBOX.</summary>
    public string OutboxStreamName { get; set; } = "CATGA_OUTBOX";

    /// <summary>Create default StreamConfig for a stream.</summary>
    public StreamConfig CreateDefaultStreamConfig(string streamName, string[] subjects)
    {
        return CustomStreamConfig ?? new StreamConfig(streamName, subjects)
        {
            Retention = StreamConfigRetention.Limits,
            Storage = StreamConfigStorage.File,
            MaxAge = TimeSpan.FromDays(7),
            Discard = StreamConfigDiscard.Old
        };
    }
}

