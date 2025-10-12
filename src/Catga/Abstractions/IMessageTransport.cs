using System.Diagnostics.CodeAnalysis;
using System.IO.Compression;

namespace Catga.Transport;

/// <summary>Unified message transport interface</summary>
public interface IMessageTransport
{
    public Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class;
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(TMessage message, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class;
    public Task SubscribeAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(Func<TMessage, TransportContext, Task> handler, CancellationToken cancellationToken = default) where TMessage : class;
    public Task PublishBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class;
    public Task SendBatchAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TMessage>(IEnumerable<TMessage> messages, string destination, TransportContext? context = null, CancellationToken cancellationToken = default) where TMessage : class;
    public string Name { get; }
    public BatchTransportOptions? BatchOptions { get; }
    public CompressionTransportOptions? CompressionOptions { get; }
}

/// <summary>Transport context - carries message metadata (zero-allocation struct)</summary>
public readonly struct TransportContext
{
    public string? MessageId { get; init; }
    public string? CorrelationId { get; init; }
    public string? MessageType { get; init; }
    public DateTime? SentAt { get; init; }
    public int RetryCount { get; init; }
    public Dictionary<string, string>? Metadata { get; init; }
}

/// <summary>Batch transport options (immutable record)</summary>
public record BatchTransportOptions
{
    public int MaxBatchSize { get; init; } = 100;
    public TimeSpan BatchTimeout { get; init; } = TimeSpan.FromMilliseconds(100);
    public bool EnableAutoBatching { get; init; } = true;
    public int MaxBatchSizeBytes { get; init; } = 1024 * 1024;
}

/// <summary>Compression transport options (immutable record)</summary>
public record CompressionTransportOptions
{
    public bool EnableCompression { get; init; } = true;
    public CompressionAlgorithm Algorithm { get; init; } = CompressionAlgorithm.GZip;
    public CompressionLevel Level { get; init; } = CompressionLevel.Fastest;
    public int MinSizeToCompress { get; init; } = 1024;
    public double ExpectedCompressionRatio { get; init; } = 0.3;
}

/// <summary>Compression algorithms</summary>
public enum CompressionAlgorithm
{
    None = 0,
    GZip = 1,
    Brotli = 2,
    Deflate = 3
}
