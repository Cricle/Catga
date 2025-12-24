namespace Catga.Hosting;

/// <summary>
/// 托管服务配置选项
/// </summary>
public sealed class HostingOptions
{
    /// <summary>
    /// 是否启用自动恢复
    /// </summary>
    public bool EnableAutoRecovery { get; set; } = true;
    
    /// <summary>
    /// 是否启用传输层托管
    /// </summary>
    public bool EnableTransportHosting { get; set; } = true;
    
    /// <summary>
    /// 是否启用 Outbox 处理器
    /// </summary>
    public bool EnableOutboxProcessor { get; set; } = true;
    
    /// <summary>
    /// 恢复选项
    /// </summary>
    public RecoveryOptions Recovery { get; set; } = new();
    
    /// <summary>
    /// Outbox 处理器选项
    /// </summary>
    public OutboxProcessorOptions OutboxProcessor { get; set; } = new();
    
    /// <summary>
    /// 优雅停机超时时间
    /// </summary>
    public TimeSpan ShutdownTimeout { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 验证配置选项
    /// </summary>
    public void Validate()
    {
        if (ShutdownTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentException("ShutdownTimeout must be greater than zero", nameof(ShutdownTimeout));
        }
        
        if (EnableAutoRecovery)
        {
            Recovery.Validate();
        }
        
        if (EnableOutboxProcessor)
        {
            OutboxProcessor.Validate();
        }
    }
}
