using Catga.CatGa.Models;

namespace Catga.CatGa.Core;

/// <summary>
/// CatGa 执行器接口 - 负责执行事务（包含重试、补偿、幂等性）
/// </summary>
public interface ICatGaExecutor
{
    /// <summary>
    /// 执行事务（带返回值）
    /// </summary>
    Task<CatGaResult<TResponse>> ExecuteAsync<TRequest, TResponse>(
        TRequest request,
        CatGaContext? context = null,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 执行事务（无返回值）
    /// </summary>
    Task<CatGaResult> ExecuteAsync<TRequest>(
        TRequest request,
        CatGaContext? context = null,
        CancellationToken cancellationToken = default);
}

