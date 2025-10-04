namespace Catga.CatGa.Repository;

/// <summary>
/// CatGa 仓储接口 - 单一职责：数据持久化
/// </summary>
public interface ICatGaRepository
{
    /// <summary>
    /// 检查消息是否已处理（幂等性）
    /// </summary>
    bool IsProcessed(string idempotencyKey);

    /// <summary>
    /// 标记消息为已处理
    /// </summary>
    void MarkProcessed(string idempotencyKey);

    /// <summary>
    /// 缓存执行结果
    /// </summary>
    void CacheResult<T>(string idempotencyKey, T? result);

    /// <summary>
    /// 尝试获取缓存的结果
    /// </summary>
    bool TryGetCachedResult<T>(string idempotencyKey, out T? result);

    /// <summary>
    /// 保存事务上下文（可选，用于事务状态持久化）
    /// </summary>
    Task SaveContextAsync<TRequest, TResponse>(
        string transactionId,
        TRequest request,
        Models.CatGaContext context,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 加载事务上下文
    /// </summary>
    Task<Models.CatGaContext?> LoadContextAsync(
        string transactionId,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 删除事务上下文
    /// </summary>
    Task DeleteContextAsync(
        string transactionId,
        CancellationToken cancellationToken = default);
}

