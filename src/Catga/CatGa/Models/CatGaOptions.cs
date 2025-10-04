namespace Catga.CatGa.Models;

/// <summary>
/// CatGa 配置选项（优化版 - 安全、高性能、可靠、分布式）
/// </summary>
public class CatGaOptions
{
    // ═══════════════════════════════════════════════════════════
    // 1️⃣ 安全性 (Security)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 全局超时时间（防止长时间占用资源）
    /// </summary>
    public TimeSpan GlobalTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 补偿超时时间（补偿操作必须快速完成）
    /// </summary>
    public TimeSpan CompensationTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>
    /// 是否启用输入验证
    /// </summary>
    public bool EnableValidation { get; set; } = true;

    /// <summary>
    /// 最大请求大小（字节，防止大对象攻击）
    /// </summary>
    public int MaxRequestSize { get; set; } = 10 * 1024 * 1024; // 10 MB

    /// <summary>
    /// 是否在错误中包含内部详细信息（生产环境应为 false）
    /// </summary>
    public bool IncludeInternalErrorDetails { get; set; } = false;

    // ═══════════════════════════════════════════════════════════
    // 2️⃣ 高性能 (Performance)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 是否启用幂等性（防重复处理）
    /// </summary>
    public bool IdempotencyEnabled { get; set; } = true;

    /// <summary>
    /// 幂等性存储分片数（必须是 2 的幂，建议 64-256）
    /// </summary>
    public int IdempotencyShardCount { get; set; } = 128;

    /// <summary>
    /// 幂等性缓存过期时间
    /// </summary>
    public TimeSpan IdempotencyExpiry { get; set; } = TimeSpan.FromHours(1);

    /// <summary>
    /// 是否启用对象池（减少 GC 压力）
    /// </summary>
    public bool EnableObjectPooling { get; set; } = true;

    /// <summary>
    /// 最大并发事务数（0 = 无限制）
    /// </summary>
    public int MaxConcurrentTransactions { get; set; } = 1000;

    /// <summary>
    /// 是否预热（首次调用前加载所有处理器）
    /// </summary>
    public bool EnableWarmup { get; set; } = false;

    // ═══════════════════════════════════════════════════════════
    // 3️⃣ 可靠性 (Reliability)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 是否启用自动补偿
    /// </summary>
    public bool AutoCompensate { get; set; } = true;

    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetryAttempts { get; set; } = 3;

    /// <summary>
    /// 初始重试延迟
    /// </summary>
    public TimeSpan InitialRetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);

    /// <summary>
    /// 最大重试延迟
    /// </summary>
    public TimeSpan MaxRetryDelay { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否使用 Jitter（随机化重试时间，防止雷鸣）
    /// </summary>
    public bool UseJitter { get; set; } = true;

    /// <summary>
    /// 是否启用断路器
    /// </summary>
    public bool EnableCircuitBreaker { get; set; } = false;

    /// <summary>
    /// 断路器失败阈值
    /// </summary>
    public int CircuitBreakerFailureThreshold { get; set; } = 5;

    /// <summary>
    /// 断路器重置超时（秒）
    /// </summary>
    public int CircuitBreakerResetTimeout { get; set; } = 60;

    /// <summary>
    /// 是否启用健康检查
    /// </summary>
    public bool EnableHealthCheck { get; set; } = true;

    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan HealthCheckInterval { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 是否启用优雅关闭
    /// </summary>
    public bool EnableGracefulShutdown { get; set; } = true;

    /// <summary>
    /// 优雅关闭超时
    /// </summary>
    public TimeSpan GracefulShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);

    // ═══════════════════════════════════════════════════════════
    // 4️⃣ 分布式 (Distributed)
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 服务 ID（用于分布式追踪）
    /// </summary>
    public string ServiceId { get; set; } = Environment.MachineName;

    /// <summary>
    /// 是否启用分布式追踪
    /// </summary>
    public bool EnableDistributedTracing { get; set; } = true;

    /// <summary>
    /// 是否启用分布式锁（Redis 环境）
    /// </summary>
    public bool EnableDistributedLock { get; set; } = false;

    /// <summary>
    /// 分布式锁超时
    /// </summary>
    public TimeSpan DistributedLockTimeout { get; set; } = TimeSpan.FromSeconds(10);

    /// <summary>
    /// 是否启用故障转移
    /// </summary>
    public bool EnableFailover { get; set; } = false;

    /// <summary>
    /// 故障转移端点
    /// </summary>
    public string? FallbackEndpoint { get; set; }

    /// <summary>
    /// 是否启用服务发现
    /// </summary>
    public bool EnableServiceDiscovery { get; set; } = false;

    /// <summary>
    /// 是否启用上下文持久化（用于事务状态追踪）
    /// </summary>
    public bool EnableContextPersistence { get; set; } = false;

    // ═══════════════════════════════════════════════════════════
    // 预设配置
    // ═══════════════════════════════════════════════════════════

    /// <summary>
    /// 极致性能模式（最少开销）
    /// </summary>
    public CatGaOptions WithExtremePerformance()
    {
        IdempotencyEnabled = false;
        EnableValidation = false;
        AutoCompensate = false;
        MaxRetryAttempts = 0;
        EnableCircuitBreaker = false;
        EnableHealthCheck = false;
        EnableDistributedTracing = false;
        IncludeInternalErrorDetails = false;

        IdempotencyShardCount = 256;  // 最大分片
        MaxConcurrentTransactions = 10000;
        EnableObjectPooling = true;
        EnableWarmup = true;

        return this;
    }

    /// <summary>
    /// 高可靠性模式（生产环境推荐）
    /// </summary>
    public CatGaOptions WithHighReliability()
    {
        IdempotencyEnabled = true;
        EnableValidation = true;
        AutoCompensate = true;
        MaxRetryAttempts = 5;
        UseJitter = true;
        EnableCircuitBreaker = true;
        EnableHealthCheck = true;
        EnableGracefulShutdown = true;
        EnableDistributedTracing = true;
        IncludeInternalErrorDetails = false;

        IdempotencyExpiry = TimeSpan.FromHours(24);
        GlobalTimeout = TimeSpan.FromSeconds(60);
        CompensationTimeout = TimeSpan.FromSeconds(30);

        return this;
    }

    /// <summary>
    /// 分布式模式（跨服务场景）
    /// </summary>
    public CatGaOptions WithDistributed()
    {
        EnableDistributedTracing = true;
        EnableDistributedLock = true;
        EnableServiceDiscovery = true;
        EnableFailover = true;
        EnableHealthCheck = true;

        IdempotencyEnabled = true;
        AutoCompensate = true;
        MaxRetryAttempts = 3;

        return this;
    }

    /// <summary>
    /// 开发模式（详细日志和错误）
    /// </summary>
    public CatGaOptions ForDevelopment()
    {
        IncludeInternalErrorDetails = true;
        EnableValidation = true;
        EnableHealthCheck = true;
        EnableDistributedTracing = true;

        IdempotencyEnabled = true;
        IdempotencyExpiry = TimeSpan.FromMinutes(5);
        GlobalTimeout = TimeSpan.FromSeconds(120);

        return this;
    }

    /// <summary>
    /// 简化模式（最简单）
    /// </summary>
    public CatGaOptions WithSimpleMode()
    {
        IdempotencyEnabled = false;
        EnableValidation = false;
        AutoCompensate = false;
        MaxRetryAttempts = 0;
        EnableCircuitBreaker = false;
        EnableHealthCheck = false;
        EnableDistributedTracing = false;
        EnableDistributedLock = false;

        return this;
    }
}
