using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.Hosting;

/// <summary>
/// 恢复服务健康检查
/// </summary>
public sealed class RecoveryHealthCheck : IHealthCheck
{
    private readonly RecoveryHostedService? _recoveryService;
    private readonly IEnumerable<IRecoverableComponent> _components;

    public RecoveryHealthCheck(
        IEnumerable<IRecoverableComponent> components,
        RecoveryHostedService? recoveryService = null)
    {
        _components = components ?? throw new ArgumentNullException(nameof(components));
        _recoveryService = recoveryService;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var components = _components.ToList();
            var data = new Dictionary<string, object>
            {
                ["total_components"] = components.Count
            };

            // 检查恢复服务本身的状态
            if (_recoveryService != null)
            {
                data["recovery_service_active"] = true;
                data["is_recovering"] = _recoveryService.IsRecovering;
            }
            else
            {
                data["recovery_service_active"] = false;
            }

            if (components.Count == 0)
            {
                return Task.FromResult(
                    HealthCheckResult.Healthy(
                        "No recoverable components registered",
                        data));
            }

            var unhealthyComponents = new List<string>();
            var healthyComponents = new List<string>();

            foreach (var component in components)
            {
                var componentName = component.ComponentName;
                var isHealthy = component.IsHealthy;

                if (isHealthy)
                {
                    healthyComponents.Add(componentName);
                }
                else
                {
                    unhealthyComponents.Add(componentName);
                }

                data[$"{componentName}_is_healthy"] = isHealthy;
            }

            data["healthy_count"] = healthyComponents.Count;
            data["unhealthy_count"] = unhealthyComponents.Count;

            if (unhealthyComponents.Count > 0)
            {
                data["unhealthy_components"] = unhealthyComponents;
                
                // 如果有不健康的组件，返回 Degraded 状态
                // 因为恢复服务应该正在尝试恢复它们
                return Task.FromResult(
                    HealthCheckResult.Degraded(
                        $"Recovery service has {unhealthyComponents.Count} unhealthy component(s): {string.Join(", ", unhealthyComponents)}",
                        null,
                        data));
            }

            data["healthy_components"] = healthyComponents;
            
            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"All {components.Count} recoverable component(s) are healthy",
                    data));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    "Error checking recovery service health",
                    ex,
                    new Dictionary<string, object>
                    {
                        ["error"] = ex.Message
                    }));
        }
    }
}
