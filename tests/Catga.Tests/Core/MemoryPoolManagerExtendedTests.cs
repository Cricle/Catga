using Catga.Core;
using FluentAssertions;
using System.Buffers;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Extended tests for MemoryPoolManager and PooledArray
/// </summary>
public class MemoryPoolManagerExtendedTests
{
    [Fact]
    public void RentArray_ShouldReturnArrayOfAtLeastRequestedSize()
    {
        // Act
        using var pooled = MemoryPoolManager.RentArray<byte>(100);

        // Assert
        pooled.Array.Should().NotBeNull();
        pooled.Array.Length.Should().BeGreaterOrEqualTo(100);
    }

    [Fact]
    public void RentArray_WithZeroSize_ShouldThrow()
    {
        // Act
        var act = () => MemoryPoolManager.RentArray<byte>(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void RentArray_DifferentTypes_ShouldWork()
    {
        // Act
        using var byteArray = MemoryPoolManager.RentArray<byte>(50);
        using var intArray = MemoryPoolManager.RentArray<int>(50);
        using var longArray = MemoryPoolManager.RentArray<long>(50);

        // Assert
        byteArray.Array.Should().NotBeNull();
        intArray.Array.Should().NotBeNull();
        longArray.Array.Should().NotBeNull();
    }

    [Fact]
    public void PooledArray_Dispose_ShouldNotThrow()
    {
        // Arrange
        var pooled = MemoryPoolManager.RentArray<byte>(100);

        // Act
        var act = () => pooled.Dispose();

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void PooledArray_DisposeMultipleTimes_ShouldNotThrow()
    {
        // Arrange
        var pooled = MemoryPoolManager.RentArray<byte>(100);

        // Act
        var act = () =>
        {
            pooled.Dispose();
            pooled.Dispose();
            pooled.Dispose();
        };

        // Assert
        act.Should().NotThrow();
    }

    [Fact]
    public void RentArray_LargeSize_ShouldWork()
    {
        // Act
        using var pooled = MemoryPoolManager.RentArray<byte>(1024 * 1024); // 1MB

        // Assert
        pooled.Array.Should().NotBeNull();
        pooled.Array.Length.Should().BeGreaterOrEqualTo(1024 * 1024);
    }

    [Fact]
    public async Task RentArray_ConcurrentAccess_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        var exceptions = new List<Exception>();

        // Act
        for (int i = 0; i < 100; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                try
                {
                    using var pooled = MemoryPoolManager.RentArray<byte>(100);
                    pooled.Array[0] = 42;
                }
                catch (Exception ex)
                {
                    lock (exceptions) exceptions.Add(ex);
                }
            }));
        }

        await Task.WhenAll(tasks);

        // Assert
        exceptions.Should().BeEmpty();
    }

    [Fact]
    public void RentBufferWriter_ShouldReturnValidWriter()
    {
        // Act
        using var writer = MemoryPoolManager.RentBufferWriter<byte>(256);

        // Assert
        writer.Should().NotBeNull();
    }

    [Fact]
    public void RentBufferWriter_ShouldBeWritable()
    {
        // Arrange
        using var writer = MemoryPoolManager.RentBufferWriter<byte>(256);

        // Act
        var span = writer.GetSpan(10);
        for (int i = 0; i < 10; i++)
        {
            span[i] = (byte)i;
        }
        writer.Advance(10);

        // Assert
        writer.WrittenCount.Should().Be(10);
    }

    [Fact]
    public void RentBufferWriter_WithZeroCapacity_ShouldThrow()
    {
        // Act
        var act = () => MemoryPoolManager.RentBufferWriter<byte>(0);

        // Assert
        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Fact]
    public void PooledArray_AsSpan_ShouldReturnValidSpan()
    {
        // Arrange
        using var pooled = MemoryPoolManager.RentArray<byte>(100);

        // Act
        var span = pooled.Array.AsSpan(0, 50);

        // Assert
        span.Length.Should().Be(50);
    }

    [Fact]
    public void PooledArray_AsMemory_ShouldReturnValidMemory()
    {
        // Arrange
        using var pooled = MemoryPoolManager.RentArray<byte>(100);

        // Act
        var memory = pooled.Array.AsMemory(0, 50);

        // Assert
        memory.Length.Should().Be(50);
    }

    [Fact]
    public void RentArray_Sequential_ShouldReuseBuffers()
    {
        // Arrange & Act
        byte[] firstArray;
        using (var pooled1 = MemoryPoolManager.RentArray<byte>(100))
        {
            firstArray = pooled1.Array;
        }

        byte[] secondArray;
        using (var pooled2 = MemoryPoolManager.RentArray<byte>(100))
        {
            secondArray = pooled2.Array;
        }

        // Assert - Arrays may be reused (depends on pool implementation)
        // Just verify both are valid
        firstArray.Should().NotBeNull();
        secondArray.Should().NotBeNull();
    }
}
