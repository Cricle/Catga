using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using Catga.Debugger.Core;
using Catga.Debugger.Models;
using Catga.Debugger.Storage;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Pipeline;

/// <summary>Pipeline behavior that captures all events for replay</summary>
/// <remarks>
/// For AOT compatibility, implement IDebugCapture on your message types.
/// Reflection-based variable capture is only used as a fallback in development.
/// </remarks>
[UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "Generic constraints are enforced by IPipelineBehavior. Types are validated at registration time.")]
public sealed class ReplayableEventCapturer<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly IEventStore _eventStore;
    private readonly ReplayOptions _options;
    private readonly AdaptiveSampler _sampler;
    private readonly ILogger<ReplayableEventCapturer<TRequest, TResponse>> _logger;

    public ReplayableEventCapturer(
        IEventStore eventStore,
        ReplayOptions options,
        AdaptiveSampler sampler,
        ILogger<ReplayableEventCapturer<TRequest, TResponse>> logger)
    {
        _eventStore = eventStore;
        _options = options;
        _sampler = sampler;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Check if replay is enabled and should sample
        if (!_options.EnableReplay)
            return await next();

        var correlationId = GetCorrelationId(request);

        if (!_sampler.ShouldSample(correlationId))
            return await next();

        var captureContext = new CaptureContext(correlationId)
        {
            ServiceName = Environment.MachineName
        };

        var stopwatch = Stopwatch.StartNew();

        try
        {
            // Capture input state
            await CaptureSnapshotAsync("BeforeExecution", captureContext, request);

            // Capture message received event
            if (_options.TrackMessageFlows)
            {
                CaptureMessageEvent(EventType.MessageReceived, captureContext, request);
            }

            // Execute pipeline
            var result = await next();

            stopwatch.Stop();

            // Capture output state
            await CaptureSnapshotAsync("AfterExecution", captureContext, result);

            // Capture performance metric
            if (_options.TrackPerformance)
            {
                CapturePerformanceMetric(captureContext, stopwatch.Elapsed);
            }

            // Save events to store
            await _eventStore.SaveAsync(captureContext.Events, cancellationToken);

            return result;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();

            // Capture exception
            if (_options.TrackExceptions)
            {
                CaptureException(captureContext, ex, stopwatch.Elapsed);
            }

            // Save events even on failure
            try
            {
                await _eventStore.SaveAsync(captureContext.Events, cancellationToken);
            }
            catch (Exception saveEx)
            {
                _logger.LogError(saveEx, "Failed to save replay events for {CorrelationId}", correlationId);
            }

            throw;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private string GetCorrelationId(TRequest request)
    {
        // Try to get correlation ID from IMessage
        if (request is IMessage message && !string.IsNullOrEmpty(message.CorrelationId))
            return message.CorrelationId;

        // Generate new correlation ID
        return Guid.NewGuid().ToString("N");
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "CaptureVariables is marked with RequiresUnreferencedCode. Callers are aware of AOT limitations.")]
    [UnconditionalSuppressMessage("AOT", "IL3050", Justification = "CaptureVariables is marked with RequiresDynamicCode. Callers are aware of AOT limitations.")]
    private async Task CaptureSnapshotAsync(string stage, CaptureContext context, object? data)
    {
        if (!_options.TrackStateSnapshots) return;

        var snapshot = new StateSnapshot
        {
            Timestamp = DateTime.UtcNow,
            Stage = stage,
            CorrelationId = context.CorrelationId
        };

        // Capture variables
        if (_options.CaptureVariables && data != null)
        {
            snapshot.Variables = CaptureVariables(data);
        }

        // Capture call stack
        if (_options.CaptureCallStacks)
        {
            snapshot.CallStack.AddRange(CaptureCallStack());
        }

        // Capture memory state
        if (_options.CaptureMemoryState)
        {
            snapshot.MemoryState = CaptureMemoryState();
        }

        context.Events.Add(new ReplayableEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = context.CorrelationId,
            Type = EventType.StateSnapshot,
            Timestamp = snapshot.Timestamp,
            Data = snapshot,
            ServiceName = context.ServiceName
        });

        await Task.CompletedTask;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CaptureMessageEvent(EventType eventType, CaptureContext context, object message)
    {
        context.Events.Add(new ReplayableEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = context.CorrelationId,
            Type = eventType,
            Timestamp = DateTime.UtcNow,
            Data = message,
            ServiceName = context.ServiceName,
            ParentEventId = context.ParentEventId
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CapturePerformanceMetric(CaptureContext context, TimeSpan duration)
    {
        context.Events.Add(new ReplayableEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = context.CorrelationId,
            Type = EventType.PerformanceMetric,
            Timestamp = DateTime.UtcNow,
            Data = new { Duration = duration.TotalMilliseconds },
            ServiceName = context.ServiceName
        });
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void CaptureException(CaptureContext context, Exception exception, TimeSpan duration)
    {
        context.Events.Add(new ReplayableEvent
        {
            Id = Guid.NewGuid().ToString("N"),
            CorrelationId = context.CorrelationId,
            Type = EventType.ExceptionThrown,
            Timestamp = DateTime.UtcNow,
            Data = new
            {
                ExceptionType = exception.GetType().FullName,
                Message = exception.Message,
                StackTrace = exception.StackTrace,
                Duration = duration.TotalMilliseconds
            },
            ServiceName = context.ServiceName
        });
    }

    [System.Diagnostics.CodeAnalysis.RequiresUnreferencedCode("Variable capture uses reflection. For AOT, implement custom IDebugCapture on your types.")]
    [System.Diagnostics.CodeAnalysis.RequiresDynamicCode("Variable capture uses reflection. For AOT, implement custom IDebugCapture on your types.")]
    private Dictionary<string, object?> CaptureVariables(object data)
    {
        var variables = new Dictionary<string, object?>();

        // Check if type implements custom capture interface
        if (data is IDebugCapture debugCapture)
        {
            // AOT-friendly: use custom capture
            return debugCapture.CaptureVariables();
        }

        // Fallback to reflection (not AOT-compatible)
        try
        {
            var type = data.GetType();
            var properties = type.GetProperties(System.Reflection.BindingFlags.Public |
                                               System.Reflection.BindingFlags.Instance);

            foreach (var prop in properties)
            {
                try
                {
                    var value = prop.GetValue(data);
                    variables[prop.Name] = value;
                }
                catch
                {
                    // Skip properties that can't be read
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture variables for {Type}", data.GetType().Name);
        }

        return variables;
    }

    [UnconditionalSuppressMessage("Trimming", "IL2026", Justification = "StackFrame.GetMethod uses reflection. Call stack capture is optional and disabled in production AOT builds.")]
    private List<CallFrame> CaptureCallStack()
    {
        var frames = new List<CallFrame>();

        try
        {
            var stackTrace = new StackTrace(true);
            var stackFrames = stackTrace.GetFrames();

            if (stackFrames != null)
            {
                foreach (var frame in stackFrames.Take(10)) // Limit to 10 frames
                {
                    var method = frame.GetMethod();
                    if (method == null) continue;

                    frames.Add(new CallFrame
                    {
                        MethodName = $"{method.DeclaringType?.FullName}.{method.Name}",
                        FileName = frame.GetFileName(),
                        LineNumber = frame.GetFileLineNumber()
                    });
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to capture call stack");
        }

        return frames;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private MemoryState CaptureMemoryState()
    {
        return new MemoryState
        {
            AllocatedBytes = GC.GetTotalMemory(false),
            Gen0Collections = GC.CollectionCount(0),
            Gen1Collections = GC.CollectionCount(1),
            Gen2Collections = GC.CollectionCount(2)
        };
    }
}

