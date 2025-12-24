namespace Catga.Hosting;

/// <summary>
/// 恢复服务配置选项
/// </summary>
public sealed class RecoveryOptions
{
    /// <summary>
    /// 健康检查间隔
    /// </summary>
    public TimeSpan CheckInterval { get; set; } = TimeSpan.FromSeconds(30);
    
    /// <summary>
    /// 最大重试次数
    /// </summary>
    public int MaxRetries { get; set; } = 3;
    
    /// <summary>
    /// 重试延迟（基础延迟，使用指数退避）
    /// </summary>
    public TimeSpan RetryDelay { get; set; } = TimeSpan.FromSeconds(5);
    
    /// <summary>
    /// 是否启用自动恢复
    /// </summary>
    public bool EnableAutoRecovery { get; set; } = true;
    
    /// <summary>
    /// 是否使用指数退避
    /// </summary>
    public bool UseExponentialBackoff { get; set; } = true;
    
    /// <summary>
    /// 验证配置选项
    /// </summary>
    public void Validate()
    {
        if (CheckInterval <= TimeSpan.Zero)
        {
            throw new ArgumentException("CheckInterval must be greater than zero", nameof(CheckInterval));
        }
        
        if (MaxRetries < 0)
        {
            throw new ArgumentException("MaxRetries must be non-negative", nameof(MaxRetries));
        }
        
        if (RetryDelay < TimeSpan.Zero)
        {
            throw new ArgumentException("RetryDelay must be non-negative", nameof(RetryDelay));
        }
    }
}
