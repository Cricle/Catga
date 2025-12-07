using Catga.Abstractions;
using Catga.Core;
using Catga.Idempotency;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;

namespace Catga.Tests.Pipeline;

/// <summary>
/// 幂等性行为测试
/// </summary>
public class IdempotencyBehaviorTests
{
    [Fact]
    public async Task HandleAsync_WithCachedResult_ShouldReturnCachedValue()
    {
        // Arrange
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var logger = NullLogger<IdempotencyBehavior<TestRequest, TestResponse>>.Instance;
        var behavior = new IdempotencyBehavior<TestRequest, TestResponse>(idempotencyStore, logger);

        var request = new TestRequest("test");
        var cachedResponse = new TestResponse("Cached");

        idempotencyStore
            .HasBeenProcessedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(true);

        idempotencyStore
            .GetCachedResultAsync<TestResponse>(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(cachedResponse);

        var nextCalled = false;
        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("New")));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Value!.Message.Should().Be("Cached");
        nextCalled.Should().BeFalse(); // next 不应该被调用
    }

    [Fact]
    public async Task HandleAsync_WithoutCache_ShouldExecuteAndCache()
    {
        // Arrange
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var logger = NullLogger<IdempotencyBehavior<TestRequest, TestResponse>>.Instance;
        var behavior = new IdempotencyBehavior<TestRequest, TestResponse>(idempotencyStore, logger);

        var request = new TestRequest("test");

        idempotencyStore
            .HasBeenProcessedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(false);

        var nextCalled = false;
        PipelineDelegate<TestResponse> next = () =>
        {
            nextCalled = true;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("New Result")));
        };

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.Should().NotBeNull();
        result.IsSuccess.Should().BeTrue();
        result.Value!.Message.Should().Be("New Result");
        nextCalled.Should().BeTrue(); // next 应该被调用

        // 验证结果被缓存 (缓存的是 TestResponse 而不是 CatgaResult<TestResponse>)
        await idempotencyStore
            .Received(1)
            .MarkAsProcessedAsync(Arg.Any<long>(), Arg.Any<TestResponse>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNextThrows_ShouldNotCache()
    {
        // Arrange
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var logger = NullLogger<IdempotencyBehavior<TestRequest, TestResponse>>.Instance;
        var behavior = new IdempotencyBehavior<TestRequest, TestResponse>(idempotencyStore, logger);

        var request = new TestRequest("test");

        idempotencyStore
            .HasBeenProcessedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(false);

        PipelineDelegate<TestResponse> next = () =>
        {
            throw new InvalidOperationException("Test exception");
        };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () => await behavior.HandleAsync(request, next));

        // 验证结果没有被缓存
        await idempotencyStore
            .DidNotReceive()
            .MarkAsProcessedAsync(Arg.Any<long>(), Arg.Any<CatgaResult<TestResponse>>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WithCachedNullResult_ShouldReturnCachedNull()
    {
        // Arrange
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var logger = NullLogger<IdempotencyBehavior<TestRequest, TestResponse>>.Instance;
        var behavior = new IdempotencyBehavior<TestRequest, TestResponse>(idempotencyStore, logger);

        var request = new TestRequest("test");

        idempotencyStore
            .HasBeenProcessedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(true);

        idempotencyStore
            .GetCachedResultAsync<TestResponse>(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns((TestResponse?)null);

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("New")));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().BeNull();
    }

    [Fact]
    public async Task HandleAsync_WithCancellationToken_ShouldPassToStore()
    {
        // Arrange
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var logger = NullLogger<IdempotencyBehavior<TestRequest, TestResponse>>.Instance;
        var behavior = new IdempotencyBehavior<TestRequest, TestResponse>(idempotencyStore, logger);

        var request = new TestRequest("test");
        var cts = new CancellationTokenSource();

        idempotencyStore
            .HasBeenProcessedAsync(Arg.Any<long>(), cts.Token)
            .Returns(false);

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("Result")));

        // Act
        await behavior.HandleAsync(request, next, cts.Token);

        // Assert
        await idempotencyStore.Received(1).HasBeenProcessedAsync(Arg.Any<long>(), cts.Token);
    }

    [Fact]
    public async Task HandleAsync_WithFailedResult_ShouldNotCache()
    {
        // Arrange
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var logger = NullLogger<IdempotencyBehavior<TestRequest, TestResponse>>.Instance;
        var behavior = new IdempotencyBehavior<TestRequest, TestResponse>(idempotencyStore, logger);

        var request = new TestRequest("test");

        idempotencyStore
            .HasBeenProcessedAsync(Arg.Any<long>(), Arg.Any<CancellationToken>())
            .Returns(false);

        PipelineDelegate<TestResponse> next = () =>
            new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Failure("Error"));

        // Act
        var result = await behavior.HandleAsync(request, next);

        // Assert
        result.IsSuccess.Should().BeFalse();
        await idempotencyStore
            .DidNotReceive()
            .MarkAsProcessedAsync(Arg.Any<long>(), Arg.Any<TestResponse>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_MultipleCallsWithSameMessageId_ShouldReturnCached()
    {
        // Arrange
        var idempotencyStore = Substitute.For<IIdempotencyStore>();
        var logger = NullLogger<IdempotencyBehavior<TestRequest, TestResponse>>.Instance;
        var behavior = new IdempotencyBehavior<TestRequest, TestResponse>(idempotencyStore, logger);

        var messageId = 12345L;
        var request = new TestRequest("test") { MessageId = messageId };
        var cachedResponse = new TestResponse("Cached");

        var callCount = 0;
        idempotencyStore
            .HasBeenProcessedAsync(messageId, Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                callCount++;
                return callCount > 1; // First call returns false, subsequent return true
            });

        idempotencyStore
            .GetCachedResultAsync<TestResponse>(messageId, Arg.Any<CancellationToken>())
            .Returns(cachedResponse);

        var nextCallCount = 0;
        PipelineDelegate<TestResponse> next = () =>
        {
            nextCallCount++;
            return new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse("New")));
        };

        // Act - First call
        var result1 = await behavior.HandleAsync(request, next);
        // Act - Second call
        var result2 = await behavior.HandleAsync(request, next);

        // Assert
        result1.IsSuccess.Should().BeTrue();
        result2.IsSuccess.Should().BeTrue();
        result2.Value!.Message.Should().Be("Cached");
        nextCallCount.Should().Be(1); // next should only be called once
    }
}

// 测试用的请求和响应类型
public record TestRequest(string Value) : IRequest<TestResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record TestResponse(string Message);

