using Catga.Exceptions;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Results;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Pipeline;

public class ValidationBehaviorTests
{
    private readonly ILogger<ValidationBehavior<TestCommand, TestResponse>> _logger;

    public ValidationBehaviorTests()
    {
        _logger = Substitute.For<ILogger<ValidationBehavior<TestCommand, TestResponse>>>();
    }

    [Fact]
    public async Task HandleAsync_NoValidators_ShouldCallNext()
    {
        // Arrange
        var validators = Enumerable.Empty<IValidator<TestCommand>>();
        var behavior = new ValidationBehavior<TestCommand, TestResponse>(validators, _logger);
        var request = new TestCommand("Test");
        var expectedResponse = new TestResponse("Success");
        var nextCalled = false;

        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task HandleAsync_WithValidRequest_ShouldCallNext()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<string>()));

        var validators = new[] { validator };
        var behavior = new ValidationBehavior<TestCommand, TestResponse>(validators, _logger);
        var request = new TestCommand("Test");
        var expectedResponse = new TestResponse("Success");
        var nextCalled = false;

        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        nextCalled.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
        await validator.Received(1).ValidateAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithInvalidRequest_ShouldReturnFailure()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestCommand>>();
        var errors = new List<string> { "Name is required", "Name is too short" };
        validator.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(errors));

        var validators = new[] { validator };
        var behavior = new ValidationBehavior<TestCommand, TestResponse>(validators, _logger);
        var request = new TestCommand("");
        var nextCalled = false;

        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("Success")));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        nextCalled.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Validation failed");
        result.Exception.Should().BeOfType<CatgaValidationException>();
        var validationException = (CatgaValidationException)result.Exception!;
        validationException.ValidationErrors.Should().BeEquivalentTo(errors);
    }

    [Fact]
    public async Task HandleAsync_WithMultipleValidators_ShouldAggregateErrors()
    {
        // Arrange
        var validator1 = Substitute.For<IValidator<TestCommand>>();
        validator1.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<string> { "Error 1" }));

        var validator2 = Substitute.For<IValidator<TestCommand>>();
        validator2.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new List<string> { "Error 2", "Error 3" }));

        var validators = new[] { validator1, validator2 };
        var behavior = new ValidationBehavior<TestCommand, TestResponse>(validators, _logger);
        var request = new TestCommand("");
        var nextCalled = false;

        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("Success")));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        nextCalled.Should().BeFalse();
        result.IsSuccess.Should().BeFalse();
        var validationException = (CatgaValidationException)result.Exception!;
        validationException.ValidationErrors.Should().HaveCount(3);
        validationException.ValidationErrors.Should().Contain("Error 1");
        validationException.ValidationErrors.Should().Contain("Error 2");
        validationException.ValidationErrors.Should().Contain("Error 3");
    }

    [Fact]
    public async Task HandleAsync_WithCancellation_ShouldPropagateCancellation()
    {
        // Arrange
        var validator = Substitute.For<IValidator<TestCommand>>();
        validator.ValidateAsync(Arg.Any<TestCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException<List<string>>(new OperationCanceledException()));

        var validators = new[] { validator };
        var behavior = new ValidationBehavior<TestCommand, TestResponse>(validators, _logger);
        var request = new TestCommand("Test");
        var cts = new CancellationTokenSource();
        cts.Cancel();

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("Success")));

        // Act & Assert
        await Assert.ThrowsAsync<OperationCanceledException>(async () =>
            await behavior.HandleAsync(request, next, cts.Token));
    }

    // Test data classes
    public record TestCommand(string Name) : IRequest<TestResponse>
    {
        public string MessageId { get; init; } = Guid.NewGuid().ToString();
        public string? CorrelationId { get; init; }
        public DateTime CreatedAt { get; init; } = DateTime.UtcNow;
    }

    public record TestResponse(string Message);
}
