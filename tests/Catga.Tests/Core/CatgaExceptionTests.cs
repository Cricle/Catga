using Catga.Exceptions;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// CatgaException及派生异常单元测试
/// 目标覆盖率: 从 0% → 95%+
/// </summary>
public class CatgaExceptionTests
{
    #region CatgaException Basic Tests

    [Fact]
    public void Constructor_WithMessage_ShouldCreateException()
    {
        // Act
        var exception = new CatgaException("Test error");

        // Assert
        exception.Message.Should().Be("Test error");
        exception.ErrorCode.Should().BeNull();
        exception.IsRetryable.Should().BeFalse();
        exception.Details.Should().BeNull();
    }

    [Fact]
    public void Constructor_WithMessageAndErrorCode_ShouldSetProperties()
    {
        // Act
        var exception = new CatgaException("Test error", "ERR_001");

        // Assert
        exception.Message.Should().Be("Test error");
        exception.ErrorCode.Should().Be("ERR_001");
        exception.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void Constructor_WithRetryable_ShouldSetRetryable()
    {
        // Act
        var exception = new CatgaException("Retryable error", "ERR_002", isRetryable: true);

        // Assert
        exception.IsRetryable.Should().BeTrue();
        exception.ErrorCode.Should().Be("ERR_002");
    }

    [Fact]
    public void Constructor_WithInnerException_ShouldSetInnerException()
    {
        // Arrange
        var inner = new InvalidOperationException("Inner error");

        // Act
        var exception = new CatgaException("Outer error", inner, "ERR_003");

        // Assert
        exception.Message.Should().Be("Outer error");
        exception.InnerException.Should().BeSameAs(inner);
        exception.ErrorCode.Should().Be("ERR_003");
    }

    [Fact]
    public void Constructor_WithInnerExceptionAndRetryable_ShouldSetAll()
    {
        // Arrange
        var inner = new TimeoutException("Timeout");

        // Act
        var exception = new CatgaException("Service timeout", inner, "TIMEOUT", isRetryable: true);

        // Assert
        exception.Message.Should().Be("Service timeout");
        exception.InnerException.Should().BeSameAs(inner);
        exception.ErrorCode.Should().Be("TIMEOUT");
        exception.IsRetryable.Should().BeTrue();
    }

    #endregion

    #region CatgaTimeoutException Tests

    [Fact]
    public void CatgaTimeoutException_ShouldInheritFromCatgaException()
    {
        // Act
        var exception = new CatgaTimeoutException("Timeout");

        // Assert
        exception.Should().BeAssignableTo<CatgaException>();
    }

    [Fact]
    public void CatgaTimeoutException_ShouldBeRetryableByDefault()
    {
        // Act
        var exception = new CatgaTimeoutException("Operation timed out");

        // Assert
        exception.IsRetryable.Should().BeTrue();
    }

    #endregion

    #region CatgaValidationException Tests

    [Fact]
    public void CatgaValidationException_ShouldInheritFromCatgaException()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2" };

        // Act
        var exception = new CatgaValidationException("Validation failed", errors);

        // Assert
        exception.Should().BeAssignableTo<CatgaException>();
    }

    [Fact]
    public void CatgaValidationException_ShouldNotBeRetryable()
    {
        // Arrange
        var errors = new List<string> { "Invalid input" };

        // Act
        var exception = new CatgaValidationException("Validation failed", errors);

        // Assert
        exception.IsRetryable.Should().BeFalse();
    }

    [Fact]
    public void CatgaValidationException_ShouldStoreValidationErrors()
    {
        // Arrange
        var errors = new List<string> { "Error 1", "Error 2", "Error 3" };

        // Act
        var exception = new CatgaValidationException("Multiple errors", errors);

        // Assert
        exception.ValidationErrors.Should().HaveCount(3);
        exception.ValidationErrors.Should().Contain("Error 1");
        exception.ValidationErrors.Should().Contain("Error 2");
        exception.ValidationErrors.Should().Contain("Error 3");
    }

    [Fact]
    public void CatgaValidationException_ErrorCode_ShouldBeVALIDATION_FAILED()
    {
        // Arrange
        var errors = new List<string> { "Error" };

        // Act
        var exception = new CatgaValidationException("Validation failed", errors);

        // Assert
        exception.ErrorCode.Should().Be("VALIDATION_FAILED");
    }

    #endregion

    #region Exception Throwing Tests

    [Fact]
    public void CatgaException_CanBeThrown()
    {
        // Act
        Action act = () => throw new CatgaException("Test error", "TEST");

        // Assert
        act.Should().Throw<CatgaException>()
            .WithMessage("Test error")
            .Which.ErrorCode.Should().Be("TEST");
    }

    [Fact]
    public void CatgaTimeoutException_CanBeThrown()
    {
        // Act
        Action act = () => throw new CatgaTimeoutException("Timeout error");

        // Assert
        act.Should().Throw<CatgaTimeoutException>()
            .WithMessage("Timeout error");
    }

    [Fact]
    public void CatgaValidationException_CanBeThrown()
    {
        // Arrange
        var errors = new List<string> { "Field 1 is required", "Field 2 is invalid" };

        // Act
        Action act = () => throw new CatgaValidationException("Validation error", errors);

        // Assert
        act.Should().Throw<CatgaValidationException>()
            .WithMessage("Validation error")
            .Which.ValidationErrors.Should().HaveCount(2);
    }

    #endregion

    #region Details Property Tests

    [Fact]
    public void Details_WhenSet_ShouldReturnDetails()
    {
        // Act
        var exception = new CatgaException("Error")
        {
            Details = new Dictionary<string, string> { ["key"] = "value" }
        };

        // Assert
        exception.Details.Should().NotBeNull();
        exception.Details.Should().ContainKey("key");
        exception.Details!["key"].Should().Be("value");
    }

    [Fact]
    public void Details_WhenNotSet_ShouldBeNull()
    {
        // Act
        var exception = new CatgaException("Error");

        // Assert
        exception.Details.Should().BeNull();
    }

    #endregion
}







