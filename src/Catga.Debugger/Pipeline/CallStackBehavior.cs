using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Catga.Debugger.CallStack;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Results;

namespace Catga.Debugger.Pipeline;

/// <summary>
/// Pipeline behavior that tracks the call stack for debugging.
/// Production-safe: Zero overhead when disabled.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "Generic constraints are enforced by IPipelineBehavior")]
public sealed class CallStackBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly CallStackTracker _callStackTracker;
    private readonly bool _enabled;

    public CallStackBehavior(CallStackTracker callStackTracker, bool enabled = false)
    {
        _callStackTracker = callStackTracker;
        _enabled = enabled;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Fast path: if disabled, skip tracking
        if (!_enabled)
        {
            return await next();
        }

        var requestType = typeof(TRequest).Name;
        var handlerType = $"{requestType}Handler"; // Inferred
        var correlationId = (request as IMessage)?.CorrelationId;

        // Push frame onto call stack
        using var frame = _callStackTracker.PushFrame(
            methodName: "HandleAsync",
            typeName: handlerType,
            messageType: requestType,
            correlationId: correlationId
        );

        try
        {
            // Add request data as variable (if capture enabled)
            _callStackTracker.AddVariable("request", request);

            // Execute handler
            var result = await next();

            // Add response as variable
            if (result.IsSuccess)
            {
                _callStackTracker.AddVariable("response", result.Value);
            }

            return result;
        }
        catch
        {
            // Exception will be tracked by frame disposal
            throw;
        }
    }
}

