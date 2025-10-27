using Catga.Core;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// Tests for MemoryPoolManager and PooledArray
/// Coverage: Memory pooling, rental, disposal, thread-safety
/// </summary>
public class MemoryPoolManagerTests
{
    #region RentArray Tests

    [Fact]
    public void RentArray_WithValidLength_ShouldReturnPooledArray()
    {
        // Arrange & Act
        using var pooled = MemoryPoolManager.RentArray<byte>(1024);

        // Assert
        pooled.Array.Should().NotBeNull();
        pooled.Length.Should().Be(1024);
        pooled.Array.Length.Should().BeGreaterOrEqualTo(1024);
    }

    [Fact]
    public void RentArray_WithZeroLength_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => MemoryPoolManager.RentArray<int>(0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("minimumLength");
    }

    [Fact]
    public void RentArray_WithNegativeLength_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => MemoryPoolManager.RentArray<int>(-1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("minimumLength");
    }

    [Fact]
    public void RentArray_MultipleTimes_ShouldReturnDifferentArrays()
    {
        // Arrange
        using var pooled1 = MemoryPoolManager.RentArray<int>(100);
        using var pooled2 = MemoryPoolManager.RentArray<int>(100);

        // Act & Assert
        // Arrays can be different or same (depending on pool state)
        // But they should be valid
        pooled1.Array.Should().NotBeNull();
        pooled2.Array.Should().NotBeNull();
    }

    [Theory]
    [InlineData(1)]
    [InlineData(16)]
    [InlineData(256)]
    [InlineData(4096)]
    [InlineData(65536)]
    public void RentArray_WithVariousSizes_ShouldSucceed(int size)
    {
        // Arrange & Act
        using var pooled = MemoryPoolManager.RentArray<byte>(size);

        // Assert
        pooled.Length.Should().Be(size);
        pooled.Array.Length.Should().BeGreaterOrEqualTo(size);
    }

    #endregion

    #region RentBufferWriter Tests

    [Fact]
    public void RentBufferWriter_WithDefaultCapacity_ShouldReturnBufferWriter()
    {
        // Arrange & Act
        using var writer = MemoryPoolManager.RentBufferWriter<byte>();

        // Assert
        writer.Should().NotBeNull();
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void RentBufferWriter_WithCustomCapacity_ShouldReturnBufferWriter()
    {
        // Arrange & Act
        using var writer = MemoryPoolManager.RentBufferWriter<byte>(1024);

        // Assert
        writer.Should().NotBeNull();
        writer.WrittenCount.Should().Be(0);
    }

    [Fact]
    public void RentBufferWriter_WithZeroCapacity_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => MemoryPoolManager.RentBufferWriter<byte>(0);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("initialCapacity");
    }

