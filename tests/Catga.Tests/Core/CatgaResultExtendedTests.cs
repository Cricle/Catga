using Catga.Core;
using Catga.Exceptions;
using FluentAssertions;

namespace Catga.Tests.Core;

/// <summary>
/// Extended unit tests for CatgaResult - covering edge cases and advanced scenarios
/// </summary>
public class CatgaResultExtendedTests
{
    [Fact]
    public void Success_WithValue_ShouldSetProperties()
    {
        // Arrange & Act
        var result = CatgaResult<int>.Success(42);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
    }

    [Fact]
    public void Success_WithNullableValue_ShouldAllowNull()
    {
        // Arrange & Act
        var result = CatgaResult<string?>.Success(null);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public void Failure_WithErrorMessage_ShouldSetProperties()
    {
        // Arrange & Act
        var result = CatgaResult<int>.Failure("Test error message");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Test error message");
        result.Value.Should().Be(default);
    }

    [Fact]
    public void Failure_WithException_ShouldSetErrorCode()
    {
        // Arrange
        var exception = new CatgaException("Something went wrong", "ERR_001");

        // Act
        var result = CatgaResult<int>.Failure("Something went wrong", exception);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Something went wrong");
        result.ErrorCode.Should().Be("ERR_001");
        result.Exception.Should().Be(exception);
    }

    [Fact]
    public void Failure_WithErrorInfo_ShouldSetAllProperties()
    {
        // Arrange
        var errorInfo = new ErrorInfo { Code = "ERR_002", Message = "Detailed error" };

        // Act
        var result = CatgaResult<int>.Failure(errorInfo);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Detailed error");
        result.ErrorCode.Should().Be("ERR_002");
    }

    [Fact]
    public void NonGenericResult_Success_ShouldWork()
    {
        // Arrange & Act
        var result = CatgaResult.Success();

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Error.Should().BeNull();
    }

    [Fact]
    public void NonGenericResult_Failure_ShouldWork()
    {
        // Arrange & Act
        var result = CatgaResult.Failure("Error message");

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Error message");
    }

    [Fact]
    public void NonGenericResult_Failure_WithException_ShouldSetErrorCode()
    {
        // Arrange
        var exception = new CatgaException("Error", "ERR_003");

        // Act
        var result = CatgaResult.Failure("Error", exception);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.ErrorCode.Should().Be("ERR_003");
    }

    [Fact]
    public void NonGenericResult_Failure_WithErrorInfo_ShouldWork()
    {
        // Arrange
        var errorInfo = new ErrorInfo { Code = "ERR_004", Message = "Info error" };

        // Act
        var result = CatgaResult.Failure(errorInfo);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Info error");
        result.ErrorCode.Should().Be("ERR_004");
    }

    [Fact]
    public void Result_IsRecordStruct_ShouldSupportEquality()
    {
        // Arrange
        var result1 = CatgaResult<int>.Success(42);
        var result2 = CatgaResult<int>.Success(42);
        var result3 = CatgaResult<int>.Success(99);

        // Assert
        result1.Should().Be(result2);
        result1.Should().NotBe(result3);
    }

    [Fact]
    public void Result_IsRecordStruct_ShouldSupportPropertyAccess()
    {
        // Arrange
        var result = CatgaResult<int>.Success(42);

        // Act & Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(42);
        result.Error.Should().BeNull();
        result.ErrorCode.Should().BeNull();
        result.Exception.Should().BeNull();
    }

    [Fact]
    public void Result_WithInit_ShouldAllowPropertySetting()
    {
        // Arrange & Act
        var result = new CatgaResult<int>
        {
            IsSuccess = true,
            Value = 100,
            Error = null,
            ErrorCode = null
        };

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(100);
    }

    [Fact]
    public void NonGenericResult_IsRecordStruct_ShouldSupportEquality()
    {
        // Arrange
        var result1 = CatgaResult.Success();
        var result2 = CatgaResult.Success();
        var result3 = CatgaResult.Failure("error");

        // Assert
        result1.Should().Be(result2);
        result1.Should().NotBe(result3);
    }

    [Fact]
    public void Result_DefaultValue_ShouldBeFailure()
    {
        // Arrange & Act
        var result = default(CatgaResult<int>);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Value.Should().Be(default);
    }

    [Fact]
    public void Result_SuccessWithReferenceType_ShouldWork()
    {
        // Arrange
        var obj = new TestObject { Name = "Test", Value = 123 };

        // Act
        var result = CatgaResult<TestObject>.Success(obj);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeSameAs(obj);
        result.Value!.Name.Should().Be("Test");
    }

    [Fact]
    public void Result_SuccessWithCollection_ShouldWork()
    {
        // Arrange
        var list = new List<int> { 1, 2, 3 };

        // Act
        var result = CatgaResult<List<int>>.Success(list);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeEquivalentTo(new[] { 1, 2, 3 });
    }

    private class TestObject
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}






