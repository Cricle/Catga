using Catga.Core;
using Microsoft.Extensions.Logging;
using NATS.Client.Core;

namespace Catga.Transport.Nats;

/// <summary>
/// NATS 可恢复传输 - 自动重连和状态恢复
/// </summary>
public sealed class NatsRecoverableTransport : IRecoverableComponent
{
    private readonly INatsConnection _connection;
    private readonly ILogger<NatsRecoverableTransport> _logger;
    private volatile bool _isHealthy = true;

    public NatsRecoverableTransport(
        INatsConnection connection,
        ILogger<NatsRecoverableTransport> logger)
    {
        _connection = connection;
        _logger = logger;

        // 监听连接状态变化
        MonitorConnectionStatus();
    }

    public bool IsHealthy => _isHealthy;

    public async Task RecoverAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogInformation("开始恢复 NATS 连接");

        try
        {
            // NATS 客户端自动重连，我们只需要等待连接恢复
            var timeout = TimeSpan.FromSeconds(30);
            var startTime = DateTime.UtcNow;

            while (_connection.ConnectionState != NatsConnectionState.Open &&
                   DateTime.UtcNow - startTime < timeout)
            {
                await Task.Delay(100, cancellationToken);
            }

            if (_connection.ConnectionState == NatsConnectionState.Open)
            {
                _isHealthy = true;
                _logger.LogInformation("NATS 连接恢复成功");
            }
            else
            {
                _logger.LogWarning("NATS 连接恢复超时");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "NATS 连接恢复失败");
            throw;
        }
    }

    private void MonitorConnectionStatus()
    {
        // 监控连接状态变化
        Task.Run(async () =>
        {
            while (true)
            {
                await Task.Delay(TimeSpan.FromSeconds(5));

                var wasHealthy = _isHealthy;
                _isHealthy = _connection.ConnectionState == NatsConnectionState.Open;

                if (wasHealthy && !_isHealthy)
                {
                    _logger.LogWarning("NATS 连接断开");
                }
                else if (!wasHealthy && _isHealthy)
                {
                    _logger.LogInformation("NATS 连接已恢复");
                }
            }
        });
    }
}

