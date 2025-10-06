// ⚠️ 实验性功能 - 功能不完整，API 可能在未来版本中变化
// 适用场景：需要完整审计日志和事件溯源的系统

using Catga.Messages;

namespace Catga.EventSourcing;

/// <summary>
/// 事件存储抽象接口
/// <para>⚠️ 实验性功能 - 功能不完整</para>
/// <para>适用场景：需要完整审计日志的系统</para>
/// </summary>
public interface IEventStore
{
    /// <summary>
    /// 追加事件到流
    /// </summary>
    Task AppendToStreamAsync(
        string streamId,
        IEnumerable<IEvent> events,
        long expectedVersion = -1,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取事件流
    /// </summary>
    Task<IReadOnlyList<StoredEvent>> ReadStreamAsync(
        string streamId,
        long fromVersion = 0,
        int maxCount = int.MaxValue,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 读取所有事件（从某个位置开始）
    /// </summary>
    IAsyncEnumerable<StoredEvent> ReadAllAsync(
        long fromPosition = 0,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// 保存快照
    /// </summary>
    Task SaveSnapshotAsync<TSnapshot>(
        string streamId,
        long version,
        TSnapshot snapshot,
        CancellationToken cancellationToken = default) where TSnapshot : class;

    /// <summary>
    /// 加载快照
    /// </summary>
    Task<(TSnapshot? Snapshot, long Version)> LoadSnapshotAsync<TSnapshot>(
        string streamId,
        CancellationToken cancellationToken = default) where TSnapshot : class;

    /// <summary>
    /// 删除流
    /// </summary>
    Task DeleteStreamAsync(string streamId, CancellationToken cancellationToken = default);
}

/// <summary>
/// 存储的事件
/// </summary>
public record StoredEvent(
    string StreamId,
    long Version,
    string EventType,
    string EventData,
    string? Metadata,
    DateTime Timestamp)
{
    /// <summary>
    /// 全局位置（用于读取所有事件）
    /// </summary>
    public long Position { get; init; }
}

/// <summary>
/// 事件流
/// </summary>
public record EventStream(
    string StreamId,
    long Version,
    IReadOnlyList<StoredEvent> Events);

/// <summary>
/// 聚合根基类（用于事件溯源）
/// </summary>
public abstract class AggregateRoot
{
    private readonly List<IEvent> _uncommittedEvents = new();

    public string Id { get; protected set; } = string.Empty;
    public long Version { get; protected set; } = -1;

    /// <summary>
    /// 未提交的事件
    /// </summary>
    public IReadOnlyList<IEvent> UncommittedEvents => _uncommittedEvents;

    /// <summary>
    /// 应用事件（用于重建状态）
    /// </summary>
    protected abstract void Apply(IEvent @event);

    /// <summary>
    /// 应用新事件
    /// </summary>
    protected void RaiseEvent(IEvent @event)
    {
        Apply(@event);
        _uncommittedEvents.Add(@event);
        Version++;
    }

    /// <summary>
    /// 从事件流重建聚合根
    /// </summary>
    public void LoadFromHistory(IEnumerable<IEvent> events)
    {
        foreach (var @event in events)
        {
            Apply(@event);
            Version++;
        }
    }

    /// <summary>
    /// 标记事件已提交
    /// </summary>
    public void MarkEventsAsCommitted()
    {
        _uncommittedEvents.Clear();
    }
}

/// <summary>
/// 投影基类
/// </summary>
public interface IProjection
{
    /// <summary>
    /// 投影名称
    /// </summary>
    string ProjectionName { get; }

    /// <summary>
    /// 处理事件
    /// </summary>
    Task HandleAsync(StoredEvent storedEvent, CancellationToken cancellationToken = default);
}

/// <summary>
/// 投影管理器
/// </summary>
public interface IProjectionManager
{
    /// <summary>
    /// 注册投影
    /// </summary>
    void RegisterProjection(IProjection projection);

    /// <summary>
    /// 启动所有投影
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 停止所有投影
    /// </summary>
    Task StopAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// 重建投影
    /// </summary>
    Task RebuildProjectionAsync(string projectionName, CancellationToken cancellationToken = default);
}

