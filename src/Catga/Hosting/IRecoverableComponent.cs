namespace Catga.Hosting;

/// <summary>
/// 可恢复组件接口
/// </summary>
public interface IRecoverableComponent
{
    /// <summary>
    /// 指示组件是否健康
    /// </summary>
    bool IsHealthy { get; }
    
    /// <summary>
    /// 组件名称
    /// </summary>
    string ComponentName { get; }
    
    /// <summary>
    /// 尝试恢复组件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>恢复任务</returns>
    Task RecoverAsync(CancellationToken cancellationToken = default);
}
