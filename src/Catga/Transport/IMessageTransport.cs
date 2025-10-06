using System.Diagnostics.CodeAnalysis;

namespace Catga.Transport;

/// <summary>
/// 消息传输层接口 - 负责消息的发送和接收
/// 与 Outbox/Inbox 存储层分离，遵循单一职责原则
/// </summary>
public interface IMessageTransport
{
    /// <summary>
    /// 发布消息到传输层
    /// </summary>
    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// 发送消息到指定目标
    /// </summary>
    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// 订阅消息
    /// </summary>
    Task SubscribeAsync<TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class;

    /// <summary>
    /// 传输层名称（NATS, Redis, RabbitMQ 等）
    /// </summary>
    string Name { get; }
}

/// <summary>
/// 传输上下文 - 携带消息元数据
/// </summary>
public class TransportContext
{
    /// <summary>
    /// 消息 ID
    /// </summary>
    public string? MessageId { get; set; }

    /// <summary>
    /// 关联 ID（用于分布式追踪）
    /// </summary>
    public string? CorrelationId { get; set; }

    /// <summary>
    /// 消息类型
    /// </summary>
    public string? MessageType { get; set; }

    /// <summary>
    /// 发送时间
    /// </summary>
    public DateTime? SentAt { get; set; }

    /// <summary>
    /// 重试次数
    /// </summary>
    public int RetryCount { get; set; }

    /// <summary>
    /// 自定义元数据
    /// </summary>
    public Dictionary<string, string>? Metadata { get; set; }
}

