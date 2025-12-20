using Catga.Abstractions;
using Catga.Core;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using Catga.Resilience;
using FluentAssertions;
using NSubstitute;

namespace Catga.Tests.Pipeline;

/// <summary>
/// Comprehensive tests for PollyBehavior
/// </summary>
public class PollyBehaviorTests
{
    #region Test Types

    public record TestRequest(int Value) : IRequest<string>
    {
        public long MessageId { get; init; }
    }

    #endregion

    [Fact]
    public async Task HandleAsync_ShouldExecuteThroughResiliencePipeline()
    {
        // Arrange
        var provider = Substitute.For<IResiliencePipelineProvider>();
        provider.ExecuteMediatorAsync(Arg.Any<Func<CancellationToken, ValueTask<CatgaResult<string>>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<CatgaResult<string>>>>();
                return func(CancellationToken.None);
            });

        var behavior = new PollyBehavior<TestRequest, string>(provider);
        var request = new TestRequest(42);
        var executed = false;

        // Act
        var result = await behavior.HandleAsync(
            request,
            () =>
            {
                executed = true;
                return ValueTask.FromResult(CatgaResult<string>.Success("ok"));
            });

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
        executed.Should().BeTrue();
        await provider.Received(1).ExecuteMediatorAsync(
            Arg.Any<Func<CancellationToken, ValueTask<CatgaResult<string>>>>(),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task HandleAsync_WhenNextFails_ShouldReturnFailure()
    {
        // Arrange
        var provider = Substitute.For<IResiliencePipelineProvider>();
        provider.ExecuteMediatorAsync(Arg.Any<Func<CancellationToken, ValueTask<CatgaResult<string>>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<CatgaResult<string>>>>();
                return func(CancellationToken.None);
            });

        var behavior = new PollyBehavior<TestRequest, string>(provider);
        var request = new TestRequest(42);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Failure("Something went wrong")));

        // Assert
        result.IsSuccess.Should().BeFalse();
        result.Error.Should().Contain("Something went wrong");
    }

    [Fact]
    public async Task HandleAsync_ShouldPassCancellationToken()
    {
        // Arrange
        var provider = Substitute.For<IResiliencePipelineProvider>();
        CancellationToken receivedToken = default;
        provider.ExecuteMediatorAsync(Arg.Any<Func<CancellationToken, ValueTask<CatgaResult<string>>>>(), Arg.Any<CancellationToken>())
            .Returns(callInfo =>
            {
                receivedToken = callInfo.Arg<CancellationToken>();
                var func = callInfo.Arg<Func<CancellationToken, ValueTask<CatgaResult<string>>>>();
                return func(receivedToken);
            });

        var behavior = new PollyBehavior<TestRequest, string>(provider);
        var request = new TestRequest(42);
        var cts = new CancellationTokenSource();

        // Act
        await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Success("ok")),
            cts.Token);

        // Assert
        await provider.Received(1).ExecuteMediatorAsync(
            Arg.Any<Func<CancellationToken, ValueTask<CatgaResult<string>>>>(),
            cts.Token);
    }

    [Fact]
    public async Task HandleAsync_WhenProviderThrows_ShouldPropagateException()
    {
        // Arrange
        var provider = Substitute.For<IResiliencePipelineProvider>();
        provider.ExecuteMediatorAsync(Arg.Any<Func<CancellationToken, ValueTask<CatgaResult<string>>>>(), Arg.Any<CancellationToken>())
            .Returns<ValueTask<CatgaResult<string>>>(x => throw new InvalidOperationException("Provider failed"));

        var behavior = new PollyBehavior<TestRequest, string>(provider);
        var request = new TestRequest(42);

        // Act & Assert
        var act = async () => await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Success("ok")));

        await act.Should().ThrowAsync<InvalidOperationException>()
            .WithMessage("Provider failed");
    }

    [Fact]
    public async Task HandleAsync_WithDefaultResilienceProvider_ShouldWork()
    {
        // Arrange
        var options = new CatgaResilienceOptions
        {
            MediatorTimeout = TimeSpan.FromSeconds(5),
            TransportRetryCount = 3,
            TransportRetryDelay = TimeSpan.FromMilliseconds(100)
        };
        var provider = new DefaultResiliencePipelineProvider(options);
        var behavior = new PollyBehavior<TestRequest, string>(provider);
        var request = new TestRequest(42);

        // Act
        var result = await behavior.HandleAsync(
            request,
            () => ValueTask.FromResult(CatgaResult<string>.Success("ok")));

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value.Should().Be("ok");
    }
}
