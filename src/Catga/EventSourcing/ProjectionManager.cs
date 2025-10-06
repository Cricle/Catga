using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.EventSourcing;

/// <summary>
/// 投影管理器实现
/// </summary>
public class ProjectionManager : IProjectionManager, IHostedService
{
    private readonly IEventStore _eventStore;
    private readonly ILogger<ProjectionManager> _logger;
    private readonly List<IProjection> _projections = new();
    private readonly Dictionary<string, long> _projectionPositions = new();
    private CancellationTokenSource? _cts;
    private Task? _processingTask;

    public ProjectionManager(
        IEventStore eventStore,
        ILogger<ProjectionManager>? logger = null)
    {
        _eventStore = eventStore;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<ProjectionManager>.Instance;
    }

    public void RegisterProjection(IProjection projection)
    {
        _projections.Add(projection);
        _projectionPositions[projection.ProjectionName] = 0;
        _logger.LogInformation("Registered projection: {ProjectionName}", projection.ProjectionName);
    }

    public Task StartAsync(CancellationToken cancellationToken = default)
    {
        _cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        _processingTask = Task.Run(() => ProcessEventsAsync(_cts.Token), _cts.Token);
        _logger.LogInformation("Projection manager started with {Count} projections", _projections.Count);
        return Task.CompletedTask;
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (_cts != null)
        {
            _cts.Cancel();
            _cts.Dispose();
        }

        if (_processingTask != null)
        {
            try
            {
                await _processingTask;
            }
            catch (OperationCanceledException)
            {
                // 正常取消
            }
        }

        _logger.LogInformation("Projection manager stopped");
    }

    public async Task RebuildProjectionAsync(string projectionName, CancellationToken cancellationToken = default)
    {
        var projection = _projections.FirstOrDefault(p => p.ProjectionName == projectionName);
        if (projection == null)
        {
            throw new InvalidOperationException($"Projection {projectionName} not found");
        }

        _logger.LogInformation("Rebuilding projection: {ProjectionName}", projectionName);

        // 重置位置
        _projectionPositions[projectionName] = 0;

        // 重新处理所有事件
        await foreach (var storedEvent in _eventStore.ReadAllAsync(0, cancellationToken))
        {
            try
            {
                await projection.HandleAsync(storedEvent, cancellationToken);
                _projectionPositions[projectionName] = storedEvent.Position;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error rebuilding projection {ProjectionName} at position {Position}",
                    projectionName, storedEvent.Position);
                throw;
            }
        }

        _logger.LogInformation("Projection {ProjectionName} rebuilt successfully", projectionName);
    }

    private async Task ProcessEventsAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Starting event processing for projections");

        while (!cancellationToken.IsCancellationRequested)
        {
            try
            {
                // 找到最小的处理位置
                var minPosition = _projectionPositions.Values.Min();

                // 读取新事件
                await foreach (var storedEvent in _eventStore.ReadAllAsync(minPosition, cancellationToken))
                {
                    foreach (var projection in _projections)
                    {
                        var currentPosition = _projectionPositions[projection.ProjectionName];

                        // 只处理新事件
                        if (storedEvent.Position > currentPosition)
                        {
                            try
                            {
                                await projection.HandleAsync(storedEvent, cancellationToken);
                                _projectionPositions[projection.ProjectionName] = storedEvent.Position;

                                _logger.LogTrace("Projection {ProjectionName} processed event at position {Position}",
                                    projection.ProjectionName, storedEvent.Position);
                            }
                            catch (Exception ex)
                            {
                                _logger.LogError(ex,
                                    "Error processing event at position {Position} for projection {ProjectionName}",
                                    storedEvent.Position, projection.ProjectionName);
                            }
                        }
                    }
                }

                // 等待一段时间再检查新事件
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error in projection processing loop");
                await Task.Delay(TimeSpan.FromSeconds(5), cancellationToken);
            }
        }

        _logger.LogInformation("Event processing stopped");
    }
}

