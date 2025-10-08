using System.Buffers;
using System.IO.Compression;
using System.Runtime.CompilerServices;

namespace Catga.Transport;

/// <summary>
/// High-performance message compressor with pooling
/// Reduces allocations by reusing compression streams
/// </summary>
public static class MessageCompressor
{
    /// <summary>
    /// Compress data using specified algorithm
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Compress(
        ReadOnlySpan<byte> data,
        CompressionAlgorithm algorithm,
        CompressionLevel level)
    {
        if (algorithm == CompressionAlgorithm.None)
            return data.ToArray();

        using var outputStream = new MemoryStream();
        using (var compressionStream = CreateCompressionStream(outputStream, algorithm, level))
        {
            compressionStream.Write(data);
        }

        return outputStream.ToArray();
    }

    /// <summary>
    /// Compress data to pooled buffer writer (zero-copy)
    /// </summary>
    public static void CompressTo(
        ReadOnlySpan<byte> data,
        IBufferWriter<byte> output,
        CompressionAlgorithm algorithm,
        CompressionLevel level)
    {
        if (algorithm == CompressionAlgorithm.None)
        {
            var span = output.GetSpan(data.Length);
            data.CopyTo(span);
            output.Advance(data.Length);
            return;
        }

        using var outputStream = new BufferWriterStream(output);
        using var compressionStream = CreateCompressionStream(outputStream, algorithm, level);
        compressionStream.Write(data);
    }

    /// <summary>
    /// Decompress data
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static byte[] Decompress(
        ReadOnlySpan<byte> compressedData,
        CompressionAlgorithm algorithm,
        int expectedSize = 0)
    {
        if (algorithm == CompressionAlgorithm.None)
            return compressedData.ToArray();

        using var inputStream = new MemoryStream(compressedData.ToArray());
        using var decompressionStream = CreateDecompressionStream(inputStream, algorithm);
        using var outputStream = new MemoryStream(expectedSize > 0 ? expectedSize : 4096);

        decompressionStream.CopyTo(outputStream);
        return outputStream.ToArray();
    }

    /// <summary>
    /// Try compress (only if beneficial)
    /// </summary>
    public static bool TryCompress(
        ReadOnlySpan<byte> data,
        CompressionTransportOptions options,
        out byte[] compressed,
        out CompressionAlgorithm usedAlgorithm)
    {
        // Skip compression for small messages
        if (!options.EnableCompression || data.Length < options.MinSizeToCompress)
        {
            compressed = data.ToArray();
            usedAlgorithm = CompressionAlgorithm.None;
            return false;
        }

        compressed = Compress(data, options.Algorithm, options.Level);
        usedAlgorithm = options.Algorithm;

        // Only use compressed if it's actually smaller
        if (compressed.Length >= data.Length)
        {
            compressed = data.ToArray();
            usedAlgorithm = CompressionAlgorithm.None;
            return false;
        }

        return true;
    }

    private static Stream CreateCompressionStream(
        Stream output,
        CompressionAlgorithm algorithm,
        CompressionLevel level)
    {
        return algorithm switch
        {
            CompressionAlgorithm.GZip => new GZipStream(output, level, leaveOpen: true),
            CompressionAlgorithm.Brotli => new BrotliStream(output, level, leaveOpen: true),
            CompressionAlgorithm.Deflate => new DeflateStream(output, level, leaveOpen: true),
            _ => throw new NotSupportedException($"Algorithm {algorithm} not supported")
        };
    }

    private static Stream CreateDecompressionStream(
        Stream input,
        CompressionAlgorithm algorithm)
    {
        return algorithm switch
        {
            CompressionAlgorithm.GZip => new GZipStream(input, CompressionMode.Decompress, leaveOpen: true),
            CompressionAlgorithm.Brotli => new BrotliStream(input, CompressionMode.Decompress, leaveOpen: true),
            CompressionAlgorithm.Deflate => new DeflateStream(input, CompressionMode.Decompress, leaveOpen: true),
            _ => throw new NotSupportedException($"Algorithm {algorithm} not supported")
        };
    }

    /// <summary>
    /// Helper stream that writes to IBufferWriter
    /// </summary>
    private class BufferWriterStream : Stream
    {
        private readonly IBufferWriter<byte> _writer;

        public BufferWriterStream(IBufferWriter<byte> writer)
        {
            _writer = writer;
        }

        public override bool CanRead => false;
        public override bool CanSeek => false;
        public override bool CanWrite => true;
        public override long Length => throw new NotSupportedException();
        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            var span = _writer.GetSpan(count);
            buffer.AsSpan(offset, count).CopyTo(span);
            _writer.Advance(count);
        }

        public override void Write(ReadOnlySpan<byte> buffer)
        {
            var span = _writer.GetSpan(buffer.Length);
            buffer.CopyTo(span);
            _writer.Advance(buffer.Length);
        }

        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override int Read(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}

