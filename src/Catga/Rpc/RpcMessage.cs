namespace Catga.Rpc;

/// <summary>RPC request wrapper</summary>
public sealed class RpcRequest
{
    public required string ServiceName { get; set; }
    public required string MethodName { get; set; }
    public required string RequestId { get; set; }
    public required byte[] Payload { get; set; }
    public string? RequestType { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
    public TimeSpan? Timeout { get; set; }
}

/// <summary>RPC response wrapper</summary>
public sealed class RpcResponse
{
    public required string RequestId { get; set; }
    public byte[]? Payload { get; set; }
    public string? ResponseType { get; set; }
    public bool Success { get; set; }
    public string? ErrorMessage { get; set; }
    public string? ErrorCode { get; set; }
    public DateTime Timestamp { get; set; } = DateTime.UtcNow;
}

/// <summary>RPC options</summary>
public sealed class RpcOptions
{
    public string ServiceName { get; set; } = "default";
    public TimeSpan DefaultTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public int MaxConcurrentCalls { get; set; } = 100;
    public bool EnableMetrics { get; set; } = true;
}

