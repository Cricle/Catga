namespace Catga.Hosting;

/// <summary>
/// 恢复结果
/// </summary>
public readonly record struct RecoveryResult
{
    /// <summary>
    /// 成功恢复的组件数量
    /// </summary>
    public int Succeeded { get; init; }
    
    /// <summary>
    /// 恢复失败的组件数量
    /// </summary>
    public int Failed { get; init; }
    
    /// <summary>
    /// 恢复耗时
    /// </summary>
    public TimeSpan Duration { get; init; }
    
    /// <summary>
    /// 是否成功（所有组件都恢复成功）
    /// </summary>
    public bool IsSuccess => Failed == 0 && Succeeded > 0;
    
    /// <summary>
    /// 表示恢复已在进行中的特殊结果
    /// </summary>
    public static RecoveryResult AlreadyRecovering => new(-1, -1, TimeSpan.Zero);
    
    /// <summary>
    /// 创建恢复结果
    /// </summary>
    public RecoveryResult(int succeeded, int failed, TimeSpan duration)
    {
        Succeeded = succeeded;
        Failed = failed;
        Duration = duration;
    }
}
