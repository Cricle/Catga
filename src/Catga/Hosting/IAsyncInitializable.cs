namespace Catga.Hosting;

/// <summary>
/// 支持异步初始化的接口
/// </summary>
public interface IAsyncInitializable
{
    /// <summary>
    /// 异步初始化组件
    /// </summary>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>初始化任务</returns>
    Task InitializeAsync(CancellationToken cancellationToken = default);
}
