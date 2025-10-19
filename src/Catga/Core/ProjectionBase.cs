using Catga.Messages;
using Microsoft.Extensions.Logging;
using System.Diagnostics;

namespace Catga.Projections;

/// <summary>
/// Projection interface for event-driven read model updates
/// </summary>
public interface IProjection
{
    string Name { get; }
    ValueTask HandleAsync(IEvent @event, CancellationToken ct = default);
}

/// <summary>
/// Guided base class for read model projections.
/// Users only need to implement 1 method: HandleEventAsync (business logic).
/// Framework automatically handles: tracing, logging, error handling, and CRUD operations.
/// </summary>
/// <typeparam name="TReadModel">Read model type</typeparam>
public abstract class ProjectionBase<TReadModel> : IProjection where TReadModel : class
{
    protected readonly ILogger Logger;

    protected ProjectionBase(ILogger logger) => Logger = logger ?? throw new ArgumentNullException(nameof(logger));

    public abstract string Name { get; }

    /// <summary>User implements: Handle event and update read model</summary>
    protected abstract ValueTask HandleEventAsync(IEvent @event, CancellationToken ct);

    /// <summary>Framework method: Handle event with automatic tracing and error handling</summary>
    public async ValueTask HandleAsync(IEvent @event, CancellationToken ct = default)
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        var eventType = @event.GetType().Name;
        using var activity = Activity.Current?.Source.StartActivity($"Projection.{Name}");
        activity?.SetTag("projection.name", Name);
        activity?.SetTag("event.type", eventType);

        var stopwatch = Stopwatch.StartNew();

        try
        {
            CatgaLog.ProjectionHandlingEvent(Logger, Name, eventType);

            await HandleEventAsync(@event, ct);

            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Ok);
            activity?.SetTag("projection.duration_ms", stopwatch.ElapsedMilliseconds);

            CatgaLog.ProjectionHandledEvent(Logger, Name, eventType, stopwatch.ElapsedMilliseconds);
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, "Cancelled");

            CatgaLog.ProjectionCancelled(Logger, Name, eventType);
            throw;
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity?.SetTag("exception.type", ex.GetType().FullName);
            activity?.SetTag("exception.message", ex.Message);

            CatgaLog.ProjectionFailed(Logger, Name, eventType, ex.Message, ex);
            throw;
        }
    }

    // Helper methods for CRUD operations (users implement these)
    protected abstract ValueTask<TReadModel?> GetAsync(string id, CancellationToken ct);
    protected abstract ValueTask SaveAsync(TReadModel model, CancellationToken ct);
    protected abstract ValueTask DeleteAsync(string id, CancellationToken ct);
}

/// <summary>
/// Logging extensions for projections (high-performance source-generated logs)
/// </summary>
public static partial class CatgaLog
{
    [LoggerMessage(
        EventId = 4001,
        Level = LogLevel.Debug,
        Message = "Projection {ProjectionName} handling event {EventType}")]
    public static partial void ProjectionHandlingEvent(
        ILogger logger, string projectionName, string eventType);

    [LoggerMessage(
        EventId = 4002,
        Level = LogLevel.Debug,
        Message = "Projection {ProjectionName} handled event {EventType} in {DurationMs}ms")]
    public static partial void ProjectionHandledEvent(
        ILogger logger, string projectionName, string eventType, long durationMs);

    [LoggerMessage(
        EventId = 4003,
        Level = LogLevel.Warning,
        Message = "Projection {ProjectionName} cancelled while handling event {EventType}")]
    public static partial void ProjectionCancelled(
        ILogger logger, string projectionName, string eventType);

    [LoggerMessage(
        EventId = 4004,
        Level = LogLevel.Error,
        Message = "Projection {ProjectionName} failed handling event {EventType}, Error: {ErrorMessage}")]
    public static partial void ProjectionFailed(
        ILogger logger, string projectionName, string eventType, string errorMessage, Exception ex);
}

