using System.Diagnostics;
using Catga;
using Catga.Observability;
using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Aspire.Hosting;

/// <summary>
/// Health check for Catga message broker
/// </summary>
public sealed class CatgaHealthCheck : IHealthCheck
{
    private readonly ICatgaMediator _mediator;
    private static readonly DateTime _startTime = DateTime.UtcNow;

    public CatgaHealthCheck(ICatgaMediator mediator)
    {
        _mediator = mediator ?? throw new ArgumentNullException(nameof(mediator));
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // 基础检查：验证 Mediator 是否可用
            if (_mediator == null)
            {
                return Task.FromResult(HealthCheckResult.Unhealthy("Catga mediator is not initialized"));
            }

            var data = new Dictionary<string, object>
            {
                ["mediator_type"] = _mediator.GetType().Name,
                ["uptime_seconds"] = (DateTime.UtcNow - _startTime).TotalSeconds,
                ["framework_version"] = typeof(ICatgaMediator).Assembly.GetName().Version?.ToString() ?? "Unknown"
            };

            // 检查 ActivitySource 是否活跃
            var activitySourceName = CatgaActivitySource.Instance.Name;
            data["activity_source"] = activitySourceName;

            // 检查运行时信息
            var process = Process.GetCurrentProcess();
            data["working_set_mb"] = process.WorkingSet64 / 1024.0 / 1024.0;
            data["thread_count"] = process.Threads.Count;

            // 所有检查通过
            return Task.FromResult(HealthCheckResult.Healthy(
                "Catga is operational",
                data));
        }
        catch (Exception ex)
        {
            var data = new Dictionary<string, object>
            {
                ["exception_type"] = ex.GetType().Name,
                ["exception_message"] = ex.Message
            };

            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Catga health check failed",
                ex,
                data));
        }
    }
}

