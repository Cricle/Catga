namespace Catga.Hosting;

/// <summary>
/// 支持等待完成的接口
/// </summary>
public interface IWaitable
{
    /// <summary>
    /// 等待所有正在处理的操作完成
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>等待任务</returns>
    Task WaitForCompletionAsync(CancellationToken cancellationToken = default);
    
    /// <summary>
    /// 获取当前正在处理的操作数量
    /// </summary>
    int PendingOperations { get; }
}
