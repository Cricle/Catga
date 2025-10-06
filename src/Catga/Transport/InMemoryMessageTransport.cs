using System.Collections.Concurrent;
using System.Diagnostics.CodeAnalysis;

namespace Catga.Transport;

/// <summary>
/// 内存消息传输 - 用于测试和本地开发
/// </summary>
public class InMemoryMessageTransport : IMessageTransport
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _subscribers = new();

    public string Name => "InMemory";

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public async Task PublishAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        if (!_subscribers.TryGetValue(messageType, out var handlers))
            return;

        context ??= new TransportContext
        {
            MessageId = Guid.NewGuid().ToString(),
            MessageType = messageType.FullName,
            SentAt = DateTime.UtcNow
        };

        var tasks = handlers
            .Cast<Func<TMessage, TransportContext, Task>>()
            .Select(handler => handler(message, context));

        await Task.WhenAll(tasks);
    }

    [RequiresUnreferencedCode("消息序列化可能需要无法静态分析的类型")]
    [RequiresDynamicCode("消息序列化可能需要运行时代码生成")]
    public Task SendAsync<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicProperties | DynamicallyAccessedMemberTypes.PublicFields)] TMessage>(
        TMessage message,
        string destination,
        TransportContext? context = null,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        // 对于内存传输，Send 和 Publish 行为一致
        return PublishAsync(message, context, cancellationToken);
    }

    public Task SubscribeAsync<TMessage>(
        Func<TMessage, TransportContext, Task> handler,
        CancellationToken cancellationToken = default)
        where TMessage : class
    {
        var messageType = typeof(TMessage);
        _subscribers.AddOrUpdate(
            messageType,
            _ => new List<Delegate> { handler },
            (_, list) =>
            {
                list.Add(handler);
                return list;
            });

        return Task.CompletedTask;
    }
}

