using System.IO.Compression;
using Catga.Transport;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Transport;

/// <summary>
/// Tests for TransportContext and related types
/// </summary>
public class TransportContextTests
{
    [Fact]
    public void TransportContext_ShouldBeCreatable()
    {
        // Act
        var context = new TransportContext
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            CorrelationId = 67890L
        };

        // Assert
        context.MessageId.Should().Be(12345L);
        context.MessageType.Should().Be("TestMessage");
        context.CorrelationId.Should().Be(67890L);
    }

    [Fact]
    public void TransportContext_Metadata_ShouldBeSettable()
    {
        // Act
        var context = new TransportContext
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            Metadata = new Dictionary<string, string>
            {
                ["key1"] = "value1",
                ["key2"] = "value2"
            }
        };

        // Assert
        context.Metadata.Should().HaveCount(2);
        context.Metadata!["key1"].Should().Be("value1");
    }

    [Fact]
    public void TransportContext_SentAt_ShouldBeSettable()
    {
        // Arrange
        var sentAt = DateTime.UtcNow;

        // Act
        var context = new TransportContext
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            SentAt = sentAt
        };

        // Assert
        context.SentAt.Should().Be(sentAt);
    }

    [Fact]
    public void TransportContext_RetryCount_ShouldBeSettable()
    {
        // Act
        var context = new TransportContext
        {
            MessageId = 12345L,
            MessageType = "TestMessage",
            RetryCount = 3
        };

        // Assert
        context.RetryCount.Should().Be(3);
    }

    [Fact]
    public void TransportContext_IsStruct_ShouldBeValueType()
    {
        // Assert
        typeof(TransportContext).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void BatchTransportOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new BatchTransportOptions();

        // Assert
        options.MaxBatchSize.Should().Be(100);
        options.BatchTimeout.Should().Be(TimeSpan.FromMilliseconds(100));
        options.EnableAutoBatching.Should().BeTrue();
        options.MaxBatchSizeBytes.Should().Be(1024 * 1024);
    }

    [Fact]
    public void BatchTransportOptions_CanBeModified()
    {
        // Act
        var options = new BatchTransportOptions
        {
            MaxBatchSize = 50,
            BatchTimeout = TimeSpan.FromMilliseconds(200),
            EnableAutoBatching = false,
            MaxBatchSizeBytes = 512 * 1024
        };

        // Assert
        options.MaxBatchSize.Should().Be(50);
        options.BatchTimeout.Should().Be(TimeSpan.FromMilliseconds(200));
        options.EnableAutoBatching.Should().BeFalse();
    }

    [Fact]
    public void CompressionTransportOptions_ShouldHaveDefaults()
    {
        // Act
        var options = new CompressionTransportOptions();

        // Assert
        options.EnableCompression.Should().BeTrue();
        options.Algorithm.Should().Be(CompressionAlgorithm.GZip);
        options.Level.Should().Be(CompressionLevel.Fastest);
        options.MinSizeToCompress.Should().Be(1024);
    }

    [Fact]
    public void CompressionTransportOptions_CanBeModified()
    {
        // Act
        var options = new CompressionTransportOptions
        {
            EnableCompression = false,
            Algorithm = CompressionAlgorithm.Brotli,
            Level = CompressionLevel.Optimal,
            MinSizeToCompress = 2048
        };

        // Assert
        options.EnableCompression.Should().BeFalse();
        options.Algorithm.Should().Be(CompressionAlgorithm.Brotli);
        options.Level.Should().Be(CompressionLevel.Optimal);
    }

    [Fact]
    public void CompressionAlgorithm_ShouldHaveExpectedValues()
    {
        // Assert
        CompressionAlgorithm.None.Should().BeDefined();
        CompressionAlgorithm.GZip.Should().BeDefined();
        CompressionAlgorithm.Brotli.Should().BeDefined();
        CompressionAlgorithm.Deflate.Should().BeDefined();
    }
}
