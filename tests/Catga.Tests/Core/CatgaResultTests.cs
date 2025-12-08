using Catga.Core;
using Catga.Exceptions;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.Core;

/// <summary>
/// CatgaResult&lt;T&gt; 和 CatgaResult 单元测试
/// 目标覆盖率: 从 0% → 100%
/// </summary>
public class CatgaResultTests
{
    #region CatgaResult<T> Success Tests

    [Fact]
    public void CatgaResultT_Success_ShouldCreateSuccessResult()
    {
        // Act
        var result = CatgaResult<string>.Success("test-value");

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("test-value");
        result.Error.Should().BeNull();
        result.Exception.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void CatgaResultT_Success_WithNullValue_ShouldCreateSuccessResult()
    {
        // Act
        var result = CatgaResult<string>.Success(null!);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void CatgaResultT_Success_WithComplexType_ShouldStoreValue()
    {
        // Arrange
        var complexObject = new TestData { Id = 123, Name = "Test" };

        // Act
        var result = CatgaResult<TestData>.Success(complexObject);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(complexObject);
        result.Value!.Id.Should().Be(123);
        result.Value.Name.Should().Be("Test");
    }

    #endregion

    #region CatgaResult<T> Failure Tests

    [Fact]
    public void CatgaResultT_Failure_WithErrorMessage_ShouldCreateFailureResult()
    {
        // Act
        var result = CatgaResult<string>.Failure("Something went wrong");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().BeNull();
        result.Error.Should().Be("Something went wrong");
        result.Exception.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void CatgaResultT_Failure_WithException_ShouldStoreException()
    {
        // Arrange
        var exception = new CatgaException("Test error", "CATGA_TEST");

        // Act
        var result = CatgaResult<string>.Failure("Error occurred", exception);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Error occurred");
        result.Exception.Should().BeSameAs(exception);
        result.ErrorCode.Should().Be("CATGA_TEST");
    }

    [Fact]
    public void CatgaResultT_Failure_WithErrorInfo_ShouldCreateFromErrorInfo()
    {
        // Arrange
        var errorInfo = new ErrorInfo
        {
            Code = "ERR_001",
            Message = "Validation failed",
            Exception = new CatgaException("Validation failed", "ERR_001")
        };

        // Act
        var result = CatgaResult<string>.Failure(errorInfo);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Validation failed");
        result.ErrorCode.Should().Be("ERR_001");
        result.Exception.Should().BeSameAs(errorInfo.Exception);
    }

    [Fact]
    public void CatgaResultT_Failure_WithErrorInfoWithoutException_ShouldWork()
    {
        // Arrange
        var errorInfo = new ErrorInfo
        {
            Code = "ERR_002",
            Message = "Simple error"
        };

        // Act
        var result = CatgaResult<int>.Failure(errorInfo);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Simple error");
        result.ErrorCode.Should().Be("ERR_002");
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void CatgaResultT_Failure_WithNonCatgaException_ShouldNotSetException()
    {
        // Arrange
        var errorInfo = new ErrorInfo
        {
            Code = "ERR_003",
            Message = "Error with non-Catga exception",
            Exception = new InvalidOperationException("Not a CatgaException")
        };

        // Act
        var result = CatgaResult<string>.Failure(errorInfo);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Exception.Should().BeNull(); // Only CatgaException is stored
        result.ErrorCode.Should().Be("ERR_003");
    }

    #endregion

    #region CatgaResult (Non-Generic) Success Tests

    [Fact]
    public void CatgaResult_Success_ShouldCreateSuccessResult()
    {
        // Act
        var result = CatgaResult.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
        result.Exception.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    #endregion

    #region CatgaResult (Non-Generic) Failure Tests

    [Fact]
    public void CatgaResult_Failure_WithErrorMessage_ShouldCreateFailureResult()
    {
        // Act
        var result = CatgaResult.Failure("Operation failed");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Operation failed");
        result.Exception.Should().BeNull();
        result.ErrorCode.Should().BeNull();
    }

    [Fact]
    public void CatgaResult_Failure_WithException_ShouldStoreException()
    {
        // Arrange
        var exception = new CatgaException("Internal error", "CATGA_500");

        // Act
        var result = CatgaResult.Failure("Failed", exception);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Failed");
        result.Exception.Should().BeSameAs(exception);
        result.ErrorCode.Should().Be("CATGA_500");
    }

    [Fact]
    public void CatgaResult_Failure_WithErrorInfo_ShouldCreateFromErrorInfo()
    {
        // Arrange
        var errorInfo = new ErrorInfo
        {
            Code = "ERR_100",
            Message = "Business rule violation"
        };

        // Act
        var result = CatgaResult.Failure(errorInfo);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Business rule violation");
        result.ErrorCode.Should().Be("ERR_100");
    }

    [Fact]
    public void CatgaResult_Failure_WithErrorInfoAndCatgaException_ShouldStoreAll()
    {
        // Arrange
        var exception = new CatgaException("Not found", "CATGA_404");
        var errorInfo = new ErrorInfo
        {
            Code = "CATGA_404",
            Message = "Resource not found",
            Exception = exception
        };

        // Act
        var result = CatgaResult.Failure(errorInfo);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Resource not found");
        result.ErrorCode.Should().Be("CATGA_404");
        result.Exception.Should().BeSameAs(exception);
    }

    #endregion

    #region Struct Behavior Tests

    [Fact]
    public void CatgaResultT_AsStruct_ShouldBeValueType()
    {
        // Arrange & Act
        var result1 = CatgaResult<int>.Success(42);
        var result2 = result1; // Copy

        // Assert - structs are copied by value
        result1.Should().Be(result2);
        typeof(CatgaResult<int>).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void CatgaResult_AsStruct_ShouldBeValueType()
    {
        // Arrange & Act
        var result1 = CatgaResult.Success();
        var result2 = result1;

        // Assert
        result1.Should().Be(result2);
        typeof(CatgaResult).IsValueType.Should().BeTrue();
    }

    [Fact]
    public void CatgaResultT_RecordStruct_ShouldSupportEqualityComparison()
    {
        // Arrange
        var result1 = CatgaResult<string>.Success("test");
        var result2 = CatgaResult<string>.Success("test");
        var result3 = CatgaResult<string>.Success("different");

        // Assert
        result1.Should().Be(result2);
        result1.Should().NotBe(result3);
    }

    [Fact]
    public void CatgaResult_RecordStruct_ShouldSupportEqualityComparison()
    {
        // Arrange
        var result1 = CatgaResult.Success();
        var result2 = CatgaResult.Success();
        var result3 = CatgaResult.Failure("error");

        // Assert
        result1.Should().Be(result2);
        result1.Should().NotBe(result3);
    }

    #endregion

    #region Edge Cases

    [Fact]
    public void CatgaResultT_Failure_WithEmptyErrorMessage_ShouldWork()
    {
        // Act
        var result = CatgaResult<string>.Failure(string.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeEmpty();
    }

    [Fact]
    public void CatgaResultT_Failure_WithNullErrorMessage_ShouldWork()
    {
        // Act
        var result = CatgaResult<string>.Failure(null!);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void CatgaResult_Failure_WithEmptyErrorMessage_ShouldWork()
    {
        // Act
        var result = CatgaResult.Failure(string.Empty);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().BeEmpty();
    }

    [Fact]
    public void CatgaResultT_WithMultipleFailures_ShouldMaintainIndependentState()
    {
        // Arrange
        var result1 = CatgaResult<int>.Failure("Error 1");
        var result2 = CatgaResult<int>.Failure("Error 2");

        // Assert
        result1.Error.Should().Be("Error 1");
        result2.Error.Should().Be("Error 2");
        result1.Should().NotBe(result2);
    }

    [Fact]
    public void CatgaResultT_Success_WithDefaultValue_ShouldWork()
    {
        // Act
        var result = CatgaResult<int>.Success(default);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(0);
    }

    [Fact]
    public void CatgaResultT_Failure_WithLongErrorMessage_ShouldStoreFullMessage()
    {
        // Arrange
        var longError = new string('x', 10000);

        // Act
        var result = CatgaResult<string>.Failure(longError);

        // Assert
        result.Error.Should().Be(longError);
        result.Error!.Length.Should().Be(10000);
    }

    #endregion

    #region Integration Tests

    [Fact]
    public void CatgaResultT_CanBeUsedInMethodReturn()
    {
        // Arrange & Act
        var successResult = ProcessData(true);
        var failureResult = ProcessData(false);

        // Assert
        successResult.IsSuccess.Should().BeTrue();
        successResult.Value.Should().Be(42);

        failureResult.IsSuccess.Should().BeFalse();
        failureResult.Error.Should().Contain("failed");
    }

    [Fact]
    public void CatgaResult_CanBeUsedInMethodReturn()
    {
        // Arrange & Act
        var successResult = ExecuteOperation(true);
        var failureResult = ExecuteOperation(false);

        // Assert
        successResult.IsSuccess.Should().BeTrue();
        failureResult.IsSuccess.Should().BeFalse();
        failureResult.Error.Should().Contain("failed");
    }

    #endregion

    #region Helper Methods

    private static CatgaResult<int> ProcessData(bool shouldSucceed)
    {
        if (shouldSucceed)
            return CatgaResult<int>.Success(42);

        return CatgaResult<int>.Failure("Processing failed");
    }

    private static CatgaResult ExecuteOperation(bool shouldSucceed)
    {
        if (shouldSucceed)
            return CatgaResult.Success();

        return CatgaResult.Failure("Operation failed");
    }

    #endregion

    #region Test Helper Classes

    private class TestData
    {
        public int Id { get; init; }
        public string Name { get; init; } = string.Empty;
    }

    #endregion
}







