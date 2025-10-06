namespace Catga.Transport.Nats;

/// <summary>
/// NATS 消息传输配置
/// </summary>
public class NatsTransportOptions
{
    /// <summary>
    /// NATS 服务器 URL
    /// </summary>
    public string Url { get; set; } = "nats://localhost:4222";

    /// <summary>
    /// 主题前缀
    /// </summary>
    public string SubjectPrefix { get; set; } = "catga.";

    /// <summary>
    /// 连接超时（秒）
    /// </summary>
    public int ConnectTimeout { get; set; } = 5;

    /// <summary>
    /// 请求超时（秒）
    /// </summary>
    public int RequestTimeout { get; set; } = 30;

    /// <summary>
    /// 是否启用 JetStream
    /// </summary>
    public bool EnableJetStream { get; set; } = false;

    /// <summary>
    /// JetStream 流名称
    /// </summary>
    public string? StreamName { get; set; }
}

