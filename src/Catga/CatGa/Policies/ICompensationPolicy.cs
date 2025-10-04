namespace Catga.CatGa.Policies;

/// <summary>
/// 补偿策略接口 - 单一职责：补偿逻辑
/// </summary>
public interface ICompensationPolicy
{
    /// <summary>
    /// 是否应该执行补偿
    /// </summary>
    bool ShouldCompensate(Exception? exception);

    /// <summary>
    /// 补偿超时时间
    /// </summary>
    TimeSpan CompensationTimeout { get; }

    /// <summary>
    /// 补偿失败时是否抛出异常
    /// </summary>
    bool ThrowOnCompensationFailure { get; }
}

