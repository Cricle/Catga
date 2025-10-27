using Catga.Abstractions;
using Catga.Core;
using Catga.Exceptions;
using FluentAssertions;
using NSubstitute;
using Xunit;

namespace Catga.Tests.Core;

public class FastPathTests
{
    // ==================== ExecuteRequestDirectAsync ====================

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithSuccessfulHandler_ShouldReturnSuccess()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var expectedResponse = new TestResponse { Result = "success" };
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult<TestResponse>.Success(expectedResponse)));

        // Act
        var result = await FastPath.ExecuteRequestDirectAsync(handler, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
    }

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithFailedHandler_ShouldReturnFailure()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult<TestResponse>.Failure("Handler failed")));

        // Act
        var result = await FastPath.ExecuteRequestDirectAsync(handler, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Be("Handler failed");
    }

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithCatgaException_ShouldWrapException()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var catgaException = new CatgaException("Catga error", "CATGA_ERR");
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, Arg.Any<CancellationToken>())
            .Returns<Task<CatgaResult<TestResponse>>>(_ => throw catgaException);

        // Act
        var result = await FastPath.ExecuteRequestDirectAsync(handler, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Handler failed: Catga error");
        result.Exception.Should().Be(catgaException);
    }

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithGeneralException_ShouldCatchAndReturnFailure()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var exception = new InvalidOperationException("Operation failed");
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, Arg.Any<CancellationToken>())
            .Returns<Task<CatgaResult<TestResponse>>>(_ => throw exception);

        // Act
        var result = await FastPath.ExecuteRequestDirectAsync(handler, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Handler failed: Operation failed");
    }

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithCancellationToken_ShouldPassToHandler()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var cts = new CancellationTokenSource();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, cts.Token)
            .Returns(Task.FromResult(CatgaResult<TestResponse>.Success(new TestResponse())));

        // Act
        await FastPath.ExecuteRequestDirectAsync(handler, request, cts.Token);

        // Assert
        await handler.Received(1).HandleAsync(request, cts.Token);
    }

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithMultipleRequests_ShouldHandleAll()
    {
        // Arrange
        var requests = new[]
        {
            new TestRequest { Data = "test1" },
            new TestRequest { Data = "test2" },
            new TestRequest { Data = "test3" }
        };
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var req = callInfo.ArgAt<TestRequest>(0);
                return Task.FromResult(CatgaResult<TestResponse>.Success(
                    new TestResponse { Result = req.Data }));
            });

        // Act
        var tasks = requests.Select(r => FastPath.ExecuteRequestDirectAsync(handler, r, CancellationToken.None));
        var results = await Task.WhenAll(tasks.Select(t => t.AsTask()));

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        results[0].Value!.Result.Should().Be("test1");
        results[1].Value!.Result.Should().Be("test2");
        results[2].Value!.Result.Should().Be("test3");
    }

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithOperationCanceledException_ShouldReturnFailure()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, cts.Token)
            .Returns<Task<CatgaResult<TestResponse>>>(_ => throw new OperationCanceledException(cts.Token));

        // Act
        var result = await FastPath.ExecuteRequestDirectAsync(handler, request, cts.Token);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Handler failed");
    }

    [Fact]
    public async Task ExecuteRequestDirectAsync_WithTimeoutException_ShouldReturnFailure()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, Arg.Any<CancellationToken>())
            .Returns<Task<CatgaResult<TestResponse>>>(_ => throw new CatgaTimeoutException("Timeout occurred"));

        // Act
        var result = await FastPath.ExecuteRequestDirectAsync(handler, request, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Timeout occurred");
    }

    // ==================== PublishEventSingleAsync ====================

    [Fact]
    public async Task PublishEventSingleAsync_WithSuccessfulHandler_ShouldComplete()
    {
        // Arrange
        var @event = new TestEvent { Data = "test" };
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(@event, Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        await FastPath.PublishEventSingleAsync(handler, @event, CancellationToken.None);

        // Assert
        await handler.Received(1).HandleAsync(@event, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishEventSingleAsync_WithException_ShouldSwallowException()
    {
        // Arrange
        var @event = new TestEvent { Data = "test" };
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(@event, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new InvalidOperationException("Handler error"));

        // Act
        Func<Task> act = () => FastPath.PublishEventSingleAsync(handler, @event, CancellationToken.None).AsTask();

        // Assert - Should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishEventSingleAsync_WithCatgaException_ShouldSwallowException()
    {
        // Arrange
        var @event = new TestEvent { Data = "test" };
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(@event, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new CatgaException("Catga error", "ERR"));

        // Act
        Func<Task> act = () => FastPath.PublishEventSingleAsync(handler, @event, CancellationToken.None).AsTask();

        // Assert - Should not throw
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishEventSingleAsync_WithCancellationToken_ShouldPassToHandler()
    {
        // Arrange
        var @event = new TestEvent { Data = "test" };
        var cts = new CancellationTokenSource();
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(@event, cts.Token)
            .Returns(Task.CompletedTask);

        // Act
        await FastPath.PublishEventSingleAsync(handler, @event, cts.Token);

        // Assert
        await handler.Received(1).HandleAsync(@event, cts.Token);
    }

    [Fact]
    public async Task PublishEventSingleAsync_WithMultipleEvents_ShouldHandleAll()
    {
        // Arrange
        var events = new[]
        {
            new TestEvent { Data = "event1" },
            new TestEvent { Data = "event2" },
            new TestEvent { Data = "event3" }
        };
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        // Act
        var tasks = events.Select(e => FastPath.PublishEventSingleAsync(handler, e, CancellationToken.None));
        await Task.WhenAll(tasks.Select(t => t.AsTask()));

        // Assert
        await handler.Received(3).HandleAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishEventSingleAsync_WithOperationCanceledException_ShouldSwallowException()
    {
        // Arrange
        var @event = new TestEvent { Data = "test" };
        var cts = new CancellationTokenSource();
        cts.Cancel();
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(@event, cts.Token)
            .Returns<Task>(_ => throw new OperationCanceledException(cts.Token));

        // Act
        Func<Task> act = () => FastPath.PublishEventSingleAsync(handler, @event, cts.Token).AsTask();

        // Assert - Should not throw even when cancelled
        await act.Should().NotThrowAsync();
    }

    [Fact]
    public async Task PublishEventSingleAsync_WithArgumentException_ShouldSwallowException()
    {
        // Arrange
        var @event = new TestEvent { Data = "test" };
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(@event, Arg.Any<CancellationToken>())
            .Returns<Task>(_ => throw new ArgumentException("Invalid argument"));

        // Act
        Func<Task> act = () => FastPath.PublishEventSingleAsync(handler, @event, CancellationToken.None).AsTask();

        // Assert - Should not throw
        await act.Should().NotThrowAsync();
    }

    // ==================== CanUseFastPath ====================

    [Fact]
    public void CanUseFastPath_WithZeroBehaviors_ShouldReturnTrue()
    {
        // Act
        var result = FastPath.CanUseFastPath(0);

        // Assert
        result.Should().BeTrue();
    }

    [Fact]
    public void CanUseFastPath_WithOneBehavior_ShouldReturnFalse()
    {
        // Act
        var result = FastPath.CanUseFastPath(1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanUseFastPath_WithMultipleBehaviors_ShouldReturnFalse()
    {
        // Act
        var result1 = FastPath.CanUseFastPath(2);
        var result2 = FastPath.CanUseFastPath(5);
        var result3 = FastPath.CanUseFastPath(10);

        // Assert
        result1.Should().BeFalse();
        result2.Should().BeFalse();
        result3.Should().BeFalse();
    }

    [Fact]
    public void CanUseFastPath_WithNegativeBehaviors_ShouldReturnFalse()
    {
        // Act
        var result = FastPath.CanUseFastPath(-1);

        // Assert
        result.Should().BeFalse();
    }

    [Fact]
    public void CanUseFastPath_WithLargeNumber_ShouldReturnFalse()
    {
        // Act
        var result = FastPath.CanUseFastPath(1000);

        // Assert
        result.Should().BeFalse();
    }

    // ==================== Performance & Concurrency ====================

    [Fact]
    public async Task ExecuteRequestDirectAsync_ShouldBeThreadSafe()
    {
        // Arrange
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(CatgaResult<TestResponse>.Success(new TestResponse())));

        var requests = Enumerable.Range(1, 100).Select(i => new TestRequest { Data = $"test{i}" }).ToArray();

        // Act
        var tasks = requests.Select(r => FastPath.ExecuteRequestDirectAsync(handler, r, CancellationToken.None));
        var results = await Task.WhenAll(tasks.Select(t => t.AsTask()));

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeTrue());
        await handler.Received(100).HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task PublishEventSingleAsync_ShouldBeThreadSafe()
    {
        // Arrange
        var handler = Substitute.For<IEventHandler<TestEvent>>();
        handler.HandleAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>())
            .Returns(Task.CompletedTask);

        var events = Enumerable.Range(1, 100).Select(i => new TestEvent { Data = $"event{i}" }).ToArray();

        // Act
        var tasks = events.Select(e => FastPath.PublishEventSingleAsync(handler, e, CancellationToken.None));
        await Task.WhenAll(tasks.Select(t => t.AsTask()));

        // Assert
        await handler.Received(100).HandleAsync(Arg.Any<TestEvent>(), Arg.Any<CancellationToken>());
    }

    // ==================== Test Helpers ====================

    public record TestRequest : IRequest<TestResponse>
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }

    public record TestResponse
    {
        public string Result { get; init; } = string.Empty;
    }

    public record TestEvent : IEvent
    {
        public long MessageId { get; init; }
        public string Data { get; init; } = string.Empty;
    }
}

