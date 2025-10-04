namespace Catga.CatGa.Policies;

/// <summary>
/// 默认补偿策略
/// </summary>
public sealed class DefaultCompensationPolicy : ICompensationPolicy
{
    public TimeSpan CompensationTimeout { get; }
    public bool ThrowOnCompensationFailure { get; }

    public DefaultCompensationPolicy(
        TimeSpan? compensationTimeout = null,
        bool throwOnCompensationFailure = false)
    {
        CompensationTimeout = compensationTimeout ?? TimeSpan.FromSeconds(15);
        ThrowOnCompensationFailure = throwOnCompensationFailure;
    }

    public bool ShouldCompensate(Exception? exception)
    {
        // 默认情况下，任何异常都应该补偿
        // 可以根据异常类型自定义
        return exception != null;
    }
}

