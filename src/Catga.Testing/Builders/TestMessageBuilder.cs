using Catga.Abstractions;
using Catga.Core;

namespace Catga.Testing.Builders;

/// <summary>
/// 测试消息构建器 - 使用 Fluent API 构建测试消息
/// </summary>
/// <remarks>
/// 注意: 由于 IMessage 属性是只读的，此构建器仅适用于具有可写属性的消息类型
/// </remarks>
public class TestMessageBuilder<TMessage> where TMessage : new()
{
    private long? _messageId;
    private long? _correlationId;
    private QualityOfService? _qos;
    private Action<TMessage>? _configure;

    /// <summary>
    /// 设置 MessageId
    /// </summary>
    public TestMessageBuilder<TMessage> WithMessageId(long messageId)
    {
        _messageId = messageId;
        return this;
    }

    /// <summary>
    /// 设置 CorrelationId
    /// </summary>
    public TestMessageBuilder<TMessage> WithCorrelationId(long correlationId)
    {
        _correlationId = correlationId;
        return this;
    }

    /// <summary>
    /// 设置 QoS
    /// </summary>
    public TestMessageBuilder<TMessage> WithQoS(QualityOfService qos)
    {
        _qos = qos;
        return this;
    }

    /// <summary>
    /// 配置消息属性
    /// </summary>
    public TestMessageBuilder<TMessage> Configure(Action<TMessage> configure)
    {
        _configure = configure;
        return this;
    }

    /// <summary>
    /// 构建消息
    /// </summary>
    public TMessage Build()
    {
        var message = new TMessage();
        _configure?.Invoke(message);
        return message;
    }

    /// <summary>
    /// 隐式转换为消息
    /// </summary>
    public static implicit operator TMessage(TestMessageBuilder<TMessage> builder) => builder.Build();
}

/// <summary>
/// 测试消息构建器扩展
/// </summary>
public static class TestMessageBuilderExtensions
{
    /// <summary>
    /// 创建测试命令构建器
    /// </summary>
    public static TestMessageBuilder<TCommand> CreateCommand<TCommand>() where TCommand : new()
    {
        return new TestMessageBuilder<TCommand>();
    }

    /// <summary>
    /// 创建测试事件构建器
    /// </summary>
    public static TestMessageBuilder<TEvent> CreateEvent<TEvent>() where TEvent : new()
    {
        return new TestMessageBuilder<TEvent>();
    }
}
