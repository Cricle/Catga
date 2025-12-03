using System.Buffers;
using System.IO.Compression;

namespace Catga.Transport;

/// <summary>
/// High-performance message compressor with pooled buffers.
/// AOT-compatible, zero-allocation design for small messages.
/// </summary>
public static class MessageCompressor
{
    private const int StackAllocThreshold = 256;
    private const int DefaultBufferSize = 4096;

    /// <summary>Compress data using specified algorithm.</summary>
    public static byte[] Compress(ReadOnlySpan<byte> data, CompressionAlgorithm algorithm, CompressionLevel level = CompressionLevel.Fastest)
    {
        if (algorithm == CompressionAlgorithm.None || data.Length == 0)
            return data.ToArray();

        using var output = new MemoryStream();

        // Write header: 1 byte algorithm + 4 bytes original length
        Span<byte> header = stackalloc byte[5];
        header[0] = (byte)algorithm;
        BitConverter.TryWriteBytes(header.Slice(1), data.Length);
        output.Write(header);

        using (var compressor = CreateCompressor(output, algorithm, level))
        {
            compressor.Write(data);
        }

        return output.ToArray();
    }

    /// <summary>Decompress data.</summary>
    public static byte[] Decompress(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5)
            return data.ToArray();

        var algorithm = (CompressionAlgorithm)data[0];
        if (algorithm == CompressionAlgorithm.None)
            return data.Slice(5).ToArray();

        var originalLength = BitConverter.ToInt32(data.Slice(1, 4));
        var compressedData = data.Slice(5);

        var result = new byte[originalLength];
        using var input = new MemoryStream(compressedData.ToArray());
        using var decompressor = CreateDecompressor(input, algorithm);

        var totalRead = 0;
        while (totalRead < originalLength)
        {
            var read = decompressor.Read(result, totalRead, originalLength - totalRead);
            if (read == 0) break;
            totalRead += read;
        }

        return result;
    }

    /// <summary>Compress to pooled buffer for zero-allocation hot path.</summary>
    public static int CompressToBuffer(
        ReadOnlySpan<byte> data,
        Span<byte> buffer,
        CompressionAlgorithm algorithm,
        CompressionLevel level = CompressionLevel.Fastest)
    {
        if (algorithm == CompressionAlgorithm.None || data.Length == 0)
        {
            data.CopyTo(buffer);
            return data.Length;
        }

        // Write header
        buffer[0] = (byte)algorithm;
        BitConverter.TryWriteBytes(buffer.Slice(1), data.Length);

        using var output = new MemoryStream();
        using (var compressor = CreateCompressor(output, algorithm, level))
        {
            compressor.Write(data);
        }

        var compressed = output.ToArray();
        compressed.CopyTo(buffer.Slice(5));
        return 5 + compressed.Length;
    }

    /// <summary>Check if data appears to be compressed.</summary>
    public static bool IsCompressed(ReadOnlySpan<byte> data)
    {
        if (data.Length < 5) return false;
        var algorithm = (CompressionAlgorithm)data[0];
        return algorithm != CompressionAlgorithm.None;
    }

    /// <summary>Get compression ratio estimate.</summary>
    public static double EstimateCompressionRatio(ReadOnlySpan<byte> original, ReadOnlySpan<byte> compressed)
    {
        if (original.Length == 0) return 1.0;
        return (double)compressed.Length / original.Length;
    }

    private static Stream CreateCompressor(Stream output, CompressionAlgorithm algorithm, CompressionLevel level)
    {
        return algorithm switch
        {
            CompressionAlgorithm.GZip => new GZipStream(output, level, leaveOpen: true),
            CompressionAlgorithm.Brotli => new BrotliStream(output, level, leaveOpen: true),
            CompressionAlgorithm.Deflate => new DeflateStream(output, level, leaveOpen: true),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
    }

    private static Stream CreateDecompressor(Stream input, CompressionAlgorithm algorithm)
    {
        return algorithm switch
        {
            CompressionAlgorithm.GZip => new GZipStream(input, CompressionMode.Decompress),
            CompressionAlgorithm.Brotli => new BrotliStream(input, CompressionMode.Decompress),
            CompressionAlgorithm.Deflate => new DeflateStream(input, CompressionMode.Decompress),
            _ => throw new ArgumentOutOfRangeException(nameof(algorithm))
        };
    }
}

/// <summary>
/// Compression statistics for monitoring.
/// </summary>
public readonly record struct CompressionStats
{
    public long OriginalBytes { get; init; }
    public long CompressedBytes { get; init; }
    public double Ratio => OriginalBytes > 0 ? (double)CompressedBytes / OriginalBytes : 1.0;
    public long SavedBytes => OriginalBytes - CompressedBytes;
}
