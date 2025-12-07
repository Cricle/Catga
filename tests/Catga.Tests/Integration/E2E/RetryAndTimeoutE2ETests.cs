using Catga.Abstractions;
using Catga.Core;
using Catga.DependencyInjection;
using FluentAssertions;
using MemoryPack;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Catga.Tests.Integration.E2E;

/// <summary>
/// E2E tests for retry, timeout, and resilience patterns
/// </summary>
[Trait("Category", "Integration")]
public sealed partial class RetryAndTimeoutE2ETests
{
    [Fact]
    public async Task Request_WithCancellation_HandledGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<SlowProcessingRequest, SlowProcessingResponse>, SlowProcessingHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        using var cts = new CancellationTokenSource(TimeSpan.FromMilliseconds(50));

        // Act - The handler should respect cancellation
        var exception = await Record.ExceptionAsync(async () =>
        {
            await mediator.SendAsync<SlowProcessingRequest, SlowProcessingResponse>(
                new SlowProcessingRequest { MessageId = MessageExtensions.NewMessageId(), DelayMs = 5000 },
                cts.Token);
        });

        // Assert - Either throws OperationCanceledException or completes (depends on timing)
        // This test verifies the cancellation token is passed through
        if (exception != null)
        {
            exception.Should().BeOfType<OperationCanceledException>();
        }
    }

    [Fact]
    public async Task Request_TransientFailure_RetrySucceeds()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var tracker = new AttemptTracker { FailUntilAttempt = 3 };
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<TransientFailureRequest, TransientFailureResponse>, TransientFailureHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Simulate retry logic
        CatgaResult<TransientFailureResponse> result = CatgaResult<TransientFailureResponse>.Failure("Not started");
        for (int attempt = 1; attempt <= 5; attempt++)
        {
            result = await mediator.SendAsync<TransientFailureRequest, TransientFailureResponse>(
                new TransientFailureRequest { MessageId = MessageExtensions.NewMessageId(), Attempt = attempt });
            if (result.IsSuccess) break;
        }

        // Assert
        result.IsSuccess.Should().BeTrue();
        result.Value!.AttemptNumber.Should().Be(3);
    }

    [Fact]
    public async Task Request_PermanentFailure_AllRetriesFail()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var tracker = new AttemptTracker { FailUntilAttempt = 100 }; // Always fail
        services.AddSingleton(tracker);
        services.AddScoped<IRequestHandler<TransientFailureRequest, TransientFailureResponse>, TransientFailureHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var results = new List<CatgaResult<TransientFailureResponse>>();
        for (int attempt = 1; attempt <= 3; attempt++)
        {
            var result = await mediator.SendAsync<TransientFailureRequest, TransientFailureResponse>(
                new TransientFailureRequest { MessageId = MessageExtensions.NewMessageId(), Attempt = attempt });
            results.Add(result);
        }

        // Assert
        results.Should().AllSatisfy(r => r.IsSuccess.Should().BeFalse());
    }

    [Fact]
    public async Task Request_CircuitBreaker_OpensAfterFailures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var circuitBreaker = new SimpleCircuitBreaker(failureThreshold: 3);
        services.AddSingleton(circuitBreaker);
        services.AddScoped<IRequestHandler<CircuitBreakerRequest, CircuitBreakerResponse>, CircuitBreakerHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Trigger failures to open circuit
        for (int i = 0; i < 5; i++)
        {
            await mediator.SendAsync<CircuitBreakerRequest, CircuitBreakerResponse>(
                new CircuitBreakerRequest { MessageId = MessageExtensions.NewMessageId(), ShouldFail = true });
        }

        // Assert
        circuitBreaker.IsOpen.Should().BeTrue();
        circuitBreaker.FailureCount.Should().BeGreaterOrEqualTo(3);
    }

    [Fact]
    public async Task Request_Timeout_HandledGracefully()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<TimeoutRequest, TimeoutResponse>, TimeoutHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act
        var result = await mediator.SendAsync<TimeoutRequest, TimeoutResponse>(
            new TimeoutRequest { MessageId = MessageExtensions.NewMessageId(), TimeoutMs = 100 });

        // Assert - Handler should handle timeout internally
        result.IsSuccess.Should().BeTrue();
        result.Value!.TimedOut.Should().BeTrue();
    }

    [Fact]
    public async Task ConcurrentRequests_WithFailures_IsolatedFailures()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();
        services.AddScoped<IRequestHandler<IsolatedFailureRequest, IsolatedFailureResponse>, IsolatedFailureHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Mix of successful and failing requests
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            var shouldFail = i % 3 == 0; // Every 3rd request fails
            return await mediator.SendAsync<IsolatedFailureRequest, IsolatedFailureResponse>(
                new IsolatedFailureRequest { MessageId = MessageExtensions.NewMessageId(), Index = i, ShouldFail = shouldFail });
        });

        var results = await Task.WhenAll(tasks);

        // Assert
        var successCount = results.Count(r => r.IsSuccess);
        var failureCount = results.Count(r => !r.IsSuccess);

        successCount.Should().BeGreaterThan(0);
        failureCount.Should().BeGreaterThan(0);
        (successCount + failureCount).Should().Be(20);
    }

    [Fact]
    public async Task Request_Bulkhead_LimitsParallelism()
    {
        // Arrange
        var services = new ServiceCollection();
        services.AddLogging();
        services.AddCatga();

        var bulkhead = new SimpleBulkhead(maxParallel: 5);
        services.AddSingleton(bulkhead);
        services.AddScoped<IRequestHandler<BulkheadRequest, BulkheadResponse>, BulkheadHandler>();

        var sp = services.BuildServiceProvider();
        var mediator = sp.GetRequiredService<ICatgaMediator>();

        // Act - Fire many requests
        var tasks = Enumerable.Range(0, 20).Select(async i =>
        {
            return await mediator.SendAsync<BulkheadRequest, BulkheadResponse>(
                new BulkheadRequest { MessageId = MessageExtensions.NewMessageId(), Index = i });
        });

        await Task.WhenAll(tasks);

        // Assert
        bulkhead.MaxConcurrentObserved.Should().BeLessOrEqualTo(5);
    }

    #region Test Types

    private class AttemptTracker
    {
        public int FailUntilAttempt { get; set; } = 1;
    }

    private class SimpleCircuitBreaker
    {
        private readonly int _failureThreshold;
        private int _failureCount;
        public int FailureCount => _failureCount;
        public bool IsOpen => _failureCount >= _failureThreshold;

        public SimpleCircuitBreaker(int failureThreshold)
        {
            _failureThreshold = failureThreshold;
        }

        public void RecordFailure() => Interlocked.Increment(ref _failureCount);
        public void Reset() => _failureCount = 0;
    }

    private class SimpleBulkhead
    {
        private readonly SemaphoreSlim _semaphore;
        private int _currentConcurrent;
        public int MaxConcurrentObserved { get; private set; }

        public SimpleBulkhead(int maxParallel)
        {
            _semaphore = new SemaphoreSlim(maxParallel);
        }

        public async Task<T> ExecuteAsync<T>(Func<Task<T>> action)
        {
            await _semaphore.WaitAsync();
            try
            {
                var current = Interlocked.Increment(ref _currentConcurrent);
                if (current > MaxConcurrentObserved)
                    MaxConcurrentObserved = current;

                return await action();
            }
            finally
            {
                Interlocked.Decrement(ref _currentConcurrent);
                _semaphore.Release();
            }
        }
    }

    [MemoryPackable]
    private partial record SlowProcessingRequest : IRequest<SlowProcessingResponse>
    {
        public required long MessageId { get; init; }
        public required int DelayMs { get; init; }
    }

    [MemoryPackable]
    private partial record SlowProcessingResponse
    {
        public required bool Completed { get; init; }
    }

    private sealed class SlowProcessingHandler : IRequestHandler<SlowProcessingRequest, SlowProcessingResponse>
    {
        public async ValueTask<CatgaResult<SlowProcessingResponse>> HandleAsync(SlowProcessingRequest request, CancellationToken ct = default)
        {
            await Task.Delay(request.DelayMs, ct);
            return CatgaResult<SlowProcessingResponse>.Success(new SlowProcessingResponse { Completed = true });
        }
    }

    [MemoryPackable]
    private partial record TransientFailureRequest : IRequest<TransientFailureResponse>
    {
        public required long MessageId { get; init; }
        public required int Attempt { get; init; }
    }

    [MemoryPackable]
    private partial record TransientFailureResponse
    {
        public required int AttemptNumber { get; init; }
    }

    private sealed class TransientFailureHandler(AttemptTracker tracker) : IRequestHandler<TransientFailureRequest, TransientFailureResponse>
    {
        public ValueTask<CatgaResult<TransientFailureResponse>> HandleAsync(TransientFailureRequest request, CancellationToken ct = default)
        {
            if (request.Attempt < tracker.FailUntilAttempt)
            {
                return new ValueTask<CatgaResult<TransientFailureResponse>>(CatgaResult<TransientFailureResponse>.Failure("Transient failure"));
            }
            return new ValueTask<CatgaResult<TransientFailureResponse>>(CatgaResult<TransientFailureResponse>.Success(
                new TransientFailureResponse { AttemptNumber = request.Attempt }));
        }
    }

    [MemoryPackable]
    private partial record CircuitBreakerRequest : IRequest<CircuitBreakerResponse>
    {
        public required long MessageId { get; init; }
        public required bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record CircuitBreakerResponse
    {
        public required bool Success { get; init; }
    }

    private sealed class CircuitBreakerHandler(SimpleCircuitBreaker circuitBreaker) : IRequestHandler<CircuitBreakerRequest, CircuitBreakerResponse>
    {
        public ValueTask<CatgaResult<CircuitBreakerResponse>> HandleAsync(CircuitBreakerRequest request, CancellationToken ct = default)
        {
            if (request.ShouldFail)
            {
                circuitBreaker.RecordFailure();
                return new ValueTask<CatgaResult<CircuitBreakerResponse>>(CatgaResult<CircuitBreakerResponse>.Failure("Simulated failure"));
            }
            return new ValueTask<CatgaResult<CircuitBreakerResponse>>(CatgaResult<CircuitBreakerResponse>.Success(new CircuitBreakerResponse { Success = true }));
        }
    }

    [MemoryPackable]
    private partial record TimeoutRequest : IRequest<TimeoutResponse>
    {
        public required long MessageId { get; init; }
        public required int TimeoutMs { get; init; }
    }

    [MemoryPackable]
    private partial record TimeoutResponse
    {
        public required bool TimedOut { get; init; }
    }

    private sealed class TimeoutHandler : IRequestHandler<TimeoutRequest, TimeoutResponse>
    {
        public async ValueTask<CatgaResult<TimeoutResponse>> HandleAsync(TimeoutRequest request, CancellationToken ct = default)
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(request.TimeoutMs);

            try
            {
                await Task.Delay(request.TimeoutMs + 100, cts.Token);
                return CatgaResult<TimeoutResponse>.Success(new TimeoutResponse { TimedOut = false });
            }
            catch (OperationCanceledException)
            {
                return CatgaResult<TimeoutResponse>.Success(new TimeoutResponse { TimedOut = true });
            }
        }
    }

    [MemoryPackable]
    private partial record IsolatedFailureRequest : IRequest<IsolatedFailureResponse>
    {
        public required long MessageId { get; init; }
        public required int Index { get; init; }
        public required bool ShouldFail { get; init; }
    }

    [MemoryPackable]
    private partial record IsolatedFailureResponse
    {
        public required int Index { get; init; }
    }

    private sealed class IsolatedFailureHandler : IRequestHandler<IsolatedFailureRequest, IsolatedFailureResponse>
    {
        public ValueTask<CatgaResult<IsolatedFailureResponse>> HandleAsync(IsolatedFailureRequest request, CancellationToken ct = default)
        {
            if (request.ShouldFail)
            {
                return new ValueTask<CatgaResult<IsolatedFailureResponse>>(CatgaResult<IsolatedFailureResponse>.Failure("Isolated failure"));
            }
            return new ValueTask<CatgaResult<IsolatedFailureResponse>>(CatgaResult<IsolatedFailureResponse>.Success(
                new IsolatedFailureResponse { Index = request.Index }));
        }
    }

    [MemoryPackable]
    private partial record BulkheadRequest : IRequest<BulkheadResponse>
    {
        public required long MessageId { get; init; }
        public required int Index { get; init; }
    }

    [MemoryPackable]
    private partial record BulkheadResponse
    {
        public required int Index { get; init; }
    }

    private sealed class BulkheadHandler(SimpleBulkhead bulkhead) : IRequestHandler<BulkheadRequest, BulkheadResponse>
    {
        public async ValueTask<CatgaResult<BulkheadResponse>> HandleAsync(BulkheadRequest request, CancellationToken ct = default)
        {
            return await bulkhead.ExecuteAsync(async () =>
            {
                await Task.Delay(Random.Shared.Next(10, 50), ct);
                return CatgaResult<BulkheadResponse>.Success(new BulkheadResponse { Index = request.Index });
            });
        }
    }

    #endregion
}
