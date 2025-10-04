namespace Catga.CatGa.Policies;

/// <summary>
/// 指数退避重试策略（带 Jitter）
/// </summary>
public sealed class ExponentialBackoffRetryPolicy : IRetryPolicy
{
    private readonly TimeSpan _initialDelay;
    private readonly TimeSpan _maxDelay;
    private readonly bool _useJitter;
    private readonly Random _random;

    public int MaxAttempts { get; }

    public ExponentialBackoffRetryPolicy(
        int maxAttempts = 3,
        TimeSpan? initialDelay = null,
        TimeSpan? maxDelay = null,
        bool useJitter = true)
    {
        MaxAttempts = maxAttempts;
        _initialDelay = initialDelay ?? TimeSpan.FromMilliseconds(100);
        _maxDelay = maxDelay ?? TimeSpan.FromSeconds(10);
        _useJitter = useJitter;
        _random = new Random();
    }

    public bool ShouldRetry(int attemptCount, Exception? exception)
    {
        // 总是重试，直到达到最大次数
        return attemptCount < MaxAttempts;
    }

    public TimeSpan CalculateDelay(int attemptCount)
    {
        if (attemptCount <= 0)
            return TimeSpan.Zero;

        // 指数退避: delay = initialDelay * 2^(attemptCount - 1)
        var exponentialDelay = _initialDelay.TotalMilliseconds * Math.Pow(2, attemptCount - 1);

        // 限制最大延迟
        var delay = Math.Min(exponentialDelay, _maxDelay.TotalMilliseconds);

        // 添加 Jitter（随机化）
        if (_useJitter)
        {
            var jitter = _random.NextDouble() * 0.3; // ±30%
            delay = delay * (1 + jitter - 0.15);
        }

        return TimeSpan.FromMilliseconds(delay);
    }
}

