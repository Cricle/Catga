using Catga.Transport;
using FluentAssertions;
using System.IO.Compression;
using System.Text;

namespace Catga.Tests.Transport;

/// <summary>
/// Comprehensive tests for MessageCompressor
/// </summary>
public class MessageCompressorTests
{
    #region Compress Tests

    [Fact]
    public void Compress_WithNoneAlgorithm_ShouldReturnOriginalData()
    {
        var data = Encoding.UTF8.GetBytes("Hello, World!");
        
        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.None);
        
        compressed.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Compress_WithEmptyData_ShouldReturnEmpty()
    {
        var data = Array.Empty<byte>();
        
        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.GZip);
        
        compressed.Should().BeEmpty();
    }

    [Theory]
    [InlineData(CompressionAlgorithm.GZip)]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.Deflate)]
    public void Compress_WithAllAlgorithms_ShouldCompressData(CompressionAlgorithm algorithm)
    {
        var data = Encoding.UTF8.GetBytes(new string('A', 1000));
        
        var compressed = MessageCompressor.Compress(data, algorithm);
        
        compressed.Should().NotBeEmpty();
        compressed.Length.Should().BeLessThan(data.Length);
    }

    [Theory]
    [InlineData(CompressionLevel.Fastest)]
    [InlineData(CompressionLevel.Optimal)]
    [InlineData(CompressionLevel.SmallestSize)]
    public void Compress_WithDifferentLevels_ShouldWork(CompressionLevel level)
    {
        var data = Encoding.UTF8.GetBytes(new string('B', 1000));
        
        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.GZip, level);
        
        compressed.Should().NotBeEmpty();
    }

    [Fact]
    public void Compress_ShouldIncludeHeader()
    {
        var data = Encoding.UTF8.GetBytes(new string('C', 100));
        
        var compressed = MessageCompressor.Compress(data, CompressionAlgorithm.GZip);
        
        // First byte should be algorithm
        compressed[0].Should().Be((byte)CompressionAlgorithm.GZip);
        // Next 4 bytes should be original length
        var originalLength = BitConverter.ToInt32(compressed, 1);
        originalLength.Should().Be(data.Length);
    }

    #endregion

    #region Decompress Tests

    [Fact]
    public void Decompress_WithTooShortData_ShouldReturnOriginal()
    {
        var data = new byte[] { 1, 2, 3 };
        
        var decompressed = MessageCompressor.Decompress(data);
        
        decompressed.Should().BeEquivalentTo(data);
    }

    [Fact]
    public void Decompress_WithNoneAlgorithm_ShouldReturnDataWithoutHeader()
    {
        var originalData = Encoding.UTF8.GetBytes("Test data");
        var dataWithHeader = new byte[5 + originalData.Length];
        dataWithHeader[0] = (byte)CompressionAlgorithm.None;
        BitConverter.TryWriteBytes(dataWithHeader.AsSpan(1), originalData.Length);
        originalData.CopyTo(dataWithHeader, 5);
        
        var decompressed = MessageCompressor.Decompress(dataWithHeader);
        
        decompressed.Should().BeEquivalentTo(originalData);
    }

    [Theory]
    [InlineData(CompressionAlgorithm.GZip)]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.Deflate)]
    public void CompressDecompress_RoundTrip_ShouldReturnOriginal(CompressionAlgorithm algorithm)
    {
        var original = Encoding.UTF8.GetBytes("This is a test message for compression round-trip testing.");
        
        var compressed = MessageCompressor.Compress(original, algorithm);
        var decompressed = MessageCompressor.Decompress(compressed);
        
        decompressed.Should().BeEquivalentTo(original);
    }

    [Fact]
    public void CompressDecompress_LargeData_ShouldWork()
    {
        var original = Encoding.UTF8.GetBytes(new string('X', 100000));
        
        var compressed = MessageCompressor.Compress(original, CompressionAlgorithm.GZip);
        var decompressed = MessageCompressor.Decompress(compressed);
        
        decompressed.Should().BeEquivalentTo(original);
    }

    #endregion

    #region CompressToBuffer Tests

    [Fact]
    public void CompressToBuffer_WithNoneAlgorithm_ShouldCopyData()
    {
        var data = Encoding.UTF8.GetBytes("Hello");
        var buffer = new byte[100];
        
        var length = MessageCompressor.CompressToBuffer(data, buffer, CompressionAlgorithm.None);
        
        length.Should().Be(data.Length);
        buffer.AsSpan(0, length).ToArray().Should().BeEquivalentTo(data);
    }

    [Fact]
    public void CompressToBuffer_WithEmptyData_ShouldReturnZero()
    {
        var data = Array.Empty<byte>();
        var buffer = new byte[100];
        
        var length = MessageCompressor.CompressToBuffer(data, buffer, CompressionAlgorithm.GZip);
        
        length.Should().Be(0);
    }

    [Fact]
    public void CompressToBuffer_WithGZip_ShouldCompressToBuffer()
    {
        var data = Encoding.UTF8.GetBytes(new string('D', 500));
        var buffer = new byte[1000];
        
        var length = MessageCompressor.CompressToBuffer(data, buffer, CompressionAlgorithm.GZip);
        
        length.Should().BeGreaterThan(5); // At least header
        buffer[0].Should().Be((byte)CompressionAlgorithm.GZip);
    }

    #endregion

    #region IsCompressed Tests

    [Fact]
    public void IsCompressed_WithTooShortData_ShouldReturnFalse()
    {
        var data = new byte[] { 1, 2, 3 };
        
        var result = MessageCompressor.IsCompressed(data);
        
        result.Should().BeFalse();
    }

    [Fact]
    public void IsCompressed_WithNoneAlgorithm_ShouldReturnFalse()
    {
        var data = new byte[10];
        data[0] = (byte)CompressionAlgorithm.None;
        
        var result = MessageCompressor.IsCompressed(data);
        
        result.Should().BeFalse();
    }

    [Theory]
    [InlineData(CompressionAlgorithm.GZip)]
    [InlineData(CompressionAlgorithm.Brotli)]
    [InlineData(CompressionAlgorithm.Deflate)]
    public void IsCompressed_WithCompressedData_ShouldReturnTrue(CompressionAlgorithm algorithm)
    {
        var original = Encoding.UTF8.GetBytes("Test data for compression");
        var compressed = MessageCompressor.Compress(original, algorithm);
        
        var result = MessageCompressor.IsCompressed(compressed);
        
        result.Should().BeTrue();
    }

    #endregion

    #region EstimateCompressionRatio Tests

    [Fact]
    public void EstimateCompressionRatio_WithEmptyOriginal_ShouldReturnOne()
    {
        var original = Array.Empty<byte>();
        var compressed = Array.Empty<byte>();
        
        var ratio = MessageCompressor.EstimateCompressionRatio(original, compressed);
        
        ratio.Should().Be(1.0);
    }

    [Fact]
    public void EstimateCompressionRatio_WithCompressedData_ShouldReturnCorrectRatio()
    {
        var original = new byte[100];
        var compressed = new byte[50];
        
        var ratio = MessageCompressor.EstimateCompressionRatio(original, compressed);
        
        ratio.Should().Be(0.5);
    }

    [Fact]
    public void EstimateCompressionRatio_WithSameSize_ShouldReturnOne()
    {
        var original = new byte[100];
        var compressed = new byte[100];
        
        var ratio = MessageCompressor.EstimateCompressionRatio(original, compressed);
        
        ratio.Should().Be(1.0);
    }

    #endregion

    #region CompressionStats Tests

    [Fact]
    public void CompressionStats_DefaultValues_ShouldBeZero()
    {
        var stats = new CompressionStats();
        
        stats.OriginalBytes.Should().Be(0);
        stats.CompressedBytes.Should().Be(0);
        stats.Ratio.Should().Be(1.0);
        stats.SavedBytes.Should().Be(0);
    }

    [Fact]
    public void CompressionStats_WithValues_ShouldCalculateCorrectly()
    {
        var stats = new CompressionStats
        {
            OriginalBytes = 1000,
            CompressedBytes = 400
        };
        
        stats.Ratio.Should().Be(0.4);
        stats.SavedBytes.Should().Be(600);
    }

    [Fact]
    public void CompressionStats_WithZeroOriginal_ShouldReturnRatioOne()
    {
        var stats = new CompressionStats
        {
            OriginalBytes = 0,
            CompressedBytes = 100
        };
        
        stats.Ratio.Should().Be(1.0);
    }

    #endregion

    #region CompressionAlgorithm Tests

    [Theory]
    [InlineData(CompressionAlgorithm.None, 0)]
    [InlineData(CompressionAlgorithm.GZip, 1)]
    [InlineData(CompressionAlgorithm.Brotli, 2)]
    [InlineData(CompressionAlgorithm.Deflate, 3)]
    public void CompressionAlgorithm_AllValues_ShouldHaveCorrectValue(CompressionAlgorithm algorithm, int expectedValue)
    {
        ((int)algorithm).Should().Be(expectedValue);
    }

    #endregion
}
