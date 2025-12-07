using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using FluentAssertions;
using NSubstitute;
using NSubstitute.Core;
using Xunit;

namespace Catga.Tests.Pipeline;

/// <summary>
/// PipelineExecutor单元测试
/// 目标覆盖率: 从 0% → 95%+
/// </summary>
public class PipelineExecutorTests
{
    #region Empty Pipeline Tests

    [Fact]
    public async Task ExecuteAsync_WithNoBehaviors_ShouldExecuteHandlerDirectly()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var expectedResponse = new TestResponse { Result = "OK" };
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(request, Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(expectedResponse)));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>>();

        // Act
        var result = await PipelineExecutor.ExecuteAsync(
            request, handler, behaviors, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(expectedResponse);
        await handler.Received(1).HandleAsync(request, Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithEmptyBehaviorList_ShouldNotInvokeBehaviors()
    {
        // Arrange
        var request = new TestRequest();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse())));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>>();

        // Act
        await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert - 只验证handler被调用，无需验证behaviors（因为列表为空）
        await handler.Received(1).HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Single Behavior Tests

    [Fact]
    public async Task ExecuteAsync_WithSingleBehavior_ShouldExecuteBehaviorThenHandler()
    {
        // Arrange
        var request = new TestRequest { Data = "test" };
        var response = new TestResponse { Result = "OK" };
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(response)));

        var executionOrder = new List<string>();
        var behavior = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                executionOrder.Add("Behavior");
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                var result = await next();
                executionOrder.Add("Behavior-After");
                return result;
            }));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior };

        // Act
        var result = await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        executionOrder.Should().ContainInOrder("Behavior", "Behavior-After");
        await handler.Received(1).HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_WithSingleBehavior_ShouldPassRequestToHandler()
    {
        // Arrange
        var request = new TestRequest { Data = "original" };
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse())));

        var behavior = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                return await next();
            }));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior };

        // Act
        await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert
        await handler.Received(1).HandleAsync(
            Arg.Is<TestRequest>(r => r.Data == "original"),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Multiple Behaviors Tests

    [Fact]
    public async Task ExecuteAsync_WithMultipleBehaviors_ShouldExecuteInOrder()
    {
        // Arrange
        var request = new TestRequest();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse())));

        var executionOrder = new List<string>();

        var behavior1 = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior1.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                executionOrder.Add("B1-Start");
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                var result = await next();
                executionOrder.Add("B1-End");
                return result;
            }));

        var behavior2 = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior2.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                executionOrder.Add("B2-Start");
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                var result = await next();
                executionOrder.Add("B2-End");
                return result;
            }));

        var behavior3 = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior3.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                executionOrder.Add("B3-Start");
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                var result = await next();
                executionOrder.Add("B3-End");
                return result;
            }));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>>
        {
            behavior1, behavior2, behavior3
        };

        // Act
        await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert - 验证"洋葱模型"执行顺序
        executionOrder.Should().ContainInOrder(
            "B1-Start", "B2-Start", "B3-Start",
            "B3-End", "B2-End", "B1-End");
    }

    [Fact]
    public async Task ExecuteAsync_WithMultipleBehaviors_ShouldCallHandlerOnce()
    {
        // Arrange
        var request = new TestRequest();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse())));

        var behavior1 = CreatePassThroughBehavior();
        var behavior2 = CreatePassThroughBehavior();
        var behavior3 = CreatePassThroughBehavior();

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>>
        {
            behavior1, behavior2, behavior3
        };

        // Act
        await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert
        await handler.Received(1).HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    #endregion

    #region Short-Circuit Tests

    [Fact]
    public async Task ExecuteAsync_BehaviorDoesNotCallNext_ShouldShortCircuit()
    {
        // Arrange
        var request = new TestRequest();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();

        var shortCircuitResponse = new TestResponse { Result = "Short-circuited" };
        var behavior = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(shortCircuitResponse)));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior };

        // Act
        var result = await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(shortCircuitResponse);
        await handler.DidNotReceive().HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task ExecuteAsync_FirstBehaviorShortCircuits_ShouldNotExecuteOtherBehaviors()
    {
        // Arrange
        var request = new TestRequest();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();

        var behavior1 = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior1.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Failure("Short-circuit")));

        var behavior2 = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior1, behavior2 };

        // Act
        var result = await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeFalse();
        await behavior2.DidNotReceive().HandleAsync(
            Arg.Any<TestRequest>(),
            Arg.Any<PipelineDelegate<TestResponse>>(),
            Arg.Any<CancellationToken>());
    }

    #endregion

    #region Exception Propagation Tests

    [Fact]
    public async Task ExecuteAsync_HandlerThrowsException_ShouldPropagateToCallerAsync()
    {
        // Arrange
        var request = new TestRequest();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(Task.FromException<CatgaResult<TestResponse>>(new InvalidOperationException("Handler error"))));

        var behavior = CreatePassThroughBehavior();
        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None));
    }

    [Fact]
    public async Task ExecuteAsync_BehaviorThrowsException_ShouldPropagateToCallerAsync()
    {
        // Arrange
        var request = new TestRequest();
        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();

        var behavior = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<CatgaResult<TestResponse>>>(_ => throw new InvalidOperationException("Behavior error"));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior };

        // Act & Assert
        await Assert.ThrowsAsync<InvalidOperationException>(async () =>
            await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None));
    }

    #endregion

    #region CancellationToken Tests

    [Fact]
    public async Task ExecuteAsync_WithCancellationToken_ShouldPassToHandler()
    {
        // Arrange
        var request = new TestRequest();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), cancellationToken)
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse())));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>>();

        // Act
        await PipelineExecutor.ExecuteAsync(request, handler, behaviors, cancellationToken);

        // Assert
        await handler.Received(1).HandleAsync(Arg.Any<TestRequest>(), cancellationToken);
    }

    [Fact]
    public async Task ExecuteAsync_WithCancellationToken_ShouldPassToBehaviors()
    {
        // Arrange
        var request = new TestRequest();
        var cts = new CancellationTokenSource();
        var cancellationToken = cts.Token;

        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(new TestResponse())));

        var behavior = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), cancellationToken)
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                return await next();
            }));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior };

        // Act
        await PipelineExecutor.ExecuteAsync(request, handler, behaviors, cancellationToken);

        // Assert
        await behavior.Received(1).HandleAsync(
            Arg.Any<TestRequest>(),
            Arg.Any<PipelineDelegate<TestResponse>>(),
            cancellationToken);
    }

    #endregion

    #region Result Transformation Tests

    [Fact]
    public async Task ExecuteAsync_BehaviorModifiesResult_ShouldReturnModifiedResult()
    {
        // Arrange
        var request = new TestRequest();
        var handlerResponse = new TestResponse { Result = "Original" };
        var modifiedResponse = new TestResponse { Result = "Modified" };

        var handler = Substitute.For<IRequestHandler<TestRequest, TestResponse>>();
        handler.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<CancellationToken>())
            .Returns(new ValueTask<CatgaResult<TestResponse>>(CatgaResult<TestResponse>.Success(handlerResponse)));

        var behavior = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior.HandleAsync(Arg.Any<TestRequest>(), Arg.Any<PipelineDelegate<TestResponse>>(), Arg.Any<CancellationToken>())
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                await next(); // 忽略原结果
                return CatgaResult<TestResponse>.Success(modifiedResponse);
            }));

        var behaviors = new List<IPipelineBehavior<TestRequest, TestResponse>> { behavior };

        // Act
        var result = await PipelineExecutor.ExecuteAsync(request, handler, behaviors, CancellationToken.None);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be(modifiedResponse);
        result.Value!.Result.Should().Be("Modified");
    }

    #endregion

    #region Helper Methods

    private IPipelineBehavior<TestRequest, TestResponse> CreatePassThroughBehavior()
    {
        var behavior = Substitute.For<IPipelineBehavior<TestRequest, TestResponse>>();
        behavior.HandleAsync(
            Arg.Any<TestRequest>(),
            Arg.Any<PipelineDelegate<TestResponse>>(),
            Arg.Any<CancellationToken>())
            .Returns(new Func<CallInfo, ValueTask<CatgaResult<TestResponse>>>(async callInfo =>
            {
                var next = callInfo.Arg<PipelineDelegate<TestResponse>>();
                return await next();
            }));
        return behavior;
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

