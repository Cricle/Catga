namespace Catga.Transport;

/// <summary>
/// Compressed message transport interface
/// Reduces network bandwidth by compressing message payloads
/// </summary>
public interface ICompressedMessageTransport : IMessageTransport
{
    /// <summary>
    /// Compression options
    /// </summary>
    public CompressionTransportOptions CompressionOptions { get; }
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
    public System.IO.Compression.CompressionLevel Level { get; set; } =
        System.IO.Compression.CompressionLevel.Fastest;

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

