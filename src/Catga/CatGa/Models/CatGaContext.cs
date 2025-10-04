namespace Catga.CatGa.Models;

/// <summary>
/// CatGa 事务上下文 - 轻量级，只包含必要信息
/// </summary>
public sealed class CatGaContext
{
    /// <summary>
    /// 事务 ID（唯一标识）
    /// </summary>
    public string TransactionId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 幂等性键（用于去重）
    /// </summary>
    public string IdempotencyKey { get; init; } = string.Empty;

    /// <summary>
    /// 追踪ID（分布式追踪）
    /// </summary>
    public string TraceId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 跨度ID（分布式追踪）
    /// </summary>
    public string SpanId { get; init; } = Guid.NewGuid().ToString("N");

    /// <summary>
    /// 创建时间
    /// </summary>
    public DateTime CreatedAt { get; init; } = DateTime.UtcNow;

    /// <summary>
    /// 事务状态
    /// </summary>
    public CatGaTransactionState State { get; private set; } = CatGaTransactionState.Pending;

    /// <summary>
    /// 当前尝试次数
    /// </summary>
    public int AttemptCount { get; private set; }

    /// <summary>
    /// 是否已补偿
    /// </summary>
    public bool WasCompensated { get; private set; }

    /// <summary>
    /// 元数据（用于传递额外信息）
    /// </summary>
    public Dictionary<string, string> Metadata { get; } = new();

    // ═══════════════════════════════════════════════════════════
    // 内部方法（由 Executor 调用）
    // ═══════════════════════════════════════════════════════════

    internal void SetAttemptCount(int count)
    {
        AttemptCount = count;
    }

    internal void MarkCompensated()
    {
        WasCompensated = true;
        State = CatGaTransactionState.Compensated;
    }

    internal void MarkSucceeded()
    {
        State = CatGaTransactionState.Succeeded;
    }

    internal void MarkFailed()
    {
        State = CatGaTransactionState.Failed;
    }
}

/// <summary>
/// CatGa 事务状态
/// </summary>
public enum CatGaTransactionState
{
    /// <summary>
    /// 待处理
    /// </summary>
    Pending,

    /// <summary>
    /// 执行中
    /// </summary>
    Executing,

    /// <summary>
    /// 成功
    /// </summary>
    Succeeded,

    /// <summary>
    /// 失败
    /// </summary>
    Failed,

    /// <summary>
    /// 已补偿
    /// </summary>
    Compensated
}
