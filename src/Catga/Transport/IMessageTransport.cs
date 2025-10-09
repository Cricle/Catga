using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Catga.Transport;

/// <summary>
/// Unified message transport interface - handles message sending, receiving, batching, and compression
/// Combines IMessageTransport, IBatchMessageTransport, and ICompressedMessageTransport
/// </summary>
public interface IMessageTransport
{
    /// <summary>
    /// Publish message to transport layer
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Send message to specific destination
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Subscribe to messages
    /// </summary>
    [RequiresUnreferencedCode("Message deserialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message deserialization may require runtime code generation")]
    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields | DynamicallyAccessedMemberTypes.PublicConstructors)] TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Publish multiple messages in a single batch (optimized for bulk operations)
    /// Reduces network round-trips by batching multiple messages
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Send multiple messages to specific destination in a batch
    /// </summary>
    [RequiresUnreferencedCode("Message serialization may require types that cannot be statically analyzed")]
    [RequiresDynamicCode("Message serialization may require runtime code generation")]
    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        IEnumerable<TMessage> messages,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// Transport name (NATS, Redis, RabbitMQ, etc.)
    /// </summary>
    public string Name { get; }

    /// <summary>
    /// Batch transport options (optional, null if batching not supported)
    /// </summary>
    public BatchTransportOptions? BatchOptions { get; }

    /// <summary>
    /// Compression options (optional, null if compression not supported)
    /// </summary>
    public CompressionTransportOptions? CompressionOptions { get; }
}

/// <summary>
/// Transport context - carries message metadata
/// </summary>
public class TransportContext
{
    public string? MessageId { get; set; }
    public string? CorrelationId { get; set; }
    public string? MessageType { get; set; }
    public DateTime? SentAt { get; set; }
    public int RetryCount { get; set; }
    public Dictionary<string, string>? Metadata { get; set; }
}

/// <summary>
/// Batch transport configuration options
/// </summary>
public class BatchTransportOptions
{
    /// <summary>
    /// Maximum batch size (default: 100)
    /// </summary>
    public int MaxBatchSize { get; set; } = 100;

    /// <summary>
    /// Batch timeout - flush after this duration even if not full (default: 100ms)
    /// </summary>
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// Enable auto-batching (default: true)
    /// Automatically batch individual sends into bulk operations
    /// </summary>
    public bool EnableAutoBatching { get; set; } = true;

    /// <summary>
    /// Maximum memory per batch (default: 1MB)
    /// Prevents oversized batches from consuming too much memory
    /// </summary>
    public int MaxBatchSizeBytes { get; set; } = 1024 * 1024;
}

/// <summary>
/// Compression transport options
/// </summary>
public class CompressionTransportOptions
{
    /// <summary>
    /// Enable compression (default: true)
    /// </summary>
    public bool EnableCompression { get; set; } = true;

    /// <summary>
    /// Compression algorithm (default: GZip)
    /// </summary>
    public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.GZip;

    /// <summary>
    /// Compression level (default: Fastest)
    /// </summary>
    public CompressionLevel Level { get; set; } = CompressionLevel.Fastest;

    /// <summary>
    /// Minimum size to compress (default: 1KB)
    /// Don't compress small messages (compression overhead > benefit)
    /// </summary>
    public int MinSizeToCompress { get; set; } = 1024;

    /// <summary>
    /// Expected compression ratio (default: 0.3 = 70% reduction)
    /// Used for buffer pre-allocation
    /// </summary>
    public double ExpectedCompressionRatio { get; set; } = 0.3;
}

/// <summary>
/// Supported compression algorithms
/// </summary>
public enum CompressionAlgorithm
{
    /// <summary>
    /// No compression
    /// </summary>
    None = 0,

    /// <summary>
    /// GZip compression (standard, good ratio)
    /// </summary>
    GZip = 1,

    /// <summary>
    /// Brotli compression (better ratio, slower)
    /// </summary>
    Brotli = 2,

    /// <summary>
    /// Deflate compression (faster, slightly worse ratio)
    /// </summary>
    Deflate = 3
}

