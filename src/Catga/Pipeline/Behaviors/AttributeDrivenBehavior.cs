using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Catga.Abstractions;
using Catga.Core;
using Catga.Idempotency;
using Catga.Locking;
using Microsoft.Extensions.Logging;
using Polly;
using Polly.Retry;
using Polly.Timeout;
using Polly.CircuitBreaker;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Unified behavior that handles all attribute-driven capabilities:
/// [Idempotent], [DistributedLock], [Retry], [Timeout], [CircuitBreaker]
/// </summary>
public partial class AttributeDrivenBehavior<
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest,
    [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse>
    : BaseBehavior<TRequest, TResponse> where TRequest : IRequest<TResponse>
{
    private static readonly ConcurrentDictionary<Type, HandlerAttributes> _attributeCache = new();

    private readonly IIdempotencyStore? _idempotencyStore;
    private readonly IDistributedLockProvider? _lockProvider;

    public AttributeDrivenBehavior(
        ILogger<AttributeDrivenBehavior<TRequest, TResponse>> logger,
        IIdempotencyStore? idempotencyStore = null,
        IDistributedLockProvider? lockProvider = null) : base(logger)
    {
        _idempotencyStore = idempotencyStore;
        _lockProvider = lockProvider;
    }

    public override async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        var attrs = GetHandlerAttributes(request);
        if (!attrs.HasAnyAttribute)
            return await next();

        // 1. Idempotency check
        if (attrs.Idempotent != null && _idempotencyStore != null)
        {
            var key = BuildKey(attrs.Idempotent.Key, request);
            var messageId = key.GetHashCode();

            if (await _idempotencyStore.HasBeenProcessedAsync(messageId, cancellationToken))
            {
                LogIdempotentHit(Logger, key);
                var cached = await _idempotencyStore.GetCachedResultAsync<TResponse>(messageId, cancellationToken);
                return CatgaResult<TResponse>.Success(cached ?? default!);
            }
        }

        // 3. Build resilience pipeline
        var pipeline = BuildResiliencePipeline(attrs);

        // 4. Execute with optional distributed lock
        if (attrs.DistributedLock != null && _lockProvider != null)
        {
            var lockKey = BuildKey(attrs.DistributedLock.Key, request);
            var timeout = TimeSpan.FromSeconds(attrs.DistributedLock.TimeoutSeconds);
            var wait = TimeSpan.FromSeconds(attrs.DistributedLock.WaitSeconds);

            await using var lockHandle = await _lockProvider.AcquireAsync(lockKey, timeout, wait, cancellationToken);
            if (lockHandle == null)
            {
                LogLockFailed(Logger, lockKey);
                return CatgaResult<TResponse>.Failure("Failed to acquire lock");
            }

            return await ExecuteWithPipeline(pipeline, request, next, attrs, cancellationToken);
        }

        return await ExecuteWithPipeline(pipeline, request, next, attrs, cancellationToken);
    }

    private async ValueTask<CatgaResult<TResponse>> ExecuteWithPipeline(
        ResiliencePipeline<CatgaResult<TResponse>>? pipeline,
        TRequest request,
        PipelineDelegate<TResponse> next,
        HandlerAttributes attrs,
        CancellationToken ct)
    {
        CatgaResult<TResponse> result;

        if (pipeline != null)
        {
            result = await pipeline.ExecuteAsync(async _ => await next(), ct);
        }
        else
        {
            result = await next();
        }

        // Cache successful result for idempotency
        if (result.IsSuccess && attrs.Idempotent != null && _idempotencyStore != null)
        {
            var key = BuildKey(attrs.Idempotent.Key, request);
            var messageId = key.GetHashCode();
            await _idempotencyStore.MarkAsProcessedAsync(messageId, result.Value, ct);
        }

        return result;
    }

    private ResiliencePipeline<CatgaResult<TResponse>>? BuildResiliencePipeline(HandlerAttributes attrs)
    {
        if (attrs.Retry == null && attrs.Timeout == null && attrs.CircuitBreaker == null)
            return null;

        var builder = new ResiliencePipelineBuilder<CatgaResult<TResponse>>();

        if (attrs.Timeout != null)
        {
            builder.AddTimeout(new TimeoutStrategyOptions
            {
                Timeout = TimeSpan.FromSeconds(attrs.Timeout.Seconds)
            });
        }

        if (attrs.Retry != null)
        {
            builder.AddRetry(new RetryStrategyOptions<CatgaResult<TResponse>>
            {
                MaxRetryAttempts = attrs.Retry.MaxAttempts,
                Delay = TimeSpan.FromMilliseconds(attrs.Retry.DelayMs),
                BackoffType = attrs.Retry.Exponential ? DelayBackoffType.Exponential : DelayBackoffType.Constant,
                UseJitter = true,
                ShouldHandle = new PredicateBuilder<CatgaResult<TResponse>>()
                    .Handle<Exception>()
                    .HandleResult(r => !r.IsSuccess)
            });
        }

        if (attrs.CircuitBreaker != null)
        {
            builder.AddCircuitBreaker(new CircuitBreakerStrategyOptions<CatgaResult<TResponse>>
            {
                FailureRatio = 0.5,
                MinimumThroughput = attrs.CircuitBreaker.FailureThreshold,
                BreakDuration = TimeSpan.FromSeconds(attrs.CircuitBreaker.BreakDurationSeconds),
                ShouldHandle = new PredicateBuilder<CatgaResult<TResponse>>()
                    .Handle<Exception>()
                    .HandleResult(r => !r.IsSuccess)
            });
        }

        return builder.Build();
    }

    private static string BuildKey(string? template, TRequest request)
    {
        if (string.IsNullOrEmpty(template))
            return typeof(TRequest).Name + ":" + request.GetHashCode();

        // Simple template replacement: {request.PropertyName}
        var result = template;
        var type = typeof(TRequest);

        foreach (var prop in type.GetProperties(BindingFlags.Public | BindingFlags.Instance))
        {
            var placeholder = $"{{request.{prop.Name}}}";
            if (result.Contains(placeholder))
            {
                var value = prop.GetValue(request)?.ToString() ?? "";
                result = result.Replace(placeholder, value);
            }
        }

        return result;
    }

    private static HandlerAttributes GetHandlerAttributes(TRequest request)
    {
        var requestType = typeof(TRequest);
        return _attributeCache.GetOrAdd(requestType, _ =>
        {
            // Find handler type from request type name convention
            var handlerTypeName = requestType.Name.Replace("Command", "Handler").Replace("Query", "Handler");
            var handlerType = requestType.Assembly.GetTypes()
                .FirstOrDefault(t => t.Name == handlerTypeName || t.Name == requestType.Name + "Handler");

            if (handlerType == null)
                return new HandlerAttributes();

            return new HandlerAttributes
            {
                Idempotent = handlerType.GetCustomAttribute<IdempotentAttribute>(),
                DistributedLock = handlerType.GetCustomAttribute<DistributedLockAttribute>(),
                Retry = handlerType.GetCustomAttribute<RetryAttribute>(),
                Timeout = handlerType.GetCustomAttribute<TimeoutAttribute>(),
                CircuitBreaker = handlerType.GetCustomAttribute<CircuitBreakerAttribute>(),
                LeaderOnly = handlerType.GetCustomAttribute<LeaderOnlyAttribute>() != null
            };
        });
    }

    [LoggerMessage(Level = LogLevel.Warning, Message = "Request {RequestType} rejected - not leader node")]
    static partial void LogNotLeader(ILogger logger, string requestType);

    [LoggerMessage(Level = LogLevel.Information, Message = "Idempotent hit for key {Key}")]
    static partial void LogIdempotentHit(ILogger logger, string key);

    [LoggerMessage(Level = LogLevel.Warning, Message = "Failed to acquire distributed lock: {Key}")]
    static partial void LogLockFailed(ILogger logger, string key);

    private class HandlerAttributes
    {
        public IdempotentAttribute? Idempotent { get; init; }
        public DistributedLockAttribute? DistributedLock { get; init; }
        public RetryAttribute? Retry { get; init; }
        public TimeoutAttribute? Timeout { get; init; }
        public CircuitBreakerAttribute? CircuitBreaker { get; init; }
        public bool LeaderOnly { get; init; }

        public bool HasAnyAttribute =>
            Idempotent != null || DistributedLock != null || Retry != null ||
            Timeout != null || CircuitBreaker != null || LeaderOnly;
    }
}
