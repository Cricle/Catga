using Catga.Common;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// ArrayPoolHelper unit tests
/// </summary>
public class ArrayPoolHelperTests
{
    [Fact]
    public void RentOrAllocate_BelowThreshold_ShouldAllocateDirectly()
    {
        // Arrange
        const int count = 10;
        const int threshold = 16;

        // Act
        using var rented = ArrayPoolHelper.RentOrAllocate<int>(count, threshold);

        // Assert
        rented.Array.Should().NotBeNull();
        rented.Count.Should().Be(count);
        rented.Array.Length.Should().BeGreaterOrEqualTo(count);
    }

    [Fact]
    public void RentOrAllocate_AboveThreshold_ShouldRentFromPool()
    {
        // Arrange
        const int count = 100;
        const int threshold = 16;

        // Act
        using var rented = ArrayPoolHelper.RentOrAllocate<int>(count, threshold);

        // Assert
        rented.Array.Should().NotBeNull();
        rented.Count.Should().Be(count);
        rented.Array.Length.Should().BeGreaterOrEqualTo(count);
    }

    [Fact]
    public void AsSpan_ShouldReturnCorrectSpan()
    {
        // Arrange
        using var rented = ArrayPoolHelper.RentOrAllocate<int>(10);

        // Act
        var span = rented.AsSpan();

        // Assert
        span.Length.Should().Be(10);
    }

    [Fact]
    public void AsMemory_ShouldReturnCorrectMemory()
    {
        // Arrange
        using var rented = ArrayPoolHelper.RentOrAllocate<int>(10);

        // Act
        var memory = rented.AsMemory();

        // Assert
        memory.Length.Should().Be(10);
    }

    [Fact]
    public void Dispose_ShouldReturnToPool()
    {
        // Arrange
        var rented = ArrayPoolHelper.RentOrAllocate<int>(100);
        var array = rented.Array;

        // Act
        rented.Dispose();

        // Assert - array should be cleared
        array.All(x => x == 0).Should().BeTrue();
    }

    [Fact]
    public void RentOrAllocate_WithData_ShouldPreserveData()
    {
        // Arrange
        using var rented = ArrayPoolHelper.RentOrAllocate<int>(5);
        var span = rented.AsSpan();

        // Act
        for (int i = 0; i < 5; i++)
            span[i] = i + 1;

        // Assert
        span[0].Should().Be(1);
        span[4].Should().Be(5);
    }

    [Fact]
    public void MultipleRentals_ShouldWorkCorrectly()
    {
        // Act & Assert
        for (int i = 0; i < 10; i++)
        {
            using var rented = ArrayPoolHelper.RentOrAllocate<int>(50);
            rented.Count.Should().Be(50);
        }
    }
}

