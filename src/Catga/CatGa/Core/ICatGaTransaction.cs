namespace Catga.CatGa.Core;

/// <summary>
/// CatGa 事务接口 - 极简 API，只需实现两个方法
/// </summary>
/// <typeparam name="TRequest">请求类型</typeparam>
/// <typeparam name="TResponse">响应类型</typeparam>
public interface ICatGaTransaction<in TRequest, TResponse>
{
    /// <summary>
    /// 执行主操作
    /// </summary>
    /// <param name="request">请求数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>操作结果</returns>
    Task<TResponse> ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 补偿操作（失败时调用）
    /// 注意：如果不需要补偿，可以返回 Task.CompletedTask
    /// </summary>
    /// <param name="request">请求数据</param>
    /// <param name="cancellationToken">取消令牌</param>
    /// <returns>补偿任务</returns>
    Task CompensateAsync(TRequest request, CancellationToken cancellationToken = default);
}

/// <summary>
/// CatGa 无返回值事务接口
/// </summary>
/// <typeparam name="TRequest">请求类型</typeparam>
public interface ICatGaTransaction<in TRequest>
{
    /// <summary>
    /// 执行主操作
    /// </summary>
    Task ExecuteAsync(TRequest request, CancellationToken cancellationToken = default);

    /// <summary>
    /// 补偿操作
    /// </summary>
    Task CompensateAsync(TRequest request, CancellationToken cancellationToken = default);
}

