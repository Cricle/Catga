using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Debugging;

/// <summary>
/// Debug pipeline behavior - tracks message flow with minimal overhead
/// </summary>
public sealed class DebugPipelineBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private readonly MessageFlowTracker? _tracker;
    private readonly DebugOptions _options;
    private readonly ILogger<DebugPipelineBehavior<TRequest, TResponse>> _logger;

    public DebugPipelineBehavior(
        MessageFlowTracker? tracker,
        DebugOptions options,
        ILogger<DebugPipelineBehavior<TRequest, TResponse>> logger)
    {
        _tracker = tracker;
        _options = options;
        _logger = logger;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Fast path if debug disabled
        if (!_options.EnableDebug || _tracker == null)
        {
            return await next();
        }

        var message = request as IMessage;
        var correlationId = message?.CorrelationId ?? Guid.NewGuid().ToString("N");
        var messageType = typeof(TRequest).Name;

        // Begin flow tracking
        var flowContext = _tracker.BeginFlow(correlationId, messageType);

        var sw = Stopwatch.StartNew();
        CatgaResult<TResponse> result;

        try
        {
            result = await next();
            sw.Stop();

            // Record step
            _tracker.RecordStep(correlationId, new StepInfo(
                "Handler",
                $"{messageType}Handler",
                sw.Elapsed,
                result.IsSuccess,
                result.Error
            ));

            if (_options.EnableConsoleOutput)
            {
                LogToConsole(correlationId, messageType, sw.Elapsed, result.IsSuccess);
            }
        }
        catch (Exception ex)
        {
            sw.Stop();
            _tracker.RecordStep(correlationId, new StepInfo(
                "Handler",
                $"{messageType}Handler",
                sw.Elapsed,
                false,
                ex.Message
            ));

            if (_options.EnableConsoleOutput)
            {
                LogToConsole(correlationId, messageType, sw.Elapsed, false, ex.Message);
            }

            throw;
        }
        finally
        {
            // End flow and return to pool
            var summary = _tracker.EndFlow(correlationId);

            if (_options.EnableConsoleOutput && summary.StepCount > 0)
            {
                _logger.LogInformation("Flow {CorrelationId} complete: {Steps} steps, {Duration}ms",
                    correlationId, summary.StepCount, summary.TotalDuration.TotalMilliseconds);
            }
        }

        return result;
    }

    private void LogToConsole(string correlationId, string messageType, TimeSpan duration, bool success, string? error = null)
    {
        // Use zero-allocation formatter
        var formatted = ConsoleFlowFormatter.FormatCompact(new FlowSummary
        {
            CorrelationId = correlationId,
            MessageType = messageType,
            TotalDuration = duration,
            Success = success
        });

        Console.WriteLine(formatted);  // Direct console output (faster than logger for debug)
    }
}

