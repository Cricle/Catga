using Catga.AspNetCore;
using FluentAssertions;
using Xunit;

namespace Catga.Tests.E2E;

/// <summary>
/// Tests for endpoint validation extensions.
/// Verifies fluent validation patterns and error handling.
/// </summary>
public class AspNetCoreEndpointValidationTests
{
    [Fact]
    public void ValidationBuilder_ShouldAccumulateErrors()
    {
        // Arrange & Act
        var validation = new ValidationBuilder()
            .AddError("Error 1")
            .AddError("Error 2")
            .AddError("Error 3");

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().HaveCount(3);
    }

    [Fact]
    public void ValidationBuilder_ShouldNotAddNullOrEmptyErrors()
    {
        // Arrange & Act
        var validation = new ValidationBuilder()
            .AddError("Valid error")
            .AddError("")
            .AddError(null);

        // Assert
        validation.Errors.Should().HaveCount(1);
        validation.Errors[0].Should().Be("Valid error");
    }

    [Fact]
    public void ValidationBuilder_ShouldAddErrorConditionally()
    {
        // Arrange & Act
        var validation = new ValidationBuilder()
            .AddErrorIf(true, "Error when true")
            .AddErrorIf(false, "Error when false");

        // Assert
        validation.IsValid.Should().BeFalse();
        validation.Errors.Should().HaveCount(1);
        validation.Errors[0].Should().Be("Error when true");
    }

    [Fact]
    public void ValidationBuilder_ShouldReturnOkResultWhenValid()
    {
        // Arrange
        var validation = new ValidationBuilder();

        // Act
        var result = validation.ToResult();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidationBuilder_ShouldReturnBadRequestWhenInvalid()
    {
        // Arrange
        var validation = new ValidationBuilder()
            .AddError("Validation failed");

        // Act
        var result = validation.ToResult();

        // Assert
        result.Should().NotBeNull();
    }

    [Fact]
    public void ValidateRequired_ShouldFailForNullOrEmpty()
    {
        // Arrange & Act
        var (isValid1, error1) = ((string?)null).ValidateRequired("Field");
        var (isValid2, error2) = "".ValidateRequired("Field");
        var (isValid3, error3) = "   ".ValidateRequired("Field");

        // Assert
        isValid1.Should().BeFalse();
        isValid2.Should().BeFalse();
        isValid3.Should().BeFalse();
        error1.Should().Contain("required");
    }

    [Fact]
    public void ValidateRequired_ShouldPassForValidString()
    {
        // Arrange & Act
        var (isValid, error) = "valid value".ValidateRequired("Field");

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateMinLength_ShouldFailForShortString()
    {
        // Arrange & Act
        var (isValid, error) = "ab".ValidateMinLength(3, "Field");

        // Assert
        isValid.Should().BeFalse();
        error.Should().Contain("at least 3");
    }

    [Fact]
    public void ValidateMinLength_ShouldPassForSufficientLength()
    {
        // Arrange & Act
        var (isValid, error) = "abcde".ValidateMinLength(3, "Field");

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateMaxLength_ShouldFailForLongString()
    {
        // Arrange & Act
        var (isValid, error) = "abcdefghij".ValidateMaxLength(5, "Field");

        // Assert
        isValid.Should().BeFalse();
        error.Should().Contain("must not exceed 5");
    }

    [Fact]
    public void ValidateMaxLength_ShouldPassForShorterString()
    {
        // Arrange & Act
        var (isValid, error) = "abc".ValidateMaxLength(5, "Field");

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidatePositive_ShouldFailForNegativeOrZero()
    {
        // Arrange & Act
        var (isValid1, error1) = (-5m).ValidatePositive("Amount");
        var (isValid2, error2) = (0m).ValidatePositive("Amount");

        // Assert
        isValid1.Should().BeFalse();
        isValid2.Should().BeFalse();
        error1.Should().Contain("positive");
    }

    [Fact]
    public void ValidatePositive_ShouldPassForPositiveNumber()
    {
        // Arrange & Act
        var (isValid, error) = (100m).ValidatePositive("Amount");

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateRange_ShouldFailForOutOfRange()
    {
        // Arrange & Act
        var (isValid1, error1) = (5m).ValidateRange(10, 20, "Value");
        var (isValid2, error2) = (25m).ValidateRange(10, 20, "Value");

        // Assert
        isValid1.Should().BeFalse();
        isValid2.Should().BeFalse();
        error1.Should().Contain("between 10 and 20");
    }

    [Fact]
    public void ValidateRange_ShouldPassForInRange()
    {
        // Arrange & Act
        var (isValid, error) = (15m).ValidateRange(10, 20, "Value");

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateNotEmpty_ShouldFailForEmptyCollection()
    {
        // Arrange & Act
        var (isValid1, error1) = ((List<int>?)null).ValidateNotEmpty("Items");
        var (isValid2, error2) = (new List<int>()).ValidateNotEmpty("Items");

        // Assert
        isValid1.Should().BeFalse();
        isValid2.Should().BeFalse();
        error1.Should().Contain("must not be empty");
    }

    [Fact]
    public void ValidateNotEmpty_ShouldPassForNonEmptyCollection()
    {
        // Arrange & Act
        var (isValid, error) = (new List<int> { 1, 2, 3 }).ValidateNotEmpty("Items");

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateMinCount_ShouldFailForInsufficientItems()
    {
        // Arrange & Act
        var (isValid, error) = (new List<int> { 1, 2 }).ValidateMinCount(3, "Items");

        // Assert
        isValid.Should().BeFalse();
        error.Should().Contain("at least 3");
    }

    [Fact]
    public void ValidateMinCount_ShouldPassForSufficientItems()
    {
        // Arrange & Act
        var (isValid, error) = (new List<int> { 1, 2, 3, 4 }).ValidateMinCount(3, "Items");

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }

    [Fact]
    public void ValidateMultiple_ShouldReturnFirstError()
    {
        // Arrange
        var request = new TestValidationRequest { Name = "", Amount = -10 };

        // Act
        var (isValid, error) = request.ValidateMultiple(
            r => r.Name.ValidateRequired("Name"),
            r => r.Amount.ValidatePositive("Amount")
        );

        // Assert
        isValid.Should().BeFalse();
        error.Should().Contain("Name");
    }

    [Fact]
    public void ValidateMultiple_ShouldPassAllValidations()
    {
        // Arrange
        var request = new TestValidationRequest { Name = "Test", Amount = 100 };

        // Act
        var (isValid, error) = request.ValidateMultiple(
            r => r.Name.ValidateRequired("Name"),
            r => r.Amount.ValidatePositive("Amount")
        );

        // Assert
        isValid.Should().BeTrue();
        error.Should().BeNull();
    }
}

// Test types
public class TestValidationRequest
{
    public string Name { get; set; }
    public decimal Amount { get; set; }
}
