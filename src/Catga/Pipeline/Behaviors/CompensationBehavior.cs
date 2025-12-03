using System.Diagnostics.CodeAnalysis;
using Catga.Abstractions;
using Catga.Core;
using Microsoft.Extensions.Logging;

namespace Catga.Pipeline.Behaviors;

/// <summary>
/// Pipeline behavior that automatically invokes compensation factory on failure.
/// Register ICompensationFactory&lt;TRequest, TEvent&gt; to define compensation logic.
/// Zero reflection - uses static generic cache for type names.
/// </summary>
public sealed partial class CompensationBehavior<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TResponse> : IPipelineBehavior<TRequest, TResponse>
    where TRequest : IRequest<TResponse>
{
    private static readonly string RequestTypeName = typeof(TRequest).Name;

    private readonly ICatgaMediator _mediator;
    private readonly ICompensationPublisher<TRequest>? _compensationPublisher;
    private readonly ILogger<CompensationBehavior<TRequest, TResponse>> _logger;

    public CompensationBehavior(
        ICatgaMediator mediator,
        ILogger<CompensationBehavior<TRequest, TResponse>> logger,
        ICompensationPublisher<TRequest>? compensationPublisher = null)
    {
        _mediator = mediator;
        _logger = logger;
        _compensationPublisher = compensationPublisher;
    }

    public async ValueTask<CatgaResult<TResponse>> HandleAsync(
        TRequest request,
        PipelineDelegate<TResponse> next,
        CancellationToken ct = default)
    {
        var result = await next();

        // Only compensate on failure
        if (result.IsSuccess || _compensationPublisher == null)
            return result;

        // Execute compensation
        try
        {
            var eventTypeName = await _compensationPublisher.PublishCompensationAsync(_mediator, request, result.Error, ct);
            if (eventTypeName != null)
            {
                LogCompensationPublished(_logger, RequestTypeName, eventTypeName);
            }
        }
        catch (Exception ex)
        {
            LogCompensationError(_logger, RequestTypeName, ex.Message);
        }

        return result;
    }

    #region Logging

    [LoggerMessage(Level = LogLevel.Information, Message = "Compensation published: {RequestType} -> {EventType}")]
    private static partial void LogCompensationPublished(ILogger logger, string requestType, string eventType);

    [LoggerMessage(Level = LogLevel.Error, Message = "Compensation failed for {RequestType}: {Error}")]
    private static partial void LogCompensationError(ILogger logger, string requestType, string error);

    #endregion
}

/// <summary>
/// Publishes compensation events. Implement per request type.
/// Zero reflection - type name is cached statically.
/// </summary>
public interface ICompensationPublisher<TRequest>
{
    /// <summary>
    /// Create and publish compensation event for the failed request.
    /// Returns the event type name for logging, or null if no event was published.
    /// </summary>
    ValueTask<string?> PublishCompensationAsync(ICatgaMediator mediator, TRequest request, string? error, CancellationToken ct);
}

/// <summary>
/// Typed compensation publisher base class. Zero reflection.
/// </summary>
public abstract class CompensationPublisher<TRequest, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.All)] TEvent> : ICompensationPublisher<TRequest>
    where TEvent : IEvent
{
    private static readonly string EventTypeName = typeof(TEvent).Name;

    public async ValueTask<string?> PublishCompensationAsync(ICatgaMediator mediator, TRequest request, string? error, CancellationToken ct)
    {
        var compensationEvent = CreateCompensationEvent(request, error);
        if (compensationEvent == null)
            return null;

        await mediator.PublishAsync(compensationEvent, ct);
        return EventTypeName;
    }

    /// <summary>Create the compensation event.</summary>
    protected abstract TEvent? CreateCompensationEvent(TRequest request, string? error);
}
