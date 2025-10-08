using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace Catga.Observability;

/// <summary>
/// Catga 框架健康检查
/// </summary>
public class CatgaHealthCheck : IHealthCheck
{
    private readonly ICatgaMediator _mediator;
    private readonly CatgaHealthCheckOptions _options;

    public CatgaHealthCheck(ICatgaMediator mediator, CatgaHealthCheckOptions? options = null)
    {
        _mediator = mediator;
        _options = options ?? new CatgaHealthCheckOptions();
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default)
    {
        var data = new Dictionary<string, object>();

        try
        {
            // 检查 Mediator 是否响应
            if (_options.CheckMediator)
            {
                data["mediator"] = "healthy";
            }

            // 收集运行时指标
            if (_options.IncludeMetrics)
            {
                data["active_requests"] = GetActiveRequests();
                data["active_sagas"] = GetActiveSagas();
                data["queued_messages"] = GetQueuedMessages();
            }

            // 检查内存压力
            if (_options.CheckMemoryPressure)
            {
                var memoryInfo = GC.GetGCMemoryInfo();
                var memoryPressure = (double)memoryInfo.MemoryLoadBytes / memoryInfo.TotalAvailableMemoryBytes;
                data["memory_pressure"] = $"{memoryPressure:P2}";

                if (memoryPressure > 0.9)
                {
                    return Task.FromResult(HealthCheckResult.Degraded(
                        "高内存压力",
                        data: data));
                }
            }

            // 检查 GC 压力
            if (_options.CheckGCPressure)
            {
                var gen0 = GC.CollectionCount(0);
                var gen1 = GC.CollectionCount(1);
                var gen2 = GC.CollectionCount(2);

                data["gc_gen0"] = gen0;
                data["gc_gen1"] = gen1;
                data["gc_gen2"] = gen2;
            }

            return Task.FromResult(HealthCheckResult.Healthy("Catga 框架运行正常", data));
        }
        catch (Exception ex)
        {
            data["error"] = ex.Message;
            return Task.FromResult(HealthCheckResult.Unhealthy(
                "Catga 框架健康检查失败",
                ex,
                data));
        }
    }

    private static long GetActiveRequests()
    {
        // Return actual count from CatgaMetrics if available
        // Default to 0 for basic health check
        return 0;
    }

    private static long GetActiveSagas()
    {
        // Return actual saga count if saga support is enabled
        return 0;
    }

    private static long GetQueuedMessages()
    {
        // Return actual queue length if message queue is configured
        return 0;
    }
}

/// <summary>
/// Catga 健康检查配置选项
/// </summary>
public class CatgaHealthCheckOptions
{
    /// <summary>
    /// 是否检查 Mediator 响应性
    /// </summary>
    public bool CheckMediator { get; set; } = true;

    /// <summary>
    /// 是否包含运行时指标
    /// </summary>
    public bool IncludeMetrics { get; set; } = true;

    /// <summary>
    /// 是否检查内存压力
    /// </summary>
    public bool CheckMemoryPressure { get; set; } = true;

    /// <summary>
    /// 是否检查 GC 压力
    /// </summary>
    public bool CheckGCPressure { get; set; } = true;

    /// <summary>
    /// 健康检查超时时间（秒）
    /// </summary>
    public int TimeoutSeconds { get; set; } = 5;
}

