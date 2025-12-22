using Catga.Core;
using FsCheck;

namespace Catga.Tests.PropertyTests.Generators;

/// <summary>
/// FsCheck 生成器 - 消息相关类型
/// 用于属性测试的消息数据生成
/// </summary>
public static class MessageGenerators
{
    /// <summary>
    /// 生成有效的消息 ID
    /// </summary>
    public static Arbitrary<long> MessageIdArbitrary()
    {
        return Gen.Fresh(() => MessageExtensions.NewMessageId()).ToArbitrary();
    }

    /// <summary>
    /// 生成主题名称
    /// </summary>
    public static Arbitrary<string> TopicArbitrary()
    {
        return Gen.Elements(
            "topic.events", "topic.commands", "orders.created",
            "users.updated", "system.notifications", "payments.processed"
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成目标地址
    /// </summary>
    public static Arbitrary<string> DestinationArbitrary()
    {
        return Gen.Elements(
            "queue.orders", "queue.users", "queue.notifications",
            "service.payment", "service.inventory", "service.shipping"
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成消息负载（字节数组）
    /// </summary>
    public static Arbitrary<byte[]> PayloadArbitrary()
    {
        return Gen.Choose(1, 1000)
            .SelectMany(size => Gen.ArrayOf(size, Arb.Generate<byte>()))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成消息类型名称
    /// </summary>
    public static Arbitrary<string> MessageTypeArbitrary()
    {
        return Gen.Elements(
            "OrderCreatedEvent", "OrderUpdatedEvent", "OrderCompletedEvent",
            "UserRegisteredEvent", "UserUpdatedEvent",
            "PaymentReceivedEvent", "PaymentRefundedEvent",
            "CreateOrderCommand", "UpdateOrderCommand", "CancelOrderCommand"
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成测试消息（用于其他测试文件）
    /// </summary>
    public static Arbitrary<TestMessage> TestMessageArbitrary()
    {
        return Gen.Fresh(() => MessageExtensions.NewMessageId())
            .SelectMany(msgId =>
                Arb.Default.NonEmptyString().Generator.Select(content =>
                    new TestMessage
                    {
                        MessageId = msgId,
                        Content = content.Get
                    }))
            .ToArbitrary();
    }
}

/// <summary>
/// 用于属性测试的测试消息
/// </summary>
[MemoryPack.MemoryPackable]
public partial record TestMessage
{
    public required long MessageId { get; init; }
    public required string Content { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}
