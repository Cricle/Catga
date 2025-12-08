using Catga.Core;
using FluentAssertions;
using System.Buffers;

namespace Catga.Tests.Core;

/// <summary>
/// Extended unit tests for PooledBufferWriter - edge cases and stress tests
/// </summary>
public class PooledBufferWriterExtendedTests
{
    [Fact]
    public void Constructor_WithDefaultCapacity_ShouldSucceed()
    {
        // Act
        using var writer = new PooledBufferWriter<byte>();

        // Assert
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithCustomCapacity_ShouldSucceed()
    {
        // Act
        using var writer = new PooledBufferWriter<byte>(1024);

        // Assert
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithZeroCapacity_ShouldThrow()
    {
        // Act
        var act = () => new PooledBufferWriter<byte>(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_ShouldThrow()
    {
        // Act
        var act = () => new PooledBufferWriter<byte>(-1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void GetSpan_ShouldReturnWritableSpan()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);

        // Act
        var span = writer.GetSpan(10);

        // Assert
        span.Length.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public void GetMemory_ShouldReturnWritableMemory()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);

        // Act
        var memory = writer.GetMemory(10);

        // Assert
        memory.Length.Should().BeGreaterOrEqualTo(10);
    }

    [Fact]
    public void Advance_ShouldUpdateWrittenCount()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        var span = writer.GetSpan(10);
        span[0] = 1;
        span[1] = 2;

        // Act
        writer.Advance(2);

        // Assert
        writer.WrittenCount.Should().Be(2);
    }

    [Fact]
    public void Advance_WithNegativeCount_ShouldThrow()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);

        // Act
        var act = () => writer.Advance(-1);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void Advance_PastBufferEnd_ShouldThrow()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        writer.GetSpan(10);

        // Act
        var act = () => writer.Advance(1000000);

        // Assert
        act.Should().Throw<InvalidOperationException>();
    }

    [Fact]
    public void WrittenSpan_ShouldReturnWrittenData()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        var span = writer.GetSpan(3);
        span[0] = 10;
        span[1] = 20;
        span[2] = 30;
        writer.Advance(3);

        // Act
        var writtenSpan = writer.WrittenSpan;

        // Assert
        writtenSpan.Length.Should().Be(3);
        writtenSpan[0].Should().Be(10);
        writtenSpan[1].Should().Be(20);
        writtenSpan[2].Should().Be(30);
    }

    [Fact]
    public void WrittenMemory_ShouldReturnWrittenData()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        var span = writer.GetSpan(2);
        span[0] = 100;
        span[1] = 200;
        writer.Advance(2);

        // Act
        var writtenMemory = writer.WrittenMemory;

        // Assert
        writtenMemory.Length.Should().Be(2);
        writtenMemory.Span[0].Should().Be(100);
        writtenMemory.Span[1].Should().Be(200);
    }

    [Fact]
    public void Clear_ShouldResetWrittenCount()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        var span = writer.GetSpan(10);
        writer.Advance(10);

        // Act
        writer.Clear();

        // Assert
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void Clear_ShouldAllowReuse()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        var span = writer.GetSpan(5);
        span[0] = 1;
        writer.Advance(5);
        writer.Clear();

        // Act
        span = writer.GetSpan(3);
        span[0] = 99;
        writer.Advance(3);

        // Assert
        writer.WrittenCount.Should().Be(3);
        writer.WrittenSpan[0].Should().Be(99);
    }

    [Fact]
    public void Dispose_ShouldPreventFurtherUse()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(256);
        writer.Dispose();

        // Act & Assert
        FluentActions.Invoking(() => writer.GetSpan(10)).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => writer.GetMemory(10)).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => writer.Advance(1)).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => { var _ = writer.WrittenSpan; }).Should().Throw<ObjectDisposedException>();
        FluentActions.Invoking(() => { var _ = writer.WrittenMemory; }).Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_CalledMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>(256);

        // Act
        var act = () =>
        {
            writer.Dispose();
            writer.Dispose();
            writer.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void AutoResize_WhenBufferFull_ShouldGrow()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(16);

        // Act - Write more than initial capacity
        for (var i = 0; i < 100; i++)
        {
            var span = writer.GetSpan(1);
            span[0] = (byte)(i % 256);
            writer.Advance(1);
        }

        // Assert
        writer.WrittenCount.Should().Be(100);
    }

    [Fact]
    public void LargeWrite_ShouldSucceed()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        const int largeSize = 10000;

        // Act
        var span = writer.GetSpan(largeSize);
        for (var i = 0; i < largeSize; i++)
        {
            span[i] = (byte)(i % 256);
        }
        writer.Advance(largeSize);

        // Assert
        writer.WrittenCount.Should().Be(largeSize);
    }

    [Fact]
    public void WithCustomPool_ShouldUseProvidedPool()
    {
        // Arrange
        var pool = ArrayPool<byte>.Create();

        // Act
        using var writer = new PooledBufferWriter<byte>(256, pool);
        var span = writer.GetSpan(10);
        writer.Advance(10);

        // Assert
        writer.WrittenCount.Should().Be(10);
    }

    [Fact]
    public void GetSpan_WithZeroHint_ShouldReturnAvailableSpace()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);

        // Act
        var span = writer.GetSpan(0);

        // Assert
        span.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void MultipleWriteOperations_ShouldAccumulate()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);

        // Act
        for (var batch = 0; batch < 5; batch++)
        {
            var span = writer.GetSpan(10);
            for (var i = 0; i < 10; i++)
            {
                span[i] = (byte)(batch * 10 + i);
            }
            writer.Advance(10);
        }

        // Assert
        writer.WrittenCount.Should().Be(50);
        writer.WrittenSpan[0].Should().Be(0);
        writer.WrittenSpan[10].Should().Be(10);
        writer.WrittenSpan[49].Should().Be(49);
    }
}






