using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace Catga.Distributed;

/// <summary>
/// 节点心跳后台服务（完全无锁）
/// 定期发送心跳，维持节点在线状态
/// </summary>
public sealed class HeartbeatBackgroundService : BackgroundService
{
    private readonly INodeDiscovery _discovery;
    private readonly NodeInfo _currentNode;
    private readonly ILogger<HeartbeatBackgroundService> _logger;
    private readonly TimeSpan _heartbeatInterval;

    public HeartbeatBackgroundService(
        INodeDiscovery discovery,
        NodeInfo currentNode,
        ILogger<HeartbeatBackgroundService> logger,
        TimeSpan? heartbeatInterval = null)
    {
        _discovery = discovery;
        _currentNode = currentNode;
        _logger = logger;
        _heartbeatInterval = heartbeatInterval ?? TimeSpan.FromSeconds(10);
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            // 启动时注册节点
            await _discovery.RegisterAsync(_currentNode, stoppingToken);
            _logger.LogInformation("Node {NodeId} registered, starting heartbeat every {Interval}s", 
                _currentNode.NodeId, _heartbeatInterval.TotalSeconds);

            // 无锁心跳循环
            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    await Task.Delay(_heartbeatInterval, stoppingToken);

                    // 发送心跳（无锁）
                    await _discovery.HeartbeatAsync(_currentNode.NodeId, _currentNode.Load, stoppingToken);
                    
                    _logger.LogTrace("Heartbeat sent for node {NodeId}", _currentNode.NodeId);
                }
                catch (OperationCanceledException)
                {
                    // 正常取消
                    break;
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to send heartbeat for node {NodeId}", _currentNode.NodeId);
                }
            }
        }
        catch (OperationCanceledException)
        {
            // 正常取消
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in heartbeat service for node {NodeId}", _currentNode.NodeId);
        }
        finally
        {
            // 优雅下线：注销节点
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5));
            try
            {
                await _discovery.UnregisterAsync(_currentNode.NodeId, cts.Token);
                _logger.LogInformation("Node {NodeId} unregistered successfully", _currentNode.NodeId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to unregister node {NodeId} during shutdown", _currentNode.NodeId);
            }
        }
    }
}

