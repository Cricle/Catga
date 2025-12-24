namespace Catga.Hosting;

/// <summary>
/// 支持健康检查的接口
/// </summary>
public interface IHealthCheckable
{
    /// <summary>
    /// 指示组件是否健康
    /// </summary>
    bool IsHealthy { get; }
    
    /// <summary>
    /// 获取健康状态描述
    /// </summary>
    string? HealthStatus { get; }
    
    /// <summary>
    /// 获取最后一次健康检查时间
    /// </summary>
    DateTimeOffset? LastHealthCheck { get; }
}
