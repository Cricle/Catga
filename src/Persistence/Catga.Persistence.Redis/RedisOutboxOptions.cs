namespace Catga.Persistence.Redis;

/// <summary>
/// Redis Outbox 持久化存储配置
/// </summary>
public class RedisOutboxOptions
{
    /// <summary>
    /// Redis 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Outbox 键前缀
    /// </summary>
    public string KeyPrefix { get; set; } = "outbox:";

    /// <summary>
    /// 消息保留时间（默认 24 小时）
    /// </summary>
    public TimeSpan RetentionPeriod { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 轮询间隔（默认 5 秒）
    /// </summary>
    public TimeSpan PollingInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 每批次处理消息数量（默认 100）
    /// </summary>
    public int BatchSize { get; set; } = 100;

    /// <summary>
    /// 数据库编号（默认 0）
    /// </summary>
    public int Database { get; set; } = 0;
}

