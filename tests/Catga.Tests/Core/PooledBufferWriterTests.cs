using Catga.Core;
using FluentAssertions;
using System.Buffers;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for PooledBufferWriter
/// Coverage: IBufferWriter interface, growth, pooling, disposal
/// </summary>
public class PooledBufferWriterTests
{
    #region Constructor Tests

    [Fact]
    public void Constructor_WithDefaultCapacity_ShouldInitialize()
    {
        // Arrange & Act
        using var writer = new PooledBufferWriter<byte>();

        // Assert
        writer.WrittenCount.Should().Be(0);
        writer.WrittenSpan.Length.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithCustomCapacity_ShouldInitialize()
    {
        // Arrange & Act
        using var writer = new PooledBufferWriter<byte>(1024);

        // Assert
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void Constructor_WithZeroCapacity_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => new PooledBufferWriter<byte>(0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("initialCapacity");
    }

    [Fact]
    public void Constructor_WithNegativeCapacity_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => new PooledBufferWriter<byte>(-1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("initialCapacity");
    }

    #endregion

    #region IBufferWriter Tests

    [Fact]
    public void GetSpan_ShouldReturnWritableSpan()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act
        var span = writer.GetSpan();

        // Assert
        span.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetMemory_ShouldReturnWritableMemory()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act
        var memory = writer.GetMemory();

        // Assert
        memory.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void GetSpan_WithSizeHint_ShouldReturnAtLeastRequestedSize()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act
        var span = writer.GetSpan(512);

        // Assert
        span.Length.Should().BeGreaterOrEqualTo(512);
    }

    [Fact]
    public void GetMemory_WithSizeHint_ShouldReturnAtLeastRequestedSize()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act
        var memory = writer.GetMemory(512);

        // Assert
        memory.Length.Should().BeGreaterOrEqualTo(512);
    }

    [Fact]
    public void Advance_ShouldIncreaseWrittenCount()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();
        var span = writer.GetSpan(10);
        for (int i = 0; i < 10; i++)
            span[i] = (byte)i;

        // Act
        writer.Advance(10);

