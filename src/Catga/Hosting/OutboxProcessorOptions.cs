namespace Catga.Hosting;

/// <summary>
/// Outbox 处理器配置选项
/// </summary>
public sealed class OutboxProcessorOptions
{
    /// <summary>
    /// 扫描间隔
    /// </summary>
    public TimeSpan ScanInterval { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// 批次大小
    /// </summary>
    public int BatchSize { get; set; } = 100;
    
    /// <summary>
    /// 错误延迟
    /// </summary>
    public TimeSpan ErrorDelay { get; set; } = TimeSpan.FromSeconds(10);
    
    /// <summary>
    /// 是否在停机时完成当前批次
    /// </summary>
    public bool CompleteCurrentBatchOnShutdown { get; set; } = true;
    
    /// <summary>
    /// 验证配置选项
    /// </summary>
    public void Validate()
    {
        if (ScanInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("ScanInterval must be greater than zero", nameof(ScanInterval));
        }
        
        if (BatchSize <= 0)
        {
            throw new ArgumentException("BatchSize must be greater than zero", nameof(BatchSize));
        }
        
        if (ErrorDelay < TimeSpan.Zero)
        {
            throw new ArgumentException("ErrorDelay must be non-negative", nameof(ErrorDelay));
        }
    }
}
