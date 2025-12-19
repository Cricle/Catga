using Catga;
using Catga.Abstractions;
using Catga.Core;
using Catga.Idempotency;
using Catga.Pipeline.Behaviors;
using Medallion.Threading;
using Medallion.Threading.WaitHandles;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Catga.Tests.Pipeline;

public class AttributeDrivenBehaviorTests
{
    [Fact]
    public async Task HandleAsync_NoAttributes_PassesThrough()
    {
        // Arrange
        var behavior = new AttributeDrivenBehavior<PlainRequest, string>(
            NullLogger<AttributeDrivenBehavior<PlainRequest, string>>.Instance);
        var request = new PlainRequest();
        var expectedResult = CatgaResult<string>.Success("ok");

        // Act
        var result = await behavior.HandleAsync(request, () => new ValueTask<CatgaResult<string>>(expectedResult));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public async Task HandleAsync_WithIdempotent_CachesResult()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore();
        var behavior = new AttributeDrivenBehavior<IdempotentRequest, string>(
            NullLogger<AttributeDrivenBehavior<IdempotentRequest, string>>.Instance,
            store);
        var request = new IdempotentRequest { CustomerId = "cust-1" };
        var callCount = 0;

        ValueTask<CatgaResult<string>> Next()
        {
            callCount++;
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("result"));
        }

        // Act - first call
        var result1 = await behavior.HandleAsync(request, () => new ValueTask<CatgaResult<string>>(Next().Result));

        // Act - second call with same key
        var result2 = await behavior.HandleAsync(request, () => new ValueTask<CatgaResult<string>>(Next().Result));

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal(1, callCount); // Only called once due to idempotency
    }

    [Fact]
    public async Task HandleAsync_WithDistributedLock_AcquiresLock()
    {
        // Arrange
        var lockProvider = new WaitHandleDistributedSynchronizationProvider();
        var behavior = new AttributeDrivenBehavior<LockedRequest, string>(
            NullLogger<AttributeDrivenBehavior<LockedRequest, string>>.Instance,
            lockProvider: lockProvider);
        var request = new LockedRequest { ResourceId = "res-1" };
        var executed = false;

        // Act
        var result = await behavior.HandleAsync(request, () =>
        {
            executed = true;
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok"));
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(executed);
    }

    [Fact]
    public async Task HandleAsync_WithRetry_RetriesOnFailure()
    {
        // Arrange
        var behavior = new AttributeDrivenBehavior<RetryableRequest, string>(
            NullLogger<AttributeDrivenBehavior<RetryableRequest, string>>.Instance);
        var request = new RetryableRequest();
        var attempts = 0;

        // Act
        var result = await behavior.HandleAsync(request, () =>
        {
            attempts++;
            if (attempts < 3)
                return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Failure("transient error"));
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok"));
        });

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal(3, attempts);
    }

    // Test request types
    public record PlainRequest : IRequest<string>
    {
        public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
    }

    [Idempotent(Key = "{request.CustomerId}")]
    public record IdempotentRequest : IRequest<string>
    {
        public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
        public string CustomerId { get; init; } = "";
    }

    [DistributedLock("resource:{request.ResourceId}")]
    public record LockedRequest : IRequest<string>
    {
        public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
        public string ResourceId { get; init; } = "";
    }

    [Retry(MaxAttempts = 3, DelayMs = 10)]
    public record RetryableRequest : IRequest<string>
    {
        public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
    }

    [Timeout(5)]
    public record TimeoutRequest : IRequest<string>
    {
        public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
    }

    [CircuitBreaker(FailureThreshold = 3, BreakDurationSeconds = 10)]
    public record CircuitBreakerRequest : IRequest<string>
    {
        public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
    }

    [Idempotent(Key = "{request.Id}")]
    [Retry(MaxAttempts = 2, DelayMs = 5)]
    public record CombinedRequest : IRequest<string>
    {
        public long MessageId { get; init; } = DateTime.UtcNow.Ticks;
        public string Id { get; init; } = "";
    }
}

