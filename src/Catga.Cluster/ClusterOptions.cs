namespace Catga.Cluster;

/// <summary>
/// 集群配置
/// </summary>
public sealed class ClusterOptions
{
    /// <summary>
    /// 节点 ID（默认：机器名）
    /// </summary>
    public string NodeId { get; set; } = Environment.MachineName;

    /// <summary>
    /// 节点端点（http://ip:port）
    /// </summary>
    public string? Endpoint { get; set; }

    /// <summary>
    /// 心跳间隔（默认：5 秒）
    /// </summary>
    public TimeSpan HeartbeatInterval { get; set; } = TimeSpan.FromSeconds(5);

    /// <summary>
    /// 心跳超时（默认：30 秒）
    /// </summary>
    public TimeSpan HeartbeatTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>
    /// 节点元数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }

    /// <summary>
    /// 启用自动故障转移和重试（默认：true）
    /// </summary>
    public bool EnableFailover { get; set; } = true;

    /// <summary>
    /// 最大重试次数（默认：2）
    /// </summary>
    public int MaxRetries { get; set; } = 2;

    /// <summary>
    /// 重试延迟（默认：100ms）
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromMilliseconds(100);
}

