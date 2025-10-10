namespace Catga.Cluster.Metrics;

/// <summary>
/// 负载上报接口
/// </summary>
public interface ILoadReporter
{
    /// <summary>
    /// 获取当前节点负载（0-100）
    /// </summary>
    Task<int> GetCurrentLoadAsync(CancellationToken cancellationToken = default);
}