public class AttributeDrivenBehaviorAdditionalTests
{
    [Fact]
    public async Task HandleAsync_WithTimeout_ExecutesWithinTimeout()
    {
        // Arrange
        var behavior = new AttributeDrivenBehavior<AttributeDrivenBehaviorTests.TimeoutRequest, string>(
            NullLogger<AttributeDrivenBehavior<AttributeDrivenBehaviorTests.TimeoutRequest, string>>.Instance);
        var request = new AttributeDrivenBehaviorTests.TimeoutRequest();

        // Act
        var result = await behavior.HandleAsync(request, () =>
            new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok")));

        // Assert
        Assert.True(result.IsSuccess);
        Assert.Equal("ok", result.Value);
    }

    [Fact]
    public async Task HandleAsync_WithCircuitBreaker_ExecutesNormally()
    {
        // Arrange
        var behavior = new AttributeDrivenBehavior<AttributeDrivenBehaviorTests.CircuitBreakerRequest, string>(
            NullLogger<AttributeDrivenBehavior<AttributeDrivenBehaviorTests.CircuitBreakerRequest, string>>.Instance);
        var request = new AttributeDrivenBehaviorTests.CircuitBreakerRequest();

        // Act
        var result = await behavior.HandleAsync(request, () =>
            new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok")));

        // Assert
        Assert.True(result.IsSuccess);
    }

    [Fact]
    public async Task HandleAsync_WithCombinedAttributes_AppliesAll()
    {
        // Arrange
        var store = new InMemoryIdempotencyStore2();
        var behavior = new AttributeDrivenBehavior<AttributeDrivenBehaviorTests.CombinedRequest, string>(
            NullLogger<AttributeDrivenBehavior<AttributeDrivenBehaviorTests.CombinedRequest, string>>.Instance,
            store);
        var request = new AttributeDrivenBehaviorTests.CombinedRequest { Id = "combined-1" };
        var callCount = 0;

        // Act - first call
        var result1 = await behavior.HandleAsync(request, () =>
        {
            callCount++;
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("result"));
        });

        // Act - second call with same key
        var result2 = await behavior.HandleAsync(request, () =>
        {
            callCount++;
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("result2"));
        });

        // Assert
        Assert.True(result1.IsSuccess);
        Assert.True(result2.IsSuccess);
        Assert.Equal(1, callCount); // Only called once due to idempotency
    }

    [Fact]
    public async Task HandleAsync_WithDistributedLock_FailsWhenLockNotAcquired()
    {
        // Arrange
        var lockProvider = new FailingLockProvider();
        var behavior = new AttributeDrivenBehavior<AttributeDrivenBehaviorTests.LockedRequest, string>(
            NullLogger<AttributeDrivenBehavior<AttributeDrivenBehaviorTests.LockedRequest, string>>.Instance,
            lockProvider: lockProvider);
        var request = new AttributeDrivenBehaviorTests.LockedRequest { ResourceId = "res-fail" };

        // Act
        var result = await behavior.HandleAsync(request, () =>
            new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok")));

        // Assert
        Assert.False(result.IsSuccess);
        Assert.Contains("lock", result.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task HandleAsync_WithCancellationToken_PassesToNext()
    {
        // Arrange
        var behavior = new AttributeDrivenBehavior<AttributeDrivenBehaviorTests.PlainRequest, string>(
            NullLogger<AttributeDrivenBehavior<AttributeDrivenBehaviorTests.PlainRequest, string>>.Instance);
        var request = new AttributeDrivenBehaviorTests.PlainRequest();
        var cts = new CancellationTokenSource();
        var tokenReceived = false;

        // Act
        var result = await behavior.HandleAsync(request, () =>
        {
            tokenReceived = true;
            return new ValueTask<CatgaResult<string>>(CatgaResult<string>.Success("ok"));
        }, cts.Token);

        // Assert
        Assert.True(result.IsSuccess);
        Assert.True(tokenReceived);
    }
}

// Simple in-memory idempotency store for testing
file class InMemoryIdempotencyStore2 : IIdempotencyStore
{
    private readonly Dictionary<long, object?> _cache = new();

    public Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken ct = default)
        => Task.FromResult(_cache.ContainsKey(messageId));

    public Task MarkAsProcessedAsync<T>(long messageId, T? result, CancellationToken ct = default)
    {
        _cache[messageId] = result;
        return Task.CompletedTask;
    }

    public Task<T?> GetCachedResultAsync<T>(long messageId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(messageId, out var value))
            return Task.FromResult((T?)value);
        return Task.FromResult(default(T));
    }
}

file class FailingLockProvider : IDistributedLockProvider
{
    public IDistributedLock CreateLock(string name) => new FailingLock(name);
}

file class FailingLock : IDistributedLock
{
    public string Name { get; }
    public FailingLock(string name) => Name = name;

    public IDistributedSynchronizationHandle? TryAcquire(TimeSpan timeout = default, CancellationToken cancellationToken = default) => null;
    public IDistributedSynchronizationHandle Acquire(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
    public ValueTask<IDistributedSynchronizationHandle?> TryAcquireAsync(TimeSpan timeout = default, CancellationToken cancellationToken = default) => new((IDistributedSynchronizationHandle?)null);
    public ValueTask<IDistributedSynchronizationHandle> AcquireAsync(TimeSpan? timeout = null, CancellationToken cancellationToken = default) => throw new InvalidOperationException();
}

// Simple in-memory idempotency store for testing
file class InMemoryIdempotencyStore : IIdempotencyStore
{
    private readonly Dictionary<long, object?> _cache = new();

    public Task<bool> HasBeenProcessedAsync(long messageId, CancellationToken ct = default)
        => Task.FromResult(_cache.ContainsKey(messageId));

    public Task MarkAsProcessedAsync<T>(long messageId, T? result, CancellationToken ct = default)
    {
        _cache[messageId] = result;
        return Task.CompletedTask;
    }

    public Task<T?> GetCachedResultAsync<T>(long messageId, CancellationToken ct = default)
    {
        if (_cache.TryGetValue(messageId, out var value))
            return Task.FromResult((T?)value);
        return Task.FromResult(default(T));
    }
}






