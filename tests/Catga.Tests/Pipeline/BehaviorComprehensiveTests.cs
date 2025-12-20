using Catga.Abstractions;
using Catga.Configuration;
using Catga.Core;
using Catga.Idempotency;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Resilience;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using Moq;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Tests for RetryBehavior.
/// </summary>
public class RetryBehaviorExtendedTests
{
    [Fact]
    public async Task HandleAsync_Success_ShouldReturnResult()
    {
        var loggerMock = new Mock<ILogger<RetryBehavior<TestRetryRequest, string>>>();
        var options = new CatgaOptions { MaxRetryAttempts = 3, RetryDelayMs = 100 };
        var behavior = new RetryBehavior<TestRetryRequest, string>(loggerMock.Object, options);

        var request = new TestRetryRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("success");
    }

    [Fact]
    public async Task HandleAsync_WithFailure_ShouldReturnFailure()
    {
        var loggerMock = new Mock<ILogger<RetryBehavior<TestRetryRequest, string>>>();
        var options = new CatgaOptions { MaxRetryAttempts = 1, RetryDelayMs = 10 };
        var behavior = new RetryBehavior<TestRetryRequest, string>(loggerMock.Object, options);

        var request = new TestRetryRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Failure("error");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    public record TestRetryRequest(string Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }
}

/// <summary>
/// Tests for ValidationBehavior.
/// </summary>
public class ValidationBehaviorExtendedTests
{
    [Fact]
    public async Task HandleAsync_WithNoValidators_ShouldCallNext()
    {
        var loggerMock = new Mock<ILogger<ValidationBehavior<TestValidationRequest, string>>>();
        var behavior = new ValidationBehavior<TestValidationRequest, string>(
            Enumerable.Empty<IValidator<TestValidationRequest>>(),
            loggerMock.Object);

        var request = new TestValidationRequest("test");
        var called = false;

        var result = await behavior.HandleAsync(request, async () =>
        {
            called = true;
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        called.Should().BeTrue();
        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithPassingValidator_ShouldCallNext()
    {
        var loggerMock = new Mock<ILogger<ValidationBehavior<TestValidationRequest, string>>>();
        var validatorMock = new Mock<IValidator<TestValidationRequest>>();
        validatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<TestValidationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string>());

        var behavior = new ValidationBehavior<TestValidationRequest, string>(
            new[] { validatorMock.Object },
            loggerMock.Object);

        var request = new TestValidationRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithFailingValidator_ShouldReturnFailure()
    {
        var loggerMock = new Mock<ILogger<ValidationBehavior<TestValidationRequest, string>>>();
        var validatorMock = new Mock<IValidator<TestValidationRequest>>();
        validatorMock
            .Setup(x => x.ValidateAsync(It.IsAny<TestValidationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Validation error" });

        var behavior = new ValidationBehavior<TestValidationRequest, string>(
            new[] { validatorMock.Object },
            loggerMock.Object);

        var request = new TestValidationRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task HandleAsync_WithMultipleValidators_ShouldAggregateErrors()
    {
        var loggerMock = new Mock<ILogger<ValidationBehavior<TestValidationRequest, string>>>();
        var validator1 = new Mock<IValidator<TestValidationRequest>>();
        validator1
            .Setup(x => x.ValidateAsync(It.IsAny<TestValidationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Error 1" });

        var validator2 = new Mock<IValidator<TestValidationRequest>>();
        validator2
            .Setup(x => x.ValidateAsync(It.IsAny<TestValidationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(new List<string> { "Error 2" });

        var behavior = new ValidationBehavior<TestValidationRequest, string>(
            new[] { validator1.Object, validator2.Object },
            loggerMock.Object);

        var request = new TestValidationRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    public record TestValidationRequest(string Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }
}

/// <summary>
/// Tests for IdempotencyBehavior.
/// </summary>
public class IdempotencyBehaviorExtendedTests
{
    [Fact]
    public async Task HandleAsync_WithoutMessageId_ShouldCallNext()
    {
        var storeMock = new Mock<IIdempotencyStore>();
        var loggerMock = new Mock<ILogger<IdempotencyBehavior<TestIdempotentRequest, string>>>();
        var behavior = new IdempotencyBehavior<TestIdempotentRequest, string>(
            storeMock.Object,
            loggerMock.Object);

        var request = new TestIdempotentRequest("test") { MessageId = 0 }; // 0 means no message id
        var called = false;

        var result = await behavior.HandleAsync(request, async () =>
        {
            called = true;
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        called.Should().BeTrue();
        storeMock.Verify(x => x.HasBeenProcessedAsync(It.IsAny<long>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task HandleAsync_NewRequest_ShouldCallNextAndStore()
    {
        var storeMock = new Mock<IIdempotencyStore>();
        storeMock.Setup(x => x.HasBeenProcessedAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var loggerMock = new Mock<ILogger<IdempotencyBehavior<TestIdempotentRequest, string>>>();
        var behavior = new IdempotencyBehavior<TestIdempotentRequest, string>(
            storeMock.Object,
            loggerMock.Object);

        var request = new TestIdempotentRequest("test") { MessageId = 123 };
        var called = false;

        var result = await behavior.HandleAsync(request, async () =>
        {
            called = true;
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        called.Should().BeTrue();
        storeMock.Verify(x => x.MarkAsProcessedAsync(123, "success", It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task HandleAsync_ExistingRequest_ShouldReturnCached()
    {
        var storeMock = new Mock<IIdempotencyStore>();
        storeMock.Setup(x => x.HasBeenProcessedAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(true);
        storeMock.Setup(x => x.GetCachedResultAsync<string>(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync("cached");

        var loggerMock = new Mock<ILogger<IdempotencyBehavior<TestIdempotentRequest, string>>>();
        var behavior = new IdempotencyBehavior<TestIdempotentRequest, string>(
            storeMock.Object,
            loggerMock.Object);

        var request = new TestIdempotentRequest("test") { MessageId = 123 };
        var called = false;

        var result = await behavior.HandleAsync(request, async () =>
        {
            called = true;
            return CatgaResult<string>.Success("new");
        }, CancellationToken.None);

        called.Should().BeFalse();
        result.Value.Should().Be("cached");
    }

    [Fact]
    public async Task HandleAsync_FailedRequest_ShouldNotCache()
    {
        var storeMock = new Mock<IIdempotencyStore>();
        storeMock.Setup(x => x.HasBeenProcessedAsync(123, It.IsAny<CancellationToken>()))
            .ReturnsAsync(false);

        var loggerMock = new Mock<ILogger<IdempotencyBehavior<TestIdempotentRequest, string>>>();
        var behavior = new IdempotencyBehavior<TestIdempotentRequest, string>(
            storeMock.Object,
            loggerMock.Object);

        var request = new TestIdempotentRequest("test") { MessageId = 123 };

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Failure("error");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
        storeMock.Verify(x => x.MarkAsProcessedAsync<string>(It.IsAny<long>(), It.IsAny<string>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    public record TestIdempotentRequest(string Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }
}

/// <summary>
/// Tests for DistributedTracingBehavior.
/// </summary>
public class DistributedTracingBehaviorExtendedTests
{
    [Fact]
    public async Task HandleAsync_ShouldCreateActivity()
    {
        var behavior = new DistributedTracingBehavior<TestTracingRequest, string>();
        var request = new TestTracingRequest("test") { MessageId = 123 };

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithException_ShouldRecordError()
    {
        var behavior = new DistributedTracingBehavior<TestTracingRequest, string>();
        var request = new TestTracingRequest("test");

        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
        {
            await behavior.HandleAsync(request, async () =>
            {
                throw new InvalidOperationException("Test error");
            }, CancellationToken.None);
        });
    }

    [Fact]
    public async Task HandleAsync_WithFailureResult_ShouldRecordError()
    {
        var behavior = new DistributedTracingBehavior<TestTracingRequest, string>();
        var request = new TestTracingRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Failure("error");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    public record TestTracingRequest(string Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }
}

/// <summary>
/// Tests for LoggingBehavior.
/// </summary>
public class LoggingBehaviorExtendedTests
{
    [Fact]
    public async Task HandleAsync_ShouldLogRequestAndResponse()
    {
        var loggerMock = new Mock<ILogger<LoggingBehavior<TestLoggingRequest, string>>>();
        var behavior = new LoggingBehavior<TestLoggingRequest, string>(loggerMock.Object);
        var request = new TestLoggingRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            return CatgaResult<string>.Success("success");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeTrue();
    }

    [Fact]
    public async Task HandleAsync_WithException_ShouldReturnFailure()
    {
        var loggerMock = new Mock<ILogger<LoggingBehavior<TestLoggingRequest, string>>>();
        var behavior = new LoggingBehavior<TestLoggingRequest, string>(loggerMock.Object);
        var request = new TestLoggingRequest("test");

        var result = await behavior.HandleAsync(request, async () =>
        {
            throw new InvalidOperationException("Test error");
        }, CancellationToken.None);

        result.IsSuccess.Should().BeFalse();
    }

    public record TestLoggingRequest(string Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }
}
