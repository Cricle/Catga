namespace Catga;

/// <summary>Enables idempotent execution using Inbox pattern.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class IdempotentAttribute : Attribute
{
    public string? Key { get; set; }
    public int TtlSeconds { get; set; } = 86400;
}

/// <summary>Acquires distributed lock before execution.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class DistributedLockAttribute : Attribute
{
    public string Key { get; }
    public int TimeoutSeconds { get; set; } = 30;
    public int WaitSeconds { get; set; } = 10;
    public DistributedLockAttribute(string key) => Key = key;
}

/// <summary>Enables automatic retry on transient failures.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class RetryAttribute : Attribute
{
    public int MaxAttempts { get; set; } = 3;
    public int DelayMs { get; set; } = 100;
    public bool Exponential { get; set; } = true;
}

/// <summary>Sets execution timeout.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class TimeoutAttribute : Attribute
{
    public int Seconds { get; }
    public TimeoutAttribute(int seconds) => Seconds = seconds;
}

/// <summary>Enables circuit breaker pattern.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class CircuitBreakerAttribute : Attribute
{
    public int FailureThreshold { get; set; } = 5;
    public int BreakDurationSeconds { get; set; } = 30;
}

/// <summary>Handler only executes on cluster leader node.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class LeaderOnlyAttribute : Attribute { }

/// <summary>Routes request to specific shard based on key.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ShardedAttribute : Attribute
{
    public string Key { get; }
    public ShardedAttribute(string key) => Key = key;
}

/// <summary>Broadcasts request to all cluster nodes.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class BroadcastAttribute : Attribute { }

/// <summary>Marks a background task that runs as singleton across cluster.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class ClusterSingletonAttribute : Attribute { }
