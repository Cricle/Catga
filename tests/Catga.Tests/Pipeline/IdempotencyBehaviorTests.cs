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
}

// 测试用的请求和响应类型
public record TestRequest(string Value) : IRequest<TestResponse>
{
    public long MessageId { get; init; } = MessageExtensions.NewMessageId();
}

public record TestResponse(string Message);

