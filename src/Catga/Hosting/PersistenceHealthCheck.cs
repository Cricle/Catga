using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.Hosting;

/// <summary>
/// 持久化层健康检查
/// </summary>
public sealed class PersistenceHealthCheck : IHealthCheck
{
    private readonly IEnumerable<IHealthCheckable> _persistenceComponents;

    public PersistenceHealthCheck(IEnumerable<IHealthCheckable> persistenceComponents)
    {
        _persistenceComponents = persistenceComponents ?? throw new ArgumentNullException(nameof(persistenceComponents));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            var components = _persistenceComponents.ToList();
            
            if (components.Count == 0)
            {
                // 没有注册的持久化组件
                return Task.FromResult(
                    HealthCheckResult.Healthy(
                        "No persistence components registered",
                        new Dictionary<string, object>
                        {
                            ["component_count"] = 0
                        }));
            }

            var unhealthyComponents = new List<string>();
            var healthyComponents = new List<string>();
            var componentDetails = new Dictionary<string, object>();

            foreach (var component in components)
            {
                var componentName = component.GetType().Name;
                var isHealthy = component.IsHealthy;
                var status = component.HealthStatus ?? "Unknown";
                var lastCheck = component.LastHealthCheck;

                componentDetails[$"{componentName}_is_healthy"] = isHealthy;
                componentDetails[$"{componentName}_status"] = status;
                
                if (lastCheck.HasValue)
                {
                    componentDetails[$"{componentName}_last_check"] = lastCheck.Value;
                    componentDetails[$"{componentName}_seconds_since_check"] = 
                        (DateTimeOffset.UtcNow - lastCheck.Value).TotalSeconds;
                }

                if (isHealthy)
                {
                    healthyComponents.Add(componentName);
                }
                else
                {
                    unhealthyComponents.Add(componentName);
                }
            }

            componentDetails["total_components"] = components.Count;
            componentDetails["healthy_count"] = healthyComponents.Count;
            componentDetails["unhealthy_count"] = unhealthyComponents.Count;

            if (unhealthyComponents.Count > 0)
            {
                componentDetails["unhealthy_components"] = unhealthyComponents;
                
                return Task.FromResult(
                    HealthCheckResult.Unhealthy(
                        $"Persistence layer has {unhealthyComponents.Count} unhealthy component(s): {string.Join(", ", unhealthyComponents)}",
                        null,
                        componentDetails));
            }

            componentDetails["healthy_components"] = healthyComponents;
            
            return Task.FromResult(
                HealthCheckResult.Healthy(
                    $"All {components.Count} persistence component(s) are healthy",
                    componentDetails));
        }
        catch (Exception ex)
        {
            return Task.FromResult(
                HealthCheckResult.Unhealthy(
                    "Error checking persistence layer health",
                    ex,
                    new Dictionary<string, object>
                    {
                        ["error"] = ex.Message
                    }));
        }
    }
}
