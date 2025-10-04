namespace Catga.Redis;

/// <summary>
/// Redis Transit 配置选项
/// </summary>
public class RedisCatgaOptions
{
    /// <summary>
    /// Redis 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// Saga 键前缀
    /// </summary>
    public string SagaKeyPrefix { get; set; } = "saga:";

    /// <summary>
    /// Saga 过期时间（默认 7 天）
    /// </summary>
    public TimeSpan SagaExpiry { get; set; } = TimeSpan.FromDays(7);

    /// <summary>
    /// 幂等性键前缀
    /// </summary>
    public string IdempotencyKeyPrefix { get; set; } = "idempotency:";

    /// <summary>
    /// 幂等性过期时间（默认 24 小时）
    /// </summary>
    public TimeSpan IdempotencyExpiry { get; set; } = TimeSpan.FromHours(24);

    /// <summary>
    /// 数据库编号（默认 0）
    /// </summary>
    public int Database { get; set; } = 0;

    /// <summary>
    /// 连接超时（毫秒）
    /// </summary>
    public int ConnectTimeout { get; set; } = 5000;

    /// <summary>
    /// 同步超时（毫秒）
    /// </summary>
    public int SyncTimeout { get; set; } = 5000;

    /// <summary>
    /// 是否允许管理员命令
    /// </summary>
    public bool AllowAdmin { get; set; } = false;

    /// <summary>
    /// 保持连接间隔（秒，-1 表示禁用）
    /// </summary>
    public int KeepAlive { get; set; } = 60;

    /// <summary>
    /// 重试次数
    /// </summary>
    public int ConnectRetry { get; set; } = 3;

    /// <summary>
    /// 是否使用 SSL
    /// </summary>
    public bool UseSsl { get; set; } = false;

    /// <summary>
    /// SSL 主机名
    /// </summary>
    public string? SslHost { get; set; }
}

