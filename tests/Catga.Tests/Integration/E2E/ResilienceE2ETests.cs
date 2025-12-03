using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using Catga.Exceptions;
using Catga.Pipeline;
using Catga.Pipeline.Behaviors;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// E2E tests for resilience scenarios
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class ResilienceE2ETests
{
    [Fact]
    public async Task Handler_ThrowsTransientError_WithRetryBehavior_ShouldRetry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(opt => opt.EnableRetry = true);
        var attemptTracker = new AttemptTracker();
        services.AddSingleton(attemptTracker);
        services.AddScoped<IRequestHandler<RetryableCommand, RetryableResponse>, RetryableHandler>();
        services.AddScoped(typeof(IPipelineBehavior<,>), typeof(RetryBehavior<,>));
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new RetryableCommand { MessageId = MessageExtensions.NewMessageId(), FailCount = 2 };
        var result = await mediator.SendAsync<RetryableCommand, RetryableResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        attemptTracker.Attempts.Should().BeGreaterOrEqualTo(1);
    }

    [Fact]
    public async Task Handler_ThrowsPermanentError_ShouldNotRetry()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        var attemptTracker = new AttemptTracker();
        services.AddSingleton(attemptTracker);
        services.AddScoped<IRequestHandler<PermanentFailCommand, PermanentFailResponse>, PermanentFailHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new PermanentFailCommand { MessageId = MessageExtensions.NewMessageId() };
        var result = await mediator.SendAsync<PermanentFailCommand, PermanentFailResponse>(command);

        // Assert
        result.IsSuccess.Should().BeFalse();
        attemptTracker.Attempts.Should().Be(1);
    }

    [Fact]
    public async Task Timeout_ShouldReturnFailure()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga(opt => opt.TimeoutSeconds = 1);
        services.AddScoped<IRequestHandler<SlowCommand, SlowResponse>, SlowHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(100));

        // Act
        var command = new SlowCommand { MessageId = MessageExtensions.NewMessageId(), DelayMs = 5000 };

        try
        {
            var result = await mediator.SendAsync<SlowCommand, SlowResponse>(command, cts.Token);
            result.IsSuccess.Should().BeFalse();
        }
        catch (OperationCanceledException)
        {
            // Expected
        }
    }

    [Fact]
    public async Task MultipleHandlers_OneThrows_OthersShouldComplete()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IEventHandler<MixedEvent>, SuccessfulEventHandler>();
        services.AddScoped<IEventHandler<MixedEvent>, FailingEventHandler>();
        services.AddScoped<IEventHandler<MixedEvent>, AnotherSuccessfulEventHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        SuccessfulEventHandler.ReceivedCount = 0;
        AnotherSuccessfulEventHandler.ReceivedCount = 0;

        // Act
        var @event = new MixedEvent { MessageId = MessageExtensions.NewMessageId() };
        await mediator.PublishAsync(@event);

        // Assert
        SuccessfulEventHandler.ReceivedCount.Should().Be(1);
        AnotherSuccessfulEventHandler.ReceivedCount.Should().Be(1);
    }

    [Fact]
    public async Task ConcurrentRequests_ShouldAllComplete()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<ConcurrentCommand, ConcurrentResponse>, ConcurrentHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var tasks = Enumerable.Range(1, 50)
            .Select(i => mediator.SendAsync<ConcurrentCommand, ConcurrentResponse>(
                new ConcurrentCommand { MessageId = MessageExtensions.NewMessageId(), Value = i }))
            .ToList();

        var results = await Task.WhenAll(tasks.Select(t => t.AsTask()));

        // Assert
        results.Should().HaveCount(50);
        results.All(r => r.IsSuccess).Should().BeTrue();
    }

    [Fact]
    public async Task LargePayload_ShouldProcess()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<LargePayloadCommand, LargePayloadResponse>, LargePayloadHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var largeData = new string('x', 100_000);
        var command = new LargePayloadCommand { MessageId = MessageExtensions.NewMessageId(), Data = largeData };
        var result = await mediator.SendAsync<LargePayloadCommand, LargePayloadResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.DataLength.Should().Be(100_000);
    }

    [Fact]
    public async Task NestedRequests_ShouldWork()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<OuterCommand, OuterResponse>, OuterHandler>();
        services.AddScoped<IRequestHandler<InnerCommand, InnerResponse>, InnerHandler>();
        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var command = new OuterCommand { MessageId = MessageExtensions.NewMessageId(), Value = 5 };
        var result = await mediator.SendAsync<OuterCommand, OuterResponse>(command);

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.FinalValue.Should().Be(10); // 5 * 2 from inner handler
    }

    #region Test Types

    private sealed class AttemptTracker
    {
        public int Attempts;
    }

    [MemoryPackable]
    private partial record RetryableCommand : IRequest<RetryableResponse>
    {
        public required long MessageId { get; init; }
        public int FailCount { get; init; }
    }

    [MemoryPackable]
    private partial record RetryableResponse { }

    private sealed class RetryableHandler : IRequestHandler<RetryableCommand, RetryableResponse>
    {
        private readonly AttemptTracker _tracker;
        private static int _callCount;

        public RetryableHandler(AttemptTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<RetryableResponse>> HandleAsync(RetryableCommand request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _tracker.Attempts);
            var count = Interlocked.Increment(ref _callCount);

            if (count <= request.FailCount)
            {
                throw new CatgaException("Transient error") { IsRetryable = true };
            }

            return Task.FromResult(CatgaResult<RetryableResponse>.Success(new RetryableResponse()));
        }
    }

    [MemoryPackable]
    private partial record PermanentFailCommand : IRequest<PermanentFailResponse>
    {
        public required long MessageId { get; init; }
    }

    [MemoryPackable]
    private partial record PermanentFailResponse { }

    private sealed class PermanentFailHandler : IRequestHandler<PermanentFailCommand, PermanentFailResponse>
    {
        private readonly AttemptTracker _tracker;

        public PermanentFailHandler(AttemptTracker tracker) => _tracker = tracker;

        public Task<CatgaResult<PermanentFailResponse>> HandleAsync(PermanentFailCommand request, CancellationToken ct = default)
        {
            Interlocked.Increment(ref _tracker.Attempts);
            throw new InvalidOperationException("Permanent error");
        }
    }

    [MemoryPackable]
    private partial record SlowCommand : IRequest<SlowResponse>
    {
        public required long MessageId { get; init; }
        public int DelayMs { get; init; }
    }

    [MemoryPackable]
    private partial record SlowResponse { }

    private sealed class SlowHandler : IRequestHandler<SlowCommand, SlowResponse>
    {
        public async Task<CatgaResult<SlowResponse>> HandleAsync(SlowCommand request, CancellationToken ct = default)
        {
            await Task.Delay(request.DelayMs, ct);
            return CatgaResult<SlowResponse>.Success(new SlowResponse());
        }
    }

    [MemoryPackable]
    private partial record MixedEvent : IEvent
    {
        public required long MessageId { get; init; }
    }

    private sealed class SuccessfulEventHandler : IEventHandler<MixedEvent>
    {
        public static int ReceivedCount;
        public Task HandleAsync(MixedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return Task.CompletedTask;
        }
    }

    private sealed class FailingEventHandler : IEventHandler<MixedEvent>
    {
        public Task HandleAsync(MixedEvent @event, CancellationToken ct = default)
        {
            throw new InvalidOperationException("Event handler failed");
        }
    }

    private sealed class AnotherSuccessfulEventHandler : IEventHandler<MixedEvent>
    {
        public static int ReceivedCount;
        public Task HandleAsync(MixedEvent @event, CancellationToken ct = default)
        {
            Interlocked.Increment(ref ReceivedCount);
            return Task.CompletedTask;
        }
    }

    [MemoryPackable]
    private partial record ConcurrentCommand : IRequest<ConcurrentResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record ConcurrentResponse
    {
        public int ProcessedValue { get; init; }
    }

    private sealed class ConcurrentHandler : IRequestHandler<ConcurrentCommand, ConcurrentResponse>
    {
        public async Task<CatgaResult<ConcurrentResponse>> HandleAsync(ConcurrentCommand request, CancellationToken ct = default)
        {
            await Task.Delay(10, ct); // Simulate some work
            return CatgaResult<ConcurrentResponse>.Success(new ConcurrentResponse { ProcessedValue = request.Value * 2 });
        }
    }

    [MemoryPackable]
    private partial record LargePayloadCommand : IRequest<LargePayloadResponse>
    {
        public required long MessageId { get; init; }
        public required string Data { get; init; }
    }

    [MemoryPackable]
    private partial record LargePayloadResponse
    {
        public int DataLength { get; init; }
    }

    private sealed class LargePayloadHandler : IRequestHandler<LargePayloadCommand, LargePayloadResponse>
    {
        public Task<CatgaResult<LargePayloadResponse>> HandleAsync(LargePayloadCommand request, CancellationToken ct = default)
        {
            return Task.FromResult(CatgaResult<LargePayloadResponse>.Success(new LargePayloadResponse { DataLength = request.Data.Length }));
        }
    }

    [MemoryPackable]
    private partial record OuterCommand : IRequest<OuterResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record OuterResponse
    {
        public int FinalValue { get; init; }
    }

    [MemoryPackable]
    private partial record InnerCommand : IRequest<InnerResponse>
    {
        public required long MessageId { get; init; }
        public int Value { get; init; }
    }

    [MemoryPackable]
    private partial record InnerResponse
    {
        public int DoubledValue { get; init; }
    }

    private sealed class OuterHandler : IRequestHandler<OuterCommand, OuterResponse>
    {
        private readonly ICatgaMediator _mediator;

        public OuterHandler(ICatgaMediator mediator) => _mediator = mediator;

        public async Task<CatgaResult<OuterResponse>> HandleAsync(OuterCommand request, CancellationToken ct = default)
        {
            var innerCommand = new InnerCommand { MessageId = MessageExtensions.NewMessageId(), Value = request.Value };
            var innerResult = await _mediator.SendAsync<InnerCommand, InnerResponse>(innerCommand, ct);

            if (!innerResult.IsSuccess)
            {
                return CatgaResult<OuterResponse>.Failure(innerResult.Error ?? "Inner command failed");
            }

            return CatgaResult<OuterResponse>.Success(new OuterResponse { FinalValue = innerResult.Value!.DoubledValue });
        }
    }

    private sealed class InnerHandler : IRequestHandler<InnerCommand, InnerResponse>
    {
        public Task<CatgaResult<InnerResponse>> HandleAsync(InnerCommand request, CancellationToken ct = default)
        {
            return Task.FromResult(CatgaResult<InnerResponse>.Success(new InnerResponse { DoubledValue = request.Value * 2 }));
        }
    }

    #endregion
}
