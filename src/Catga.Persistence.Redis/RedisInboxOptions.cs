namespace Catga.Persistence.Redis;

/// <summary>
/// Redis Inbox 持久化存储配置
/// </summary>
public class RedisInboxOptions
{
    /// <summary>
    /// Redis 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Inbox 键前缀
    /// </summary>
    public string KeyPrefix { get; set; } = "inbox:";

    /// <summary>
    /// 消息保留时间（默认 24 小时）
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 锁定超时时间（默认 5 分钟）
    /// </summary>
    public TimeSpan LockTimeout { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>
    /// 数据库编号（默认 0）
    /// </summary>
    public int Database { get; set; } = 0;
}

