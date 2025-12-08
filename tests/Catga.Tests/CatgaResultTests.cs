using Catga.Core;
using Catga.Exceptions;
using FluentAssertions;

namespace Catga.Tests;

/// <summary>
/// CatgaResult 类型测试
/// </summary>
public class CatgaResultTests
{
    [Fact]
    public void Success_ShouldCreateSuccessResult()
    {
        // Act
        var result = CatgaResult<string>.Success("test value");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test value");
        result.Error.Should().BeNull();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void Failure_ShouldCreateFailureResult()
    {
        // Act
        var result = CatgaResult<string>.Failure("Error message");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Error message");
    }

    [Fact]
    public void Failure_WithException_ShouldStoreException()
    {
        // Arrange
        var exception = new CatgaException("Test exception");

        // Act
        var result = CatgaResult<string>.Failure("Error", exception);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().Be(exception);
        result.Error.Should().Be("Error");
    }

    [Fact]
    public void NonGenericSuccess_ShouldCreateSuccessResult()
    {
        // Act
        var result = CatgaResult.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void NonGenericFailure_ShouldCreateFailureResult()
    {
        // Act
        var result = CatgaResult.Failure("Error occurred");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Error occurred");
    }
}






