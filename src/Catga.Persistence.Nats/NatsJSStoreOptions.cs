using NATS.Client.JetStream.Models;

namespace Catga.Persistence;

/// <summary>
/// Configuration options for NATS JetStream-based stores
/// </summary>
public class NatsJSStoreOptions
{
    /// <summary>
    /// Stream name (default: "CATGA")
    /// </summary>
    public string StreamName { get; set; } = "CATGA";

    /// <summary>
    /// Stream retention policy (default: Limits)
    /// - Limits: Based on limits (MaxAge, MaxMsgs, MaxBytes)
    /// - Interest: Messages deleted when all known consumers have acknowledged
    /// - WorkQueue: Only one consumer can have a message at a time
    /// </summary>
    public StreamConfigRetention Retention { get; set; } = StreamConfigRetention.Limits;

    /// <summary>
    /// Maximum age of messages in the stream (default: 7 days)
    /// </summary>
    public TimeSpan MaxAge { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// Maximum number of messages in the stream (default: 1,000,000)
    /// </summary>
    public long MaxMessages { get; set; } = 1_000_000;

    /// <summary>
    /// Maximum total bytes in the stream (default: -1, unlimited)
    /// </summary>
    public long MaxBytes { get; set; } = -1;

    /// <summary>
    /// Number of replicas for high availability (default: 1, no replication)
    /// Set to 3 for production HA deployments
    /// </summary>
    public int Replicas { get; set; } = 1;

    /// <summary>
    /// Storage type (default: File)
    /// - File: Persistent storage on disk
    /// - Memory: In-memory storage (faster but not durable)
    /// </summary>
    public StreamConfigStorage Storage { get; set; } = StreamConfigStorage.File;

    /// <summary>
    /// Whether to compress data in the stream (default: false)
    /// Reduces storage but adds CPU overhead
    /// </summary>
    public StreamConfigCompression Compression { get; set; } = StreamConfigCompression.None;

    /// <summary>
    /// Discard policy when limits are reached (default: Old)
    /// - Old: Discard oldest messages
    /// - New: Discard new messages
    /// </summary>
    public StreamConfigDiscard Discard { get; set; } = StreamConfigDiscard.Old;

    /// <summary>
    /// Maximum message size in bytes (default: -1, use server default)
    /// </summary>
    public long MaxMessageSize { get; set; } = -1;

    /// <summary>
    /// Duplicate message window (default: 2 minutes)
    /// NATS will reject duplicate messages within this time window
    /// </summary>
    public TimeSpan DuplicateWindow { get; set; } = TimeSpan.FromMinutes(2);

    /// <summary>
    /// Create a StreamConfig from these options
    /// </summary>
    internal StreamConfig CreateStreamConfig(string streamName, string[] subjects)
    {
        var config = new StreamConfig(streamName, subjects)
        {
            Retention = Retention,
            MaxAge = MaxAge,
            MaxMsgs = MaxMessages,
            Storage = Storage,
            Discard = Discard,
            DuplicateWindow = DuplicateWindow
        };

        // Optional settings
        if (MaxBytes > 0)
            config.MaxBytes = MaxBytes;

        if (Replicas > 1)
            config.NumReplicas = Replicas;

        if (Compression != StreamConfigCompression.None)
            config.Compression = Compression;

        if (MaxMessageSize > 0)
            config.MaxMsgSize = (int)MaxMessageSize;

        return config;
    }
}

