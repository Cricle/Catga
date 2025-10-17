using Catga;
using Catga.Debugger.Core;
using Catga.Debugger.Models;
using Catga.Debugger.Storage;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Catga.Debugger.Pipeline;

/// <summary>
/// Pipeline behavior that captures performance metrics for profiling and analysis.
/// Records execution time, memory allocation, CPU usage, and thread information.
/// </summary>
/// <typeparam name="TRequest">Request type</typeparam>
/// <typeparam name="TResponse">Response type</typeparam>
public sealed class PerformanceCaptureBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly IEventStore _eventStore;
    private readonly ReplayOptions _options;
    private readonly AdaptiveSampler _sampler;
    private readonly ILogger<PerformanceCaptureBehavior<TRequest, TResponse>> _logger;

    public PerformanceCaptureBehavior(
        IEventStore eventStore,
        ReplayOptions options,
        AdaptiveSampler sampler,
        ILogger<PerformanceCaptureBehavior<TRequest, TResponse>> logger)
    {
        _eventStore = eventStore;
        _options = options;
        _sampler = sampler;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken)
    {
        // Skip if performance tracking is disabled
        if (!_options.TrackPerformance)
        {
            return await next();
        }

        var correlationId = GetCorrelationId(request);
        var messageType = typeof(TRequest).Name;

        // Adaptive sampling to reduce overhead
        if (!_sampler.ShouldSample(correlationId))
        {
            return await next();
        }
        
        var startTime = DateTime.UtcNow;
        var startMemory = GC.GetAllocatedBytesForCurrentThread();
        var threadId = Environment.CurrentManagedThreadId;
        
        // For CPU time measurement
        var process = Process.GetCurrentProcess();
        var startCpuTime = process.TotalProcessorTime;

        CatgaResult<TResponse> response;
        Exception? capturedException = null;

        try
        {
            response = await next();
        }
        catch (Exception ex)
        {
            capturedException = ex;
            throw;
        }
        finally
        {
            try
            {
                var endTime = DateTime.UtcNow;
                var endMemory = GC.GetAllocatedBytesForCurrentThread();
                var endCpuTime = process.TotalProcessorTime;

                var duration = (endTime - startTime).TotalMilliseconds;
                var memoryAllocated = endMemory - startMemory;
                var cpuTime = (endCpuTime - startCpuTime).TotalMilliseconds;

                // Create performance metric event
                var perfEvent = new ReplayableEvent
                {
                    Id = Guid.NewGuid().ToString(),
                    CorrelationId = correlationId,
                    Type = EventType.PerformanceMetric,
                    Timestamp = startTime,
                    CompletedAt = endTime,
                    Duration = duration,
                    MemoryAllocated = memoryAllocated,
                    ThreadId = threadId,
                    CpuTime = cpuTime,
                    MessageType = messageType,
                    ServiceName = _options.ServiceName,
                    Exception = capturedException?.Message,
                    Data = new PerformanceMetricData
                    {
                        RequestType = messageType,
                        StartTime = startTime,
                        EndTime = endTime,
                        DurationMs = duration,
                        MemoryAllocatedBytes = memoryAllocated,
                        ThreadId = threadId,
                        CpuTimeMs = cpuTime,
                        IsSuccess = capturedException == null,
                        ErrorMessage = capturedException?.Message
                    },
                    Metadata = new Dictionary<string, string>
                    {
                        ["Category"] = "Performance",
                        ["Sampled"] = "true"
                    }
                };

                await _eventStore.SaveAsync(new[] { perfEvent }, cancellationToken);

                // Log slow queries for immediate visibility
                if (duration > _options.SlowQueryThresholdMs && _logger.IsEnabled(LogLevel.Warning))
                {
                    _logger.LogWarning(
                        "Slow query detected: {MessageType} took {Duration}ms (Memory: {Memory} bytes, CPU: {CpuTime}ms)",
                        messageType,
                        duration,
                        memoryAllocated,
                        cpuTime);
                }
            }
            catch (Exception ex)
            {
                // Don't fail the request if performance capture fails
                _logger.LogError(ex, "Failed to capture performance metrics for {MessageType}", messageType);
            }
        }

        return response;
    }

    private static string GetCorrelationId(TRequest request)
    {
        // Try to get correlation ID from request metadata
        if (request is IHasCorrelationId hasCorrelationId)
        {
            return hasCorrelationId.CorrelationId;
        }

        // Fallback: generate a new one
        return Guid.NewGuid().ToString();
    }
}

/// <summary>
/// Interface for requests that have a correlation ID
/// </summary>
public interface IHasCorrelationId
{
    string CorrelationId { get; }
}

/// <summary>
/// Performance metric data captured for analysis
/// </summary>
public sealed class PerformanceMetricData
{
    public required string RequestType { get; init; }
    public required DateTime StartTime { get; init; }
    public required DateTime EndTime { get; init; }
    public required double DurationMs { get; init; }
    public required long MemoryAllocatedBytes { get; init; }
    public required int ThreadId { get; init; }
    public required double CpuTimeMs { get; init; }
    public required bool IsSuccess { get; init; }
    public string? ErrorMessage { get; init; }
}

