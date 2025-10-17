using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using Catga.Debugger.Breakpoints;
using Catga.Messages;
using Catga.Pipeline;
using Catga.Results;
using Microsoft.Extensions.Logging;

namespace Catga.Debugger.Pipeline;

/// <summary>
/// Pipeline behavior that checks for breakpoints.
/// Production-safe: Zero overhead when breakpoints are disabled.
/// </summary>
[UnconditionalSuppressMessage("Trimming", "IL2091", Justification = "Generic constraints are enforced by IPipelineBehavior")]
public sealed class BreakpointBehavior<TRequest, TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : class, IRequest<TResponse>
{
    private readonly BreakpointManager _breakpointManager;
    private readonly ILogger<BreakpointBehavior<TRequest, TResponse>> _logger;
    private readonly bool _enabled;

    public BreakpointBehavior(
        BreakpointManager breakpointManager,
        ILogger<BreakpointBehavior<TRequest, TResponse>> logger,
        bool enabled = false)
    {
        _breakpointManager = breakpointManager;
        _logger = logger;
        _enabled = enabled;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken cancellationToken = default)
    {
        // Fast path: if disabled, skip breakpoint check
        if (!_enabled)
        {
            return await next();
        }

        // Get correlation ID from message
        var correlationId = (request as IMessage)?.CorrelationId ?? "unknown";

        // Check if we should break
        var action = await _breakpointManager.CheckBreakpointAsync(request, correlationId, cancellationToken);

        // Handle debug action
        switch (action)
        {
            case DebugAction.Continue:
                // Normal execution
                return await next();

            case DebugAction.StepOver:
                // Execute this message, then break on next
                var result = await next();
                // TODO: Set temporary breakpoint for next message
                return result;

            case DebugAction.StepInto:
                // Execute and break on next event handler
                // TODO: Implement step into
                return await next();

            case DebugAction.StepOut:
                // Execute until current handler completes
                // TODO: Implement step out
                return await next();

            default:
                return await next();
        }
    }
}

