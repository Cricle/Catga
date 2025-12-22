using Catga.Abstractions;
using Catga.Core;
using Catga.EventSourcing;
using FsCheck;
using MemoryPack;

namespace Catga.Tests.PropertyTests.Generators;

/// <summary>
/// FsCheck 生成器 - 事件相关类型
/// 用于属性测试的事件数据生成
/// </summary>
public static class EventGenerators
{
    /// <summary>
    /// 生成有效的流 ID
    /// </summary>
    public static Arbitrary<string> StreamIdArbitrary()
    {
        return Gen.OneOf(
            // 常见格式
            Gen.Elements("orders", "users", "products", "events", "aggregate"),
            // 带 ID 的格式
            Gen.Fresh(() => $"stream-{Guid.NewGuid():N}"),
            // 带前缀的格式
            Gen.Elements("order-", "user-", "product-").SelectMany(prefix =>
                Gen.Choose(1, 10000).Select(id => $"{prefix}{id}"))
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成测试事件
    /// </summary>
    public static Arbitrary<TestPropertyEvent> TestEventArbitrary()
    {
        return Gen.Fresh(() => MessageExtensions.NewMessageId())
            .SelectMany(msgId =>
                Arb.Default.NonEmptyString().Generator.SelectMany(data =>
                    Gen.Choose(1, 1000).Select(amount => new TestPropertyEvent
                    {
                        MessageId = msgId,
                        Data = data.Get,
                        Amount = amount
                    })))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成事件列表（1-20个事件）
    /// </summary>
    public static Arbitrary<List<IEvent>> EventListArbitrary()
    {
        return Gen.Choose(1, 20)
            .SelectMany(count => Gen.ListOf(count, TestEventArbitrary().Generator))
            .Select(events => events.Cast<IEvent>().ToList())
            .ToArbitrary();
    }

    /// <summary>
    /// 生成小型事件列表（1-5个事件，用于快速测试）
    /// </summary>
    public static Arbitrary<List<IEvent>> SmallEventListArbitrary()
    {
        return Gen.Choose(1, 5)
            .SelectMany(count => Gen.ListOf(count, TestEventArbitrary().Generator))
            .Select(events => events.Cast<IEvent>().ToList())
            .ToArbitrary();
    }

    /// <summary>
    /// 生成 StoredEvent
    /// </summary>
    public static Arbitrary<StoredEvent> StoredEventArbitrary()
    {
        return Gen.Choose(0, 10000)
            .SelectMany(version =>
                TestEventArbitrary().Generator.Select(evt => new StoredEvent
                {
                    Version = version,
                    Event = evt,
                    Timestamp = DateTime.UtcNow.AddMinutes(-System.Random.Shared.Next(0, 10000)),
                    EventType = evt.GetType().Name
                }))
            .ToArbitrary();
    }

    /// <summary>
    /// 生成 StoredEvent 列表（带有顺序版本号）
    /// </summary>
    public static Arbitrary<List<StoredEvent>> StoredEventListArbitrary()
    {
        return Gen.Choose(1, 20)
            .SelectMany(count => Gen.ListOf(count, TestEventArbitrary().Generator))
            .Select(events =>
            {
                var eventList = events.ToList();
                var result = new List<StoredEvent>();
                var timestamp = DateTime.UtcNow;
                for (int i = 0; i < eventList.Count; i++)
                {
                    result.Add(new StoredEvent
                    {
                        Version = i,
                        Event = eventList[i],
                        Timestamp = timestamp.AddMilliseconds(i),
                        EventType = eventList[i].GetType().Name
                    });
                }
                return result;
            }).ToArbitrary();
    }

    /// <summary>
    /// 生成有效的预期版本号
    /// </summary>
    public static Arbitrary<long> ExpectedVersionArbitrary()
    {
        return Gen.OneOf(
            Gen.Constant(-1L), // Any version
            Gen.Constant(-2L), // No stream
            Gen.Choose(0, 10000).Select(i => (long)i) // Specific version
        ).ToArbitrary();
    }

    /// <summary>
    /// 生成事件类型名称
    /// </summary>
    public static Arbitrary<string> EventTypeArbitrary()
    {
        return Gen.Elements(
            "OrderCreated", "OrderUpdated", "OrderCompleted", "OrderCancelled",
            "UserRegistered", "UserUpdated", "UserDeleted",
            "ProductAdded", "ProductUpdated", "ProductRemoved",
            "PaymentReceived", "PaymentRefunded"
        ).ToArbitrary();
    }
}

/// <summary>
/// 用于属性测试的测试事件
/// </summary>
[MemoryPackable]
public partial record TestPropertyEvent : IEvent
{
    public required long MessageId { get; init; }
    public required string Data { get; init; }
    public int Amount { get; init; }
    public DateTime OccurredAt { get; init; } = DateTime.UtcNow;
}
