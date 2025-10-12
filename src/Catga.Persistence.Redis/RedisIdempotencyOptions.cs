namespace Catga.Persistence.Redis;

/// <summary>
/// Redis 幂等性存储配置 (immutable record)
/// </summary>
public record RedisIdempotencyOptions
{
    /// <summary>
    /// Redis 连接字符串
    /// </summary>
    public string ConnectionString { get; init; } = "localhost:6379";

    /// <summary>
    /// 幂等性键前缀
    /// </summary>
    public string KeyPrefix { get; init; } = "idempotency:";

    /// <summary>
    /// 幂等性过期时间（默认 24 小时）
    /// </summary>
    public TimeSpan Expiry { get; init; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 数据库编号（默认 0）
    /// </summary>
    public int Database { get; init; } = 0;
}

