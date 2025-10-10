namespace Catga.Cluster.Remote;

/// <summary>
/// 远程请求包装器
/// </summary>
public sealed record RemoteRequest
{
    /// <summary>
    /// 请求类型全名
    /// </summary>
    public required string RequestTypeName { get; init; }

    /// <summary>
    /// 响应类型全名
    /// </summary>
    public required string ResponseTypeName { get; init; }

    /// <summary>
    /// 序列化的请求数据
    /// </summary>
    public required byte[] PayloadData { get; init; }

    /// <summary>
    /// 源节点 ID
    /// </summary>
    public string? SourceNodeId { get; init; }

    /// <summary>
    /// 请求 ID（用于追踪）
    /// </summary>
    public string RequestId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 请求时间戳
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// 远程响应包装器
/// </summary>
public sealed record RemoteResponse
{
    /// <summary>
    /// 请求 ID
    /// </summary>
    public required string RequestId { get; init; }

    /// <summary>
    /// 是否成功
    /// </summary>
    public bool IsSuccess { get; init; }

    /// <summary>
    /// 序列化的响应数据
    /// </summary>
    public byte[]? PayloadData { get; init; }

    /// <summary>
    /// 错误消息
    /// </summary>
    public string? ErrorMessage { get; init; }

    /// <summary>
    /// 处理节点 ID
    /// </summary>
    public string? ProcessedByNodeId { get; init; }

    /// <summary>
    /// 处理时间（毫秒）
    /// </summary>
    public long ProcessingTimeMs { get; init; }
}

