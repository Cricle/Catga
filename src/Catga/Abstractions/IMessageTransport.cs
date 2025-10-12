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

/// <summary>Batch transport options</summary>
public class BatchTransportOptions
{
    public int MaxBatchSize { get; set; } = 100;
    public TimeSpan BatchTimeout { get; set; } = TimeSpan.FromMilliseconds(100);
    public bool EnableAutoBatching { get; set; } = true;
    public int MaxBatchSizeBytes { get; set; } = 1024 * 1024;
}

/// <summary>Compression transport options</summary>
public class CompressionTransportOptions
{
    public bool EnableCompression { get; set; } = true;
    public CompressionAlgorithm Algorithm { get; set; } = CompressionAlgorithm.GZip;
    public CompressionLevel Level { get; set; } = CompressionLevel.Fastest;
    public int MinSizeToCompress { get; set; } = 1024;
    public double ExpectedCompressionRatio { get; set; } = 0.3;
}

/// <summary>Compression algorithms</summary>
public enum CompressionAlgorithm
{
    None = 0,
    GZip = 1,
    Brotli = 2,
    Deflate = 3
}
