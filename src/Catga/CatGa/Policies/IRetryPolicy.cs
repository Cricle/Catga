using Catga.CatGa.Models;

namespace Catga.CatGa.Policies;

/// <summary>
/// 重试策略接口 - 单一职责：重试逻辑
/// </summary>
public interface IRetryPolicy
{
    /// <summary>
    /// 是否应该重试
    /// </summary>
    bool ShouldRetry(int attemptCount, Exception? exception);

    /// <summary>
    /// 计算重试延迟
    /// </summary>
    TimeSpan CalculateDelay(int attemptCount);

    /// <summary>
    /// 最大重试次数
    /// </summary>
    int MaxAttempts { get; }
}