        // Assert
        writer.WrittenCount.Should().Be(10);
    }

    [Fact]
    public void Advance_WithNegativeCount_ShouldThrow()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act & Assert
        var act = () => writer.Advance(-1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("count");
    }

    [Fact]
    public void Advance_BeyondBufferEnd_ShouldThrow()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(10);
        var span = writer.GetSpan();
        var spanLength = span.Length;

        // Act & Assert
        var act = () => writer.Advance(spanLength + 1);
        act.Should().Throw<InvalidOperationException>();
    }

    #endregion

    #region Write and Read Tests

    [Fact]
    public void Write_ShouldBeReadableFromWrittenSpan()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();
        var data = new byte[] { 1, 2, 3, 4, 5 };

        // Act
        var span = writer.GetSpan(data.Length);
        data.CopyTo(span);
        writer.Advance(data.Length);

        // Assert
        writer.WrittenSpan.ToArray().Should().Equal(data);
    }

    [Fact]
    public void Write_ShouldBeReadableFromWrittenMemory()
    {
        // Arrange
        using var writer = new PooledBufferWriter<int>();
        var data = new int[] { 10, 20, 30, 40, 50 };

        // Act
        var memory = writer.GetMemory(data.Length);
        data.CopyTo(memory.Span);
        writer.Advance(data.Length);

        // Assert
        writer.WrittenMemory.ToArray().Should().Equal(data);
    }

    [Fact]
    public void Write_MultipleOperations_ShouldAccumulate()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act
        // Write first batch
        {
            var span1 = writer.GetSpan(3);
            span1[0] = 1;
            span1[1] = 2;
            span1[2] = 3;
            writer.Advance(3);
        }

        // Write second batch
        {
            var span2 = writer.GetSpan(2);
            span2[0] = 4;
            span2[1] = 5;
            writer.Advance(2);
        }

        // Assert
        writer.WrittenCount.Should().Be(5);
        writer.WrittenSpan.ToArray().Should().Equal(new byte[] { 1, 2, 3, 4, 5 });
    }

    #endregion

    #region Buffer Growth Tests

    [Fact]
    public void Write_ExceedingInitialCapacity_ShouldGrowBuffer()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(10);

        // Act - Write more than initial capacity
        for (int i = 0; i < 100; i++)
        {
            var span = writer.GetSpan(1);
            span[0] = (byte)(i % 256);
            writer.Advance(1);
        }

        // Assert
        writer.WrittenCount.Should().Be(100);
    }

    [Fact]
    public void Write_LargeData_ShouldGrowBufferAutomatically()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>(256);
        var largeData = new byte[10000];
        for (int i = 0; i < largeData.Length; i++)
            largeData[i] = (byte)(i % 256);

        // Act
        var span = writer.GetSpan(largeData.Length);
        largeData.CopyTo(span);
        writer.Advance(largeData.Length);

        // Assert
        writer.WrittenCount.Should().Be(largeData.Length);
        writer.WrittenSpan.ToArray().Should().Equal(largeData);
    }

    #endregion

    #region Clear Tests

    [Fact]
    public void Clear_ShouldResetWrittenCount()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();
        var span = writer.GetSpan(10);
        writer.Advance(10);

        // Act
        writer.Clear();

        // Assert
        writer.WrittenCount.Should().Be(0);
        writer.WrittenSpan.Length.Should().Be(0);
    }

    [Fact]
    public void Clear_AfterMultipleWrites_ShouldResetCompletely()
    {
        // Arrange
        using var writer = new PooledBufferWriter<int>();
        for (int i = 0; i < 100; i++)
        {
            var memory = writer.GetMemory(1);
            memory.Span[0] = i;
            writer.Advance(1);
        }

        // Act
        writer.Clear();

        // Assert
        writer.WrittenCount.Should().Be(0);

        // Should be able to write again
        var newMemory = writer.GetMemory(5);
        newMemory.Span[0] = 999;
        writer.Advance(1);
        writer.WrittenMemory.Span[0].Should().Be(999);
    }

    #endregion

    #region Dispose Tests

    [Fact]
    public void Dispose_ShouldReleaseResources()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>();
        writer.GetSpan(100);
        writer.Advance(100);

        // Act
        writer.Dispose();

        // Assert - Accessing after dispose should throw
        Action act = () => writer.GetSpan();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_MultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>();

        // Act
        writer.Dispose();
        var act = () => writer.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void Dispose_ShouldPreventGetMemory()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>();
        writer.Dispose();

        // Act & Assert
        var act = () => writer.GetMemory();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_ShouldPreventAdvance()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>();
        writer.Dispose();

        // Act & Assert
        var act = () => writer.Advance(1);
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_ShouldPreventClear()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>();
        writer.Dispose();

        // Act & Assert
        var act = () => writer.Clear();
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_ShouldPreventWrittenSpanAccess()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>();
        writer.Dispose();

        // Act & Assert
        Action act = () => _ = writer.WrittenSpan;
        act.Should().Throw<ObjectDisposedException>();
    }

    [Fact]
    public void Dispose_ShouldPreventWrittenMemoryAccess()
    {
        // Arrange
        var writer = new PooledBufferWriter<byte>();
        writer.Dispose();

        // Act & Assert
        var act = () => _ = writer.WrittenMemory;
        act.Should().Throw<ObjectDisposedException>();
    }

    #endregion

    #region Type Tests

    [Fact]
    public void Write_WithDifferentTypes_ShouldWork()
    {
        // Int
        using (var writer = new PooledBufferWriter<int>())
        {
            var span = writer.GetSpan(5);
            for (int i = 0; i < 5; i++)
                span[i] = i * 10;
            writer.Advance(5);
            writer.WrittenSpan.ToArray().Should().Equal(new[] { 0, 10, 20, 30, 40 });
        }

        // String
        using (var writer = new PooledBufferWriter<string>())
        {
            var span = writer.GetSpan(3);
            span[0] = "Hello";
            span[1] = "World";
            span[2] = "!";
            writer.Advance(3);
            writer.WrittenSpan.ToArray().Should().Equal(new[] { "Hello", "World", "!" });
        }
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void GetSpan_WithZeroSizeHint_ShouldReturnNonEmptySpan()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act
        var span = writer.GetSpan(0);

        // Assert
        span.Length.Should().BeGreaterThan(0);
    }

    [Fact]
    public void Advance_WithZero_ShouldNotChangeWrittenCount()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();
        writer.GetSpan(10);
        writer.Advance(5);

        // Act
        writer.Advance(0);

        // Assert
        writer.WrittenCount.Should().Be(5);
    }

    [Fact]
    public void Write_EmptyData_ShouldWork()
    {
        // Arrange
        using var writer = new PooledBufferWriter<byte>();

        // Act
        // Don't advance at all

        // Assert
        writer.WrittenCount.Should().Be(0);
        writer.WrittenSpan.Length.Should().Be(0);
        writer.WrittenMemory.Length.Should().Be(0);
    }

    #endregion

    #region Custom ArrayPool Tests

    [Fact]
    public void Constructor_WithCustomArrayPool_ShouldUseCustomPool()
    {
        // Arrange
        var customPool = ArrayPool<byte>.Create();

        // Act
        using var writer = new PooledBufferWriter<byte>(256, customPool);
        var span = writer.GetSpan(100);
        writer.Advance(100);

        // Assert
        writer.WrittenCount.Should().Be(100);
        // Custom pool is used internally, but we can't directly verify it
        // The fact that it doesn't throw is sufficient
    }

    #endregion
}







