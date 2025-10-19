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

    [Fact]
    public void ResultMetadata_ShouldStoreCustomData()
    {
        // Arrange
        var metadata = new ResultMetadata();
        metadata.Add("key1", "value1");
        metadata.Add("key2", "value2");

        // Act
        var result = CatgaResult<int>.Success(42, metadata);

        // Assert
        result.Metadata.Should().NotBeNull();
        result.Metadata!.TryGetValue("key1", out var value1).Should().BeTrue();
        value1.Should().Be("value1");
        result.Metadata.ContainsKey("key2").Should().BeTrue();
    }
}
