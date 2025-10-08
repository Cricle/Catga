using System.Diagnostics.CodeAnalysis;

namespace Catga.Transport;

/// <summary>
/// Batch message transport interface - optimized for bulk operations
/// Reduces network round-trips by batching multiple messages
/// </summary>
public interface IBatchMessageTransport : IMessageTransport
{
    /// <summary>
    /// Publish multiple messages in a single batch (optimized)
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
    /// Get batch size configuration
    /// </summary>
    public BatchTransportOptions BatchOptions { get; }
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

