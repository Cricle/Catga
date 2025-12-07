using System.Text;
using Catga.Transport;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Transport;

public class MessageCompressorTests
{
    [Fact]
    public void Compress_None_ReturnsOriginal()
    {
        var data = Encoding.UTF8.GetBytes("Hello, World!");

        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.None);

        compressed.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Compress_GZip_CompressesData()
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 1000));

        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.GZip);

        compressed.Length.Should().BeLessThan(data.Length);
    }

    [Fact]
    public void Compress_Brotli_CompressesData()
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 1000));

        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.Brotli);

        compressed.Length.Should().BeLessThan(data.Length);
    }

    [Fact]
    public void Compress_Deflate_CompressesData()
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 1000));

        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.Deflate);

        compressed.Length.Should().BeLessThan(data.Length);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.GZip)]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.Deflate)]
    public void CompressDecompress_RoundTrip(CompressionAlgorithm algorithm)
    {
        var original = Encoding.UTF8.GetBytes("Hello, World! This is a test message for compression.");

        var compressed = MessageCompressor.Compress(original, algorithm);
        var decompressed = MessageCompressor.Decompress(compressed);

        decompressed.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void Decompress_UncompressedData_ReturnsOriginal()
    {
        var data = new byte[] { 0, 0, 0, 0, 5, 1, 2, 3, 4, 5 }; // Algorithm = None

        var result = MessageCompressor.Decompress(data);

        result.Should().BeEquivalentTo(new byte[] { 1, 2, 3, 4, 5 });
    }

    [Fact]
    public void IsCompressed_CompressedData_ReturnsTrue()
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 100));
        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.GZip);

        MessageCompressor.IsCompressed(compressed).Should().BeTrue();
    }

    [Fact]
    public void IsCompressed_UncompressedData_ReturnsFalse()
    {
        var data = new byte[] { 0, 0, 0, 0, 5, 1, 2, 3, 4, 5 }; // Algorithm = None

        MessageCompressor.IsCompressed(data).Should().BeFalse();
    }

    [Fact]
    public void IsCompressed_TooShortData_ReturnsFalse()
    {
        var data = new byte[] { 1, 2, 3 };

        MessageCompressor.IsCompressed(data).Should().BeFalse();
    }

    [Fact]
    public void EstimateCompressionRatio_CalculatesCorrectly()
    {
        var original = new byte[100];
        var compressed = new byte[30];

        var ratio = MessageCompressor.EstimateCompressionRatio(original, compressed);

        ratio.Should().BeApproximately(0.3, 0.01);
    }

    [Fact]
    public void EstimateCompressionRatio_EmptyOriginal_ReturnsOne()
    {
        var original = Array.Empty<byte>();
        var compressed = Array.Empty<byte>();

        var ratio = MessageCompressor.EstimateCompressionRatio(original, compressed);

        ratio.Should().Be(1.0);
    }

    [Fact]
    public void Compress_EmptyData_ReturnsEmpty()
    {
        var data = Array.Empty<byte>();

        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.GZip);

        compressed.Should().BeEmpty();
    }

    [Fact]
    public void CompressionStats_CalculatesProperties()
    {
        var stats = new CompressionStats
        {
            OriginalBytes = 1000,
            CompressedBytes = 300
        };

        stats.Ratio.Should().BeApproximately(0.3, 0.01);
        stats.SavedBytes.Should().Be(700);
    }

    [Fact]
    public void CompressionStats_ZeroOriginal_RatioIsOne()
    {
        var stats = new CompressionStats
        {
            OriginalBytes = 0,
            CompressedBytes = 0
        };

        stats.Ratio.Should().Be(1.0);
        stats.SavedBytes.Should().Be(0);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.GZip)]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.Deflate)]
    public void CompressDecompress_LargeData_RoundTrip(CompressionAlgorithm algorithm)
    {
        var random = new Random(42);
        var original = new byte[10000];
        random.NextBytes(original);

        var compressed = MessageCompressor.Compress(original, algorithm);
        var decompressed = MessageCompressor.Decompress(compressed);

        decompressed.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void CompressToBuffer_None_CopiesData()
    {
        var data = Encoding.UTF8.GetBytes("Hello");
        var buffer = new byte[100];

        var length = MessageCompressor.CompressToBuffer(data, buffer, CompressionAlgorithm.None);

        length.Should().Be(data.Length);
        buffer.Take(length).Should().BeEquivalentTo(data);
    }

    [Fact]
    public void CompressToBuffer_GZip_CompressesData()
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 500));
        var buffer = new byte[1000];

        var length = MessageCompressor.CompressToBuffer(data, buffer, CompressionAlgorithm.GZip);

        length.Should().BeLessThan(data.Length);
        length.Should().BeGreaterThan(5); // Header size
    }

    [Fact]
    public void CompressToBuffer_EmptyData_ReturnsZero()
    {
        var data = Array.Empty<byte>();
        var buffer = new byte[100];

        var length = MessageCompressor.CompressToBuffer(data, buffer, CompressionAlgorithm.GZip);

        length.Should().Be(0);
    }

    [Fact]
    public void Decompress_TooShortData_ReturnsOriginal()
    {
        var data = new byte[] { 1, 2, 3 };

        var result = MessageCompressor.Decompress(data);

        result.Should().BeEquivalentTo(data);
    }
}