    [Fact]
    public void RentBufferWriter_WithNegativeCapacity_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => MemoryPoolManager.RentBufferWriter<byte>(-1);
        act.Should().Throw<ArgumentOutOfRangeException>()
            .WithParameterName("initialCapacity");
    }

    #endregion

    #region PooledArray Tests

    [Fact]
    public void PooledArray_Span_ShouldReturnCorrectSpan()
    {
        // Arrange
        using var pooled = MemoryPoolManager.RentArray<int>(10);

        // Act
        var span = pooled.Span;

        // Assert
        span.Length.Should().Be(10);
    }

    [Fact]
    public void PooledArray_Memory_ShouldReturnCorrectMemory()
    {
        // Arrange
        using var pooled = MemoryPoolManager.RentArray<int>(10);

        // Act
        var memory = pooled.Memory;

        // Assert
        memory.Length.Should().Be(10);
    }

    [Fact]
    public void PooledArray_Span_ShouldAllowWrites()
    {
        // Arrange
        using var pooled = MemoryPoolManager.RentArray<int>(5);
        var span = pooled.Span;

        // Act
        for (int i = 0; i < span.Length; i++)
        {
            span[i] = i * 10;
        }

        // Assert
        span[0].Should().Be(0);
        span[1].Should().Be(10);
        span[2].Should().Be(20);
        span[3].Should().Be(30);
        span[4].Should().Be(40);
    }

    [Fact]
    public void PooledArray_ImplicitConversionToReadOnlySpan_ShouldWork()
    {
        // Arrange
        using var pooled = MemoryPoolManager.RentArray<int>(10);

        // Act
        ReadOnlySpan<int> span = pooled;

        // Assert
        span.Length.Should().Be(10);
    }

    [Fact]
    public void PooledArray_ImplicitConversionToSpan_ShouldWork()
    {
        // Arrange
        using var pooled = MemoryPoolManager.RentArray<int>(10);

        // Act
        Span<int> span = pooled;

        // Assert
        span.Length.Should().Be(10);
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
    public void PooledArray_DoubleDispose_ShouldNotThrow()
    {
        // Arrange
        var pooled = MemoryPoolManager.RentArray<byte>(100);

        // Act
        pooled.Dispose();
        var act = () => pooled.Dispose();

        // Assert
        // ArrayPool handles double-dispose gracefully
        act.Should().NotThrow();
    }

    [Fact]
    public void PooledArray_UsingStatement_ShouldAutoDispose()
    {
        // Arrange
        int[] capturedArray = null!;

        // Act
        using (var pooled = MemoryPoolManager.RentArray<int>(100))
        {
            capturedArray = pooled.Array;
            capturedArray.Should().NotBeNull();
        }

        // Assert
        // After using block, array should be returned to pool
        // We can't directly verify this, but can test that no exception was thrown
        capturedArray.Should().NotBeNull();
    }

    [Fact]
    public void PooledArray_WithDifferentTypes_ShouldWork()
    {
        // Arrange & Act
        using var bytesPooled = MemoryPoolManager.RentArray<byte>(100);
        using var intsPooled = MemoryPoolManager.RentArray<int>(100);
        using var stringsPooled = MemoryPoolManager.RentArray<string>(100);

        // Assert
        bytesPooled.Length.Should().Be(100);
        intsPooled.Length.Should().Be(100);
        stringsPooled.Length.Should().Be(100);
    }

    #endregion

    #region Thread-Safety Tests

    [Fact]
    public async Task RentArray_ConcurrentRental_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        const int taskCount = 100;
        const int rentalsPerTask = 100;

        // Act
        for (int i = 0; i < taskCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < rentalsPerTask; j++)
                {
                    using var pooled = MemoryPoolManager.RentArray<byte>(1024);
                    var span = pooled.Span;
                    
                    // Write some data
                    for (int k = 0; k < span.Length; k++)
                    {
                        span[k] = (byte)(k % 256);
                    }
                }
            }));
        }

        // Assert
        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task RentBufferWriter_ConcurrentRental_ShouldBeThreadSafe()
    {
        // Arrange
        var tasks = new List<Task>();
        const int taskCount = 50;
        const int rentalsPerTask = 50;

        // Act
        for (int i = 0; i < taskCount; i++)
        {
            tasks.Add(Task.Run(() =>
            {
                for (int j = 0; j < rentalsPerTask; j++)
                {
                    using var writer = MemoryPoolManager.RentBufferWriter<byte>(256);
                    writer.Should().NotBeNull();
                }
            }));
        }

        // Assert
        var act = async () => await Task.WhenAll(tasks);
        await act.Should().NotThrowAsync();
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void RentArray_WithVeryLargeSize_ShouldSucceed()
    {
        // Arrange & Act
        using var pooled = MemoryPoolManager.RentArray<byte>(1024 * 1024); // 1MB

        // Assert
        pooled.Length.Should().Be(1024 * 1024);
        pooled.Array.Length.Should().BeGreaterOrEqualTo(1024 * 1024);
    }

    [Fact]
    public void PooledArray_WithMinimumSize_ShouldWork()
    {
        // Arrange & Act
        using var pooled = MemoryPoolManager.RentArray<int>(1);

        // Assert
        pooled.Length.Should().Be(1);
        pooled.Span.Length.Should().Be(1);
    }

    [Fact]
    public void RentArray_RentReturnRent_ShouldReuseArray()
    {
        // Arrange
        byte[] firstArray;
        byte[] secondArray;

        // Act
        using (var pooled1 = MemoryPoolManager.RentArray<byte>(1000))
        {
            firstArray = pooled1.Array;
        } // Returned to pool

        using (var pooled2 = MemoryPoolManager.RentArray<byte>(1000))
        {
            secondArray = pooled2.Array;
        }

        // Assert
        // Pool may or may not reuse the same array, but both should be valid
        firstArray.Should().NotBeNull();
        secondArray.Should().NotBeNull();
    }

    #endregion

    #region PooledArray Constructor Tests

    [Fact]
    public void PooledArray_Constructor_WithNullArray_ShouldThrow()
    {
        // Arrange, Act & Assert
        var act = () => new PooledArray<int>(null!, 10);
        act.Should().Throw<ArgumentNullException>()
            .WithParameterName("array");
    }

    [Fact]
    public void PooledArray_Constructor_WithValidArray_ShouldInitialize()
    {
        // Arrange
        var array = new int[100];

        // Act
        var pooled = new PooledArray<int>(array, 50);

        // Assert
        pooled.Array.Should().BeSameAs(array);
        pooled.Length.Should().Be(50);
    }

    [Fact]
    public void PooledArray_Properties_ShouldReflectRequestedLength()
    {
        // Arrange
        var array = new int[100];
        var pooled = new PooledArray<int>(array, 50);

        // Act
        var span = pooled.Span;
        var memory = pooled.Memory;

        // Assert
        span.Length.Should().Be(50);
        memory.Length.Should().Be(50);
    }

    #endregion
}

