using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Comprehensive tests for CompensationBehavior and CompensationPublisher
/// </summary>
public class CompensationBehaviorTests
{
    #region Test Types

    public record TestRequest(int Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    public record CompensationEvent(string Reason, int OriginalValue) : IEvent
    {
        public long MessageId { get; init; }
    }

    public class TestCompensationPublisher : CompensationPublisher<TestRequest, CompensationEvent>
    {
        protected override CompensationEvent? CreateCompensationEvent(TestRequest request, string? error)
        {
            return new CompensationEvent(error ?? "Unknown error", request.Value);
        }
    }

    public class NullCompensationPublisher : CompensationPublisher<TestRequest, CompensationEvent>
    {
        protected override CompensationEvent? CreateCompensationEvent(TestRequest request, string? error)
        {
            return null;
        }
    }

    #endregion

    #region CompensationBehavior Tests

    [Fact]
    public async Task HandleAsync_OnSuccess_ShouldNotPublishCompensation()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var logger = Substitute.For<ILogger<CompensationBehavior<TestRequest, string>>>();
        var publisher = new TestCompensationPublisher();
        var behavior = new CompensationBehavior<TestRequest, string>(mediator, logger, publisher);
        var request = new TestRequest(42);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Success("ok")));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_OnFailure_ShouldPublishCompensation()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var logger = Substitute.For<ILogger<CompensationBehavior<TestRequest, string>>>();
        var publisher = new TestCompensationPublisher();
        var behavior = new CompensationBehavior<TestRequest, string>(mediator, logger, publisher);
        var request = new TestRequest(42);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Failure("Something went wrong")));

        // Assert
        result.IsSuccess.Should().BeFalse();
        await mediator.Received(1).PublishAsync(
            Arg.Is<CompensationEvent>(e => e.Reason == "Something went wrong" && e.OriginalValue == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithNoPublisher_ShouldNotPublishCompensation()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var logger = Substitute.For<ILogger<CompensationBehavior<TestRequest, string>>>();
        var behavior = new CompensationBehavior<TestRequest, string>(mediator, logger, null);
        var request = new TestRequest(42);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Failure("Error")));

        // Assert
        result.IsSuccess.Should().BeFalse();
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPublisherReturnsNull_ShouldNotPublish()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var logger = Substitute.For<ILogger<CompensationBehavior<TestRequest, string>>>();
        var publisher = new NullCompensationPublisher();
        var behavior = new CompensationBehavior<TestRequest, string>(mediator, logger, publisher);
        var request = new TestRequest(42);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Failure("Error")));

        // Assert
        result.IsSuccess.Should().BeFalse();
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenPublishThrows_ShouldStillReturnOriginalResult()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        mediator.PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromException(new InvalidOperationException("Publish failed")));
        var logger = Substitute.For<ILogger<CompensationBehavior<TestRequest, string>>>();
        var publisher = new TestCompensationPublisher();
        var behavior = new CompensationBehavior<TestRequest, string>(mediator, logger, publisher);
        var request = new TestRequest(42);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Failure("Original error")));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Original error");
    }

    #endregion

    #region CompensationPublisher Tests

    [Fact]
    public async Task CompensationPublisher_PublishCompensationAsync_ShouldPublishEvent()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var publisher = new TestCompensationPublisher();
        var request = new TestRequest(42);

        // Act
        var eventTypeName = await publisher.PublishCompensationAsync(mediator, request, "Test error", CancellationToken.None);

        // Assert
        eventTypeName.Should().Be("CompensationEvent");
        await mediator.Received(1).PublishAsync(
            Arg.Is<CompensationEvent>(e => e.Reason == "Test error" && e.OriginalValue == 42),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompensationPublisher_WhenCreateReturnsNull_ShouldReturnNull()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var publisher = new NullCompensationPublisher();
        var request = new TestRequest(42);

        // Act
        var eventTypeName = await publisher.PublishCompensationAsync(mediator, request, "Test error", CancellationToken.None);

        // Assert
        eventTypeName.Should().BeNull();
        await mediator.DidNotReceive().PublishAsync(Arg.Any<CompensationEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task CompensationPublisher_WithNullError_ShouldStillCreateEvent()
    {
        // Arrange
        var mediator = Substitute.For<ICatgaMediator>();
        var publisher = new TestCompensationPublisher();
        var request = new TestRequest(42);

        // Act
        var eventTypeName = await publisher.PublishCompensationAsync(mediator, request, null, CancellationToken.None);

        // Assert
        eventTypeName.Should().Be("CompensationEvent");
        await mediator.Received(1).PublishAsync(
            Arg.Is<CompensationEvent>(e => e.Reason == "Unknown error"),
            Arg.Any<CancellationToken>());
    }

    #endregion
}
