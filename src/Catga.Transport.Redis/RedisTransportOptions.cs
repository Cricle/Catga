namespace Catga.Transport.Redis;

/// <summary>
/// Redis 消息传输配置
/// </summary>
public class RedisTransportOptions
{
    /// <summary>
    /// Redis 连接字符串
    /// </summary>
    public string ConnectionString { get; set; } = "localhost:6379";

    /// <summary>
    /// 消息通道前缀
    /// </summary>
    public string ChannelPrefix { get; set; } = "catga:";

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
}

