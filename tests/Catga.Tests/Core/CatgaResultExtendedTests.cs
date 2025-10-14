using Catga.Results;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Extended CatgaResult tests - comprehensive coverage
/// </summary>
public class CatgaResultExtendedTests
{
    [Fact]
    public void Success_WithValue_ShouldCreateSuccessResult()
    {
        // Act
        var result = CatgaResult<string>.Success("test-value");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test-value");
        result.Error.Should().BeNull();
        result.Metadata.Should().BeNull(); // Metadata is null by default
    }

    [Fact]
    public void Failure_WithError_ShouldCreateFailureResult()
    {
        // Act
        var result = CatgaResult<string>.Failure("error-message");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("error-message");
    }

    [Fact]
    public void Success_WithMetadata_ShouldPreserveMetadata()
    {
        // Arrange
        var metadata = new ResultMetadata();
        metadata.Add("key1", "value1");
        metadata.Add("key2", "123");

        // Act
        var result = CatgaResult<string>.Success("test", metadata);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Metadata.Should().NotBeNull();
        result.Metadata!.ContainsKey("key1").Should().BeTrue();
        result.Metadata!.ContainsKey("key2").Should().BeTrue();
        result.Metadata!.TryGetValue("key1", out var val1).Should().BeTrue();
        val1.Should().Be("value1");
    }

    [Fact]
    public void Failure_WithException_ShouldPreserveException()
    {
        // Arrange
        var exception = new Catga.Exceptions.CatgaException("test-exception");

        // Act
        var result = CatgaResult<string>.Failure("not-found", exception);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().NotBeNull();
        result.Exception!.Message.Should().Be("test-exception");
    }

    [Fact]
    public void Value_WithSuccess_ShouldReturnValue()
    {
        // Arrange
        var result = CatgaResult<int>.Success(42);

        // Act & Assert
        result.Value.Should().Be(42);
    }

    [Fact]
    public void Value_WithFailure_ShouldReturnDefault()
    {
        // Arrange
        var result = CatgaResult<int>.Failure("error");

        // Act & Assert
        result.Value.Should().Be(0);
    }

    [Fact]
    public void Error_WithSuccess_ShouldBeNull()
    {
        // Arrange
        var result = CatgaResult<int>.Success(42);

        // Act & Assert
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Error_WithFailure_ShouldReturnError()
    {
        // Arrange
        var result = CatgaResult<int>.Failure("error-message");

        // Act & Assert
        result.Error.Should().Be("error-message");
    }

    [Fact]
    public void Exception_WithFailure_ShouldReturnException()
    {
        // Arrange
        var exception = new Catga.Exceptions.CatgaException("test");
        var result = CatgaResult<int>.Failure("error", exception);

        // Act & Assert
        result.Exception.Should().NotBeNull();
        result.Exception.Should().Be(exception);
    }

    [Fact]
    public void IsSuccess_WithSuccess_ShouldBeTrue()
    {
        // Arrange
        var result = CatgaResult<string>.Success("test");

        // Act & Assert
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public void IsSuccess_WithFailure_ShouldBeFalse()
    {
        // Arrange
        var result = CatgaResult<string>.Failure("error-message");

        // Act & Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Failure_WithInvalidError_ShouldStillCreate(string? error)
    {
        // Act
        var result = CatgaResult<string>.Failure(error!);

        // Assert
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public void Success_WithNullValue_ShouldAllowNull()
    {
        // Act
        var result = CatgaResult<string?>.Success(null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Metadata_WithoutMetadata_ShouldBeNull()
    {
        // Arrange
        var result = CatgaResult<string>.Success("test");

        // Act & Assert
        result.Metadata.Should().BeNull();
    }

    [Fact]
    public void ResultMetadata_Add_ShouldStoreValue()
    {
        // Arrange
        var metadata = new ResultMetadata();

        // Act
        metadata.Add("key", "value");

        // Assert
        metadata.ContainsKey("key").Should().BeTrue();
        metadata.TryGetValue("key", out var value).Should().BeTrue();
        value.Should().Be("value");
    }

    [Fact]
    public void ResultMetadata_Count_ShouldReturnCorrectCount()
    {
        // Arrange
        var metadata = new ResultMetadata();
        metadata.Add("key1", "value1");
        metadata.Add("key2", "value2");

        // Act & Assert
        metadata.Count.Should().Be(2);
    }

    [Fact]
    public void ResultMetadata_GetAll_ShouldReturnAllData()
    {
        // Arrange
        var metadata = new ResultMetadata();
        metadata.Add("key1", "value1");
        metadata.Add("key2", "value2");

        // Act
        var all = metadata.GetAll();

        // Assert
        all.Should().HaveCount(2);
        all["key1"].Should().Be("value1");
        all["key2"].Should().Be("value2");
    }
}

