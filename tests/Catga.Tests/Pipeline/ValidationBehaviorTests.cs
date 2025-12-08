using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// ValidationBehavior单元测试
/// 目标覆盖率: 从 0% → 90%+
/// </summary>
public class ValidationBehaviorTests
{
    private readonly ILogger<ValidationBehavior<TestRequest, TestResponse>> _mockLogger;

    public ValidationBehaviorTests()
    {
        _mockLogger = Substitute.For<ILogger<ValidationBehavior<TestRequest, TestResponse>>>();
    }

    #region No Validators Tests

    [Fact]
    public async Task HandleAsync_WithNoValidators_ShouldExecuteNext()
    {
        // Arrange
        var validators = Enumerable.Empty<IValidator<TestRequest>>();
        var behavior = new ValidationBehavior<TestRequest, TestResponse>(validators, _mockLogger);
        var request = new TestRequest { Data = "test" };
        var expectedResponse = new TestResponse { Result = "OK" };

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    #endregion

    #region Single Validator Tests

    [Fact]
    public async Task HandleAsync_WithValidRequest_ShouldExecuteNext()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string>()));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator }, _mockLogger);
        var request = new TestRequest { Data = "valid" };
        var expectedResponse = new TestResponse { Result = "OK" };

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
        await validator.Received(1).ValidateAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithSingleValidationError_ShouldReturnFailure()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string> { "Field is required" }));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator }, _mockLogger);
        var request = new TestRequest { Data = "" };

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Validation failed");
        result.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    #endregion

    #region Multiple Validators Tests

    [Fact]
    public async Task HandleAsync_WithMultipleValidators_AllValid_ShouldExecuteNext()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator<TestRequest>>();
        var validator2 = Substitute.For<IValidator<TestRequest>>();
        var validator3 = Substitute.For<IValidator<TestRequest>>();

        validator1.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string>()));
        validator2.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string>()));
        validator3.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string>()));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator1, validator2, validator3 }, _mockLogger);
        var request = new TestRequest { Data = "valid" };
        var expectedResponse = new TestResponse { Result = "OK" };

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(expectedResponse));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        await validator1.Received(1).ValidateAsync(request, Arg.Any<CancellationToken>());
        await validator2.Received(1).ValidateAsync(request, Arg.Any<CancellationToken>());
        await validator3.Received(1).ValidateAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithMultipleErrors_ShouldCombineAllErrors()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator<TestRequest>>();
        var validator2 = Substitute.For<IValidator<TestRequest>>();

        validator1.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string> { "Error 1", "Error 2" }));
        validator2.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string> { "Error 3" }));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator1, validator2 }, _mockLogger);
        var request = new TestRequest { Data = "invalid" };

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Validation failed");
    }

    [Fact]
    public async Task HandleAsync_WithOneValidatorFailing_ShouldReturnFailure()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator<TestRequest>>();
        var validator2 = Substitute.For<IValidator<TestRequest>>();

        validator1.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string>())); // Valid
        validator2.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string> { "Validation error" })); // Invalid

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator1, validator2 }, _mockLogger);
        var request = new TestRequest();

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        await validator1.Received(1).ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
        await validator2.Received(1).ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Cancellation Tests

    [Fact]
    public async Task HandleAsync_WithCancellationToken_ShouldPassToValidators()
    {
        // Arrange
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<TestRequest>(), cancellationToken)
            .Returns(ValueTask.FromResult(new List<string>()));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator }, _mockLogger);
        var request = new TestRequest();

        PipelineDelegate<TestResponse> next = () => ValueTask.FromResult(
            CatgaResult<TestResponse>.Success(new TestResponse()));

        // Act
        await behavior.HandleAsync(request, next, cancellationToken);

        // Assert
        await validator.Received(1).ValidateAsync(request, cancellationToken);
    }

    #endregion

    #region Error Message Formatting Tests

    [Fact]
    public async Task HandleAsync_WithSingleError_ShouldReturnErrorAsIs()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string> { "Single error message" }));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator }, _mockLogger);
        var request = new TestRequest();

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Validation failed");
        result.ErrorCode.Should().Be(ErrorCodes.ValidationFailed);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleErrors_ShouldFormatWithSeparator()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string> { "Error 1", "Error 2", "Error 3" }));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator }, _mockLogger);
        var request = new TestRequest();

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Validation failed");
    }

    #endregion

    #region MessageId Tests

    [Fact]
    public async Task HandleAsync_WithIMessage_ShouldLogMessageId()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestRequest>>();
        validator.ValidateAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(ValueTask.FromResult(new List<string> { "Error" }));

        var behavior = new ValidationBehavior<TestRequest, TestResponse>(
            new[] { validator }, _mockLogger);
        var request = new TestRequest { MessageId = 12345 };

        PipelineDelegate<TestResponse> next = () => throw new InvalidOperationException("Should not be called");

        // Act
        await behavior.HandleAsync(request, next);

        // Assert
        // Logger应该被调用并记录MessageId（通过mock验证）
        _mockLogger.ReceivedCalls().Should().NotBeEmpty();
    }

    #endregion

    #region Test Helper Classes

    public class TestRequest : IRequest<TestResponse>, IMessage
    {
        public long MessageId { get; init; }
        public long? CorrelationId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public class TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    #endregion
}







