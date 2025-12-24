using Microsoft.Extensions.Diagnostics.HealthChecks;
using Catga.Transport;

namespace Catga.Hosting;

/// <summary>
/// 传输层健康检查
/// </summary>
public sealed class TransportHealthCheck : IHealthCheck
{
    private readonly IMessageTransport _transport;

    public TransportHealthCheck(IMessageTransport transport)
    {
        _transport = transport ?? throw new ArgumentNullException(nameof(transport));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 检查传输层是否实现了 IHealthCheckable 接口
            if (_transport is IHealthCheckable healthCheckable)
            {
                var isHealthy = healthCheckable.IsHealthy;
                var status = healthCheckable.HealthStatus ?? "Unknown";
                var lastCheck = healthCheckable.LastHealthCheck;

                var data = new Dictionary<string, object>
                {
                    ["transport_name"] = _transport.Name,
                    ["health_status"] = status,
                    ["is_healthy"] = isHealthy
                };

                if (lastCheck.HasValue)
                {
                    data["last_health_check"] = lastCheck.Value;
                    data["seconds_since_last_check"] = (DateTimeOffset.UtcNow - lastCheck.Value).TotalSeconds;
                }

                return Task.FromResult(isHealthy
                    ? HealthCheckResult.Healthy($"Transport '{_transport.Name}' is connected: {status}", data)
                    : HealthCheckResult.Unhealthy($"Transport '{_transport.Name}' is disconnected: {status}", null, data));
            }

            // 如果传输层不支持健康检查，返回健康状态（假设正常）
            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"Transport '{_transport.Name}' does not support health checks",
                    new Dictionary<string, object>
                    {
                        ["transport_name"] = _transport.Name,
                        ["supports_health_check"] = false
                    }));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    $"Error checking transport '{_transport.Name}' health",
                    ex,
                    new Dictionary<string, object>
                    {
                        ["transport_name"] = _transport.Name,
                        ["error"] = ex.Message
                    }));
        }
    }
}
